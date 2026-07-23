using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Hr.Services;

public interface ILeaveBalanceService
{
    Task EnsureSeededAsync(CancellationToken cancellationToken);
    Task<EmployeeLeaveBalanceDto> GetForEmployeeAsync(Guid employeeId, int year, CancellationToken cancellationToken);
    Task SetEntitlementAsync(Guid employeeId, int year, SetLeaveEntitlementRequest request, CancellationToken cancellationToken);
    Task AddAdjustmentAsync(Guid employeeId, int year, AddLeaveAdjustmentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<LeaveAdjustmentDto>> GetAdjustmentsAsync(Guid employeeId, int year, Guid balanceTypeId, CancellationToken cancellationToken);
    Task<LeaveAvailabilityResult> CheckRequestAsync(Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, AbsencePartDay part, CancellationToken cancellationToken);
}

/// <summary>
/// Computes leave balances live from approved/pending absences per balance type, and manages
/// per-employee/year entitlement, carry-over and the manual-adjustment ledger. Approved
/// absences are the source of truth for used days; rejected/cancelled never count.
/// </summary>
public class LeaveBalanceService : ILeaveBalanceService
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public LeaveBalanceService(TransportationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;

        var existingBalanceCodes = await _db.LeaveBalanceTypes
            .Where(t => t.TenantId == tenantId).Select(t => t.Code).ToListAsync(cancellationToken);
        foreach (var seed in LeaveDefaults.BalanceTypes)
        {
            if (existingBalanceCodes.Contains(seed.Code)) continue;
            _db.LeaveBalanceTypes.Add(new LeaveBalanceType
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Code = seed.Code, Name = seed.Name, SortOrder = seed.SortOrder,
            });
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(cancellationToken);

        var balanceByCode = await _db.LeaveBalanceTypes
            .Where(t => t.TenantId == tenantId).ToDictionaryAsync(t => t.Code, t => t.Id, cancellationToken);
        var existingLeaveCodes = await _db.LeaveTypes
            .Where(t => t.TenantId == tenantId).Select(t => t.Code).ToListAsync(cancellationToken);
        foreach (var seed in LeaveDefaults.LeaveTypes)
        {
            if (existingLeaveCodes.Contains(seed.Code)) continue;
            _db.LeaveTypes.Add(new LeaveType
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Code = seed.Code, Name = seed.Name,
                AbsenceType = seed.AbsenceType, DeductsFromBalance = seed.DeductsFromBalance,
                BalanceTypeId = seed.BalanceCode is null ? null : balanceByCode.GetValueOrDefault(seed.BalanceCode),
                IsPaid = seed.IsPaid, AllowsHalfDays = seed.AllowsHalfDays, RequiresReason = seed.RequiresReason,
                RequiresAttachment = seed.RequiresAttachment, VisibleInSelfService = seed.VisibleInSelfService,
                Colour = seed.Colour, SortOrder = seed.SortOrder,
            });
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmployeeLeaveBalanceDto> GetForEmployeeAsync(Guid employeeId, int year, CancellationToken cancellationToken)
    {
        var rows = await ComputeRowsAsync(employeeId, year, cancellationToken);
        return new EmployeeLeaveBalanceDto(employeeId, year, rows);
    }

    private async Task<IReadOnlyList<LeaveBalanceRowDto>> ComputeRowsAsync(Guid employeeId, int year, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        var tenantId = _tenant.TenantId;
        var settings = await GetOrCreateSettingsAsync(cancellationToken);

        var balanceTypes = await _db.LeaveBalanceTypes
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        var entitlements = await _db.EmployeeLeaveBalances
            .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.CalendarYear == year).ToListAsync(cancellationToken);
        var entitlementIds = entitlements.Select(e => e.Id).ToList();
        var adjustments = await _db.LeaveBalanceAdjustments
            .Where(a => a.TenantId == tenantId && entitlementIds.Contains(a.EmployeeLeaveBalanceId)).ToListAsync(cancellationToken);

        var (approved, pending) = await ComputeUsageAsync(employeeId, year, cancellationToken);

        var rows = new List<LeaveBalanceRowDto>();
        foreach (var bt in balanceTypes)
        {
            var entitlement = entitlements.FirstOrDefault(e => e.BalanceTypeId == bt.Id);
            var baseDays = entitlement?.BaseEntitlementDays
                ?? (bt.Code == LeaveDefaults.StatutoryBalanceCode ? settings.DefaultAnnualEntitlementDays : 0m);
            var carry = entitlement?.CarryOverDays ?? 0m;
            var manualAdj = entitlement is null ? 0m : adjustments.Where(a => a.EmployeeLeaveBalanceId == entitlement.Id).Sum(a => a.Days);
            var approvedUsed = approved.GetValueOrDefault(bt.Id);
            var pendingReserved = pending.GetValueOrDefault(bt.Id);
            var remaining = baseDays + carry + manualAdj - approvedUsed - (settings.PendingReservesBalance ? pendingReserved : 0m);
            rows.Add(new LeaveBalanceRowDto(bt.Id, bt.Code, bt.Name, baseDays, carry, manualAdj, approvedUsed, pendingReserved, remaining, settings.PendingReservesBalance));
        }
        return rows;
    }

    /// <summary>Sums approved and pending (Requested/UnderReview) days per balance type for the year.</summary>
    private async Task<(Dictionary<Guid, decimal> Approved, Dictionary<Guid, decimal> Pending)> ComputeUsageAsync(
        Guid employeeId, int year, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var leaveTypes = await _db.LeaveTypes.Where(t => t.TenantId == tenantId).ToListAsync(cancellationToken);
        var leaveTypeById = leaveTypes.ToDictionary(t => t.Id);
        var defaultByAbsenceType = leaveTypes.Where(t => t.IsActive)
            .GroupBy(t => t.AbsenceType)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.SortOrder).First());

        var absences = await _db.Absences
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId
                && a.StartDate <= yearEnd && a.EndDate >= yearStart
                && (a.Status == AbsenceStatus.Approved || a.Status == AbsenceStatus.Requested || a.Status == AbsenceStatus.UnderReview))
            .ToListAsync(cancellationToken);

        var approved = new Dictionary<Guid, decimal>();
        var pending = new Dictionary<Guid, decimal>();
        foreach (var absence in absences)
        {
            var leaveType = absence.LeaveTypeId is { } id && leaveTypeById.TryGetValue(id, out var byId)
                ? byId
                : defaultByAbsenceType.GetValueOrDefault(absence.Type);
            if (leaveType is null || !leaveType.DeductsFromBalance || leaveType.BalanceTypeId is not { } balanceId) continue;

            var days = LeaveDayCalculator.CountDaysInYear(absence.StartDate, absence.EndDate, absence.PartDay, year);
            if (days <= 0m) continue;

            var bucket = absence.Status == AbsenceStatus.Approved ? approved : pending;
            bucket[balanceId] = bucket.GetValueOrDefault(balanceId) + days;
        }
        return (approved, pending);
    }

    public async Task SetEntitlementAsync(Guid employeeId, int year, SetLeaveEntitlementRequest request, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        var tenantId = _tenant.TenantId;
        var balanceType = await _db.LeaveBalanceTypes.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == request.BalanceTypeId, cancellationToken)
            ?? throw new DomainValidationException("balanceTypeId", "Onbekend saldotype.");
        var settings = await GetOrCreateSettingsAsync(cancellationToken);

        var carry = settings.CarryOverEnabled ? Math.Max(0m, request.CarryOverDays) : 0m;
        if (settings.MaxCarryOverDays is { } max && carry > max) carry = max;

        var row = await _db.EmployeeLeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.CalendarYear == year && b.BalanceTypeId == request.BalanceTypeId, cancellationToken);
        object? oldValues = row is null ? null : new { row.BaseEntitlementDays, row.CarryOverDays };
        if (row is null)
        {
            row = new EmployeeLeaveBalance
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, CalendarYear = year,
                BalanceTypeId = request.BalanceTypeId, BaseEntitlementDays = request.BaseEntitlementDays, CarryOverDays = carry,
            };
            _db.EmployeeLeaveBalances.Add(row);
        }
        else
        {
            row.BaseEntitlementDays = request.BaseEntitlementDays;
            row.CarryOverDays = carry;
        }
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("EmployeeLeaveBalance", row.Id.ToString(), oldValues is null ? "Created" : "Updated",
            oldValues, new { row.BaseEntitlementDays, row.CarryOverDays, balanceType.Code, year }, cancellationToken);
    }

    public async Task AddAdjustmentAsync(Guid employeeId, int year, AddLeaveAdjustmentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DomainValidationException("reason", "Een reden is verplicht voor een saldoaanpassing.");
        if (request.Days == 0m)
            throw new DomainValidationException("days", "Een aanpassing van 0 dagen heeft geen effect.");

        var row = await GetOrCreateEntitlementRowAsync(employeeId, year, request.BalanceTypeId, cancellationToken);
        var adjustment = new LeaveBalanceAdjustment
        {
            Id = Guid.NewGuid(), TenantId = _tenant.TenantId, EmployeeLeaveBalanceId = row.Id,
            Days = request.Days, Reason = request.Reason.Trim(), Kind = request.Kind,
        };
        _db.LeaveBalanceAdjustments.Add(adjustment);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("LeaveBalanceAdjustment", adjustment.Id.ToString(), "Added", null,
            new { adjustment.Days, adjustment.Reason, adjustment.Kind, employeeId, year }, cancellationToken);
    }

    public async Task<IReadOnlyList<LeaveAdjustmentDto>> GetAdjustmentsAsync(Guid employeeId, int year, Guid balanceTypeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var row = await _db.EmployeeLeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.CalendarYear == year && b.BalanceTypeId == balanceTypeId, cancellationToken);
        if (row is null) return [];
        return await _db.LeaveBalanceAdjustments
            .Where(a => a.TenantId == tenantId && a.EmployeeLeaveBalanceId == row.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new LeaveAdjustmentDto(a.Id, a.Days, a.Reason, a.Kind, a.CreatedAt, a.CreatedByUserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<LeaveAvailabilityResult> CheckRequestAsync(Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, AbsencePartDay part, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var leaveType = await _db.LeaveTypes.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == leaveTypeId, cancellationToken);
        if (leaveType is null || !leaveType.DeductsFromBalance || leaveType.BalanceTypeId is not { } balanceTypeId)
            return new LeaveAvailabilityResult(true, 0m, 0m, null);

        var year = start.Year;
        var requested = LeaveDayCalculator.CountDaysInYear(start, end, part, year);
        var rows = await ComputeRowsAsync(employeeId, year, cancellationToken);
        var row = rows.FirstOrDefault(r => r.BalanceTypeId == balanceTypeId);
        var remaining = row?.RemainingDays ?? 0m;

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        if (!settings.AllowNegativeBalance && remaining - requested < 0m)
        {
            return new LeaveAvailabilityResult(false, remaining, requested,
                $"Onvoldoende saldo: {remaining} dag(en) beschikbaar, {requested} aangevraagd.");
        }
        return new LeaveAvailabilityResult(true, remaining, requested, null);
    }

    private async Task<EmployeeLeaveBalance> GetOrCreateEntitlementRowAsync(Guid employeeId, int year, Guid balanceTypeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var balanceType = await _db.LeaveBalanceTypes.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == balanceTypeId, cancellationToken)
            ?? throw new DomainValidationException("balanceTypeId", "Onbekend saldotype.");
        var row = await _db.EmployeeLeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.CalendarYear == year && b.BalanceTypeId == balanceTypeId, cancellationToken);
        if (row is not null) return row;

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        row = new EmployeeLeaveBalance
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, CalendarYear = year, BalanceTypeId = balanceTypeId,
            BaseEntitlementDays = balanceType.Code == LeaveDefaults.StatutoryBalanceCode ? settings.DefaultAnnualEntitlementDays : 0m,
            CarryOverDays = 0m,
        };
        _db.EmployeeLeaveBalances.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private async Task<LeaveEntitlementSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.LeaveEntitlementSettings.FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        if (settings is null)
        {
            settings = new LeaveEntitlementSettings { Id = Guid.NewGuid(), TenantId = _tenant.TenantId };
            _db.LeaveEntitlementSettings.Add(settings);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(settings).State = EntityState.Detached;
                settings = await _db.LeaveEntitlementSettings.FirstAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
            }
        }
        return settings;
    }
}
