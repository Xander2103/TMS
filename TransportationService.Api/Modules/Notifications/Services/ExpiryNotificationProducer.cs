using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Notifications.Services;

/// <summary>
/// Produces qualification/medical/fleet-document expiry events. Deliberately free of
/// ITenantContext so the hosted service can sweep every tenant (a throwaway per-tenant
/// ITenantContext feeds the NotificationEventService, same pattern MessageDispatcher and the
/// other background producers use). De-duplication is authoritative via
/// <see cref="ReminderDispatchLog"/> (same table HrReminderProducer uses): the dedupe key embeds
/// a rolling 7-day bucket, so a still-expiring item naturally re-fires once the bucket rolls over
/// — the same "at most once per ~7 days" behaviour the old #fragment-on-LinkPath scan gave, without
/// depending on which/whether any recipient actually received a Notification row (preferences,
/// permission holders and channels can all vary — the dedupe gate must not depend on any of them).
/// Publication itself is delegated to NotificationEventService (Phase 6); this producer's own job
/// is purely the domain query (who/what is expiring) and the dedupe gate.
/// </summary>
public class ExpiryNotificationProducer
{
    private const int DefaultWarningDays = 30;
    private const int DedupeWindowDays = 7;

    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public ExpiryNotificationProducer(TransportationDbContext dbContext, TimeProvider timeProvider, ILogger? logger = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    private INotificationEventService BuildEventService(Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var messageOutbox = new MessageOutboxService(_dbContext, tenant, _timeProvider);
        var notifications = new NotificationService(_dbContext, tenant, currentUser, _timeProvider);
        var communication = new CustomerCommunicationService(_dbContext, tenant, new AuditService(_dbContext, tenant, currentUser));
        return new NotificationEventService(
            _dbContext, tenant, messageOutbox, notifications, communication,
            NullLogger<NotificationEventService>.Instance);
    }

    /// <summary>Fire-and-forget: a publish failure must never abort the sweep for other rows.</summary>
    private async Task PublishSafeAsync(
        INotificationEventService events, string eventKey, NotificationEventContext context, CancellationToken cancellationToken)
    {
        try
        {
            await events.PublishAsync(eventKey, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Notification event '{EventKey}' failed to publish during the expiry sweep.", eventKey);
        }
    }

    public async Task ProduceForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var bucket = today.DayNumber / DedupeWindowDays;

        var warningDays = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (int?)s.QualificationExpiryWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultWarningDays;

        // Configurable lead times per qualification type (fallback to the tenant default).
        var qualificationPolicies = await _dbContext.ExpiryReminderPolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive
                        && p.TargetKind == Modules.Hr.Entities.ExpiryReminderTargetKind.QualificationType)
            .Select(p => new { p.TargetCode, p.LeadTimeDays })
            .ToListAsync(cancellationToken);
        var leadByTypeCode = qualificationPolicies
            .Where(p => p.TargetCode != "*")
            .ToDictionary(p => p.TargetCode, p => p.LeadTimeDays, StringComparer.OrdinalIgnoreCase);
        var wildcardLead = qualificationPolicies.FirstOrDefault(p => p.TargetCode == "*")?.LeadTimeDays;
        var maxHorizon = today.AddDays(Math.Max(warningDays,
            Math.Max(wildcardLead ?? 0, leadByTypeCode.Count > 0 ? leadByTypeCode.Values.Max() : 0)));

        var events = BuildEventService(tenantId);
        var sentKeys = (await _dbContext.ReminderDispatchLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .Select(l => l.DedupeKey)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // Qualifications: warn the employee (own user) — split personnel_medical_expiry (medical
        // fitness) from personnel_qualification_expiry (everything else) so the two are
        // separately configurable. The broad maxHorizon prefilters; the per-type lead time is
        // applied precisely below.
        var expiringQualifications = await (
                from q in _dbContext.EmployeeQualifications.AsNoTracking()
                where q.TenantId == tenantId
                      && (q.Status == QualificationStatus.Valid || q.Status == QualificationStatus.ExpiringSoon)
                      && q.ExpiryDate != null && q.ExpiryDate <= maxHorizon && q.ExpiryDate >= today.AddDays(-7)
                join t in _dbContext.QualificationTypes.AsNoTracking() on q.QualificationTypeId equals t.Id
                join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId)
                    on q.EmployeeId equals e.Id
                select new { q.Id, q.ExpiryDate, TypeName = t.Name, TypeCode = t.Code, e.FirstName, e.LastName, EmployeeId = e.Id })
            .ToListAsync(cancellationToken);

        foreach (var qualification in expiringQualifications)
        {
            // Apply the effective lead time for this qualification type.
            var lead = leadByTypeCode.GetValueOrDefault(qualification.TypeCode, wildcardLead ?? warningDays);
            if (qualification.ExpiryDate > today.AddDays(lead))
            {
                continue;
            }

            // Only warn while a linked (active) user account exists — matches the pre-Phase-6
            // behaviour of skipping (not claiming) employees without portal access.
            var hasUser = await _dbContext.Users.AsNoTracking()
                .AnyAsync(u => u.TenantId == tenantId && u.EmployeeId == qualification.EmployeeId && u.IsActive, cancellationToken);
            if (!hasUser)
            {
                continue;
            }

            var dedupeKey = $"qualification_expiring:{qualification.Id}:{bucket}";
            if (!Claim(sentKeys, dedupeKey))
            {
                continue;
            }

            var eventKey = string.Equals(qualification.TypeCode, QualificationTypeCodes.MedicalFitness, StringComparison.OrdinalIgnoreCase)
                ? MessageKinds.PersonnelMedicalExpiry
                : MessageKinds.PersonnelQualificationExpiry;
            await PublishSafeAsync(events, eventKey, new NotificationEventContext(
                "EmployeeQualification", qualification.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["employeeName"] = $"{qualification.FirstName} {qualification.LastName}",
                    ["qualification"] = qualification.TypeName,
                    ["expiryDate"] = qualification.ExpiryDate!.Value.ToString("dd-MM-yyyy"),
                })
            {
                EmployeeId = qualification.EmployeeId,
                LinkPath = "/portal/qualifications",
                InAppTitle = "Kwalificatie vervalt binnenkort",
                InAppMessage = $"{qualification.TypeName} vervalt op {qualification.ExpiryDate:dd-MM-yyyy}.",
            }, cancellationToken);

            await LogDispatchAsync(tenantId, dedupeKey, "qualification_expiring", cancellationToken);
        }

        // Fleet documents: one publish per document; NotificationEventService fans out to every
        // configured recipient (fleet_documents.view holders by default).
        var expiringDocuments = await _dbContext.FleetDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ExpiryDate != null
                        && d.ExpiryDate <= today.AddDays(DefaultWarningDays) && d.ExpiryDate >= today.AddDays(-7))
            .Select(d => new { d.Id, d.ExpiryDate, d.DocumentType, d.VehicleId, d.TrailerId })
            .ToListAsync(cancellationToken);

        foreach (var document in expiringDocuments)
        {
            var dedupeKey = $"document_expiring:{document.Id}:{bucket}";
            if (!Claim(sentKeys, dedupeKey))
            {
                continue;
            }

            var target = document.VehicleId is not null ? "voertuig" : "oplegger";
            await PublishSafeAsync(events, MessageKinds.FleetDocumentExpiry, new NotificationEventContext(
                "FleetDocument", document.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["target"] = target,
                    ["documentType"] = document.DocumentType.ToString(),
                    ["expiryDate"] = document.ExpiryDate!.Value.ToString("dd-MM-yyyy"),
                })
            {
                LinkPath = document.VehicleId is { } vehicleId ? $"/vehicles/{vehicleId}?tab=documenten" : "/trailers",
                InAppTitle = "Vlootdocument vervalt binnenkort",
                InAppMessage = $"{document.DocumentType} van een {target} vervalt op {document.ExpiryDate:dd-MM-yyyy}.",
            }, cancellationToken);

            await LogDispatchAsync(tenantId, dedupeKey, "document_expiring", cancellationToken);
        }

        // Tank cards: staged 90/30/7-day reminders (HR maturity wave, task 8). Each stage has its
        // own dedupe key (no rolling bucket — a stage fires exactly once, ever, per card). When a
        // card is first observed already inside multiple stages at once (e.g. seeded 6 days before
        // expiry: 90/30/7 all due simultaneously) we still claim every due stage's key so none of
        // them fire later, but only actually publish the single most urgent (tightest) one —
        // quieter than bursting three notifications for the same card in one sweep.
        var expiringCards = await _dbContext.TankCards.AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsBlocked && c.ValidUntil != null
                        && c.ValidUntil <= today.AddDays(TankCardExpiryStages[0]) && c.ValidUntil >= today.AddDays(-7))
            .Select(c => new { c.Id, c.ValidUntil, c.InternalName, c.CardNumber })
            .ToListAsync(cancellationToken);

        foreach (var card in expiringCards)
        {
            var daysRemaining = card.ValidUntil!.Value.DayNumber - today.DayNumber;
            var dueStages = TankCardExpiryStages.Where(stage => daysRemaining <= stage).ToList();
            if (dueStages.Count == 0)
            {
                continue;
            }

            var newlyClaimedStages = new List<int>();
            foreach (var stage in dueStages)
            {
                if (Claim(sentKeys, $"tankcard_expiry:{card.Id}:{stage}"))
                {
                    newlyClaimedStages.Add(stage);
                }
            }

            if (newlyClaimedStages.Count == 0)
            {
                continue;
            }

            foreach (var stage in newlyClaimedStages)
            {
                await LogDispatchAsync(tenantId, $"tankcard_expiry:{card.Id}:{stage}", "tankcard_expiring", cancellationToken);
            }

            var tightestStage = newlyClaimedStages.Min();
            var cardLabel = !string.IsNullOrWhiteSpace(card.InternalName) ? card.InternalName! : MaskCardNumber(card.CardNumber);
            var expiryDate = card.ValidUntil!.Value.ToString("dd-MM-yyyy");
            await PublishSafeAsync(events, MessageKinds.TankCardExpiry, new NotificationEventContext(
                "TankCard", card.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["cardLabel"] = cardLabel,
                    ["expiryDate"] = expiryDate,
                    ["stage"] = TankCardStageLabel(tightestStage),
                })
            {
                LinkPath = "/tank-cards",
                InAppTitle = "Tankkaart vervalt binnenkort",
                InAppMessage = $"Tankkaart {cardLabel} vervalt op {expiryDate}.",
            }, cancellationToken);
        }
    }

    /// <summary>Lead times (days before <c>ValidUntil</c>) at which a tank card's staged expiry
    /// reminder fires; ordered widest-first so the horizon prefilter can use the max.</summary>
    private static readonly int[] TankCardExpiryStages = [90, 30, 7];

    private static string TankCardStageLabel(int stageDays) => stageDays switch
    {
        90 => "3 maanden",
        30 => "1 maand",
        7 => "1 week",
        _ => $"{stageDays} dagen",
    };

    /// <summary>Mirrors the frontend's maskCardNumber (features/tank-cards/types.ts): only the
    /// last 4 characters survive, prefixed with a bullet mask.</summary>
    private static string MaskCardNumber(string cardNumber)
    {
        var digits = cardNumber.Replace(" ", string.Empty);
        return digits.Length <= 4 ? cardNumber : $"•••• {digits[^4..]}";
    }

    /// <summary>Reserves a dedupe key in-memory (mirrors HrReminderProducer.Claim); the caller
    /// persists it via <see cref="LogDispatchAsync"/> only once publication was attempted.</summary>
    private static bool Claim(HashSet<string> sentKeys, string key) => sentKeys.Add(key);

    private async Task LogDispatchAsync(Guid tenantId, string dedupeKey, string kind, CancellationToken cancellationToken)
    {
        _dbContext.Add(new ReminderDispatchLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DedupeKey = dedupeKey,
            Kind = kind,
            SentAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
