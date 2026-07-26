using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Services;

public interface IPricingAdminService
{
    Task<IReadOnlyList<PricingZoneDto>> ListZonesAsync(CancellationToken cancellationToken);
    Task<PricingZoneDto> CreateZoneAsync(SavePricingZoneRequest request, CancellationToken cancellationToken);
    Task<PricingZoneDto?> UpdateZoneAsync(Guid id, SavePricingZoneRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteZoneAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PricingAgreementDto>> ListAgreementsAsync(Guid? customerId, CancellationToken cancellationToken);
    Task<PricingAgreementDto> CreateAgreementAsync(SavePricingAgreementRequest request, CancellationToken cancellationToken);
    Task<PricingAgreementDto?> UpdateAgreementAsync(Guid id, SavePricingAgreementRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAgreementAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PricingAgreementAssignmentDto>?> ListAssignmentsAsync(Guid agreementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PricingAgreementAssignmentDto>?> SaveAssignmentsAsync(
        Guid agreementId, IReadOnlyList<SavePricingAssignmentRequest> requests, CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceRuleDto>> ListRulesAsync(Guid? customerId, CancellationToken cancellationToken);
    Task<PriceRuleDto> CreateRuleAsync(SavePriceRuleRequest request, CancellationToken cancellationToken);
    Task<PriceRuleDto?> UpdateRuleAsync(Guid id, SavePriceRuleRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRuleAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceOptionDto>> ListServiceOptionsAsync(bool includeInactive, bool forOrderEntry, CancellationToken cancellationToken);
    Task<ServiceOptionDto> CreateServiceOptionAsync(SaveServiceOptionRequest request, CancellationToken cancellationToken);
    Task<ServiceOptionDto?> UpdateServiceOptionAsync(Guid id, SaveServiceOptionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteServiceOptionAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerPricingConfigDto?> GetCustomerConfigAsync(Guid customerId, CancellationToken cancellationToken);
    Task<CustomerPricingConfigDto?> SaveCustomerConfigAsync(Guid customerId, SaveCustomerPricingConfigRequest request, CancellationToken cancellationToken);
}

public class PricingAdminService : IPricingAdminService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public PricingAdminService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private Guid TenantId => _tenantContext.TenantId;

    // --- Zones ---

    public async Task<IReadOnlyList<PricingZoneDto>> ListZonesAsync(CancellationToken cancellationToken)
    {
        var zones = await _dbContext.PricingZones.AsNoTracking()
            .Include(z => z.Areas)
            .Where(z => z.TenantId == TenantId)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .ToListAsync(cancellationToken);
        return zones.Select(MapZone).ToList();
    }

    public async Task<PricingZoneDto> CreateZoneAsync(SavePricingZoneRequest request, CancellationToken cancellationToken)
    {
        ValidateZone(request);
        await EnsureZoneCodeFreeAsync(request.Code, null, cancellationToken);

        var zone = new PricingZone { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyZone(zone, request);
        _dbContext.PricingZones.Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingZone", zone.Id.ToString(), "Created", null, new { zone.Code, zone.Name }, cancellationToken);
        return MapZone(zone);
    }

    public async Task<PricingZoneDto?> UpdateZoneAsync(Guid id, SavePricingZoneRequest request, CancellationToken cancellationToken)
    {
        var zone = await _dbContext.PricingZones.Include(z => z.Areas)
            .FirstOrDefaultAsync(z => z.TenantId == TenantId && z.Id == id, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        ValidateZone(request);
        await EnsureZoneCodeFreeAsync(request.Code, id, cancellationToken);

        _dbContext.PricingZoneAreas.RemoveRange(zone.Areas);
        zone.Areas.Clear();
        ApplyZone(zone, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingZone", zone.Id.ToString(), "Updated", null, new { zone.Code, zone.Name }, cancellationToken);
        return MapZone(zone);
    }

    public async Task<bool> DeleteZoneAsync(Guid id, CancellationToken cancellationToken)
    {
        var zone = await _dbContext.PricingZones.FirstOrDefaultAsync(z => z.TenantId == TenantId && z.Id == id, cancellationToken);
        if (zone is null)
        {
            return false;
        }

        var inUse = await _dbContext.PriceRules.AnyAsync(r => r.TenantId == TenantId && r.ZoneId == id, cancellationToken);
        if (inUse)
        {
            throw new DomainValidationException("Deze zone wordt gebruikt door prijsregels. Verwijder of wijzig die eerst.");
        }

        _dbContext.Remove(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingZone", zone.Id.ToString(), "Deleted", new { zone.Code }, null, cancellationToken);
        return true;
    }

    // --- Pricing agreements (rate cards) ---

    public async Task<IReadOnlyList<PricingAgreementDto>> ListAgreementsAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        var agreements = await _dbContext.PricingAgreements.AsNoTracking()
            .Include(a => a.Surcharges)
            .Include(a => a.Modifiers)
            .Where(a => a.TenantId == TenantId)
            .Where(a => customerId == null ? a.CustomerId == null : a.CustomerId == customerId)
            .OrderByDescending(a => a.EffectiveFrom).ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);
        return await MapAgreementsAsync(agreements, cancellationToken);
    }

    public async Task<PricingAgreementDto> CreateAgreementAsync(SavePricingAgreementRequest request, CancellationToken cancellationToken)
    {
        await ValidateAgreementAsync(request, null, cancellationToken);
        var agreement = new PricingAgreement { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyAgreement(agreement, request);
        _dbContext.PricingAgreements.Add(agreement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", agreement.Id.ToString(), "Created", null,
            new { agreement.Name, agreement.CustomerId, agreement.EffectiveFrom }, cancellationToken);
        return (await MapAgreementsAsync([agreement], cancellationToken))[0];
    }

    public async Task<PricingAgreementDto?> UpdateAgreementAsync(Guid id, SavePricingAgreementRequest request, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.Include(a => a.Surcharges).Include(a => a.Modifiers)
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        await ValidateAgreementAsync(request, id, cancellationToken);
        // Surcharges/modifiers are replaced wholesale; the agreement is the aggregate root.
        _dbContext.RemoveRange(agreement.Surcharges);
        agreement.Surcharges = [];
        _dbContext.RemoveRange(agreement.Modifiers);
        agreement.Modifiers = [];
        ApplyAgreement(agreement, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", agreement.Id.ToString(), "Updated", null,
            new { agreement.Name, agreement.CustomerId, agreement.EffectiveFrom }, cancellationToken);
        return (await MapAgreementsAsync([agreement], cancellationToken))[0];
    }

    public async Task<bool> DeleteAgreementAsync(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return false;
        }

        var inUse = await _dbContext.PriceRules.AnyAsync(r => r.TenantId == TenantId && r.AgreementId == id, cancellationToken);
        if (inUse)
        {
            throw new DomainValidationException(
                "Deze prijsafspraak bevat nog tariefregels. Verwijder of verplaats die eerst.");
        }

        var dependentNames = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.BaseAgreementId == id)
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);
        if (dependentNames.Count > 0)
        {
            throw new DomainValidationException(
                "Deze tabel is de basis voor " + string.Join(", ", dependentNames.Select(n => $"'{n}'"))
                + ". Verwijder eerst de afgeleide tabellen.");
        }

        _dbContext.Remove(agreement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", agreement.Id.ToString(), "Deleted", new { agreement.Name }, null, cancellationToken);
        return true;
    }

    /// <summary>
    /// Walks the proposed base-chain from <paramref name="baseAgreementId"/> upward: revisiting the
    /// agreement being saved (<paramref name="agreementId"/>, null for a new agreement) or any node
    /// already seen is a cycle; a chain longer than 3 hops from the saved agreement is too deep.
    /// </summary>
    private async Task ValidateDerivationChainAsync(Guid? agreementId, Guid baseAgreementId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        Guid? current = baseAgreementId;
        var hops = 1;
        while (current is not null)
        {
            if (current == agreementId || !visited.Add(current.Value))
            {
                throw new DomainValidationException("baseAgreementId", "Circulaire verwijzing tussen tarieventabellen.");
            }

            if (hops > 3)
            {
                throw new DomainValidationException("baseAgreementId", "Maximale afleidingsdiepte (3) overschreden.");
            }

            var next = await _dbContext.PricingAgreements.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.Id == current.Value)
                .Select(a => (Guid?)a.BaseAgreementId)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is null)
            {
                break;
            }

            hops++;
            current = next;
        }
    }

    private async Task ValidateAgreementAsync(SavePricingAgreementRequest request, Guid? agreementId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        if (request.EffectiveUntil is { } until && until < request.EffectiveFrom)
        {
            throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
        }

        if (request.MinimumAmount is < 0)
        {
            throw new DomainValidationException("minimumAmount", "Het minimumbedrag mag niet negatief zijn.");
        }

        if (request.MaximumAmount is < 0)
        {
            throw new DomainValidationException("maximumAmount", "Het maximumbedrag mag niet negatief zijn.");
        }

        if (request.MaximumAmount is { } maximum && request.MinimumAmount is { } minimum && maximum < minimum)
        {
            throw new DomainValidationException("maximumAmount", "Het maximumbedrag moet minstens het minimumbedrag zijn.");
        }

        if (request.IsShared && request.CustomerId is not null)
        {
            throw new DomainValidationException("isShared",
                "Een herbruikbare tabel is niet gekoppeld aan één klant; koppel klanten via de klantkoppelingen.");
        }

        foreach (var surcharge in request.Surcharges ?? [])
        {
            if (string.IsNullOrWhiteSpace(surcharge.Name))
            {
                throw new DomainValidationException("surcharges", "Elke toeslag heeft een naam nodig.");
            }

            if (surcharge.Kind is not (SurchargeKind.Percent or SurchargeKind.Fixed))
            {
                throw new DomainValidationException("surcharges",
                    "Een automatische toeslag op een prijsafspraak is een percentage of een vast bedrag.");
            }
        }

        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == TenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        foreach (var modifier in request.Modifiers ?? [])
        {
            if (string.IsNullOrWhiteSpace(modifier.Name))
            {
                throw new DomainValidationException("modifiers", "Elke aanpassing heeft een naam nodig.");
            }

            if ((modifier.Percent is null) == (modifier.FixedAmount is null))
            {
                throw new DomainValidationException("modifiers", "Kies per regel een percentage óf een vast bedrag.");
            }
        }

        if (request.BaseAgreementId is { } baseAgreementId)
        {
            // A derived table has no rules of its own — reject converting a table that already
            // carries rules, and reject targeting one that is itself already derived's own rules
            // (that half is enforced in ValidateRuleAsync, on rule creation).
            var hasOwnRules = agreementId is { } existingId
                && await _dbContext.PriceRules.AnyAsync(r => r.TenantId == TenantId && r.AgreementId == existingId, cancellationToken);
            if (hasOwnRules)
            {
                throw new DomainValidationException("baseAgreementId",
                    "Deze tabel heeft eigen prijsregels en kan niet afgeleid worden.");
            }

            var baseAgreement = await _dbContext.PricingAgreements.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == baseAgreementId, cancellationToken);
            if (baseAgreement is null)
            {
                throw new InvalidTenantReferenceException("basistabel");
            }

            // A shared/company-default derived table on a private base would leak that customer's
            // prices to everyone the derived table applies to.
            if (request.CustomerId is null && baseAgreement.CustomerId is not null)
            {
                throw new DomainValidationException("baseAgreementId", "Basistabel moet een gedeelde of algemene tabel zijn.");
            }

            await ValidateDerivationChainAsync(agreementId, baseAgreementId, cancellationToken);
        }
        else if (request.Modifiers is { Count: > 0 })
        {
            throw new DomainValidationException("modifiers", "Aanpassingen zijn alleen mogelijk op een afgeleide tabel.");
        }
    }

    private void ApplyAgreement(PricingAgreement agreement, SavePricingAgreementRequest request)
    {
        agreement.CustomerId = request.CustomerId;
        agreement.Name = request.Name.Trim();
        agreement.EffectiveFrom = request.EffectiveFrom;
        agreement.EffectiveUntil = request.EffectiveUntil;
        agreement.IsActive = request.IsActive;
        agreement.MinimumAmount = request.MinimumAmount;
        agreement.MaximumAmount = request.MaximumAmount;
        agreement.IsShared = request.IsShared;
        agreement.Notes = Clean(request.Notes);
        agreement.BaseAgreementId = request.BaseAgreementId;
        foreach (var surcharge in request.Surcharges ?? [])
        {
            var entity = new PricingAgreementSurcharge
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                AgreementId = agreement.Id,
                Name = surcharge.Name.Trim(),
                Kind = surcharge.Kind,
                Value = surcharge.Value,
            };
            agreement.Surcharges.Add(entity);
            // Client-set Guid keys reached via a navigation are otherwise tracked as
            // existing (Modified) — mark them Added explicitly.
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }

        foreach (var modifier in request.Modifiers ?? [])
        {
            var entity = new PricingAgreementModifier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                AgreementId = agreement.Id,
                Sequence = modifier.Sequence,
                Name = modifier.Name.Trim(),
                CountryCode = string.IsNullOrWhiteSpace(modifier.CountryCode) ? null : modifier.CountryCode.Trim().ToUpperInvariant(),
                ZoneId = modifier.ZoneId,
                Percent = modifier.Percent,
                FixedAmount = modifier.FixedAmount,
            };
            agreement.Modifiers.Add(entity);
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }
    }

    private async Task<IReadOnlyList<PricingAgreementDto>> MapAgreementsAsync(
        IReadOnlyList<PricingAgreement> agreements, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = agreements.Where(a => a.CustomerId.HasValue).Select(a => a.CustomerId!.Value).Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Two extra, batched queries for the whole list — never per-agreement — to attach the
        // "assigned today" customer count/names to shared tables without N+1.
        var agreementIds = agreements.Select(a => a.Id).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeAssignments = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && agreementIds.Contains(x.AgreementId)
                        && (x.EffectiveFrom == null || x.EffectiveFrom <= today)
                        && (x.EffectiveUntil == null || x.EffectiveUntil >= today))
            .ToListAsync(cancellationToken);
        var assignedCustomerIds = activeAssignments.Select(x => x.CustomerId).Distinct().ToList();
        var assignedCustomerNames = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && assignedCustomerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var namesByAgreement = activeAssignments
            .GroupBy(x => x.AgreementId)
            .ToDictionary(g => g.Key, g => g.Select(x => assignedCustomerNames.GetValueOrDefault(x.CustomerId, "?")).OrderBy(n => n).ToList());

        var baseAgreementIds = agreements.Where(a => a.BaseAgreementId.HasValue).Select(a => a.BaseAgreementId!.Value).Distinct().ToList();
        var baseAgreementNames = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == tenantId && baseAgreementIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var modifierZoneIds = agreements.SelectMany(a => a.Modifiers)
            .Where(m => m.ZoneId.HasValue).Select(m => m.ZoneId!.Value).Distinct().ToList();
        var zoneNames = await _dbContext.PricingZones.AsNoTracking()
            .Where(z => z.TenantId == tenantId && modifierZoneIds.Contains(z.Id))
            .ToDictionaryAsync(z => z.Id, z => z.Name, cancellationToken);

        return agreements.Select(a =>
        {
            var names = namesByAgreement.GetValueOrDefault(a.Id);
            return new PricingAgreementDto(
                a.Id, a.CustomerId,
                a.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null,
                a.Name, a.Currency, a.EffectiveFrom, a.EffectiveUntil, a.IsActive,
                a.MinimumAmount, a.Notes,
                a.Surcharges.OrderBy(s => s.Name)
                    .Select(s => new PricingAgreementSurchargeDto(s.Id, s.Name, s.Kind, s.Value))
                    .ToList(),
                a.IsShared, a.MaximumAmount, names?.Count ?? 0, names,
                a.BaseAgreementId,
                a.BaseAgreementId is { } bid ? baseAgreementNames.GetValueOrDefault(bid) : null,
                a.Modifiers.OrderBy(m => m.Sequence)
                    .Select(m => new PricingAgreementModifierDto(
                        m.Id, m.Sequence, m.Name, m.CountryCode, m.ZoneId,
                        m.ZoneId is { } zid ? zoneNames.GetValueOrDefault(zid) : null,
                        m.Percent, m.FixedAmount))
                    .ToList());
        }).ToList();
    }

    // --- Pricing agreement assignments (shared tables → customers) ---

    public async Task<IReadOnlyList<PricingAgreementAssignmentDto>?> ListAssignmentsAsync(
        Guid agreementId, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        var assignments = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        return await MapAssignmentsAsync(assignments, cancellationToken);
    }

    public async Task<IReadOnlyList<PricingAgreementAssignmentDto>?> SaveAssignmentsAsync(
        Guid agreementId, IReadOnlyList<SavePricingAssignmentRequest> requests, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var agreement = await _dbContext.PricingAgreements
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        if (!agreement.IsShared)
        {
            throw new DomainValidationException("agreementId",
                "Klantkoppelingen zijn alleen mogelijk op herbruikbare tabellen.");
        }

        foreach (var request in requests)
        {
            if (request.PercentAdjustment is < -100 or > 100)
            {
                throw new DomainValidationException("percentAdjustment", "De aanpassing moet tussen -100% en 100% liggen.");
            }

            if (request.EffectiveFrom is { } from && request.EffectiveUntil is { } until && until < from)
            {
                throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
            }

            if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == tenantId && c.Id == request.CustomerId, cancellationToken))
            {
                throw new InvalidTenantReferenceException("klant");
            }
        }

        foreach (var group in requests.GroupBy(r => r.CustomerId))
        {
            var windows = group.ToList();
            for (var i = 0; i < windows.Count; i++)
            {
                for (var j = i + 1; j < windows.Count; j++)
                {
                    if (WindowsOverlap(windows[i], windows[j]))
                    {
                        throw new DomainValidationException("assignments",
                            "Deze klant heeft al een actieve koppeling in deze periode.");
                    }
                }
            }
        }

        var existing = await _dbContext.PricingAgreementAssignments
            .Where(x => x.TenantId == tenantId && x.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        var oldSnapshot = existing
            .Select(x => new { x.CustomerId, x.PercentAdjustment, x.FixedAdjustment, x.EffectiveFrom, x.EffectiveUntil })
            .ToList();
        _dbContext.PricingAgreementAssignments.RemoveRange(existing);
        foreach (var request in requests)
        {
            _dbContext.PricingAgreementAssignments.Add(new PricingAgreementAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AgreementId = agreementId,
                CustomerId = request.CustomerId,
                PercentAdjustment = request.PercentAdjustment,
                FixedAdjustment = request.FixedAdjustment,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveUntil = request.EffectiveUntil,
                Notes = Clean(request.Notes),
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreementAssignment", agreementId.ToString(), "saved",
            oldSnapshot, requests, cancellationToken);

        var saved = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        return await MapAssignmentsAsync(saved, cancellationToken);
    }

    /// <summary>Null-open windows treated as -/+ infinity; overlap = the two ranges intersect.</summary>
    private static bool WindowsOverlap(SavePricingAssignmentRequest a, SavePricingAssignmentRequest b)
    {
        var aFrom = a.EffectiveFrom ?? DateOnly.MinValue;
        var aTo = a.EffectiveUntil ?? DateOnly.MaxValue;
        var bFrom = b.EffectiveFrom ?? DateOnly.MinValue;
        var bTo = b.EffectiveUntil ?? DateOnly.MaxValue;
        return aFrom <= bTo && bFrom <= aTo;
    }

    private async Task<IReadOnlyList<PricingAgreementAssignmentDto>> MapAssignmentsAsync(
        IReadOnlyList<PricingAgreementAssignment> assignments, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = assignments.Select(x => x.CustomerId).Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return assignments
            .Select(x => new PricingAgreementAssignmentDto(
                x.Id, x.CustomerId, customers.GetValueOrDefault(x.CustomerId, "?"),
                x.PercentAdjustment, x.FixedAdjustment, x.EffectiveFrom, x.EffectiveUntil, x.Notes))
            .OrderBy(x => x.CustomerName)
            .ToList();
    }

    // --- Price rules ---

    public async Task<IReadOnlyList<PriceRuleDto>> ListRulesAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        var rules = await _dbContext.PriceRules.AsNoTracking()
            .Include(r => r.Brackets)
            .Where(r => r.TenantId == TenantId)
            .Where(r => customerId == null ? r.CustomerId == null : r.CustomerId == customerId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        return await MapRulesAsync(rules, cancellationToken);
    }

    public async Task<PriceRuleDto> CreateRuleAsync(SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        await ValidateRuleAsync(request, cancellationToken);
        var rule = new PriceRule { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyRule(rule, request);
        _dbContext.PriceRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRule", rule.Id.ToString(), "Created", null,
            new { rule.Name, rule.Basis, rule.CustomerId, rule.UnitTypeId }, cancellationToken);
        return (await MapRulesAsync([rule], cancellationToken))[0];
    }

    public async Task<PriceRuleDto?> UpdateRuleAsync(Guid id, SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PriceRules.Include(r => r.Brackets)
            .FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == id, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        await ValidateRuleAsync(request, cancellationToken);
        _dbContext.PriceRuleBrackets.RemoveRange(rule.Brackets);
        rule.Brackets.Clear();
        ApplyRule(rule, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRule", rule.Id.ToString(), "Updated", null, new { rule.Name, rule.Basis }, cancellationToken);
        return (await MapRulesAsync([rule], cancellationToken))[0];
    }

    public async Task<bool> DeleteRuleAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PriceRules.FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == id, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        _dbContext.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRule", rule.Id.ToString(), "Deleted", new { rule.Name }, null, cancellationToken);
        return true;
    }

    // --- Service options ---

    public async Task<IReadOnlyList<ServiceOptionDto>> ListServiceOptionsAsync(
        bool includeInactive, bool forOrderEntry, CancellationToken cancellationToken)
    {
        return await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == TenantId && (includeInactive || o.IsActive))
            .Where(o => !forOrderEntry || o.SelectableInOrders)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .Select(o => new ServiceOptionDto(
                o.Id, o.Code, o.Name, o.Kind, o.DefaultValue, o.IsActive, o.SortOrder,
                o.Description, o.InvoiceDescription, o.SelectableInOrders))
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceOptionDto> CreateServiceOptionAsync(SaveServiceOptionRequest request, CancellationToken cancellationToken)
    {
        ValidateOption(request);
        var duplicate = await _dbContext.ServiceOptions.AnyAsync(
            o => o.TenantId == TenantId && o.Code == request.Code.Trim().ToUpperInvariant(), cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", $"Er bestaat al een dienst met code '{request.Code}'.");
        }

        var option = new ServiceOption { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyOption(option, request);
        _dbContext.ServiceOptions.Add(option);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ServiceOption", option.Id.ToString(), "Created", null, new { option.Code, option.Name, option.Kind }, cancellationToken);
        return MapOption(option);
    }

    public async Task<ServiceOptionDto?> UpdateServiceOptionAsync(Guid id, SaveServiceOptionRequest request, CancellationToken cancellationToken)
    {
        var option = await _dbContext.ServiceOptions.FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == id, cancellationToken);
        if (option is null)
        {
            return null;
        }

        ValidateOption(request);
        ApplyOption(option, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ServiceOption", option.Id.ToString(), "Updated", null, new { option.Code, option.Name, option.Kind }, cancellationToken);
        return MapOption(option);
    }

    public async Task<bool> DeleteServiceOptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var option = await _dbContext.ServiceOptions.FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == id, cancellationToken);
        if (option is null)
        {
            return false;
        }

        _dbContext.Remove(option);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ServiceOption", option.Id.ToString(), "Deleted", new { option.Code }, null, cancellationToken);
        return true;
    }

    // --- Customer pricing configuration ---

    public async Task<CustomerPricingConfigDto?> GetCustomerConfigAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == customerId, cancellationToken))
        {
            return null;
        }

        var preferred = await _dbContext.CustomerPreferredUnits.AsNoTracking()
            .Where(u => u.TenantId == TenantId && u.CustomerId == customerId)
            .OrderByDescending(u => u.IsFavourite).ThenBy(u => u.SortOrder)
            .Join(_dbContext.UnitTypes.Where(t => t.TenantId == TenantId),
                pu => pu.UnitTypeId, ut => ut.Id,
                (pu, ut) => new CustomerPreferredUnitDto(
                    ut.Id, ut.Code, ut.Name, pu.SortOrder,
                    pu.CustomerLabel, pu.EdiCode, pu.ExcelCode, pu.IsFavourite))
            .ToListAsync(cancellationToken);

        var options = await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == TenantId && o.IsActive)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .ToListAsync(cancellationToken);
        var overrides = await _dbContext.CustomerServiceOptionPrices.AsNoTracking()
            .Where(p => p.TenantId == TenantId && p.CustomerId == customerId)
            .ToDictionaryAsync(p => p.ServiceOptionId, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var optionDtos = options
            .Select(o =>
            {
                var over = overrides.GetValueOrDefault(o.Id);
                var overrideActiveToday = over is not null
                    && (over.EffectiveFrom is null || over.EffectiveFrom <= today)
                    && (over.EffectiveUntil is null || over.EffectiveUntil >= today);
                var effectiveValue = overrideActiveToday && over!.Value is { } v ? v : o.DefaultValue;
                var source = overrideActiveToday && (over!.Value is not null || over.Disabled)
                    ? "Klanttarief"
                    : "Algemene standaard";
                return new CustomerServiceOptionPriceDto(
                    o.Id, o.Name, o.Kind, o.DefaultValue, over?.Value,
                    over?.Disabled ?? false, over?.MinimumAmount, over?.InvoiceDescription,
                    over?.EffectiveFrom, over?.EffectiveUntil,
                    effectiveValue, source);
            })
            .ToList();

        return new CustomerPricingConfigDto(preferred, optionDtos);
    }

    public async Task<CustomerPricingConfigDto?> SaveCustomerConfigAsync(
        Guid customerId, SaveCustomerPricingConfigRequest request, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == customerId, cancellationToken))
        {
            return null;
        }

        var units = request.Units;
        var unitIds = units.Select(u => u.UnitTypeId).Distinct().ToList();
        if (unitIds.Count != units.Count)
        {
            throw new DomainValidationException("units", "Eén eenheid mag maar één keer geconfigureerd worden.");
        }

        var knownUnits = await _dbContext.UnitTypes
            .CountAsync(u => u.TenantId == TenantId && unitIds.Contains(u.Id), cancellationToken);
        if (knownUnits != unitIds.Count)
        {
            throw new DomainValidationException("units", "Eén of meer eenheden bestaan niet.");
        }

        foreach (var unit in units)
        {
            if (unit.CustomerLabel is { Length: > 150 } || unit.EdiCode is { Length: > 50 } || unit.ExcelCode is { Length: > 50 })
            {
                throw new DomainValidationException("units", "Klantbenaming of externe code is te lang.");
            }
        }

        var existingPreferred = await _dbContext.CustomerPreferredUnits
            .Where(u => u.TenantId == TenantId && u.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        _dbContext.CustomerPreferredUnits.RemoveRange(existingPreferred.Where(u => !unitIds.Contains(u.UnitTypeId)));
        foreach (var unit in units)
        {
            var row = existingPreferred.FirstOrDefault(u => u.UnitTypeId == unit.UnitTypeId);
            if (row is null)
            {
                row = new CustomerPreferredUnit
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, CustomerId = customerId,
                    UnitTypeId = unit.UnitTypeId,
                };
                _dbContext.CustomerPreferredUnits.Add(row);
            }

            row.SortOrder = unit.SortOrder;
            row.CustomerLabel = Clean(unit.CustomerLabel);
            row.EdiCode = Clean(unit.EdiCode);
            row.ExcelCode = Clean(unit.ExcelCode);
            row.IsFavourite = unit.IsFavourite;
        }

        var existingPrices = await _dbContext.CustomerServiceOptionPrices
            .Where(p => p.TenantId == TenantId && p.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        foreach (var priceRequest in request.OptionPrices)
        {
            var row = existingPrices.FirstOrDefault(p => p.ServiceOptionId == priceRequest.ServiceOptionId);
            var isOverride = priceRequest.Value is not null || priceRequest.Disabled
                             || priceRequest.MinimumAmount is not null
                             || !string.IsNullOrWhiteSpace(priceRequest.InvoiceDescription)
                             || priceRequest.EffectiveFrom is not null || priceRequest.EffectiveUntil is not null;
            if (!isOverride)
            {
                if (row is not null)
                {
                    _dbContext.Remove(row); // "Algemene waarde opnieuw gebruiken"
                }

                continue;
            }

            if (row is null)
            {
                row = new CustomerServiceOptionPrice
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, CustomerId = customerId,
                    ServiceOptionId = priceRequest.ServiceOptionId,
                };
                _dbContext.CustomerServiceOptionPrices.Add(row);
            }

            row.Value = priceRequest.Value;
            row.Disabled = priceRequest.Disabled;
            row.MinimumAmount = priceRequest.MinimumAmount;
            row.InvoiceDescription = Clean(priceRequest.InvoiceDescription);
            row.EffectiveFrom = priceRequest.EffectiveFrom;
            row.EffectiveUntil = priceRequest.EffectiveUntil;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("CustomerPricingConfig", customerId.ToString(), "Updated", null,
            new { PreferredUnits = unitIds.Count, OptionPrices = request.OptionPrices.Count }, cancellationToken);
        return await GetCustomerConfigAsync(customerId, cancellationToken);
    }

    // --- Helpers ---

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateZone(SavePricingZoneRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("code", "Code en naam zijn verplicht.");
        }

        foreach (var area in request.Areas)
        {
            if (string.IsNullOrWhiteSpace(area.PostalCodeFrom) || string.IsNullOrWhiteSpace(area.PostalCodeTo))
            {
                throw new DomainValidationException("areas", "Elke postcodereeks heeft een van- en tot-waarde nodig.");
            }

            if (int.TryParse(area.PostalCodeFrom, out var from) && int.TryParse(area.PostalCodeTo, out var to) && from > to)
            {
                throw new DomainValidationException("areas", "De van-postcode moet vóór de tot-postcode liggen.");
            }
        }
    }

    private async Task EnsureZoneCodeFreeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var duplicate = await _dbContext.PricingZones.AnyAsync(
            z => z.TenantId == TenantId && z.Code == normalized && z.Id != exceptId, cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", $"Er bestaat al een zone met code '{normalized}'.");
        }
    }

    private void ApplyZone(PricingZone zone, SavePricingZoneRequest request)
    {
        zone.Code = request.Code.Trim().ToUpperInvariant();
        zone.Name = request.Name.Trim();
        zone.IsActive = request.IsActive;
        zone.SortOrder = request.SortOrder;
        foreach (var area in request.Areas)
        {
            zone.Areas.Add(new PricingZoneArea
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ZoneId = zone.Id,
                CountryCode = area.CountryCode.Trim().ToUpperInvariant(),
                PostalCodeFrom = area.PostalCodeFrom.Trim(),
                PostalCodeTo = area.PostalCodeTo.Trim(),
            });
        }
    }

    private async Task ValidateRuleAsync(SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        var orderMeasureBasis = request.Basis
            is PriceRuleBasis.Fixed or PriceRuleBasis.PerKm or PriceRuleBasis.PerPallet or PriceRuleBasis.PerTon
            or PriceRuleBasis.PerLoadingMeter or PriceRuleBasis.PerVolume or PriceRuleBasis.PerStop;
        if (request.UnitTypeId is null && !orderMeasureBasis && request.Basis != PriceRuleBasis.WeightBracket)
        {
            throw new DomainValidationException("unitTypeId", "Kies een eenheid (alleen order-brede regels kunnen zonder).");
        }

        if (request.MinimumQuantity is < 0 || request.QuantityRoundingStep is < 0)
        {
            throw new DomainValidationException("minimumQuantity", "Minimumduur en afrondingsstap mogen niet negatief zijn.");
        }

        if (request.Priority is < -1000 or > 1000)
        {
            throw new DomainValidationException("priority", "Prioriteit moet tussen -1000 en 1000 liggen.");
        }

        if (request.BaseAmount is < 0)
        {
            throw new DomainValidationException("baseAmount", "Het basisbedrag mag niet negatief zijn.");
        }

        if (request.MaximumAmount is <= 0)
        {
            throw new DomainValidationException("maximumAmount", "Het maximumtarief moet groter zijn dan nul.");
        }

        if (request.MinimumAmount is { } ruleMinimum && request.MaximumAmount is { } ruleMaximum && ruleMinimum > ruleMaximum)
        {
            throw new DomainValidationException("maximumAmount", "Minimumtarief kan niet hoger zijn dan het maximumtarief.");
        }

        var hasOversize = request.OversizeLengthCm is not null || request.OversizeWidthCm is not null
                          || request.OversizeBillableFactor is not null;
        if (hasOversize)
        {
            if (request.OversizeBillableFactor is null or < 1)
            {
                throw new DomainValidationException("oversizeBillableFactor",
                    "Geef aan voor hoeveel factureerbare eenheden een buitenmaat telt (minstens 1).");
            }

            if (request.OversizeLengthCm is null && request.OversizeWidthCm is null)
            {
                throw new DomainValidationException("oversizeLengthCm",
                    "Geef minstens één buitenmaat-drempel (lengte of breedte) op.");
            }

            if (request.OversizeLengthCm is < 0 || request.OversizeWidthCm is < 0)
            {
                throw new DomainValidationException("oversizeLengthCm", "Een buitenmaat-drempel mag niet negatief zijn.");
            }
        }

        var usesBrackets = request.Basis is PriceRuleBasis.QuantityBracket or PriceRuleBasis.WeightBracket;
        var providedBrackets = request.Brackets ?? [];
        if (usesBrackets && providedBrackets.Count == 0)
        {
            throw new DomainValidationException("brackets", "Een staffelregel heeft minstens één staffel nodig.");
        }

        if (providedBrackets.Count > 0)
        {
            foreach (var bracket in providedBrackets)
            {
                if (bracket.ToQuantity is { } to && to < bracket.FromQuantity)
                {
                    throw new DomainValidationException("brackets", "Een staffel eindigt niet vóór hij begint.");
                }

                if (bracket.WeightToKg is <= 0 || bracket.VolumeToM3 is <= 0 || bracket.LoadingMetersTo is <= 0)
                {
                    throw new DomainValidationException("brackets", "Een staffelgrens (kg/m³/ldm) moet groter zijn dan nul.");
                }
            }

            // Two rows conflict only when BOTH their quantity ranges overlap AND they carry
            // identical dimension caps — a carrier table legitimately has several rows sharing a
            // quantity band (even "0 tot oneindig"), distinguished only by weight/volume/ldm caps.
            static bool QuantityOverlaps(SavePriceRuleBracketRequest a, SavePriceRuleBracketRequest b)
            {
                var aTo = a.ToQuantity ?? decimal.MaxValue;
                var bTo = b.ToQuantity ?? decimal.MaxValue;
                return a.FromQuantity <= bTo && b.FromQuantity <= aTo;
            }

            static bool SameCaps(SavePriceRuleBracketRequest a, SavePriceRuleBracketRequest b) =>
                a.WeightToKg == b.WeightToKg && a.VolumeToM3 == b.VolumeToM3 && a.LoadingMetersTo == b.LoadingMetersTo;

            for (var i = 0; i < providedBrackets.Count; i++)
            {
                for (var j = i + 1; j < providedBrackets.Count; j++)
                {
                    if (QuantityOverlaps(providedBrackets[i], providedBrackets[j]) && SameCaps(providedBrackets[i], providedBrackets[j]))
                    {
                        throw new DomainValidationException("brackets", "Staffels mogen elkaar niet overlappen.");
                    }
                }
            }

            if (request.BracketMode == BracketSelectionMode.PerNextUnit)
            {
                if (request.Basis != PriceRuleBasis.QuantityBracket)
                {
                    throw new DomainValidationException("bracketMode",
                        "Prijs per volgende eenheid is alleen mogelijk bij een staffel op aantal.");
                }

                var ordered = providedBrackets.OrderBy(b => b.FromQuantity).ToList();
                var gapless = ordered[0].FromQuantity == 1;
                for (var i = 1; gapless && i < ordered.Count; i++)
                {
                    gapless = ordered[i - 1].ToQuantity is { } previousTo && ordered[i].FromQuantity == previousTo + 1;
                }

                if (!gapless)
                {
                    throw new DomainValidationException("brackets",
                        "Bij prijs per volgende eenheid moeten de staffels aansluiten vanaf 1.");
                }
            }
        }
        else if (!usesBrackets && request.UnitPrice is null)
        {
            throw new DomainValidationException("unitPrice", "Geef een prijs op.");
        }

        var tenantId = TenantId;
        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        if (request.UnitTypeId is { } unitTypeId
            && !await _dbContext.UnitTypes.AnyAsync(u => u.Id == unitTypeId && u.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("eenheid");
        }

        if (request.ZoneId is { } zoneId
            && !await _dbContext.PricingZones.AnyAsync(z => z.Id == zoneId && z.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("zone");
        }

        if (request.AgreementId is { } agreementId)
        {
            var agreement = await _dbContext.PricingAgreements.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == agreementId && a.TenantId == tenantId, cancellationToken);
            if (agreement is null)
            {
                throw new InvalidTenantReferenceException("prijsafspraak");
            }

            // A customer rule cannot live inside another customer's agreement.
            if (agreement.CustomerId is { } agreementCustomer && request.CustomerId != agreementCustomer)
            {
                throw new DomainValidationException("agreementId",
                    "De regel hoort bij een andere klant dan de prijsafspraak.");
            }

            // A derived table has no rules of its own — it reuses its base-chain root's rules.
            if (agreement.BaseAgreementId is not null)
            {
                throw new DomainValidationException("agreementId", "Een afgeleide tabel kan geen eigen prijsregels hebben.");
            }
        }
    }

    private void ApplyRule(PriceRule rule, SavePriceRuleRequest request)
    {
        rule.CustomerId = request.CustomerId;
        rule.UnitTypeId = request.UnitTypeId;
        rule.Basis = request.Basis;
        rule.ZoneId = request.ZoneId;
        rule.Name = request.Name.Trim();
        rule.EffectiveFrom = request.EffectiveFrom;
        rule.EffectiveUntil = request.EffectiveUntil;
        rule.IsActive = request.IsActive;
        rule.UnitPrice = request.UnitPrice;
        rule.MinimumAmount = request.MinimumAmount;
        rule.MaximumAmount = request.MaximumAmount;
        rule.BracketMode = request.BracketMode;
        rule.AgreementId = request.AgreementId;
        rule.Priority = request.Priority;
        rule.BaseAmount = request.BaseAmount;
        rule.MinimumQuantity = request.MinimumQuantity;
        rule.QuantityRoundingStep = request.QuantityRoundingStep;
        rule.OversizeLengthCm = request.OversizeLengthCm;
        rule.OversizeWidthCm = request.OversizeWidthCm;
        rule.OversizeBillableFactor = request.OversizeBillableFactor;
        foreach (var bracket in request.Brackets ?? [])
        {
            rule.Brackets.Add(new PriceRuleBracket
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PriceRuleId = rule.Id,
                FromQuantity = bracket.FromQuantity,
                ToQuantity = bracket.ToQuantity,
                Price = bracket.Price,
                PricePerExtraUnit = bracket.PricePerExtraUnit,
                WeightToKg = bracket.WeightToKg,
                VolumeToM3 = bracket.VolumeToM3,
                LoadingMetersTo = bracket.LoadingMetersTo,
            });
        }
    }

    private static void ValidateOption(SaveServiceOptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("code", "Code en naam zijn verplicht.");
        }

        if (request.DefaultValue < 0)
        {
            throw new DomainValidationException("defaultValue", "De standaardprijs mag niet negatief zijn.");
        }
    }

    private void ApplyOption(ServiceOption option, SaveServiceOptionRequest request)
    {
        option.Code = request.Code.Trim().ToUpperInvariant();
        option.Name = request.Name.Trim();
        option.Kind = request.Kind;
        option.DefaultValue = request.DefaultValue;
        option.IsActive = request.IsActive;
        option.SortOrder = request.SortOrder;
        option.Description = Clean(request.Description);
        option.InvoiceDescription = Clean(request.InvoiceDescription);
        option.SelectableInOrders = request.SelectableInOrders;
    }

    private static ServiceOptionDto MapOption(ServiceOption option) => new(
        option.Id, option.Code, option.Name, option.Kind, option.DefaultValue, option.IsActive, option.SortOrder,
        option.Description, option.InvoiceDescription, option.SelectableInOrders);

    private async Task<IReadOnlyList<PriceRuleDto>> MapRulesAsync(IReadOnlyList<PriceRule> rules, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = rules.Where(r => r.CustomerId.HasValue).Select(r => r.CustomerId!.Value).Distinct().ToList();
        var unitIds = rules.Where(r => r.UnitTypeId.HasValue).Select(r => r.UnitTypeId!.Value).Distinct().ToList();
        var zoneIds = rules.Where(r => r.ZoneId.HasValue).Select(r => r.ZoneId!.Value).Distinct().ToList();
        var agreementIds = rules.Where(r => r.AgreementId.HasValue).Select(r => r.AgreementId!.Value).Distinct().ToList();

        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var units = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var zones = await _dbContext.PricingZones.AsNoTracking()
            .Where(z => z.TenantId == tenantId && zoneIds.Contains(z.Id))
            .ToDictionaryAsync(z => z.Id, z => z.Name, cancellationToken);
        var agreements = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == tenantId && agreementIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return rules.Select(rule => new PriceRuleDto(
            rule.Id, rule.CustomerId,
            rule.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null,
            rule.UnitTypeId,
            rule.UnitTypeId is { } uid ? units.GetValueOrDefault(uid) : null,
            rule.Basis, rule.ZoneId,
            rule.ZoneId is { } zid ? zones.GetValueOrDefault(zid) : null,
            rule.Name, rule.Currency, rule.EffectiveFrom, rule.EffectiveUntil, rule.IsActive,
            rule.UnitPrice, rule.MinimumAmount,
            rule.Brackets.OrderBy(b => b.FromQuantity)
                .Select(b => new PriceRuleBracketDto(
                    b.Id, b.FromQuantity, b.ToQuantity, b.Price, b.PricePerExtraUnit,
                    b.WeightToKg, b.VolumeToM3, b.LoadingMetersTo))
                .ToList(),
            rule.AgreementId,
            rule.AgreementId is { } aid ? agreements.GetValueOrDefault(aid) : null,
            rule.Priority, rule.BaseAmount,
            rule.OversizeLengthCm, rule.OversizeWidthCm, rule.OversizeBillableFactor,
            rule.MinimumQuantity, rule.QuantityRoundingStep,
            rule.MaximumAmount, rule.BracketMode))
            .ToList();
    }

    private static PricingZoneDto MapZone(PricingZone zone) => new(
        zone.Id, zone.Code, zone.Name, zone.IsActive, zone.SortOrder,
        zone.Areas.Select(a => new PricingZoneAreaDto(a.Id, a.CountryCode, a.PostalCodeFrom, a.PostalCodeTo)).ToList());
}
