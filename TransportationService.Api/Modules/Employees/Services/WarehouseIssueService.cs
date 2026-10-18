using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// The safe identity a warehouse clerk needs to hand something to a person: number, name, active.
/// Never contact, bank, medical, contract or any other HR field.
/// </summary>
public record InventoryEmployeeOptionDto(Guid Id, string EmployeeNumber, string Name, bool IsActive);

/// <summary>"Uitgeven" from the warehouse desk. The template is mandatory: the warehouse issues catalogue items.</summary>
public record WarehouseIssueRequest(
    Guid EmployeeId, Guid TemplateId, Guid? VariantId, int Quantity,
    string? SerialNumber = null, DateOnly? IssuedDate = null, DateOnly? ExpectedReturnDate = null,
    string? ConditionAtIssue = null, string? Notes = null,
    bool ConfirmNegativeStock = false, Guid? ExpectedVersion = null, string? OverrideReason = null);

/// <summary>
/// One issuance row as the warehouse sees it (open loans and history). <see cref="DuplicateOpenSerial"/>
/// flags legacy data where the same serial has more than one open holder (created before WH-H2).
/// </summary>
public record InventoryIssueRowDto(
    Guid ItemId, Guid EmployeeId, string EmployeeNumber, string EmployeeName, bool EmployeeActive,
    Guid? TemplateId, string? ItemCode, string ItemName, string? VariantLabel, string? SerialNumber,
    int Quantity, IssuedItemStatus Status, string? ReturnDisposition,
    DateOnly? IssuedDate, DateOnly? ExpectedReturnDate, DateOnly? ReturnedDate,
    bool IsOverdue, bool ReturnRequired, bool IsConsumable,
    string? IssuedByName, string? ReceivedBackByName, bool DuplicateOpenSerial);

public record InventoryIssueQuery(
    string Scope = "open", string? Search = null, Guid? EmployeeId = null, Guid? TemplateId = null,
    string? Serial = null, bool OverdueOnly = false, bool InactiveEmployeesOnly = false,
    bool ReturnRequiredOnly = false, DateOnly? DueBefore = null);

/// <summary>WH-H5: what an employee still holds and must bring back — shown before offboarding.</summary>
public record OutstandingEquipmentDto(int Count, IReadOnlyList<InventoryIssueRowDto> Items);

public interface IWarehouseIssueService
{
    Task<IReadOnlyList<InventoryEmployeeOptionDto>> SearchEmployeesAsync(string? search, bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryIssueRowDto>> ListIssuesAsync(InventoryIssueQuery query, CancellationToken cancellationToken);
    Task<EmployeeIssuedItemDto?> IssueAsync(WarehouseIssueRequest request, CancellationToken cancellationToken);
    Task<EmployeeIssuedItemDto?> ReturnAsync(Guid itemId, ReturnIssuedItemRequest request, CancellationToken cancellationToken);
    Task<OutstandingEquipmentDto?> OutstandingForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>
/// WH-H3 / WH-H4 / WH-H5 (warehouse acceptance 2026-10-18): issuing and returning existed only on the
/// HR employee page (HR was the warehouse clerk) and nobody could answer "who has scanner SER-001?"
/// without opening employees one by one. This is the warehouse desk over the SAME issuance rows and
/// the SAME single write path (<see cref="IIssuedItemService"/>: stock, serial rule, audit), so the
/// HR tab and the warehouse can never disagree.
/// </summary>
public class WarehouseIssueService : IWarehouseIssueService
{
    private const int MaxRows = 500;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IIssuedItemService _issuedItems;
    private readonly TimeProvider _clock;

    public WarehouseIssueService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IIssuedItemService issuedItems, TimeProvider? clock = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _issuedItems = issuedItems;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<InventoryEmployeeOptionDto>> SearchEmployeesAsync(
        string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && (includeInactive || e.IsActive));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(term)
                                     || e.EmployeeNumber.ToLower().Contains(term));
        }

        return await query
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ThenBy(e => e.EmployeeNumber)
            .Take(50)
            .Select(e => new InventoryEmployeeOptionDto(e.Id, e.EmployeeNumber, e.FirstName + " " + e.LastName, e.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryIssueRowDto>> ListIssuesAsync(InventoryIssueQuery query, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var today = await TenantTimeZone.TodayAsync(_dbContext, tenantId, _clock, cancellationToken);
        var open = !string.Equals(query.Scope, "history", StringComparison.OrdinalIgnoreCase);

        var items = _dbContext.EmployeeIssuedItems.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status != IssuedItemStatus.NotIssued);
        if (open)
        {
            items = items.Where(i => i.Status == IssuedItemStatus.Issued);
        }

        if (query.EmployeeId is { } employeeId)
        {
            items = items.Where(i => i.EmployeeId == employeeId);
        }

        if (query.TemplateId is { } templateId)
        {
            items = items.Where(i => i.TemplateId == templateId);
        }

        if (SerialNumbers.Normalize(query.Serial) is { } serial)
        {
            // The typed serial too: a legacy duplicate gave up its normalized identity in the migration.
            var typed = query.Serial!.Trim().ToUpper();
            items = items.Where(i => (i.NormalizedSerialNumber != null && i.NormalizedSerialNumber.Contains(serial))
                                     || (i.SerialNumber != null && i.SerialNumber.ToUpper().Contains(typed)));
        }

        if (query.OverdueOnly)
        {
            items = items.Where(i => i.Status == IssuedItemStatus.Issued && i.ExpectedReturnDate != null && i.ExpectedReturnDate < today);
        }

        if (query.DueBefore is { } dueBefore)
        {
            items = items.Where(i => i.ExpectedReturnDate != null && i.ExpectedReturnDate <= dueBefore);
        }

        // Templates are read including soft-deleted rows: an issuance of a (legacy) deleted item
        // must still show its code and return duty.
        var rows = from item in items
                   join employee in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId)
                       on item.EmployeeId equals employee.Id
                   join template in _dbContext.IssuedItemTemplates.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId)
                       on item.TemplateId equals template.Id into templates
                   from template in templates.DefaultIfEmpty()
                   select new { item, employee, template };

        if (query.InactiveEmployeesOnly)
        {
            rows = rows.Where(r => !r.employee.IsActive);
        }

        if (query.ReturnRequiredOnly)
        {
            rows = rows.Where(r => r.item.ExpectedReturnDate != null
                                   || (r.template != null && (r.template.ReturnRequired || r.template.RequiresSerialNumber)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            var serialTerm = SerialNumbers.Normalize(query.Search) ?? term;
            rows = rows.Where(r => r.item.NameSnapshot.ToLower().Contains(term)
                                   || (r.template != null && r.template.Code != null && r.template.Code.ToLower().Contains(term))
                                   || (r.item.NormalizedSerialNumber != null && r.item.NormalizedSerialNumber.Contains(serialTerm))
                                   || (r.item.SerialNumber != null && r.item.SerialNumber.ToLower().Contains(term))
                                   || (r.employee.FirstName + " " + r.employee.LastName).ToLower().Contains(term)
                                   || r.employee.EmployeeNumber.ToLower().Contains(term));
        }

        var ordered = open
            ? rows.OrderBy(r => r.item.ExpectedReturnDate == null).ThenBy(r => r.item.ExpectedReturnDate)
                .ThenBy(r => r.employee.FirstName).ThenBy(r => r.employee.LastName)
            : rows.OrderByDescending(r => r.item.ReturnedDate ?? r.item.IssuedDate).ThenByDescending(r => r.item.CreatedAt);
        var page = await ordered.Take(MaxRows)
            .Select(r => new
            {
                r.item.Id, r.item.EmployeeId, r.employee.EmployeeNumber,
                EmployeeName = r.employee.FirstName + " " + r.employee.LastName, EmployeeActive = r.employee.IsActive,
                r.item.TemplateId, Code = r.template != null ? r.template.Code : null, r.item.NameSnapshot, r.item.VariantSnapshot,
                r.item.SerialNumber, r.item.NormalizedSerialNumber, r.item.Quantity, r.item.Status, r.item.ReturnDisposition,
                r.item.IssuedDate, r.item.ExpectedReturnDate, r.item.ReturnedDate,
                ReturnRequired = r.template != null && (r.template.ReturnRequired || r.template.RequiresSerialNumber),
                IsConsumable = r.template != null && r.template.IsConsumable,
                r.item.IssuedByUserId, r.item.ReceivedBackByUserId,
            })
            .ToListAsync(cancellationToken);

        var userIds = page.SelectMany(r => new[] { r.IssuedByUserId, r.ReceivedBackByUserId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _dbContext.Users.AsNoTracking()
                    .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                    .ToListAsync(cancellationToken))
                .ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } name ? name : u.Email);

        // Legacy corruption made visible: a serial with more than one OPEN holder of the same item
        // (compared on the typed serial, normalized here, because the migration released the
        // normalized identity of the later duplicates).
        var serialTemplateIds = page.Where(r => r.Status == IssuedItemStatus.Issued && r.SerialNumber != null && r.TemplateId != null)
            .Select(r => r.TemplateId!.Value).Distinct().ToList();
        var duplicateKeys = serialTemplateIds.Count == 0
            ? []
            : (await _dbContext.EmployeeIssuedItems.AsNoTracking()
                    .Where(i => i.TenantId == tenantId && i.Status == IssuedItemStatus.Issued && i.SerialNumber != null
                                && i.TemplateId != null && serialTemplateIds.Contains(i.TemplateId.Value))
                    .Select(i => new { i.TemplateId, i.SerialNumber })
                    .ToListAsync(cancellationToken))
                .GroupBy(i => (i.TemplateId, Serial: SerialNumbers.Normalize(i.SerialNumber)))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

        return page.Select(r => new InventoryIssueRowDto(
                r.Id, r.EmployeeId, r.EmployeeNumber, r.EmployeeName, r.EmployeeActive,
                r.TemplateId, r.Code, r.NameSnapshot, r.VariantSnapshot, r.SerialNumber,
                r.Quantity, r.Status, r.ReturnDisposition,
                r.IssuedDate, r.ExpectedReturnDate, r.ReturnedDate,
                IsOverdue: r.Status == IssuedItemStatus.Issued && r.ExpectedReturnDate is { } due && due < today,
                r.ReturnRequired || r.ExpectedReturnDate != null, r.IsConsumable,
                r.IssuedByUserId is { } issuer ? users.GetValueOrDefault(issuer) : null,
                r.ReceivedBackByUserId is { } receiver ? users.GetValueOrDefault(receiver) : null,
                DuplicateOpenSerial: r.Status == IssuedItemStatus.Issued && r.SerialNumber != null
                                     && duplicateKeys.Contains((r.TemplateId, SerialNumbers.Normalize(r.SerialNumber)))))
            .ToList();
    }

    public async Task<EmployeeIssuedItemDto?> IssueAsync(WarehouseIssueRequest request, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            throw new DomainValidationException("employeeId", "De medewerker bestaat niet.");
        }

        if (!employee.IsActive)
        {
            throw new DomainValidationException("employeeId",
                $"{employee.FirstName} {employee.LastName} is niet meer actief: aan een inactieve medewerker wordt niets uitgegeven.");
        }

        var issuedDate = request.IssuedDate ?? await TenantTimeZone.TodayAsync(_dbContext, _tenantContext.TenantId, _clock, cancellationToken);
        return await _issuedItems.UpsertAsync(request.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
                request.TemplateId, null, null, IssuedItemStatus.Issued,
                issuedDate, request.Quantity, request.SerialNumber, request.Notes,
                null, null,
                VariantId: request.VariantId,
                ConfirmNegativeStock: request.ConfirmNegativeStock,
                ExpectedVersion: request.ExpectedVersion,
                OverrideReason: request.OverrideReason,
                ExpectedReturnDate: request.ExpectedReturnDate,
                ConditionAtIssue: request.ConditionAtIssue),
            cancellationToken);
    }

    public async Task<EmployeeIssuedItemDto?> ReturnAsync(Guid itemId, ReturnIssuedItemRequest request, CancellationToken cancellationToken)
    {
        var employeeId = await _dbContext.EmployeeIssuedItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.Id == itemId)
            .Select(i => (Guid?)i.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
        return employeeId is { } id ? await _issuedItems.ReturnAsync(id, itemId, request, cancellationToken) : null;
    }

    public async Task<OutstandingEquipmentDto?> OutstandingForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Employees.AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId, cancellationToken))
        {
            return null;
        }

        var rows = await ListIssuesAsync(new InventoryIssueQuery(EmployeeId: employeeId, ReturnRequiredOnly: true), cancellationToken);
        return new OutstandingEquipmentDto(rows.Count, rows);
    }

    /// <summary>
    /// The one "still has to come back" rule (offboarding warning, loans screen, offboarding audit):
    /// an OPEN issuance that has a return date or whose item must be returned / is serialized.
    /// </summary>
    public static Task<int> CountOutstandingAsync(TransportationDbContext db, Guid tenantId, Guid employeeId, CancellationToken cancellationToken) =>
        (from item in db.EmployeeIssuedItems
         where item.TenantId == tenantId && item.EmployeeId == employeeId && item.Status == IssuedItemStatus.Issued
         join template in db.IssuedItemTemplates.IgnoreQueryFilters() on item.TemplateId equals template.Id into templates
         from template in templates.DefaultIfEmpty()
         where item.ExpectedReturnDate != null || (template != null && (template.ReturnRequired || template.RequiresSerialNumber))
         select item.Id).CountAsync(cancellationToken);
}
