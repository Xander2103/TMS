using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public interface IEmployeeCompletenessService
{
    Task<EmployeeCompletenessDto> GetForEmployeeAsync(Guid employeeId, CancellationToken ct);

    /// <summary>Batched percentage lookup for a list view. Ids the current tenant does not own
    /// are silently absent from the result (no cross-tenant leak, no exception).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPercentagesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);

    /// <summary>Active employees of the current tenant whose dossier is not (yet) complete —
    /// for the reminder producer (task 4). Tenant comes from the ambient <see cref="ITenantContext"/>
    /// the service was constructed with; background callers construct it with a
    /// <see cref="DevTenantContext"/> per tenant, one sweep iteration at a time.</summary>
    Task<IReadOnlyList<Guid>> FindIncompleteEmployeeIdsAsync(CancellationToken ct);
}

/// <summary>
/// Declarative "dossier completeness" engine (HR maturity wave, spec §2.1): one catalogue of
/// requirements instead of scattered if-statements. Confidential fields (national register
/// number, IBAN) are only ever checked for presence — the engine never reads or reports their
/// value, so it introduces no additional permission leak. All context queries are tenant-scoped
/// and batched (no N+1) so <see cref="GetPercentagesAsync"/> and
/// <see cref="FindIncompleteEmployeeIdsAsync"/> stay O(1) round-trips regardless of employee count.
/// </summary>
public class EmployeeCompletenessService : IEmployeeCompletenessService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public EmployeeCompletenessService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    private static readonly EmployeeDocumentCategory[] RelevantDocumentCategories =
    [
        EmployeeDocumentCategory.IdentityCardFront,
        EmployeeDocumentCategory.Contract,
        EmployeeDocumentCategory.DrivingLicenceFront,
    ];

    /// <summary>Everything a requirement predicate needs, pre-computed from batched queries so
    /// no requirement ever triggers its own database round-trip.</summary>
    private sealed record CompletenessContext(
        DateOnly? DateOfBirth,
        bool HasNationalRegisterNumber,
        bool HasAddress,
        bool HasContact,
        bool HasIban,
        DateOnly? EmploymentStartDate,
        Guid? ContractTypeId,
        Guid? DepartmentId,
        bool HasJobFunction,
        bool HasEmergencyContact,
        bool HasIdentityDocument,
        bool HasContractDocument,
        bool HasDrivingLicenceDocument,
        bool IsDriver);

    private sealed record CompletenessRequirement(
        string Code, string Label, string Section,
        Func<CompletenessContext, bool> IsApplicable,
        Func<CompletenessContext, bool> IsSatisfied);

    /// <summary>Spec §2.1, verbatim codes/labels/sections. Adding a new requirement is one line
    /// here. <c>driving_licence_document</c> is the only conditional entry — applicable only
    /// when the employee has a linked Driver profile.</summary>
    private static readonly IReadOnlyList<CompletenessRequirement> Catalogue =
    [
        new("date_of_birth", "Geboortedatum", "algemeen", AlwaysApplicable, c => c.DateOfBirth is not null),
        new("national_register_number", "Rijksregisternummer", "hr", AlwaysApplicable, c => c.HasNationalRegisterNumber),
        new("address", "Adres", "algemeen", AlwaysApplicable, c => c.HasAddress),
        new("contact", "E-mail of telefoon", "algemeen", AlwaysApplicable, c => c.HasContact),
        new("iban", "IBAN", "hr", AlwaysApplicable, c => c.HasIban),
        new("employment_start", "Startdatum", "dienstverband", AlwaysApplicable, c => c.EmploymentStartDate is not null),
        new("contract_type", "Contracttype", "dienstverband", AlwaysApplicable, c => c.ContractTypeId is not null),
        new("department", "Afdeling", "dienstverband", AlwaysApplicable, c => c.DepartmentId is not null),
        new("job_function", "Functie", "dienstverband", AlwaysApplicable, c => c.HasJobFunction),
        new("emergency_contact", "Noodcontact", "noodcontacten", AlwaysApplicable, c => c.HasEmergencyContact),
        new("identity_document", "Identiteitsdocument", "documenten", AlwaysApplicable, c => c.HasIdentityDocument),
        new("contract_document", "Contractdocument", "documenten", AlwaysApplicable, c => c.HasContractDocument),
        new("driving_licence_document", "Rijbewijsdocument", "documenten", c => c.IsDriver, c => c.HasDrivingLicenceDocument),
    ];

    private static bool AlwaysApplicable(CompletenessContext _) => true;

    public async Task<EmployeeCompletenessDto> GetForEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        var contexts = await BuildContextsAsync([employeeId], ct);
        if (!contexts.TryGetValue(employeeId, out var context))
        {
            throw new KeyNotFoundException($"Employee '{employeeId}' was not found in the current tenant.");
        }

        return Evaluate(context);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetPercentagesAsync(
        IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        var contexts = await BuildContextsAsync(employeeIds, ct);
        return contexts.ToDictionary(kv => kv.Key, kv => Evaluate(kv.Value).Percentage);
    }

    public async Task<IReadOnlyList<Guid>> FindIncompleteEmployeeIdsAsync(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var activeIds = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive)
            .Select(e => e.Id)
            .ToListAsync(ct);
        if (activeIds.Count == 0)
        {
            return [];
        }

        var contexts = await BuildContextsAsync(activeIds, ct);
        return contexts.Where(kv => !Evaluate(kv.Value).IsComplete).Select(kv => kv.Key).ToList();
    }

    /// <summary>One batched round-trip per data source (employee scalars, documents, emergency
    /// contacts, driver flag) regardless of how many employee ids are requested. Ids outside the
    /// current tenant are absent from the result — never an exception, never a leak.</summary>
    private async Task<Dictionary<Guid, CompletenessContext>> BuildContextsAsync(
        IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                e.DateOfBirth,
                HasNationalRegisterNumber = !string.IsNullOrWhiteSpace(e.NationalRegisterNumber),
                HasAddress = !string.IsNullOrWhiteSpace(e.Street) && !string.IsNullOrWhiteSpace(e.PostalCode) && !string.IsNullOrWhiteSpace(e.City),
                HasContact = !string.IsNullOrWhiteSpace(e.Email) || !string.IsNullOrWhiteSpace(e.PhoneNumber) || !string.IsNullOrWhiteSpace(e.MobilePhone),
                HasIban = !string.IsNullOrWhiteSpace(e.Iban),
                e.EmploymentStartDate,
                e.ContractTypeId,
                e.DepartmentId,
                HasJobFunction = e.JobFunctions.Any(),
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var foundIds = rows.Select(r => r.Id).ToList();

        var documentRows = await _dbContext.EmployeeDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && !d.IsArchived && foundIds.Contains(d.EmployeeId)
                && RelevantDocumentCategories.Contains(d.Category))
            .Select(d => new { d.EmployeeId, d.Category })
            .Distinct()
            .ToListAsync(ct);
        var documentsByEmployee = documentRows
            .GroupBy(d => d.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Category).ToHashSet());

        var emergencyContactEmployeeIds = (await _dbContext.EmployeeEmergencyContacts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && foundIds.Contains(c.EmployeeId))
            .Select(c => c.EmployeeId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var driverEmployeeIds = (await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && foundIds.Contains(d.EmployeeId))
            .Select(d => d.EmployeeId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        return rows.ToDictionary(r => r.Id, r =>
        {
            var documents = documentsByEmployee.TryGetValue(r.Id, out var categories)
                ? categories
                : [];

            return new CompletenessContext(
                r.DateOfBirth,
                r.HasNationalRegisterNumber,
                r.HasAddress,
                r.HasContact,
                r.HasIban,
                r.EmploymentStartDate,
                r.ContractTypeId,
                r.DepartmentId,
                r.HasJobFunction,
                emergencyContactEmployeeIds.Contains(r.Id),
                documents.Contains(EmployeeDocumentCategory.IdentityCardFront),
                documents.Contains(EmployeeDocumentCategory.Contract),
                documents.Contains(EmployeeDocumentCategory.DrivingLicenceFront),
                driverEmployeeIds.Contains(r.Id));
        });
    }

    private static EmployeeCompletenessDto Evaluate(CompletenessContext context)
    {
        var applicable = Catalogue.Where(r => r.IsApplicable(context)).ToList();
        var missing = applicable
            .Where(r => !r.IsSatisfied(context))
            .Select(r => new CompletenessItemDto(r.Code, r.Label, r.Section))
            .ToList();

        var satisfied = applicable.Count - missing.Count;
        var percentage = applicable.Count == 0
            ? 100
            : (int)Math.Round(100.0 * satisfied / applicable.Count, MidpointRounding.AwayFromZero);

        return new EmployeeCompletenessDto(percentage, missing.Count == 0, missing);
    }
}
