using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy.Provisioning;

/// <summary>Everything needed to set up one company (tenant) and its first administrator.</summary>
public sealed record TenantProvisioningRequest(
    string Name,
    string Slug,
    string AdminEmail,
    string? AdminFirstName = null,
    string? AdminLastName = null,
    string? LegalEntityName = null,
    string? VatNumber = null,
    string? CompanyNumber = null,
    string? CountryCode = null,
    string? Timezone = null,
    string? Language = null,
    bool ResendActivation = false);

/// <summary>
/// Outcome of a provisioning run. <see cref="Activation"/> holds the RAW one-time token when an
/// activation was issued; callers must never write it anywhere unless the operator explicitly
/// asked for it (<c>--print-activation-link</c>) — <see cref="Report"/> never contains it.
/// </summary>
public sealed record TenantProvisioningResult(
    Guid TenantId,
    string Slug,
    bool TenantCreated,
    Guid AdminUserId,
    string AdminEmail,
    bool AdminCreated,
    ActivationIssueResult? Activation,
    IReadOnlyList<string> Report);

/// <summary>A provisioning request that cannot be honoured; the message is operator-facing Dutch.</summary>
public sealed class TenantProvisioningException(string message) : Exception(message);

/// <summary>
/// B3: the explicit, idempotent way to set up a new company on ANY environment (Production
/// included), without starting the web server and without any development shortcut.
///
/// Creates (each step add-if-missing, so a re-run with the same slug reports what exists and
/// duplicates nothing): the tenant + tenant settings (Europe/Brussels, Dutch, numbering
/// defaults), the system Administrator role with the full permission catalogue, the 12 versioned
/// role templates (AuthorizationCatalogSync → DefaultRoleSeeder, current version), the tenant's
/// reference lists, a default legal entity from the given company data, the default expiry
/// policies and the issued-item starter catalogue. Then the first administrator — WITHOUT a
/// password: the existing activation flow issues a single-use token and queues the activation
/// link as an e-mail through the outbox.
///
/// Never: admin@dev.local, demo passwords, demo/test records (DevAdminSeeder, DevDemoDataSeeder
/// and MasterDataSeeder are not used), and never a second administrator for an existing tenant.
/// </summary>
public sealed partial class TenantProvisioningService
{
    public const string DefaultTimezone = "Europe/Brussels";
    public const string AdministratorRoleName = "Administrator";

    private static readonly string[] SupportedLanguages = ["nl", "fr", "en"];

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,98}[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    private readonly TransportationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public TenantProvisioningService(
        TransportationDbContext db, IPasswordHasher passwordHasher, TimeProvider timeProvider,
        IHostEnvironment environment, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _environment = environment;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TenantProvisioningService>();
    }

    public async Task<TenantProvisioningResult> ProvisionAsync(
        TenantProvisioningRequest request, CancellationToken cancellationToken)
    {
        var input = Validate(request);
        var report = new List<string>();

        var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var result = await ProvisionCoreAsync(input, request.ResendActivation, report, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<TenantProvisioningResult> ProvisionCoreAsync(
        ValidatedInput input, bool resendActivation, List<string> report, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Global reference data the tenant depends on (idempotent; normally present already).
        await CountrySeeder.SyncAsync(_db);
        var qualificationTypesAdded = await QualificationTypeSeeder.SyncAsync(_db, cancellationToken);
        report.Add(qualificationTypesAdded > 0
            ? $"Kwalificatietypes: {qualificationTypesAdded} toegevoegd."
            : "Kwalificatietypes: al aanwezig.");

        // --- Tenant + settings -------------------------------------------------------------
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == input.Slug, cancellationToken);
        var tenantCreated = tenant is null;
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = input.Name,
                Slug = input.Slug,
                IsActive = true,
                CreatedAt = now,
            };
            _db.Tenants.Add(tenant);
            report.Add($"Tenant '{input.Slug}' aangemaakt ({tenant.Id}).");
        }
        else
        {
            report.Add($"Tenant '{input.Slug}' bestaat al ({tenant.Id}, naam '{tenant.Name}'); er wordt niets overschreven.");
        }

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, cancellationToken);
        if (settings is null)
        {
            settings = new TenantSettings
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Timezone = input.Timezone,
                DefaultLanguage = input.Language,
                EmployeeNumberPrefix = "MED-",
                EmployeeNumberNextValue = 1,
                CompanyLegalName = input.LegalEntityName,
                TradingName = input.LegalEntityName == input.Name ? null : input.Name,
                VatNumber = input.VatNumber,
                CompanyNumber = input.CompanyNumber,
                CountryCode = input.CountryCode,
            };
            _db.TenantSettings.Add(settings);
            report.Add($"Bedrijfsinstellingen aangemaakt (tijdzone {input.Timezone}, taal {input.Language}, standaardnummering).");
        }
        else
        {
            report.Add("Bedrijfsinstellingen bestaan al.");
        }

        await _db.SaveChangesAsync(cancellationToken);

        // --- Administrator e-mail guard (before anything role/user related) ---------------
        var normalizedEmail = User.NormalizeEmail(input.AdminEmail);
        var sameEmailUsers = await _db.Users
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => new { u.Id, u.TenantId })
            .ToListAsync(cancellationToken);
        if (sameEmailUsers.Any(u => u.TenantId != tenant.Id))
        {
            throw new TenantProvisioningException(
                $"Het e-mailadres {input.AdminEmail} hoort al bij een gebruiker van een andere tenant. "
                + "Gebruik een ander adres voor de eerste beheerder; er is geen tweede account aangemaakt.");
        }

        // --- System Administrator role + permission catalogue + role templates -------------
        var adminRole = await _db.Roles.FirstOrDefaultAsync(
            r => r.TenantId == tenant.Id && r.IsSystemRole && r.Name == AdministratorRoleName, cancellationToken);
        if (adminRole is null)
        {
            adminRole = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Name = AdministratorRoleName,
                Description = "Volledige toegang tot alle functies.",
                IsSystemRole = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Roles.Add(adminRole);
            await _db.SaveChangesAsync(cancellationToken);
            report.Add("Systeemrol Administrator aangemaakt.");
        }
        else
        {
            report.Add("Systeemrol Administrator bestaat al.");
        }

        // Full catalogue for system roles + the versioned role templates (idempotent, all tenants).
        await AuthorizationCatalogSync.SyncAsync(_db, _logger, cancellationToken);
        var templateRoles = await _db.Roles.CountAsync(r => r.TenantId == tenant.Id && r.TemplateCode != null, cancellationToken);
        var templateVersion = await _db.RoleTemplateStates.Where(s => s.TenantId == tenant.Id)
            .Select(s => (int?)s.AppliedVersion).FirstOrDefaultAsync(cancellationToken);
        report.Add($"Rechtencatalogus gesynchroniseerd; {templateRoles} standaardrollen (sjabloonversie v{templateVersion}).");

        // --- Tenant reference data -----------------------------------------------------------
        await ReferenceDataSeeder.SeedTenantAsync(_db, tenant.Id, includeStarterCatalogs: true, cancellationToken);
        report.Add("Referentielijsten aangevuld (alleen ontbrekende codes).");
        var policiesAdded = await ExpiryPolicySeeder.SeedTenantAsync(_db, tenant.Id, cancellationToken);
        report.Add($"Vervaltermijn-herinneringen: {policiesAdded} toegevoegd.");
        var templatesAdded = await IssuedItemTemplateSeeder.SeedTenantAsync(_db, tenant.Id, cancellationToken);
        report.Add(templatesAdded > 0
            ? $"Uitgiftesjablonen: {templatesAdded} startsjablonen toegevoegd."
            : "Uitgiftesjablonen: al aanwezig (niet opnieuw toegevoegd).");

        var hadLegalEntity = await _db.LegalEntities.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenant.Id, cancellationToken);
        if (!hadLegalEntity)
        {
            await LegalEntitySeeder.SeedAsync(_db);
            report.Add($"Eigen bedrijf (juridische entiteit) '{settings.CompanyLegalName ?? tenant.Name}' aangemaakt als standaard.");
        }
        else
        {
            report.Add("Eigen bedrijf (juridische entiteit) bestaat al.");
        }

        // --- First administrator ----------------------------------------------------------
        var existingUser = await _db.Users.FirstOrDefaultAsync(
            u => u.TenantId == tenant.Id && u.NormalizedEmail == normalizedEmail, cancellationToken);
        ActivationIssueResult? activation = null;
        bool adminCreated;
        User admin;
        if (existingUser is not null)
        {
            admin = existingUser;
            adminCreated = false;
            var isAdmin = await _db.UserRoles.AnyAsync(ur => ur.UserId == admin.Id && ur.RoleId == adminRole.Id, cancellationToken);
            report.Add($"Gebruiker {admin.Email} bestaat al ({admin.Id}){(isAdmin ? ", met rol Administrator" : ", ZONDER rol Administrator — niet gewijzigd")}.");

            if (resendActivation)
            {
                if (!string.IsNullOrEmpty(admin.PasswordHash))
                {
                    throw new TenantProvisioningException(
                        $"Het account {admin.Email} is al geactiveerd (wachtwoord ingesteld). Gebruik 'Wachtwoord vergeten' "
                        + "op het aanmeldscherm; er is geen nieuwe activatielink uitgegeven.");
                }

                activation = await IssueActivationAsync(tenant.Id, admin.Id, cancellationToken);
            }
            else if (string.IsNullOrEmpty(admin.PasswordHash))
            {
                report.Add("Het account is nog niet geactiveerd. Gebruik --resend-activation om een nieuwe activatiemail te versturen.");
            }
        }
        else
        {
            var otherAdmins = await _db.UserRoles
                .Where(ur => ur.RoleId == adminRole.Id)
                .Join(_db.Users, ur => ur.UserId, u => u.Id, (ur, u) => u.Email)
                .ToListAsync(cancellationToken);
            if (otherAdmins.Count > 0)
            {
                throw new TenantProvisioningException(
                    $"Tenant '{tenant.Slug}' heeft al een beheerder ({string.Join(", ", otherAdmins)}). "
                    + "Extra beheerders voeg je toe via Instellingen > Gebruikers; er is geen tweede beheerder aangemaakt.");
            }

            admin = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Email = input.AdminEmail,
                FirstName = input.AdminFirstName,
                LastName = input.AdminLastName,
                PasswordHash = null, // never a password: the activation link sets it
                PreferredLanguageCode = input.Language,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Users.Add(admin);
            _db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
            await _db.SaveChangesAsync(cancellationToken);
            adminCreated = true;
            report.Add($"Eerste beheerder {admin.Email} aangemaakt ({admin.Id}) met rol Administrator, zonder wachtwoord.");

            activation = await IssueActivationAsync(tenant.Id, admin.Id, cancellationToken);
        }

        if (activation is not null)
        {
            report.Add(activation.Mail is { Outcome: QueueOutcome.Queued } queued
                ? $"Activatiemail in de wachtrij gezet voor {admin.Email} (outbox-bericht {queued.MessageId}); "
                  + $"de link is eenmalig en geldig tot {activation.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC."
                : $"LET OP: de activatiemail kon niet in de wachtrij worden gezet ({activation.Mail?.Outcome.ToString() ?? "fout"}"
                  + $"{(activation.Mail?.Reason is { } reason ? $": {reason}" : string.Empty)}). "
                  + "Herhaal met --resend-activation zodra de oorzaak is opgelost.");
        }

        var tenantAudit = new AuditService(_db, new DevTenantContext(tenant.Id), new DevCurrentUserContext(null));
        await tenantAudit.RecordAsync("Tenant", tenant.Id.ToString(), tenantCreated ? "Provisioned" : "ProvisioningRerun", null,
            new { tenant.Slug, AdminUserId = admin.Id, AdminCreated = adminCreated, ActivationIssued = activation is not null },
            cancellationToken);

        return new TenantProvisioningResult(tenant.Id, tenant.Slug, tenantCreated, admin.Id, admin.Email, adminCreated, activation, report);
    }

    /// <summary>
    /// The activation link for the explicit <c>--print-activation-link</c> operator flag ONLY
    /// (air-gapped installs). Same link the e-mail carries; null when no activation was issued.
    /// </summary>
    public string? BuildActivationLinkForOperator(TenantProvisioningResult result)
    {
        if (result.Activation is not { } activation)
        {
            return null;
        }

        var query = $"?token={Uri.EscapeDataString(activation.Token)}&email={Uri.EscapeDataString(result.AdminEmail)}";
        var baseUrl = _configuration["Frontend:BaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl) ? $"/activeren{query}" : $"{baseUrl.TrimEnd('/')}/activeren{query}";
    }

    private async Task<ActivationIssueResult?> IssueActivationAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        // The existing activation flow, scoped explicitly to the new tenant (no request exists).
        var tenantContext = new DevTenantContext(tenantId);
        var flow = new UserAccountFlowService(
            _db, tenantContext, _passwordHasher,
            new AuditService(_db, tenantContext, new DevCurrentUserContext(null)),
            _timeProvider, _environment, _configuration,
            _loggerFactory.CreateLogger<UserAccountFlowService>());
        return await flow.IssueActivationAsync(userId, cancellationToken);
    }

    private sealed record ValidatedInput(
        string Name, string Slug, string AdminEmail, string AdminFirstName, string AdminLastName,
        string LegalEntityName, string? VatNumber, string? CompanyNumber, string CountryCode,
        string Timezone, string Language);

    private ValidatedInput Validate(TenantProvisioningRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
        {
            throw new TenantProvisioningException("--name is verplicht (de bedrijfsnaam, max. 200 tekens).");
        }

        var slug = request.Slug?.Trim() ?? string.Empty;
        if (!SlugPattern().IsMatch(slug))
        {
            throw new TenantProvisioningException(
                "--slug is verplicht: kleine letters, cijfers en koppeltekens (bv. 'transport-peeters'), max. 100 tekens.");
        }

        var email = request.AdminEmail?.Trim() ?? string.Empty;
        var at = email.IndexOf('@');
        if (email.Length > 250 || at <= 0 || at == email.Length - 1 || email.Contains(' '))
        {
            throw new TenantProvisioningException("--admin-email is verplicht en moet een geldig e-mailadres zijn.");
        }

        var firstName = Trimmed(request.AdminFirstName) ?? "Beheerder";
        var lastName = Trimmed(request.AdminLastName) ?? name;
        if (firstName.Length > 100 || lastName.Length > 100)
        {
            throw new TenantProvisioningException("Voor- en achternaam van de beheerder zijn max. 100 tekens.");
        }

        var timezone = Trimmed(request.Timezone) ?? DefaultTimezone;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
        {
            throw new TenantProvisioningException($"Onbekende tijdzone '{timezone}' (gebruik een IANA-naam zoals Europe/Brussels).");
        }

        var language = (Trimmed(request.Language) ?? "nl").ToLowerInvariant();
        if (!SupportedLanguages.Contains(language))
        {
            throw new TenantProvisioningException("--language moet nl, fr of en zijn.");
        }

        var country = (Trimmed(request.CountryCode) ?? "BE").ToUpperInvariant();
        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            throw new TenantProvisioningException("--country moet een ISO-landcode van 2 letters zijn (bv. BE).");
        }

        string? vat;
        try
        {
            vat = VatNumberValidator.NormalizeAndValidate(request.VatNumber, "vat");
        }
        catch (DomainValidationException exception)
        {
            throw new TenantProvisioningException($"--vat: {exception.Message}");
        }

        var legalName = Trimmed(request.LegalEntityName) ?? name;
        if (legalName.Length > 200)
        {
            throw new TenantProvisioningException("--legal-entity-name is max. 200 tekens.");
        }

        // An activation mail with a relative link is useless; outside Development the absolute
        // public URL of the web application must be configured (Frontend:BaseUrl).
        var baseUrl = _configuration["Frontend:BaseUrl"];
        if (!_environment.IsDevelopment()
            && !(Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
        {
            throw new TenantProvisioningException(
                "Frontend:BaseUrl is niet ingesteld als absolute URL (bv. https://tms.bedrijf.be). "
                + "De activatielink in de e-mail zou onbruikbaar zijn; stel FRONTEND__BASEURL in en probeer opnieuw.");
        }

        return new ValidatedInput(name, slug, email, firstName, lastName, legalName, vat,
            Trimmed(request.CompanyNumber), country, timezone, language);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
