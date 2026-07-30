using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface IPortalAnnouncementService
{
    /// <summary>Admin listing — every announcement regardless of window/active flag.</summary>
    Task<IReadOnlyList<PortalAnnouncementDto>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>Portal listing — only announcements currently inside their active window, newest first.</summary>
    Task<IReadOnlyList<PortalAnnouncementDto>> ListActiveAsync(CancellationToken cancellationToken);

    Task<PortalAnnouncementDto?> CreateAsync(SavePortalAnnouncementRequest request, CancellationToken cancellationToken);
    Task<PortalAnnouncementDto?> UpdateAsync(Guid id, SavePortalAnnouncementRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Broadcast notices shown in the customer portal dashboard/nav. No per-customer targeting in v1.</summary>
public class PortalAnnouncementService : IPortalAnnouncementService
{
    private const string EntityType = "PortalAnnouncement";
    /// <summary>Mirror PortalAnnouncementConfiguration's HasMaxLength calls — validated before the
    /// insert/update so an over-length value fails as a clean 400, never an unhandled DB error.</summary>
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 4000;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public PortalAnnouncementService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PortalAnnouncementDto>> ListAllAsync(CancellationToken cancellationToken) =>
        await _dbContext.PortalAnnouncements.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => Map(a))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalAnnouncementDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return await _dbContext.PortalAnnouncements.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.IsActive
                && (a.ActiveFrom == null || a.ActiveFrom <= now)
                && (a.ActiveUntil == null || a.ActiveUntil >= now))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => Map(a))
            .ToListAsync(cancellationToken);
    }

    public async Task<PortalAnnouncementDto?> CreateAsync(SavePortalAnnouncementRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var announcement = new PortalAnnouncement { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        Apply(announcement, request);
        _dbContext.Add(announcement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, announcement.Id.ToString(), "Created", null,
            new { announcement.Title, announcement.IsActive }, cancellationToken);
        return Map(announcement);
    }

    public async Task<PortalAnnouncementDto?> UpdateAsync(
        Guid id, SavePortalAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var announcement = await FindAsync(id, cancellationToken);
        if (announcement is null)
        {
            return null;
        }

        Validate(request);
        var before = new { announcement.Title, announcement.Body, announcement.ActiveFrom, announcement.ActiveUntil, announcement.IsActive };
        Apply(announcement, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, announcement.Id.ToString(), "Updated", before,
            new { announcement.Title, announcement.Body, announcement.ActiveFrom, announcement.ActiveUntil, announcement.IsActive },
            cancellationToken);
        return Map(announcement);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var announcement = await FindAsync(id, cancellationToken);
        if (announcement is null)
        {
            return false;
        }

        _dbContext.Remove(announcement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, announcement.Id.ToString(), "Deleted",
            new { announcement.Title }, null, cancellationToken);
        return true;
    }

    private static void Validate(SavePortalAnnouncementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new Common.DomainValidationException("title", "De titel is verplicht.");
        }

        if (request.Title.Length > MaxTitleLength)
        {
            throw new Common.DomainValidationException("title", $"De titel mag maximaal {MaxTitleLength} tekens bevatten.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new Common.DomainValidationException("body", "De inhoud is verplicht.");
        }

        if (request.Body.Length > MaxBodyLength)
        {
            throw new Common.DomainValidationException("body", $"De inhoud mag maximaal {MaxBodyLength} tekens bevatten.");
        }

        if (request.ActiveFrom is { } from && request.ActiveUntil is { } until && until < from)
        {
            throw new Common.DomainValidationException("activeUntil", "De einddatum moet na de startdatum liggen.");
        }
    }

    private static void Apply(PortalAnnouncement announcement, SavePortalAnnouncementRequest request)
    {
        announcement.Title = request.Title.Trim();
        announcement.Body = request.Body.Trim();
        announcement.ActiveFrom = request.ActiveFrom;
        announcement.ActiveUntil = request.ActiveUntil;
        announcement.IsActive = request.IsActive;
    }

    private static PortalAnnouncementDto Map(PortalAnnouncement a) =>
        new(a.Id, a.Title, a.Body, a.ActiveFrom, a.ActiveUntil, a.IsActive);

    private Task<PortalAnnouncement?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.PortalAnnouncements.FirstOrDefaultAsync(a => a.TenantId == _tenantContext.TenantId && a.Id == id, cancellationToken);
}
