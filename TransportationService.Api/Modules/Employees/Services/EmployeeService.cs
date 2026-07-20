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

    public EmployeeService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ICountryCodeValidator countryValidator, IDriverService driverService, IQualificationService qualificationService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _countryValidator = countryValidator;
        _driverService = driverService;
        _qualificationService = qualificationService;
    }

    private IQueryable<Employee> TenantScoped() =>
        _dbContext.Employees.Where(e => e.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<EmployeeListItemDto>> SearchAsync(
        string? searchText, bool? isActive, Guid? jobFunctionId, Guid? departmentId,
        EmploymentStatus? employmentStatus, bool excludeDrivers, PageRequest page, CancellationToken cancellationToken)
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

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = searchText.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.EmployeeNumber.ToLower().Contains(term) ||
                e.FirstName.ToLower().Contains(term) ||
                e.LastName.ToLower().Contains(term) ||
                e.Email.ToLower().Contains(term) ||
                e.City.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
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
                _dbContext.Drivers.Any(d => d.EmployeeId == e.Id && d.TenantId == tenantId)))
            .ToListAsync(cancellationToken);

        return new PagedResult<EmployeeListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<EmployeeDetailDto?> GetByIdAsync(Guid id, bool includeConfidential, CancellationToken cancellationToken)
    {
        var employee = await TenantScoped()
            .AsNoTracking()
            .Include(e => e.JobFunctions)
            .ThenInclude(j => j.JobFunction)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return employee is null ? null : await MapToDetailAsync(employee, includeConfidential, cancellationToken);
    }

    public async Task<EmployeeDetailDto> CreateAsync(CreateEmployeeRequest request, bool canEditConfidential, CancellationToken cancellationToken)
    {
        ValidateRequired(request.FirstName, request.LastName, request.Email);
        await ValidateLookupCodesAsync(request.NationalityCode, request.PreferredLanguageCode, cancellationToken);
        await EnsureReferencesInTenantAsync(request.DepartmentId, request.ContractTypeId, request.JobFunctionIds, cancellationToken);
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
            Email = request.Email.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            MobilePhone = Trim(request.MobilePhone),
            Street = request.Street.Trim(),
            HouseNumber = request.HouseNumber.Trim(),
            PostalCode = request.PostalCode.Trim(),
            City = request.City.Trim(),
            CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "adresland", cancellationToken, "countryCode"),
            EmergencyContactName = Trim(request.EmergencyContactName),
            EmergencyContactPhone = Trim(request.EmergencyContactPhone),
            EmploymentStartDate = request.EmploymentStartDate,
            EmploymentStatus = request.EmploymentStatus,
            DepartmentId = request.DepartmentId,
            ContractTypeId = request.ContractTypeId,
            IsActive = true,
            Notes = Trim(request.Notes),
        };

        if (canEditConfidential)
        {
            employee.NationalRegisterNumber = EmployeePersonValidators.NormalizeNationalRegisterNumber(request.NationalRegisterNumber);
            employee.Iban = EmployeePersonValidators.NormalizeIban(request.Iban);
            employee.Bic = EmployeePersonValidators.NormalizeBic(request.Bic);
        }

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
            new { employee.EmployeeNumber, employee.FirstName, employee.LastName, employee.EmploymentStatus, employee.IsActive }, cancellationToken);

        if (request.DriverProfile is { } driverProfile)
        {
            var driverResult = await _driverService.CreateAsync(new CreateDriverRequest(
                employee.Id, driverProfile.DriverCategoryId, DriverAvailabilityStatus.Available,
                Notes: driverProfile.Notes), cancellationToken);

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
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        ValidateRequired(request.FirstName, request.LastName, request.Email);
        await ValidateLookupCodesAsync(request.NationalityCode, request.PreferredLanguageCode, cancellationToken);
        await EnsureReferencesInTenantAsync(request.DepartmentId, request.ContractTypeId, request.JobFunctionIds, cancellationToken);

        var oldValues = new { employee.FirstName, employee.LastName, employee.EmploymentStatus, employee.DepartmentId, employee.IsActive };

        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.PlaceOfBirth = Trim(request.PlaceOfBirth);
        employee.NationalityCode = Trim(request.NationalityCode)?.ToUpperInvariant();
        employee.PreferredLanguageCode = Trim(request.PreferredLanguageCode)?.ToLowerInvariant();
        employee.Email = request.Email.Trim();
        employee.PhoneNumber = request.PhoneNumber.Trim();
        employee.MobilePhone = Trim(request.MobilePhone);
        employee.Street = request.Street.Trim();
        employee.HouseNumber = request.HouseNumber.Trim();
        employee.PostalCode = request.PostalCode.Trim();
        employee.City = request.City.Trim();
        employee.CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "adresland", cancellationToken, "countryCode");
        employee.EmergencyContactName = Trim(request.EmergencyContactName);
        employee.EmergencyContactPhone = Trim(request.EmergencyContactPhone);
        employee.EmploymentStartDate = request.EmploymentStartDate;
        employee.EmploymentEndDate = request.EmploymentEndDate;
        employee.EmploymentStatus = request.EmploymentStatus;
        employee.DepartmentId = request.DepartmentId;
        employee.ContractTypeId = request.ContractTypeId;
        employee.Notes = Trim(request.Notes);

        // Confidential fields are only touched by callers holding employees.view_confidential;
        // for everyone else the stored values are preserved untouched.
        if (canEditConfidential)
        {
            employee.NationalRegisterNumber = EmployeePersonValidators.NormalizeNationalRegisterNumber(request.NationalRegisterNumber);
            employee.Iban = EmployeePersonValidators.NormalizeIban(request.Iban);
            employee.Bic = EmployeePersonValidators.NormalizeBic(request.Bic);
        }

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

        await _auditService.RecordAsync(EntityType, employee.Id.ToString(), "Updated", oldValues,
            new { employee.FirstName, employee.LastName, employee.EmploymentStatus, employee.DepartmentId, employee.IsActive }, cancellationToken);

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

    private static void ValidateRequired(string firstName, string lastName, string email)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainValidationException("Voornaam en achternaam zijn verplicht.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new DomainValidationException("Een geldig e-mailadres is verplicht.");
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
        Guid? departmentId, Guid? contractTypeId, IReadOnlyList<Guid>? jobFunctionIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (departmentId is { } dept
            && !await _dbContext.Departments.AnyAsync(d => d.Id == dept && d.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("afdeling");
        }

        if (contractTypeId is { } contract
            && !await _dbContext.ContractTypes.AnyAsync(c => c.Id == contract && c.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("contracttype");
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
            includeConfidential ? e.Bic : null);
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
