using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public class EmployeeService : IEmployeeService
{
    private const string EntityType = "Employee";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ICountryCodeValidator _countryValidator;
    private readonly IDriverService _driverService;
    private readonly IQualificationService _qualificationService;
    private readonly IEmployeeCompletenessService _completenessService;

    public EmployeeService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ICountryCodeValidator countryValidator, IDriverService driverService, IQualificationService qualificationService,
        IEmployeeCompletenessService completenessService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _countryValidator = countryValidator;
        _driverService = driverService;
        _qualificationService = qualificationService;
        _completenessService = completenessService;
    }

    private IQueryable<Employee> TenantScoped() =>
        _dbContext.Employees.Where(e => e.TenantId == _tenantContext.TenantId);

    /// <summary>
    /// Server-side sort for the personnel list (HR maturity wave, task 6). Unknown/missing
    /// values fall back to name_asc — never an error. Every branch ends with LastName, FirstName
    /// as a stable tie-breaker so paging never reshuffles rows with equal primary keys.
    /// Department/function keys use correlated subqueries (no navigation property exists for
    /// Department, and job functions are a many-to-many) with an explicit "is null" flag ordered
    /// first, because SQLite and PostgreSQL disagree on default NULL ordering for ASC.
    /// </summary>
    private IOrderedQueryable<Employee> ApplySort(IQueryable<Employee> query, string? sort, Guid tenantId)
    {
        switch (sort?.Trim().ToLowerInvariant())
        {
            case "name_desc":
                return query.OrderByDescending(e => e.LastName).ThenByDescending(e => e.FirstName);

            case "number":
                return query.OrderBy(e => e.EmployeeNumber)
                    .ThenBy(e => e.LastName).ThenBy(e => e.FirstName);

            case "recent":
                return query.OrderByDescending(e => e.CreatedAt)
                    .ThenBy(e => e.LastName).ThenBy(e => e.FirstName);

            case "department":
                return query
                    .OrderBy(e => _dbContext.Departments
                        .Where(d => d.Id == e.DepartmentId && d.TenantId == tenantId)
                        .Select(d => d.Name)
                        .FirstOrDefault() == null ? 1 : 0)
                    .ThenBy(e => _dbContext.Departments
                        .Where(d => d.Id == e.DepartmentId && d.TenantId == tenantId)
                        .Select(d => d.Name)
                        .FirstOrDefault())
                    .ThenBy(e => e.LastName).ThenBy(e => e.FirstName);

            case "function":
                // "First function" must match the same definition the list projection uses
                // below (OrderBy SortOrder, ThenBy Name) — not the alphabetically smallest name.
                return query
                    .OrderBy(e => e.JobFunctions
                        .Where(j => j.JobFunction != null)
                        .OrderBy(j => j.JobFunction!.SortOrder).ThenBy(j => j.JobFunction!.Name)
                        .Select(j => j.JobFunction!.Name)
                        .FirstOrDefault() == null ? 1 : 0)
                    .ThenBy(e => e.JobFunctions
                        .Where(j => j.JobFunction != null)
                        .OrderBy(j => j.JobFunction!.SortOrder).ThenBy(j => j.JobFunction!.Name)
                        .Select(j => j.JobFunction!.Name)
                        .FirstOrDefault())
                    .ThenBy(e => e.LastName).ThenBy(e => e.FirstName);

            case "status":
                return query.OrderByDescending(e => e.IsActive)
                    .ThenBy(e => e.EmploymentStatus)
                    .ThenBy(e => e.LastName).ThenBy(e => e.FirstName);

            default: // name_asc, null, or anything unrecognised
                return query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName);
        }
    }

    public async Task<PagedResult<EmployeeListItemDto>> SearchAsync(
        string? searchText, bool? isActive, Guid? jobFunctionId, Guid? departmentId,
        EmploymentStatus? employmentStatus, bool excludeDrivers, bool? hasDriverProfile, string? sort,
        bool incompleteOnly, PageRequest page, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = TenantScoped().AsNoTracking();

        if (isActive is { } activeFilter)
        {
            query = query.Where(e => e.IsActive == activeFilter);
        }

        if (excludeDrivers)
        {
            query = query.Where(e => !_dbContext.Drivers.Any(d => d.EmployeeId == e.Id && d.TenantId == tenantId));
        }

        if (hasDriverProfile is { } driverFilter)
        {
            query = driverFilter
                ? query.Where(e => _dbContext.Drivers.Any(d => d.EmployeeId == e.Id && d.TenantId == tenantId))
                : query.Where(e => !_dbContext.Drivers.Any(d => d.EmployeeId == e.Id && d.TenantId == tenantId));
        }

        if (jobFunctionId is { } functionFilter)
        {
            query = query.Where(e => e.JobFunctions.Any(j => j.JobFunctionId == functionFilter));
        }

        if (departmentId is { } departmentFilter)
        {
            query = query.Where(e => e.DepartmentId == departmentFilter);
        }

        if (employmentStatus is { } statusFilter)
        {
            query = query.Where(e => e.EmploymentStatus == statusFilter);
        }

        if (incompleteOnly)
        {
            // HR maturity wave, task 9: the completeness engine has no SQL-level "is complete"
            // predicate (it's evaluated in memory, per employee, from several batched sources —
            // see IEmployeeCompletenessService), so filtering "onvolledige dossiers" means
            // materialising the tenant-wide incomplete-id set first and then applying a plain
            // WHERE Id IN (...) before paging. That's one extra round-trip per request and an
            // in-memory id list sized to the tenant's ACTIVE headcount — fine up to a few
            // thousand employees; a tenant far beyond that would need the engine itself to push
            // its checks into SQL instead of returning ids to filter by.
            var incompleteIds = await _completenessService.FindIncompleteEmployeeIdsAsync(cancellationToken);
            query = query.Where(e => incompleteIds.Contains(e.Id));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = searchText.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.EmployeeNumber.ToLower().Contains(term) ||
                e.FirstName.ToLower().Contains(term) ||
                e.LastName.ToLower().Contains(term) ||
                (e.Email != null && e.Email.ToLower().Contains(term)) ||
                (e.City != null && e.City.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await ApplySort(query, sort, tenantId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(e => new EmployeeListItemDto(
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.JobFunctions
                    .Where(j => j.JobFunction != null)
                    .OrderBy(j => j.JobFunction!.SortOrder).ThenBy(j => j.JobFunction!.Name)
                    .Select(j => j.JobFunction!.Name)
                    .ToList(),
                _dbContext.Departments
                    .Where(d => d.Id == e.DepartmentId && d.TenantId == tenantId)
                    .Select(d => d.Name)
                    .FirstOrDefault(),
                e.EmploymentStatus, e.IsActive,
                _dbContext.Drivers.Any(d => d.EmployeeId == e.Id && d.TenantId == tenantId),
                _dbContext.Drivers
                    .Where(d => d.EmployeeId == e.Id && d.TenantId == tenantId)
                    .Select(d => (bool?)d.IsBlocked)
                    .FirstOrDefault(),
                _dbContext.Drivers
                    .Where(d => d.EmployeeId == e.Id && d.TenantId == tenantId)
                    .Select(d => (DriverAvailabilityStatus?)d.AvailabilityStatus)
                    .FirstOrDefault(),
                // CompletenessPercentage is filled below via `with` (one batched query for the
                // whole page); passed positionally here because expression trees (this Select
                // runs through EF's LINQ provider) don't allow out-of-position named arguments.
                0,
                e.EmploymentEndDate))
            .ToListAsync(cancellationToken);

        // One batched query for the whole page instead of one per row (HR maturity wave §2.1).
        var percentages = await _completenessService.GetPercentagesAsync(
            items.Select(i => i.Id).ToList(), cancellationToken);
        items = items
            .Select(i => i with { CompletenessPercentage = percentages.GetValueOrDefault(i.Id) })
            .ToList();

        return new PagedResult<EmployeeListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<EmployeeDetailDto?> GetByIdAsync(Guid id, bool includeConfidential, CancellationToken cancellationToken)
    {
        var employee = await TenantScoped()
            .AsNoTracking()
            .Include(e => e.JobFunctions)
            .ThenInclude(j => j.JobFunction)
            .Include(e => e.EmergencyContacts)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return employee is null ? null : await MapToDetailAsync(employee, includeConfidential, cancellationToken);
    }

    public async Task<EmployeeDetailDto> CreateAsync(CreateEmployeeRequest request, bool canEditConfidential, CancellationToken cancellationToken)
    {
        ValidateRequired(request.FirstName, request.LastName, request.Email,
            request.EmploymentStartDate, request.EmploymentEndDate);
        await ValidateLookupCodesAsync(request.NationalityCode, request.PreferredLanguageCode, cancellationToken);
        await EnsureReferencesInTenantAsync(
            request.DepartmentId, request.ContractTypeId, request.EmploymentEndDate, request.JobFunctionIds, cancellationToken);
        await EnsureQualificationTypesExistAsync(request.Qualifications, cancellationToken);

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            PlaceOfBirth = Trim(request.PlaceOfBirth),
            NationalityCode = Trim(request.NationalityCode)?.ToUpperInvariant(),
            PreferredLanguageCode = Trim(request.PreferredLanguageCode)?.ToLowerInvariant(),
            Email = Trim(request.Email),
            PhoneNumber = Trim(request.PhoneNumber),
            MobilePhone = Trim(request.MobilePhone),
            Street = Trim(request.Street),
            HouseNumber = Trim(request.HouseNumber),
            PostalCode = Trim(request.PostalCode),
            City = Trim(request.City),
            CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "adresland", cancellationToken, "countryCode"),
            EmergencyContactName = Trim(request.EmergencyContactName),
            EmergencyContactPhone = Trim(request.EmergencyContactPhone),
            EmploymentStartDate = request.EmploymentStartDate,
            EmploymentEndDate = request.EmploymentEndDate,
            EmploymentStatus = request.EmploymentStatus,
            DepartmentId = request.DepartmentId,
            ContractTypeId = request.ContractTypeId,
            CivilStatus = request.CivilStatus,
            DependentChildren = ValidateDependentChildren(request.DependentChildren),
            DimonaNumber = Trim(request.DimonaNumber),
            IsActive = true,
            // Legacy Employee.Notes stays null: EmployeeNote records are the source of truth
            // (request.Notes is accepted for older clients but deliberately ignored).
        };

        if (canEditConfidential)
        {
            employee.NationalRegisterNumber = EmployeePersonValidators.NormalizeNationalRegisterNumber(request.NationalRegisterNumber);
            employee.Iban = EmployeePersonValidators.NormalizeIban(request.Iban);
            employee.Bic = EmployeePersonValidators.NormalizeBic(request.Bic);
            employee.IdentityCardNumber = Trim(request.IdentityCardNumber);
        }

        SyncEmergencyContacts(employee, request.EmergencyContacts,
            request.EmergencyContactName, request.EmergencyContactPhone);

        foreach (var functionId in (request.JobFunctionIds ?? []).Distinct())
        {
            employee.JobFunctions.Add(new EmployeeJobFunction { EmployeeId = employee.Id, JobFunctionId = functionId });
        }

        // Atomic workflow: the employee and (optionally) its driver profile commit together,
        // so a rejected driver reference never leaves a half-created person behind.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Employees.Add(employee);
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => employee.EmployeeNumber = GenerateEmployeeNumber(settings),
            cancellationToken);

        await _auditService.RecordAsync(EntityType, employee.Id.ToString(), "Created", null,
            await BuildAuditSnapshotAsync(employee, employee.JobFunctions.Select(j => j.JobFunctionId).ToList(), cancellationToken),
            cancellationToken);

        if (request.DriverProfile is { } driverProfile)
        {
            var driverResult = await _driverService.CreateAsync(new CreateDriverRequest(
                employee.Id, driverProfile.DriverCategoryId, DriverAvailabilityStatus.Available,
                Notes: driverProfile.Notes, DriverCategoryIds: driverProfile.DriverCategoryIds), cancellationToken);

            if (driverResult.Outcome != DriverOperationOutcome.Success)
            {
                // Disposing the transaction rolls back the employee as well.
                throw new InvalidTenantReferenceException("chauffeurscategorie");
            }
        }

        // Optional qualifications reuse the qualification module's own create use case; they
        // commit (or roll back) together with the employee.
        foreach (var qualificationRequest in request.Qualifications ?? [])
        {
            await _qualificationService.CreateAsync(employee.Id, qualificationRequest, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // Reload so lookup navigations (job-function names) are populated in the response.
        return (await GetByIdAsync(employee.Id, canEditConfidential, cancellationToken))!;
    }

    public async Task<EmployeeDetailDto?> UpdateAsync(Guid id, UpdateEmployeeRequest request, bool canEditConfidential, CancellationToken cancellationToken)
    {
        var employee = await TenantScoped()
            .Include(e => e.JobFunctions)
            .Include(e => e.EmergencyContacts)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        ValidateRequired(request.FirstName, request.LastName, request.Email,
            request.EmploymentStartDate, request.EmploymentEndDate);
        await ValidateLookupCodesAsync(request.NationalityCode, request.PreferredLanguageCode, cancellationToken);
        await EnsureReferencesInTenantAsync(
            request.DepartmentId, request.ContractTypeId, request.EmploymentEndDate, request.JobFunctionIds, cancellationToken);

        // Full before-image for the personnel history (corrections wave §4): every meaningful
        // field with resolved names, confidential values masked. Captured BEFORE any mutation.
        var oldSnapshot = await BuildAuditSnapshotAsync(
            employee, employee.JobFunctions.Select(j => j.JobFunctionId).ToList(), cancellationToken);

        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.PlaceOfBirth = Trim(request.PlaceOfBirth);
        employee.NationalityCode = Trim(request.NationalityCode)?.ToUpperInvariant();
        employee.PreferredLanguageCode = Trim(request.PreferredLanguageCode)?.ToLowerInvariant();
        employee.Email = Trim(request.Email);
        employee.PhoneNumber = Trim(request.PhoneNumber);
        employee.MobilePhone = Trim(request.MobilePhone);
        employee.Street = Trim(request.Street);
        employee.HouseNumber = Trim(request.HouseNumber);
        employee.PostalCode = Trim(request.PostalCode);
        employee.City = Trim(request.City);
        employee.CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "adresland", cancellationToken, "countryCode");
        employee.EmergencyContactName = Trim(request.EmergencyContactName);
        employee.EmergencyContactPhone = Trim(request.EmergencyContactPhone);
        employee.EmploymentStartDate = request.EmploymentStartDate;
        employee.EmploymentEndDate = request.EmploymentEndDate;
        employee.EmploymentStatus = request.EmploymentStatus;
        employee.DepartmentId = request.DepartmentId;
        employee.ContractTypeId = request.ContractTypeId;
        employee.CivilStatus = request.CivilStatus;
        employee.DependentChildren = ValidateDependentChildren(request.DependentChildren);
        employee.DimonaNumber = Trim(request.DimonaNumber);
        // Legacy Employee.Notes is intentionally NOT written: EmployeeNote records are the
        // source of truth and the stored legacy value stays preserved as historical data.

        // Confidential fields are only touched by callers holding employees.view_confidential;
        // for everyone else the stored values are preserved untouched.
        if (canEditConfidential)
        {
            employee.NationalRegisterNumber = EmployeePersonValidators.NormalizeNationalRegisterNumber(request.NationalRegisterNumber);
            employee.Iban = EmployeePersonValidators.NormalizeIban(request.Iban);
            employee.Bic = EmployeePersonValidators.NormalizeBic(request.Bic);
            employee.IdentityCardNumber = Trim(request.IdentityCardNumber);
        }

        SyncEmergencyContacts(employee, request.EmergencyContacts,
            request.EmergencyContactName, request.EmergencyContactPhone);

        var requestedFunctionIds = (request.JobFunctionIds ?? []).Distinct().ToHashSet();
        var toRemove = employee.JobFunctions.Where(j => !requestedFunctionIds.Contains(j.JobFunctionId)).ToList();
        foreach (var link in toRemove)
        {
            employee.JobFunctions.Remove(link);
            _dbContext.Remove(link);
        }

        var existingIds = employee.JobFunctions.Select(j => j.JobFunctionId).ToHashSet();
        foreach (var functionId in requestedFunctionIds.Where(idToAdd => !existingIds.Contains(idToAdd)))
        {
            // Add through the DbSet so EF treats the composite-keyed join row as Added.
            _dbContext.Set<EmployeeJobFunction>().Add(new EmployeeJobFunction { EmployeeId = employee.Id, JobFunctionId = functionId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var newSnapshot = await BuildAuditSnapshotAsync(employee, requestedFunctionIds.ToList(), cancellationToken);
        await _auditService.RecordAsync(EntityType, employee.Id.ToString(), "Updated", oldSnapshot, newSnapshot, cancellationToken);

        return (await GetByIdAsync(id, canEditConfidential, cancellationToken))!;
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await TenantScoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return false;
        }

        employee.IsActive = false;
        employee.EmploymentEndDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
        if (employee.EmploymentStatus is EmploymentStatus.Active or EmploymentStatus.OnLeave)
        {
            employee.EmploymentStatus = EmploymentStatus.Terminated;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, employee.Id.ToString(), "Deactivated", new { IsActive = true },
            new { employee.IsActive, employee.EmploymentEndDate, employee.EmploymentStatus }, cancellationToken);

        return true;
    }

    public async Task<bool> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await TenantScoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return false;
        }

        employee.IsActive = true;
        employee.EmploymentEndDate = null;
        if (employee.EmploymentStatus == EmploymentStatus.Terminated)
        {
            employee.EmploymentStatus = EmploymentStatus.Active;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, employee.Id.ToString(), "Reactivated", new { IsActive = false },
            new { employee.IsActive, employee.EmploymentStatus }, cancellationToken);

        return true;
    }

    /// <summary>
    /// Full audit snapshot for the personnel history (corrections wave §4): every meaningful
    /// field, lookup ids resolved to names at write time (so history never breaks when master
    /// data is renamed/removed) and confidential values masked — the history shows THAT they
    /// changed, never what they are. Keys are field identifiers translated to Dutch labels by
    /// the history projection.
    /// </summary>
    private async Task<Dictionary<string, object?>> BuildAuditSnapshotAsync(
        Employee employee, IReadOnlyCollection<Guid> jobFunctionIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        string? departmentName = employee.DepartmentId is { } departmentId
            ? await _dbContext.Departments.AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.Id == departmentId).Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        string? contractTypeName = employee.ContractTypeId is { } contractTypeId
            ? await _dbContext.ContractTypes.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == contractTypeId).Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var functionNames = jobFunctionIds.Count == 0
            ? []
            : await _dbContext.JobFunctions.AsNoTracking()
                .Where(f => f.TenantId == tenantId && jobFunctionIds.Contains(f.Id))
                .OrderBy(f => f.Name).Select(f => f.Name)
                .ToListAsync(cancellationToken);
        var contacts = employee.EmergencyContacts
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Priority)
            .Select(c => string.Join(" ", new[] { c.Name, string.IsNullOrWhiteSpace(c.Relationship) ? null : $"({c.Relationship})", c.Phone ?? c.MobilePhone }.Where(p => !string.IsNullOrWhiteSpace(p))))
            .ToList();

        return new Dictionary<string, object?>
        {
            ["EmployeeNumber"] = employee.EmployeeNumber,
            ["FirstName"] = employee.FirstName,
            ["LastName"] = employee.LastName,
            ["DateOfBirth"] = employee.DateOfBirth?.ToString("yyyy-MM-dd"),
            ["PlaceOfBirth"] = employee.PlaceOfBirth,
            ["Nationality"] = employee.NationalityCode,
            ["Language"] = employee.PreferredLanguageCode,
            ["Email"] = employee.Email,
            ["PhoneNumber"] = employee.PhoneNumber,
            ["MobilePhone"] = employee.MobilePhone,
            ["Street"] = employee.Street,
            ["HouseNumber"] = employee.HouseNumber,
            ["PostalCode"] = employee.PostalCode,
            ["City"] = employee.City,
            ["Country"] = employee.CountryCode,
            ["CivilStatus"] = employee.CivilStatus?.ToString(),
            ["DependentChildren"] = employee.DependentChildren,
            ["EmploymentStartDate"] = employee.EmploymentStartDate?.ToString("yyyy-MM-dd"),
            ["EmploymentEndDate"] = employee.EmploymentEndDate?.ToString("yyyy-MM-dd"),
            ["EmploymentStatus"] = employee.EmploymentStatus.ToString(),
            ["Department"] = departmentName,
            ["ContractType"] = contractTypeName,
            ["JobFunctions"] = functionNames.Count == 0 ? null : string.Join(", ", functionNames),
            ["DimonaNumber"] = employee.DimonaNumber,
            ["IsActive"] = employee.IsActive,
            ["Notes"] = employee.Notes,
            ["EmergencyContacts"] = contacts.Count == 0 ? null : string.Join("; ", contacts),
            ["NationalRegisterNumber"] = MaskConfidential(employee.NationalRegisterNumber, 2),
            ["IdentityCardNumber"] = MaskConfidential(employee.IdentityCardNumber, 2),
            ["Iban"] = MaskConfidential(employee.Iban, 4),
            ["Bic"] = employee.Bic,
        };
    }

    /// <summary>"••• 89"-style masking: a change stays detectable without exposing the value.</summary>
    private static string? MaskConfidential(string? value, int visibleSuffix) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= visibleSuffix ? "•••" : $"•••{value[^visibleSuffix..]}";

    /// <summary>
    /// Spec §13: only first and last name may block a save. Everything else is optional and
    /// only format-checked when a value was actually supplied.
    /// </summary>
    private static void ValidateRequired(
        string firstName, string lastName, string? email, DateOnly? startDate, DateOnly? endDate)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainValidationException("Voornaam en achternaam zijn verplicht.");
        }

        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
        {
            throw new DomainValidationException("email", "Geef een geldig e-mailadres op.");
        }

        if (startDate is { } start && endDate is { } end && end < start)
        {
            throw new DomainValidationException("employmentEndDate", "De einddatum moet na de startdatum liggen.");
        }
    }

    private async Task ValidateLookupCodesAsync(string? nationalityCode, string? languageCode, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (Trim(nationalityCode) is { } nationality)
        {
            var upper = nationality.ToUpperInvariant();
            if (!await _dbContext.Nationalities.AnyAsync(n => n.TenantId == tenantId && n.Code.ToUpper() == upper, cancellationToken))
            {
                throw new DomainValidationException($"Onbekende nationaliteit '{nationality}'.");
            }
        }

        if (Trim(languageCode) is { } language)
        {
            var lower = language.ToLowerInvariant();
            if (!await _dbContext.Languages.AnyAsync(l => l.TenantId == tenantId && l.Code.ToLower() == lower, cancellationToken))
            {
                throw new DomainValidationException($"Onbekende taal '{language}'.");
            }
        }
    }

    /// <summary>
    /// Qualification types are a global catalog; validating up front turns an unknown id into
    /// a field-level validation error instead of a database FK failure mid-transaction.
    /// </summary>
    private async Task EnsureQualificationTypesExistAsync(
        IReadOnlyList<CreateEmployeeQualificationRequest>? qualifications, CancellationToken cancellationToken)
    {
        var typeIds = (qualifications ?? []).Select(q => q.QualificationTypeId).Distinct().ToList();
        if (typeIds.Count == 0)
        {
            return;
        }

        var known = await _dbContext.QualificationTypes
            .Where(t => typeIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        if (known.Count != typeIds.Count)
        {
            throw new DomainValidationException("qualifications",
                "Eén of meer gekozen kwalificatietypes bestaan niet.");
        }
    }

    private async Task EnsureReferencesInTenantAsync(
        Guid? departmentId, Guid? contractTypeId, DateOnly? employmentEndDate,
        IReadOnlyList<Guid>? jobFunctionIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (departmentId is { } dept
            && !await _dbContext.Departments.AnyAsync(d => d.Id == dept && d.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("afdeling");
        }

        if (contractTypeId is { } contract)
        {
            // Single fetch doubles as both the tenant-reference check and the source for the
            // end-date requirement (HR maturity wave §5): fixed-term/temp/student contract
            // types demand an EmploymentEndDate, open-ended ones don't.
            var contractType = await _dbContext.ContractTypes
                .Where(c => c.Id == contract && c.TenantId == tenantId)
                .Select(c => new { c.RequiresEndDate })
                .FirstOrDefaultAsync(cancellationToken);
            if (contractType is null)
            {
                throw new InvalidTenantReferenceException("contracttype");
            }

            if (contractType.RequiresEndDate && employmentEndDate is null)
            {
                throw new DomainValidationException("employmentEndDate", "Einddatum is verplicht voor dit contracttype.");
            }
        }

        var functionIds = (jobFunctionIds ?? []).Distinct().ToList();
        if (functionIds.Count > 0)
        {
            var known = await _dbContext.JobFunctions
                .CountAsync(f => functionIds.Contains(f.Id) && f.TenantId == tenantId, cancellationToken);
            if (known != functionIds.Count)
            {
                throw new InvalidTenantReferenceException("functie");
            }
        }
    }

    private async Task<EmployeeDetailDto> MapToDetailAsync(Employee e, bool includeConfidential, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var departmentName = e.DepartmentId is { } deptId
            ? await _dbContext.Departments.Where(d => d.Id == deptId && d.TenantId == tenantId)
                .Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var contractTypeName = e.ContractTypeId is { } contractId
            ? await _dbContext.ContractTypes.Where(c => c.Id == contractId && c.TenantId == tenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var driverId = await _dbContext.Drivers
            .Where(d => d.EmployeeId == e.Id && d.TenantId == tenantId)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var orderedFunctions = e.JobFunctions
            .Where(j => j.JobFunction is not null)
            .OrderBy(j => j.JobFunction!.SortOrder).ThenBy(j => j.JobFunction!.Name)
            .ToList();

        var completeness = await _completenessService.GetForEmployeeAsync(e.Id, cancellationToken);

        return new EmployeeDetailDto(
            e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
            e.DateOfBirth, e.PlaceOfBirth, e.NationalityCode, e.PreferredLanguageCode,
            e.Email, e.PhoneNumber, e.MobilePhone,
            e.Street, e.HouseNumber, e.PostalCode, e.City, e.CountryCode,
            e.EmergencyContactName, e.EmergencyContactPhone,
            e.EmploymentStartDate, e.EmploymentEndDate, e.EmploymentStatus,
            e.DepartmentId, departmentName, e.ContractTypeId, contractTypeName,
            orderedFunctions.Select(j => j.JobFunctionId).ToList(),
            orderedFunctions.Select(j => j.JobFunction!.Name).ToList(),
            e.IsActive, e.Notes,
            driverId,
            includeConfidential ? e.NationalRegisterNumber : null,
            includeConfidential ? e.Iban : null,
            includeConfidential ? e.Bic : null,
            e.CivilStatus, e.DependentChildren, e.DimonaNumber,
            includeConfidential ? e.IdentityCardNumber : null,
            e.EmergencyContacts
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Priority).ThenBy(c => c.Name)
                .Select(c => new EmployeeEmergencyContactDto(c.Id, c.Name, c.Relationship, c.Phone, c.MobilePhone, c.Notes, c.Priority))
                .ToList(),
            completeness);
    }

    private static int? ValidateDependentChildren(int? value)
    {
        if (value is { } v and (< 0 or > 30))
        {
            throw new Common.DomainValidationException("dependentChildren", "Het aantal kinderen ten laste moet tussen 0 en 30 liggen.");
        }

        return value;
    }

    /// <summary>
    /// Wholesale sync of the emergency-contact collection. When the request carries no
    /// collection (older clients), the legacy single name/phone pair drives the priority-1
    /// row instead; either way the legacy columns end up mirroring the priority-1 contact.
    /// </summary>
    private void SyncEmergencyContacts(Employee employee,
        IReadOnlyList<EmployeeEmergencyContactInput>? contacts, string? legacyName, string? legacyPhone)
    {
        if (contacts is null)
        {
            // Legacy pair only: keep the priority-1 row aligned with it.
            var primary = employee.EmergencyContacts.Where(c => !c.IsDeleted).OrderBy(c => c.Priority).FirstOrDefault();
            var name = Trim(legacyName);
            if (name is null)
            {
                return;
            }

            if (primary is null)
            {
                _dbContext.Set<EmployeeEmergencyContact>().Add(new EmployeeEmergencyContact
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employee.Id,
                    Name = name,
                    Phone = Trim(legacyPhone),
                    Priority = 1,
                });
            }
            else
            {
                primary.Name = name;
                primary.Phone = Trim(legacyPhone);
            }

            return;
        }

        foreach (var input in contacts)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                throw new Common.DomainValidationException("emergencyContacts", "Elke noodcontactpersoon heeft een naam nodig.");
            }
        }

        var keptIds = contacts.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToHashSet();
        foreach (var removed in employee.EmergencyContacts.Where(c => !keptIds.Contains(c.Id)).ToList())
        {
            employee.EmergencyContacts.Remove(removed);
            _dbContext.Remove(removed);
        }

        var byId = employee.EmergencyContacts.ToDictionary(c => c.Id);
        foreach (var input in contacts)
        {
            if (input.Id is { } contactId && byId.TryGetValue(contactId, out var existing))
            {
                existing.Name = input.Name.Trim();
                existing.Relationship = Trim(input.Relationship);
                existing.Phone = Trim(input.Phone);
                existing.MobilePhone = Trim(input.MobilePhone);
                existing.Notes = Trim(input.Notes);
                existing.Priority = Math.Max(1, input.Priority);
            }
            else
            {
                _dbContext.Set<EmployeeEmergencyContact>().Add(new EmployeeEmergencyContact
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employee.Id,
                    Name = input.Name.Trim(),
                    Relationship = Trim(input.Relationship),
                    Phone = Trim(input.Phone),
                    MobilePhone = Trim(input.MobilePhone),
                    Notes = Trim(input.Notes),
                    Priority = Math.Max(1, input.Priority),
                });
            }
        }

        // Legacy mirror: the priority-1 contact drives the historic single pair.
        var first = contacts.OrderBy(c => c.Priority).FirstOrDefault();
        employee.EmergencyContactName = first is null ? null : first.Name.Trim();
        employee.EmergencyContactPhone = first is null ? null : (Trim(first.Phone) ?? Trim(first.MobilePhone));
    }

    private static string GenerateEmployeeNumber(Tenancy.Entities.TenantSettings? settings)
    {
        if (settings is null)
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        var number = $"{settings.EmployeeNumberPrefix}{settings.EmployeeNumberNextValue:D4}";
        settings.EmployeeNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
