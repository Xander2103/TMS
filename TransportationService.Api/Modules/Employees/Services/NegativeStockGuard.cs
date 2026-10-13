using TransportationService.Api.Common;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>Caller-supplied proof that the user explicitly confirmed a below-zero mutation.</summary>
public record NegativeStockConfirmation(bool Confirmed, Guid? ExpectedVersion, string? Reason);

/// <summary>
/// Thrown when a mutation would push stock below zero and negative stock is allowed but not
/// (validly) confirmed. Mapped to 409 + machine-readable payload so the frontend can show the
/// confirmation modal with live numbers; a frontend boolean alone can never bypass this —
/// the confirmation must carry the CURRENT Version token, which rotates on every mutation
/// (stale or replayed confirmations fail closed).
/// </summary>
public sealed class NegativeStockConfirmationRequiredException : Exception
{
    public NegativeStockConfirmationRequiredException(
        Guid templateId, Guid? variantId, string itemName, string? variantLabel,
        int currentStock, int requestedDelta, Guid version, bool requiresReason, bool versionMismatch)
        : base("Deze mutatie brengt de voorraad onder nul en vereist een expliciete bevestiging.")
    {
        TemplateId = templateId;
        VariantId = variantId;
        ItemName = itemName;
        VariantLabel = variantLabel;
        CurrentStock = currentStock;
        RequestedDelta = requestedDelta;
        Version = version;
        RequiresReason = requiresReason;
        VersionMismatch = versionMismatch;
    }

    public Guid TemplateId { get; }
    public Guid? VariantId { get; }
    public string ItemName { get; }
    public string? VariantLabel { get; }
    public int CurrentStock { get; }
    public int RequestedDelta { get; }
    public int ProjectedStock => CurrentStock + RequestedDelta;
    public Guid Version { get; }
    public bool RequiresReason { get; }

    /// <summary>True when a confirmation was sent but for an outdated stock state.</summary>
    public bool VersionMismatch { get; }
}

public interface INegativeStockGuard
{
    /// <summary>
    /// Enforces the negative-stock policy for a mutation of <paramref name="delta"/> (signed).
    /// No-op when the projected stock stays ≥ 0. Otherwise: blocks when the template forbids
    /// negative stock; requires the inventory.override_negative_stock permission; and requires
    /// a confirmed request carrying the current Version (and a reason when configured).
    /// </summary>
    Task EnsureAllowedAsync(
        IssuedItemTemplate template, IssuedItemVariant? variant, int delta,
        NegativeStockConfirmation? confirmation, CancellationToken cancellationToken);
}

public class NegativeStockGuard : INegativeStockGuard
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _permissionService;

    public NegativeStockGuard(ICurrentUserContext currentUser, IPermissionAuthorizationService permissionService)
    {
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    public async Task EnsureAllowedAsync(
        IssuedItemTemplate template, IssuedItemVariant? variant, int delta,
        NegativeStockConfirmation? confirmation, CancellationToken cancellationToken)
    {
        var current = variant?.CurrentStock ?? template.CurrentStock;
        if (current + delta >= 0)
        {
            return;
        }

        if (!template.AllowNegativeStock)
        {
            throw new DomainValidationException("quantity",
                $"Onvoldoende voorraad (beschikbaar: {current}). Negatieve voorraad is niet toegestaan voor dit artikel.");
        }

        var mayOverride = _currentUser.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(
                userId, PermissionCodes.InventoryOverrideNegativeStock, cancellationToken);
        if (!mayOverride)
        {
            throw new DomainValidationException("quantity",
                $"Onvoldoende voorraad (beschikbaar: {current}). Je hebt geen toestemming om negatieve voorraad te bevestigen.");
        }

        var version = variant?.Version ?? template.Version;
        var confirmed = confirmation is { Confirmed: true };
        var versionMatches = confirmed && confirmation!.ExpectedVersion == version;
        if (!confirmed || !versionMatches)
        {
            throw new NegativeStockConfirmationRequiredException(
                template.Id, variant?.Id, template.Name, variant?.Label,
                current, delta, version, template.NegativeStockRequiresReason,
                versionMismatch: confirmed && !versionMatches);
        }

        if (template.NegativeStockRequiresReason && string.IsNullOrWhiteSpace(confirmation!.Reason))
        {
            throw new DomainValidationException("overrideReason",
                "Een reden is verplicht bij het bevestigen van negatieve voorraad.");
        }
    }
}
