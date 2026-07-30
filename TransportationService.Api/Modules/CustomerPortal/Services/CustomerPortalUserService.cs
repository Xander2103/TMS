using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface ICustomerPortalUserService
{
    Task<PortalResult<IReadOnlyList<PortalUserListItemDto>>> ListAsync(CancellationToken cancellationToken);

    Task<PortalUserOperationResult<PortalUserInviteResultDto>> InviteAsync(
        PortalInviteUserRequest request, CancellationToken cancellationToken);

    Task<PortalUserOperationResult<PortalUserListItemDto>> DeactivateAsync(Guid userId, CancellationToken cancellationToken);

    Task<PortalUserOperationResult<PortalUserListItemDto>> ReactivateAsync(Guid userId, CancellationToken cancellationToken);

    Task<PortalUserOperationResult<PortalUserInviteResultDto>> ResendInviteAsync(Guid userId, CancellationToken cancellationToken);

    Task<PortalUserOperationResult<PortalUserListItemDto>> SetGrantsAsync(
        Guid userId, PortalUserGrantsDto grants, CancellationToken cancellationToken);
}

/// <summary>
/// Management of a customer's OWN portal users (klantportaal_* family roles only). Every
/// operation is scoped exclusively by the CALLER's own linked customer (never a client-supplied
/// id, never another tenant/customer's users) — the same rule as every other endpoint in this
/// module. Portal-assignable roles are strictly the four klantportaal_* templates resolved by
/// TemplateCode; no other role is ever readable or assignable through this service.
/// </summary>
public class CustomerPortalUserService : ICustomerPortalUserService
{
    private static readonly string[] PortalTemplateCodes =
    [
        "klantportaal", "klantportaal_documenten", "klantportaal_facturen", "klantportaal_gebruikersbeheer",
    ];

    private const int MaxNameLength = 100;
    private const int MaxEmailLength = 250;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IUserAccountFlowService _accountFlows;
    private readonly IMessageOutboxService _messageOutbox;
    private readonly IEmailProvider _emailProvider;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CustomerPortalUserService>? _logger;

    public CustomerPortalUserService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IUserAccountFlowService accountFlows,
        IMessageOutboxService messageOutbox,
        IEmailProvider emailProvider,
        IAuditService auditService,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<CustomerPortalUserService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _accountFlows = accountFlows;
        _messageOutbox = messageOutbox;
        _emailProvider = emailProvider;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Enforced guard (not just documentation): the raw activation token is only ever safe to
    /// hand back in an API response while the registered <see cref="IEmailProvider"/> is the
    /// development sink — i.e. mail delivery isn't actually configured, so there is no other way
    /// for the invited person to receive the link. The moment a real provider (SMTP/SendGrid/…)
    /// is registered in DI, this flips to false and the token is withheld from every response.
    /// </summary>
    private bool IsRawTokenSafeToReturn => _emailProvider is DevelopmentSinkProvider;

    private async Task<(Guid CustomerId, string CustomerName)?> MyCustomerAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        var link = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.CustomerId != null)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                u => u.CustomerId, c => c.Id,
                (u, c) => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return link is null ? null : (link.Id, link.Name);
    }

    public async Task<PortalResult<IReadOnlyList<PortalUserListItemDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<IReadOnlyList<PortalUserListItemDto>>.NoCustomerLink();
        }

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.CustomerId == customer.Value.CustomerId)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        var dtos = await MapManyAsync(users, cancellationToken);
        return PortalResult<IReadOnlyList<PortalUserListItemDto>>.Success(dtos);
    }

    public async Task<PortalUserOperationResult<PortalUserInviteResultDto>> InviteAsync(
        PortalInviteUserRequest request, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.Invalid(
                "Deze gebruiker is niet aan een klant gekoppeld; neem contact op met de beheerder.");
        }

        var email = request.Email.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        if (string.IsNullOrWhiteSpace(email) || email.Length > MaxEmailLength || !email.Contains('@'))
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.Invalid(
                "Vul een geldig e-mailadres in (max. 250 tekens).");
        }

        if (string.IsNullOrWhiteSpace(firstName) || firstName.Length > MaxNameLength
            || string.IsNullOrWhiteSpace(lastName) || lastName.Length > MaxNameLength)
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.Invalid(
                "Vul een voor- en achternaam in (max. 100 tekens).");
        }

        var portalRoles = await LoadPortalRoleIdsAsync(cancellationToken);
        if (!portalRoles.TryGetValue("klantportaal", out var baseRoleId))
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.Invalid(
                "De klantportaalrol ontbreekt voor deze omgeving; neem contact op met de systeembeheerder.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            CustomerId = customer.Value.CustomerId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _dbContext.Users.Add(user);
        _dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = baseRoleId });
        foreach (var roleId in ResolveOptionalRoleIds(portalRoles, request.Grants))
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.Invalid(
                "Er bestaat al een gebruiker met dit e-mailadres.");
        }

        await _auditService.RecordAsync("CustomerPortalUser", user.Id.ToString(), "Invited", null,
            new { user.Email, user.FirstName, user.LastName, CustomerId = customer.Value.CustomerId, request.Grants },
            cancellationToken);

        var started = await _accountFlows.StartActivationAsync(user.Id, cancellationToken);
        if (started is null)
        {
            // Cannot realistically happen (the user was just created in this same tenant) but
            // the invite must not silently "succeed" without an activation path.
            return PortalUserOperationResult<PortalUserInviteResultDto>.Invalid(
                "De uitnodiging kon niet worden gestart.");
        }

        await QueueInviteEmailAsync(user, customer.Value.CustomerId, customer.Value.CustomerName, started.Token, cancellationToken);

        var dto = (await MapManyAsync([user], cancellationToken))[0];
        return PortalUserOperationResult<PortalUserInviteResultDto>.Success(
            new PortalUserInviteResultDto(dto, IsRawTokenSafeToReturn ? started.Token : null, started.ExpiresAtUtc));
    }

    public async Task<PortalUserOperationResult<PortalUserListItemDto>> DeactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await LoadOwnPortalUserAsync(userId, cancellationToken);
        if (found is null)
        {
            return PortalUserOperationResult<PortalUserListItemDto>.NotFound();
        }

        var (customer, user) = found.Value;
        var wasActive = user.IsActive;
        user.IsActive = false;
        user.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await RevokeRefreshTokensAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("CustomerPortalUser", user.Id.ToString(), "Deactivated",
            new { IsActive = wasActive }, new { user.IsActive }, cancellationToken);

        return PortalUserOperationResult<PortalUserListItemDto>.Success((await MapManyAsync([user], cancellationToken))[0]);
    }

    public async Task<PortalUserOperationResult<PortalUserListItemDto>> ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await LoadOwnPortalUserAsync(userId, cancellationToken);
        if (found is null)
        {
            return PortalUserOperationResult<PortalUserListItemDto>.NotFound();
        }

        var (_, user) = found.Value;
        var wasActive = user.IsActive;
        user.IsActive = true;
        user.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("CustomerPortalUser", user.Id.ToString(), "Reactivated",
            new { IsActive = wasActive }, new { user.IsActive }, cancellationToken);

        return PortalUserOperationResult<PortalUserListItemDto>.Success((await MapManyAsync([user], cancellationToken))[0]);
    }

    public async Task<PortalUserOperationResult<PortalUserInviteResultDto>> ResendInviteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await LoadOwnPortalUserAsync(userId, cancellationToken);
        if (found is null)
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.NotFound();
        }

        var (customer, user) = found.Value;

        // StartActivationAsync revokes every still-open activation token for this user before
        // issuing the fresh one — exactly the "invalidate old tokens first" behaviour required.
        var started = await _accountFlows.StartActivationAsync(user.Id, cancellationToken);
        if (started is null)
        {
            return PortalUserOperationResult<PortalUserInviteResultDto>.NotFound();
        }

        await QueueInviteEmailAsync(user, customer.CustomerId, customer.CustomerName, started.Token, cancellationToken);

        await _auditService.RecordAsync("CustomerPortalUser", user.Id.ToString(), "InviteResent", null,
            new { user.Email }, cancellationToken);

        var dto = (await MapManyAsync([user], cancellationToken))[0];
        return PortalUserOperationResult<PortalUserInviteResultDto>.Success(
            new PortalUserInviteResultDto(dto, IsRawTokenSafeToReturn ? started.Token : null, started.ExpiresAtUtc));
    }

    public async Task<PortalUserOperationResult<PortalUserListItemDto>> SetGrantsAsync(
        Guid userId, PortalUserGrantsDto grants, CancellationToken cancellationToken)
    {
        var found = await LoadOwnPortalUserAsync(userId, cancellationToken);
        if (found is null)
        {
            return PortalUserOperationResult<PortalUserListItemDto>.NotFound();
        }

        var (_, user) = found.Value;
        var portalRoles = await LoadPortalRoleIdsAsync(cancellationToken);

        var existing = await _dbContext.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync(cancellationToken);
        var oldRoleIds = existing.Select(ur => ur.RoleId).ToHashSet();
        var wantedOptionalIds = ResolveOptionalRoleIds(portalRoles, grants).ToHashSet();

        // The base klantportaal role is never touched here; only the three optional add-ons
        // toggle. Any non-portal role a tenant might (incorrectly) have assigned is left as-is —
        // this endpoint only manages the klantportaal_* family, never anything else.
        var optionalTemplateRoleIds = portalRoles
            .Where(kv => kv.Key != "klantportaal")
            .Select(kv => kv.Value)
            .ToHashSet();

        foreach (var ur in existing.Where(ur => optionalTemplateRoleIds.Contains(ur.RoleId) && !wantedOptionalIds.Contains(ur.RoleId)))
        {
            _dbContext.UserRoles.Remove(ur);
        }

        foreach (var roleId in wantedOptionalIds.Except(oldRoleIds))
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        user.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("CustomerPortalUser", user.Id.ToString(), "GrantsChanged", null,
            new { grants }, cancellationToken);

        return PortalUserOperationResult<PortalUserListItemDto>.Success((await MapManyAsync([user], cancellationToken))[0]);
    }

    // --- shared helpers ---

    /// <summary>Loads a user IFF it belongs to the CALLER's own customer and tenant — a foreign
    /// customer's user (or one with no CustomerId at all, i.e. an internal account) is
    /// indistinguishable from a non-existent one.</summary>
    private async Task<((Guid CustomerId, string CustomerName) Customer, User User)?> LoadOwnPortalUserAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == _tenantContext.TenantId
                && u.CustomerId == customer.Value.CustomerId, cancellationToken);
        return user is null ? null : (customer.Value, user);
    }

    private async Task<Dictionary<string, Guid>> LoadPortalRoleIdsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Roles.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.TemplateCode != null
                && PortalTemplateCodes.Contains(r.TemplateCode))
            .ToDictionaryAsync(r => r.TemplateCode!, r => r.Id, cancellationToken);

    private static IEnumerable<Guid> ResolveOptionalRoleIds(Dictionary<string, Guid> portalRoles, PortalUserGrantsDto grants)
    {
        if (grants.Documents && portalRoles.TryGetValue("klantportaal_documenten", out var documentsId))
        {
            yield return documentsId;
        }

        if (grants.Invoices && portalRoles.TryGetValue("klantportaal_facturen", out var invoicesId))
        {
            yield return invoicesId;
        }

        if (grants.ManageUsers && portalRoles.TryGetValue("klantportaal_gebruikersbeheer", out var manageUsersId))
        {
            yield return manageUsersId;
        }
    }

    private async Task RevokeRefreshTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var tokens = await _dbContext.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    /// <summary>One-way reference to a raw token: usable for uniqueness, useless for activation.</summary>
    private static string TokenReference(string rawToken) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)))[..12].ToLowerInvariant();

    private async Task QueueInviteEmailAsync(
        User user, Guid customerId, string customerName, string rawToken, CancellationToken cancellationToken)
    {
        // Admins can disable the event from the notification-rules admin UI even though delivery
        // bypasses NotificationEventService's recipient resolution (see MessageKinds.PortalUserInvited).
        var rule = await _dbContext.NotificationRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId
                && r.EventKey == MessageKinds.PortalUserInvited, cancellationToken);
        if (rule?.Enabled == false)
        {
            return;
        }

        var baseUrl = _configuration["Frontend:BaseUrl"];
        var query = $"?token={Uri.EscapeDataString(rawToken)}&email={Uri.EscapeDataString(user.Email)}";
        var link = string.IsNullOrWhiteSpace(baseUrl) ? $"/activeren{query}" : $"{baseUrl.TrimEnd('/')}/activeren{query}";

        try
        {
            await _messageOutbox.QueueAsync(new MessageRequest(
                MessageKinds.PortalUserInvited,
                MessageOwnerType.Customer, customerId,
                new Dictionary<string, string>
                {
                    ["firstName"] = user.FirstName,
                    ["activationLink"] = link,
                },
                RelatedEntityType: "User",
                RelatedEntityId: user.Id.ToString(),
                // Varies per issuance (invite + every resend) so each mail is its own outbox row.
                // A one-way hash prefix keeps the key unique per token WITHOUT persisting token
                // material (C3); the dispatcher scrubs the rendered link from Body after delivery.
                IdempotencyKey: $"portal_user_invited:User:{user.Id}:{TokenReference(rawToken)}",
                OverrideAddress: user.Email,
                OverrideName: $"{user.FirstName} {user.LastName}".Trim(),
                CustomerIdForTemplate: customerId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception,
                "portal_user_invited failed to queue for user {UserId}; the account and activation token were already created.",
                user.Id);
        }
    }

    private async Task<IReadOnlyList<PortalUserListItemDto>> MapManyAsync(IReadOnlyList<User> users, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var portalRoles = await LoadPortalRoleIdsAsync(cancellationToken);
        var userIds = users.Select(u => u.Id).ToList();

        var roleIdsByUser = await _dbContext.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .ToListAsync(cancellationToken);
        var roleIdsByUserLookup = roleIdsByUser
            .GroupBy(ur => ur.UserId)
            .ToDictionary(g => g.Key, g => g.Select(ur => ur.RoleId).ToHashSet());

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var pendingActivationUserIds = await _dbContext.UserSecurityTokens.AsNoTracking()
            .Where(t => userIds.Contains(t.UserId) && t.Kind == UserSecurityTokenKind.Activation
                && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var pendingSet = pendingActivationUserIds.ToHashSet();

        return users.Select(u =>
        {
            var roleIds = roleIdsByUserLookup.GetValueOrDefault(u.Id, []);
            var grants = new PortalUserGrantsDto(
                Documents: portalRoles.TryGetValue("klantportaal_documenten", out var d) && roleIds.Contains(d),
                Invoices: portalRoles.TryGetValue("klantportaal_facturen", out var i) && roleIds.Contains(i),
                ManageUsers: portalRoles.TryGetValue("klantportaal_gebruikersbeheer", out var m) && roleIds.Contains(m));
            return new PortalUserListItemDto(
                u.Id, u.Email, u.FirstName, u.LastName, u.IsActive, u.IsBlocked, pendingSet.Contains(u.Id), grants);
        }).ToList();
    }
}
