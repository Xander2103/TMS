using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record CreateReorderProposalRequest(Guid TemplateId, Guid? VariantId = null, int? Quantity = null, string? Notes = null);

public record ReorderProposalStatusRequest(ReorderProposalStatus Status, int? ApprovedQuantity = null, string? Notes = null);

public record ReorderProposalDto(
    Guid Id, Guid TemplateId, Guid? VariantId, string Name, string? VariantLabel,
    int CurrentStockSnapshot, int? TargetStockSnapshot, int SuggestedQuantity, int? ApprovedQuantity,
    ReorderProposalStatus Status, string? Notes, Guid? CreatedByUserId, Guid? ApprovedByUserId,
    DateTime CreatedAt, DateTime? ResolvedAt);

public interface IReorderProposalService
{
    Task<IReadOnlyList<ReorderProposalDto>> ListAsync(bool openOnly, CancellationToken cancellationToken);
    Task<ReorderProposalDto> CreateAsync(CreateReorderProposalRequest request, CancellationToken cancellationToken);
    Task<ReorderProposalDto?> ChangeStatusAsync(Guid id, ReorderProposalStatusRequest request, CancellationToken cancellationToken);

    /// <summary>Suggested order size: target − current, rounded UP to a multiple of the pack size.</summary>
    static int SuggestQuantity(int current, int? target, int? packSize)
    {
        var needed = Math.Max(0, (target ?? 0) - current);
        if (needed == 0)
        {
            return packSize ?? 1;
        }

        if (packSize is { } pack and > 1)
        {
            var packs = (needed + pack - 1) / pack;
            return packs * pack;
        }

        return needed;
    }
}

public class ReorderProposalService : IReorderProposalService
{
    private const string EntityType = "ReorderProposal";
    private static readonly ReorderProposalStatus[] OpenStatuses =
    [
        ReorderProposalStatus.Proposed, ReorderProposalStatus.Reviewed,
        ReorderProposalStatus.Approved, ReorderProposalStatus.Ordered,
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;

    public ReorderProposalService(TransportationDbContext dbContext, ITenantContext tenantContext,
        ICurrentUserContext currentUser, IAuditService auditService,
        INotificationService notificationService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditService = auditService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ReorderProposalDto>> ListAsync(bool openOnly, CancellationToken cancellationToken)
    {
        var rows = _dbContext.ReorderProposals.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId);
        if (openOnly)
        {
            rows = rows.Where(p => OpenStatuses.Contains(p.Status));
        }

        var proposals = await rows.OrderByDescending(p => p.CreatedAt).Take(300).ToListAsync(cancellationToken);
        return await MapAsync(proposals, cancellationToken);
    }

    public async Task<ReorderProposalDto> CreateAsync(CreateReorderProposalRequest request, CancellationToken cancellationToken)
    {
        var template = await _dbContext.IssuedItemTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == request.TemplateId, cancellationToken)
            ?? throw new DomainValidationException("templateId", "Het artikel bestaat niet.");
        IssuedItemVariant? variant = null;
        if (request.VariantId is { } variantId)
        {
            variant = await _dbContext.IssuedItemVariants.AsNoTracking()
                .FirstOrDefaultAsync(v => v.TenantId == _tenantContext.TenantId && v.TemplateId == template.Id && v.Id == variantId, cancellationToken)
                ?? throw new DomainValidationException("variantId", "De variant bestaat niet.");
        }
        else if (template.VariantsEnabled)
        {
            throw new DomainValidationException("variantId", "Kies een variant: de voorraad wordt per variant bijgehouden.");
        }

        var open = await _dbContext.ReorderProposals
            .AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.TemplateId == template.Id
                           && p.VariantId == request.VariantId && OpenStatuses.Contains(p.Status), cancellationToken);
        if (open)
        {
            throw new DomainValidationException("Er staat al een open bestelvoorstel voor dit artikel.");
        }

        var current = variant?.CurrentStock ?? template.CurrentStock;
        var suggested = request.Quantity
            ?? IReorderProposalService.SuggestQuantity(current, template.TargetStockLevel, template.ReorderQuantity);
        if (suggested < 1)
        {
            throw new DomainValidationException("quantity", "De voorgestelde hoeveelheid moet minstens 1 zijn.");
        }

        var proposal = new ReorderProposal
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CreatedByUserId = _currentUser.CurrentUserId,
            TemplateId = template.Id,
            VariantId = request.VariantId,
            CurrentStockSnapshot = current,
            TargetStockSnapshot = template.TargetStockLevel,
            SuggestedQuantity = suggested,
            Notes = Trim(request.Notes),
        };
        _dbContext.Add(proposal);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, proposal.Id.ToString(), "Created", null,
            new { template.Name, variant?.Label, proposal.SuggestedQuantity, Current = current }, cancellationToken);
        var label = variant is null ? template.Name : $"{template.Name} — {variant.Label}";
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.InventoryReorderManage, "inventory_reorder_proposed", "Bestelvoorstel",
            $"Bestelvoorstel voor {label}: {suggested} {template.Unit ?? "stuks"} (voorraad {current}).",
            "/inventory", cancellationToken,
            new NotificationOptions(DedupeKey: $"reorder:{proposal.Id}"));
        return (await MapAsync([proposal], cancellationToken))[0];
    }

    public async Task<ReorderProposalDto?> ChangeStatusAsync(
        Guid id, ReorderProposalStatusRequest request, CancellationToken cancellationToken)
    {
        var proposal = await _dbContext.ReorderProposals
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (proposal is null)
        {
            return null;
        }

        if (!CanTransition(proposal.Status, request.Status))
        {
            throw new DomainValidationException($"Overgang van {proposal.Status} naar {request.Status} is niet toegestaan.");
        }

        var old = new { proposal.Status, proposal.ApprovedQuantity };
        proposal.Status = request.Status;
        proposal.Notes = Trim(request.Notes) ?? proposal.Notes;
        if (request.Status == ReorderProposalStatus.Approved)
        {
            proposal.ApprovedQuantity = request.ApprovedQuantity ?? proposal.SuggestedQuantity;
            proposal.ApprovedByUserId = _currentUser.CurrentUserId;
            if (proposal.ApprovedQuantity < 1)
            {
                throw new DomainValidationException("approvedQuantity", "De goedgekeurde hoeveelheid moet minstens 1 zijn.");
            }
        }

        if (request.Status is ReorderProposalStatus.Dismissed or ReorderProposalStatus.Completed)
        {
            proposal.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, proposal.Id.ToString(), "StatusChanged", old,
            new { proposal.Status, proposal.ApprovedQuantity }, cancellationToken);
        if (request.Status is ReorderProposalStatus.Dismissed or ReorderProposalStatus.Completed)
        {
            await _notificationService.ResolveByDedupeKeyAsync($"reorder:{proposal.Id}", cancellationToken);
        }

        return (await MapAsync([proposal], cancellationToken))[0];
    }

    /// <summary>Proposed → Reviewed/Approved/Dismissed · Reviewed → Approved/Dismissed ·
    /// Approved → Ordered/Dismissed · Ordered → Completed. Dismissed/Completed are final.</summary>
    private static bool CanTransition(ReorderProposalStatus from, ReorderProposalStatus to) => (from, to) switch
    {
        (ReorderProposalStatus.Proposed, ReorderProposalStatus.Reviewed) => true,
        (ReorderProposalStatus.Proposed, ReorderProposalStatus.Approved) => true,
        (ReorderProposalStatus.Proposed, ReorderProposalStatus.Dismissed) => true,
        (ReorderProposalStatus.Reviewed, ReorderProposalStatus.Approved) => true,
        (ReorderProposalStatus.Reviewed, ReorderProposalStatus.Dismissed) => true,
        (ReorderProposalStatus.Approved, ReorderProposalStatus.Ordered) => true,
        (ReorderProposalStatus.Approved, ReorderProposalStatus.Dismissed) => true,
        (ReorderProposalStatus.Ordered, ReorderProposalStatus.Completed) => true,
        _ => false,
    };

    private async Task<IReadOnlyList<ReorderProposalDto>> MapAsync(
        IReadOnlyList<ReorderProposal> proposals, CancellationToken cancellationToken)
    {
        var templateIds = proposals.Select(p => p.TemplateId).Distinct().ToList();
        var names = await _dbContext.IssuedItemTemplates.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.TenantId == _tenantContext.TenantId && templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var variantIds = proposals.Where(p => p.VariantId.HasValue).Select(p => p.VariantId!.Value).Distinct().ToList();
        var variantLabels = await _dbContext.IssuedItemVariants.AsNoTracking().IgnoreQueryFilters()
            .Where(v => v.TenantId == _tenantContext.TenantId && variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Label, cancellationToken);
        return proposals
            .Select(p => new ReorderProposalDto(
                p.Id, p.TemplateId, p.VariantId,
                names.GetValueOrDefault(p.TemplateId, "(verwijderd artikel)"),
                p.VariantId is { } variantId ? variantLabels.GetValueOrDefault(variantId) : null,
                p.CurrentStockSnapshot, p.TargetStockSnapshot, p.SuggestedQuantity, p.ApprovedQuantity,
                p.Status, p.Notes, p.CreatedByUserId, p.ApprovedByUserId, p.CreatedAt, p.ResolvedAt))
            .ToList();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
