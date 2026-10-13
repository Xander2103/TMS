using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class TankCardService : ITankCardService
{
    /// <summary>Cards expiring within this many days count as ExpiringSoon.</summary>
    public const int ExpiryWarningDays = 60;

    private const string EntityType = "TankCard";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public TankCardService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Blocked wins over the validity window; expiry wins over the warning band.</summary>
    public static TankCardStatus ComputeStatus(bool isBlocked, DateOnly? validUntil, DateOnly today)
    {
        if (isBlocked)
        {
            return TankCardStatus.Blocked;
        }

        if (validUntil is not { } until)
        {
            return TankCardStatus.Active;
        }

        if (until < today)
        {
            return TankCardStatus.Expired;
        }

        return until <= today.AddDays(ExpiryWarningDays) ? TankCardStatus.ExpiringSoon : TankCardStatus.Active;
    }

    private IQueryable<TankCard> TenantScoped() =>
        _dbContext.TankCards.Where(c => c.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<TankCardDto>> SearchAsync(
        string? search, TankCardStatus? status, bool available, PageRequest page, CancellationToken cancellationToken)
    {
        var today = Today;
        var query = TenantScoped().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var vehicleMatches = _dbContext.Vehicles
                .Where(v => v.TenantId == _tenantContext.TenantId &&
                            (v.InternalNumber.ToLower().Contains(term) || v.LicensePlate.ToLower().Contains(term)))
                .Select(v => (Guid?)v.Id);
            var employeeMatches = _dbContext.Employees
                .Where(e => e.TenantId == _tenantContext.TenantId &&
                            (e.FirstName.ToLower().Contains(term) || e.LastName.ToLower().Contains(term)))
                .Select(e => (Guid?)e.Id);
            query = query.Where(c =>
                c.CardNumber.ToLower().Contains(term) ||
                c.Provider.ToLower().Contains(term) ||
                (c.InternalName != null && c.InternalName.ToLower().Contains(term)) ||
                vehicleMatches.Contains(c.VehicleId) ||
                employeeMatches.Contains(c.EmployeeId));
        }

        // "Available" = free for linking to an employee: unassigned, unblocked, not expired.
        if (available)
        {
            query = query.Where(c =>
                c.EmployeeId == null && !c.IsBlocked && (c.ValidUntil == null || c.ValidUntil >= today));
        }

        // Status is derived, but each branch is expressible as a SQL predicate so paging stays correct.
        var warningLimit = today.AddDays(ExpiryWarningDays);
        query = status switch
        {
            TankCardStatus.Blocked => query.Where(c => c.IsBlocked),
            TankCardStatus.Expired => query.Where(c => !c.IsBlocked && c.ValidUntil != null && c.ValidUntil < today),
            TankCardStatus.ExpiringSoon => query.Where(c =>
                !c.IsBlocked && c.ValidUntil != null && c.ValidUntil >= today && c.ValidUntil <= warningLimit),
            TankCardStatus.Active => query.Where(c =>
                !c.IsBlocked && (c.ValidUntil == null || c.ValidUntil > warningLimit)),
            _ => query,
        };

        var totalCount = await query.CountAsync(cancellationToken);

        // Page membership is decided in SQL; the projection join cannot be ordered through the
        // record constructor, so presentation order is restored in memory on the page only.
        var cardPage = query
            .OrderBy(c => c.Provider).ThenBy(c => c.CardNumber)
            .Skip(page.Skip)
            .Take(page.PageSize);

        var rows = await Joined(cardPage).ToListAsync(cancellationToken);

        var items = rows
            .OrderBy(r => r.Card.Provider).ThenBy(r => r.Card.CardNumber)
            .Select(r => Map(r.Card, r.VehicleInternalNumber, r.VehicleLicensePlate, r.EmployeeName, today))
            .ToList();

        return new PagedResult<TankCardDto>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<TankCardDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await Joined(TenantScoped().AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row.Card, row.VehicleInternalNumber, row.VehicleLicensePlate, row.EmployeeName, Today);
    }

    public async Task<IReadOnlyList<TankCardDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var today = Today;
        var rows = await Joined(TenantScoped().AsNoTracking().Where(c => c.EmployeeId == employeeId))
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Card.Provider).ThenBy(r => r.Card.CardNumber)
            .Select(r => Map(r.Card, r.VehicleInternalNumber, r.VehicleLicensePlate, r.EmployeeName, today))
            .ToList();
    }

    public async Task<TankCardOperationResult> CreateAsync(CreateTankCardRequest request, CancellationToken cancellationToken)
    {
        var cardNumber = request.CardNumber?.Trim();
        var provider = request.Provider?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return TankCardOperationResult.Invalid("Kaartnummer is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return TankCardOperationResult.Invalid("Provider is verplicht.");
        }

        if (!ValidityWindowValid(request.ValidFrom, request.ValidUntil))
        {
            return TankCardOperationResult.Invalid("De einddatum moet na de begindatum liggen.");
        }

        ValidateLimits(request.DailyLimit, request.WeeklyLimit, request.MonthlyLimit);

        if (!await VehicleInTenantAsync(request.VehicleId, cancellationToken))
        {
            return TankCardOperationResult.InvalidReference;
        }

        var link = await ResolveEmployeeAndDriverAsync(request.EmployeeId, request.DriverId, cancellationToken);
        if (link.DriverReferenceInvalid)
        {
            return TankCardOperationResult.InvalidReference;
        }

        if (await TenantScoped().AnyAsync(c => c.CardNumber == cardNumber, cancellationToken))
        {
            return TankCardOperationResult.DuplicateCardNumber;
        }

        var card = new TankCard
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CardNumber = cardNumber,
            Provider = provider,
            VehicleId = request.VehicleId,
            DriverId = link.DriverId,
            EmployeeId = link.EmployeeId,
            InternalName = Trim(request.InternalName),
            FuelType = Trim(request.FuelType),
            DailyLimit = request.DailyLimit,
            WeeklyLimit = request.WeeklyLimit,
            MonthlyLimit = request.MonthlyLimit,
            CostCenter = Trim(request.CostCenter),
            ValidFrom = request.ValidFrom,
            ValidUntil = request.ValidUntil,
            Notes = Trim(request.Notes),
        };

        _dbContext.Add(card);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            // Unique index (TenantId, CardNumber) raced with a concurrent insert.
            return TankCardOperationResult.DuplicateCardNumber;
        }

        await _auditService.RecordAsync(EntityType, card.Id.ToString(), "Created", null,
            new
            {
                card.CardNumber, card.Provider, card.VehicleId, card.DriverId, card.EmployeeId,
                card.InternalName, card.FuelType, card.DailyLimit, card.WeeklyLimit, card.MonthlyLimit, card.CostCenter,
            },
            cancellationToken);

        return TankCardOperationResult.Success(await RequireDtoAsync(card.Id, cancellationToken));
    }

    public async Task<TankCardOperationResult> UpdateAsync(Guid id, UpdateTankCardRequest request, CancellationToken cancellationToken)
    {
        var cardNumber = request.CardNumber?.Trim();
        var provider = request.Provider?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return TankCardOperationResult.Invalid("Kaartnummer is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return TankCardOperationResult.Invalid("Provider is verplicht.");
        }

        if (!ValidityWindowValid(request.ValidFrom, request.ValidUntil))
        {
            return TankCardOperationResult.Invalid("De einddatum moet na de begindatum liggen.");
        }

        ValidateLimits(request.DailyLimit, request.WeeklyLimit, request.MonthlyLimit);

        var card = await TenantScoped().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null)
        {
            return TankCardOperationResult.NotFound;
        }

        if (!await VehicleInTenantAsync(request.VehicleId, cancellationToken))
        {
            return TankCardOperationResult.InvalidReference;
        }

        var link = await ResolveEmployeeAndDriverAsync(request.EmployeeId, request.DriverId, cancellationToken);
        if (link.DriverReferenceInvalid)
        {
            return TankCardOperationResult.InvalidReference;
        }

        if (await TenantScoped().AnyAsync(c => c.Id != id && c.CardNumber == cardNumber, cancellationToken))
        {
            return TankCardOperationResult.DuplicateCardNumber;
        }

        var before = new
        {
            card.CardNumber, card.Provider, card.VehicleId, card.DriverId, card.EmployeeId, card.ValidUntil,
            card.InternalName, card.FuelType, card.DailyLimit, card.WeeklyLimit, card.MonthlyLimit, card.CostCenter,
        };

        card.CardNumber = cardNumber;
        card.Provider = provider;
        card.VehicleId = request.VehicleId;
        card.DriverId = link.DriverId;
        card.EmployeeId = link.EmployeeId;
        card.InternalName = Trim(request.InternalName);
        card.FuelType = Trim(request.FuelType);
        card.DailyLimit = request.DailyLimit;
        card.WeeklyLimit = request.WeeklyLimit;
        card.MonthlyLimit = request.MonthlyLimit;
        card.CostCenter = Trim(request.CostCenter);
        card.ValidFrom = request.ValidFrom;
        card.ValidUntil = request.ValidUntil;
        card.Notes = Trim(request.Notes);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            return TankCardOperationResult.DuplicateCardNumber;
        }

        await _auditService.RecordAsync(EntityType, card.Id.ToString(), "Updated", before,
            new
            {
                card.CardNumber, card.Provider, card.VehicleId, card.DriverId, card.EmployeeId, card.ValidUntil,
                card.InternalName, card.FuelType, card.DailyLimit, card.WeeklyLimit, card.MonthlyLimit, card.CostCenter,
            },
            cancellationToken);

        return TankCardOperationResult.Success(await RequireDtoAsync(card.Id, cancellationToken));
    }

    public async Task<TankCardOperationResult> SetBlockedAsync(
        Guid id, SetTankCardBlockedRequest request, CancellationToken cancellationToken)
    {
        var card = await TenantScoped().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null)
        {
            return TankCardOperationResult.NotFound;
        }

        var before = new { card.IsBlocked, card.BlockedReason };

        card.IsBlocked = request.IsBlocked;
        card.BlockedReason = request.IsBlocked ? Trim(request.Reason) : null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, card.Id.ToString(),
            request.IsBlocked ? "Blocked" : "Unblocked", before,
            new { card.IsBlocked, card.BlockedReason }, cancellationToken);

        return TankCardOperationResult.Success(await RequireDtoAsync(card.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var card = await TenantScoped().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null)
        {
            return false;
        }

        _dbContext.Remove(card); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, card.Id.ToString(), "Deleted",
            new { card.CardNumber, card.Provider }, null, cancellationToken);

        return true;
    }

    private static bool ValidityWindowValid(DateOnly? from, DateOnly? until) =>
        from is not { } f || until is not { } u || u >= f;

    private static void ValidateLimits(decimal? dailyLimit, decimal? weeklyLimit, decimal? monthlyLimit)
    {
        ValidateLimit(dailyLimit, "dailyLimit");
        ValidateLimit(weeklyLimit, "weeklyLimit");
        ValidateLimit(monthlyLimit, "monthlyLimit");
    }

    private static void ValidateLimit(decimal? value, string field)
    {
        if (value is { } v && v < 0)
        {
            throw new DomainValidationException(field, "Limiet moet positief zijn.");
        }
    }

    private async Task<bool> VehicleInTenantAsync(Guid? vehicleId, CancellationToken cancellationToken)
    {
        if (vehicleId is not { } v)
        {
            return true;
        }

        return await _dbContext.Vehicles.AnyAsync(
            x => x.Id == v && x.TenantId == _tenantContext.TenantId, cancellationToken);
    }

    /// <summary>
    /// Resolves the canonical employee/driver link for a create or update:
    /// - EmployeeId supplied: validated against the tenant (throws <see cref="InvalidTenantReferenceException"/>
    ///   when it does not belong here), then DriverId is derived from that employee's driver profile
    ///   (null when the employee has none).
    /// - Only a legacy DriverId supplied: the employee is derived from that driver row. An unknown/foreign
    ///   driver id is reported via <see cref="ResolvedLink.DriverReferenceInvalid"/> (not an exception) to
    ///   preserve the existing InvalidReference result contract for that field.
    /// - Neither supplied: both stay null.
    /// </summary>
    private async Task<ResolvedLink> ResolveEmployeeAndDriverAsync(
        Guid? employeeId, Guid? driverId, CancellationToken cancellationToken)
    {
        if (employeeId is { } e)
        {
            await _dbContext.Employees.EnsureBelongsToTenantAsync(
                e, _tenantContext.TenantId, "medewerker", cancellationToken);

            var driverProfileId = await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && d.EmployeeId == e)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return new ResolvedLink(e, driverProfileId, false);
        }

        if (driverId is { } d)
        {
            var driver = await _dbContext.Drivers.AsNoTracking()
                .Where(x => x.TenantId == _tenantContext.TenantId && x.Id == d)
                .Select(x => new { x.Id, x.EmployeeId })
                .FirstOrDefaultAsync(cancellationToken);

            return driver is null
                ? new ResolvedLink(null, null, true)
                : new ResolvedLink(driver.EmployeeId, driver.Id, false);
        }

        return new ResolvedLink(null, null, false);
    }

    private IQueryable<JoinedCard> Joined(IQueryable<TankCard> cards) =>
        from c in cards
        join v in _dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == _tenantContext.TenantId)
            on c.VehicleId equals v.Id into vehicles
        from v in vehicles.DefaultIfEmpty()
        join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == _tenantContext.TenantId)
            on c.EmployeeId equals e.Id into employees
        from e in employees.DefaultIfEmpty()
        select new JoinedCard(
            c,
            v != null ? v.InternalNumber : null,
            v != null ? v.LicensePlate : null,
            e != null ? e.FirstName + " " + e.LastName : null);

    private async Task<TankCardDto> RequireDtoAsync(Guid id, CancellationToken cancellationToken) =>
        await GetByIdAsync(id, cancellationToken)
        ?? throw new InvalidOperationException($"Tank card {id} disappeared after save.");

    private static TankCardDto Map(
        TankCard c, string? vehicleInternalNumber, string? vehicleLicensePlate, string? employeeName, DateOnly today) => new(
        c.Id, c.CardNumber, c.Provider,
        c.VehicleId, vehicleInternalNumber, vehicleLicensePlate,
        // DriverId is kept in sync with EmployeeId, so the driver's display name is the employee's name.
        c.DriverId, c.DriverId is not null ? employeeName : null,
        c.EmployeeId, employeeName,
        c.ValidFrom, c.ValidUntil,
        ComputeStatus(c.IsBlocked, c.ValidUntil, today),
        c.IsBlocked, c.BlockedReason,
        c.InternalName, c.FuelType, c.DailyLimit, c.WeeklyLimit, c.MonthlyLimit, c.CostCenter,
        c.Notes);

    private sealed record JoinedCard(TankCard Card, string? VehicleInternalNumber, string? VehicleLicensePlate, string? EmployeeName);

    /// <summary>DriverReferenceInvalid is true only for a legacy DriverId that does not resolve in the tenant.</summary>
    private sealed record ResolvedLink(Guid? EmployeeId, Guid? DriverId, bool DriverReferenceInvalid);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
