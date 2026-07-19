using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Modules.Notifications.Services;

/// <summary>
/// Produces qualification_expiring / document_expiring notifications. Deliberately free of
/// ITenantContext so the hosted service can sweep every tenant; per user+type+link at most
/// one notification per dedupe window, so daily sweeps never spam.
/// </summary>
public class ExpiryNotificationProducer
{
    private const int DefaultWarningDays = 30;
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromDays(7);

    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ExpiryNotificationProducer(TransportationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task ProduceForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var warningDays = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (int?)s.QualificationExpiryWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultWarningDays;
        var horizon = today.AddDays(warningDays);

        var added = false;

        // Qualifications: warn the employee (own user) and HR document viewers.
        var expiringQualifications = await (
                from q in _dbContext.EmployeeQualifications.AsNoTracking()
                where q.TenantId == tenantId
                      && (q.Status == QualificationStatus.Valid || q.Status == QualificationStatus.ExpiringSoon)
                      && q.ExpiryDate != null && q.ExpiryDate <= horizon && q.ExpiryDate >= today.AddDays(-7)
                join t in _dbContext.QualificationTypes.AsNoTracking() on q.QualificationTypeId equals t.Id
                join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId)
                    on q.EmployeeId equals e.Id
                select new { q.Id, q.ExpiryDate, TypeName = t.Name, e.FirstName, e.LastName, EmployeeId = e.Id })
            .ToListAsync(cancellationToken);

        foreach (var qualification in expiringQualifications)
        {
            var userId = await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.EmployeeId == qualification.EmployeeId && u.IsActive)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (userId is not { } recipient)
            {
                continue;
            }

            var linkPath = "/portal/qualifications";
            var dedupeKey = $"qualification_expiring:{qualification.Id}";
            if (await AlreadyNotifiedAsync(tenantId, recipient, dedupeKey, now, cancellationToken))
            {
                continue;
            }

            _dbContext.Add(Build(tenantId, recipient, "qualification_expiring", dedupeKey,
                "Kwalificatie vervalt binnenkort",
                $"{qualification.TypeName} vervalt op {qualification.ExpiryDate:dd-MM-yyyy}.",
                linkPath));
            added = true;
        }

        // Fleet documents: warn fleet-document viewers per document.
        var expiringDocuments = await _dbContext.FleetDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ExpiryDate != null
                        && d.ExpiryDate <= today.AddDays(DefaultWarningDays) && d.ExpiryDate >= today.AddDays(-7))
            .Select(d => new { d.Id, d.ExpiryDate, d.DocumentType, d.VehicleId, d.TrailerId })
            .ToListAsync(cancellationToken);

        if (expiringDocuments.Count > 0)
        {
            var viewers = await (from ur in _dbContext.UserRoles.AsNoTracking()
                                 join u in _dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive)
                                     on ur.UserId equals u.Id
                                 join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive)
                                     on ur.RoleId equals r.Id
                                 join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
                                 join p in _dbContext.Permissions.AsNoTracking().Where(p => p.Code == "fleet_documents.view")
                                     on rp.PermissionId equals p.Id
                                 select u.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var document in expiringDocuments)
            {
                var target = document.VehicleId is not null ? "voertuig" : "oplegger";
                var dedupeKey = $"document_expiring:{document.Id}";
                foreach (var viewer in viewers)
                {
                    if (await AlreadyNotifiedAsync(tenantId, viewer, dedupeKey, now, cancellationToken))
                    {
                        continue;
                    }

                    _dbContext.Add(Build(tenantId, viewer, "document_expiring", dedupeKey,
                        "Vlootdocument vervalt binnenkort",
                        $"{document.DocumentType} van een {target} vervalt op {document.ExpiryDate:dd-MM-yyyy}.",
                        document.VehicleId is { } vehicleId ? $"/vehicles/{vehicleId}?tab=documenten" : "/trailers"));
                    added = true;
                }
            }
        }

        if (added)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The producing entity's id rides as a "#id" fragment on LinkPath; a notification for the
    /// same user + entity inside the window (persisted or pending in this unit of work)
    /// suppresses a repeat.
    /// </summary>
    private async Task<bool> AlreadyNotifiedAsync(
        Guid tenantId, Guid userId, string dedupeKey, DateTime now, CancellationToken cancellationToken)
    {
        var marker = "#" + dedupeKey[(dedupeKey.IndexOf(':') + 1)..];
        var since = now - DedupeWindow;

        var persisted = await _dbContext.Notifications.AsNoTracking()
            .AnyAsync(n => n.TenantId == tenantId && n.UserId == userId
                           && n.CreatedAt >= since
                           && n.LinkPath != null && n.LinkPath.EndsWith(marker), cancellationToken);
        if (persisted)
        {
            return true;
        }

        return _dbContext.ChangeTracker.Entries<Notification>().Any(e =>
            e.Entity.TenantId == tenantId && e.Entity.UserId == userId
            && e.Entity.LinkPath != null && e.Entity.LinkPath.EndsWith(marker));
    }

    private static Notification Build(
        Guid tenantId, Guid userId, string type, string dedupeKey, string title, string message, string linkPath)
    {
        var (category, severity) = NotificationTypeCatalog.Resolve(type);
        return new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Category = category,
            Severity = severity,
            Title = title,
            Message = message,
            // The entity id rides as a fragment so repeated sweeps can recognise it.
            LinkPath = $"{linkPath}#{dedupeKey[(dedupeKey.IndexOf(':') + 1)..]}",
        };
    }
}
