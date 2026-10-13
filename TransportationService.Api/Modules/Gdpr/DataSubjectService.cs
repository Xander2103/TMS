using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Gdpr;

public interface IDataSubjectService
{
    /// <summary>
    /// Complete, structured export of everything the system holds about one employee (GDPR
    /// art. 15/20). Null when the employee is unknown for this tenant. Every export is
    /// read-audited as a DataExported event with the Health classification (the payload includes
    /// sick-leave data).
    /// </summary>
    Task<object?> ExportAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Irreversibly anonymises a DEACTIVATED employee (GDPR art. 17 balanced against statutory
    /// retention): identifying and special-category data is erased or overwritten, uploaded
    /// dossier documents and certificates are deleted from storage, the linked user account is
    /// disabled and its sessions revoked. Business/financial structure (employee number,
    /// employment dates, trips, costing) survives — referential integrity is never broken.
    /// Returns an error message, or null on success.
    /// </summary>
    Task<string?> AnonymizeAsync(Guid employeeId, CancellationToken cancellationToken);
}

public class DataSubjectService : IDataSubjectService
{
    private const string AnonymizedMarker = "Geanonimiseerd";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _fileStorage;
    private readonly TimeProvider _timeProvider;

    public DataSubjectService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IFileStorageService fileStorage,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _fileStorage = fileStorage;
        _timeProvider = timeProvider;
    }

    public async Task<object?> ExportAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var employee = await _dbContext.Employees.AsNoTracking()
            .Include(e => e.EmergencyContacts)
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var absences = await _dbContext.Absences.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId)
            .Select(a => new { a.Type, a.StartDate, a.EndDate, a.Status, a.Reason, a.DecisionNote, a.InternalNote, a.AttachmentFileName })
            .ToListAsync(cancellationToken);
        var qualifications = await _dbContext.EmployeeQualifications.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.EmployeeId == employeeId)
            .Select(q => new { q.QualificationTypeId, q.DocumentNumber, q.ObtainedDate, q.ExpiryDate, q.Status, q.Notes })
            .ToListAsync(cancellationToken);
        var documents = await _dbContext.EmployeeDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .Select(d => new { d.Category, d.FileName, d.CustomLabel, d.ExpiryDate, d.Notes, d.CreatedAt })
            .ToListAsync(cancellationToken);
        var notes = await _dbContext.EmployeeNotes.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.EmployeeId == employeeId)
            .Select(n => new { n.Text, n.CreatedAt })
            .ToListAsync(cancellationToken);
        var account = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.EmployeeId == employeeId)
            .Select(u => new { u.Email, u.IsActive, u.CreatedAt })
            .ToListAsync(cancellationToken);

        // Read-audit: a full dossier leaving the application is the heaviest read there is.
        await _auditService.RecordExportAsync(
            $"gdpr-dossier:{employeeId}", new { employeeId }, cancellationToken,
            SecurityAuditEvents.Classification.Health);

        return new
        {
            ExportedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Profile = employee,
            Absences = absences,
            Qualifications = qualifications,
            Documents = documents,
            Notes = notes,
            Accounts = account,
        };
    }

    public async Task<string?> AnonymizeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var employee = await _dbContext.Employees
            .Include(e => e.EmergencyContacts)
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return "De medewerker bestaat niet.";
        }

        if (employee.IsActive)
        {
            return "Alleen een gedeactiveerde medewerker kan worden geanonimiseerd.";
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // --- Identifying / special-category fields on the person record -----------------------
        employee.FirstName = AnonymizedMarker;
        employee.LastName = $"Ex-medewerker {employee.EmployeeNumber}";
        employee.DateOfBirth = new DateOnly(1900, 1, 1);
        employee.PlaceOfBirth = null;
        employee.NationalityCode = null;
        employee.NationalRegisterNumber = null;
        employee.IdentityCardNumber = null;
        employee.PreferredLanguageCode = null;
        employee.Email = string.Empty;
        employee.PhoneNumber = string.Empty;
        employee.MobilePhone = null;
        employee.Street = string.Empty;
        employee.HouseNumber = string.Empty;
        employee.PostalCode = string.Empty;
        employee.City = string.Empty;
        employee.CountryCode = null;
        employee.EmergencyContactName = null;
        employee.EmergencyContactPhone = null;
        employee.CivilStatus = null;
        employee.DependentChildren = null;
        employee.Iban = null;
        employee.Bic = null;
        employee.DimonaNumber = null;
        employee.Notes = null;
        _dbContext.RemoveRange(employee.EmergencyContacts);

        // --- Free text and health data on absences (rows stay: capacity history is business) ---
        var absences = await _dbContext.Absences
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        foreach (var absence in absences)
        {
            absence.Reason = null;
            absence.InternalNote = null;
            absence.DecisionNote = null;
            if (absence.AttachmentPath is { } attachmentPath)
            {
                await _fileStorage.DeleteAsync(attachmentPath, cancellationToken);
                absence.AttachmentPath = null;
                absence.AttachmentFileName = null;
            }
        }

        // --- Uploaded dossier documents: file AND row. HARD delete via ExecuteDelete: the
        // soft-delete interceptor would only hide the rows, and GDPR erasure means gone. --------
        var documents = await _dbContext.EmployeeDocuments.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            await _fileStorage.DeleteAsync(document.StorageKey, cancellationToken);
        }

        await _dbContext.EmployeeDocuments.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .ExecuteDeleteAsync(cancellationToken);

        // --- Free-text notes: hard delete for the same reason ---------------------------------
        await _dbContext.EmployeeNotes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.EmployeeId == employeeId)
            .ExecuteDeleteAsync(cancellationToken);

        // --- Qualification certificates (rows stay: compliance history is business) -----------
        var qualifications = await _dbContext.EmployeeQualifications
            .Where(q => q.TenantId == tenantId && q.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        foreach (var qualification in qualifications)
        {
            qualification.DocumentNumber = null;
            qualification.Notes = null;
            if (qualification.DocumentPath is { } documentPath)
            {
                await _fileStorage.DeleteAsync(documentPath, cancellationToken);
                qualification.DocumentPath = null;
            }
        }

        // --- Linked account: disabled, unrecognisable, every session dead ---------------------
        var users = await _dbContext.Users
            .Where(u => u.TenantId == tenantId && u.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            user.IsActive = false;
            user.Email = $"geanonimiseerd+{user.Id:N}@invalid.local";
            user.FirstName = AnonymizedMarker;
            user.LastName = AnonymizedMarker;
            user.SecurityStamp = Guid.NewGuid();
            user.UpdatedAt = now;

            var refreshTokens = await _dbContext.Set<Authentication.Entities.RefreshToken>()
                .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in refreshTokens)
            {
                token.RevokedAt = now;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // The audit row deliberately carries NO before-values: the point of the operation is
        // that the erased data stops existing, including in the trail going forward.
        await _auditService.RecordAsync("Employee", employeeId.ToString(), "Anonymized", null,
            new { employee.EmployeeNumber, Classification = SecurityAuditEvents.Classification.Confidential },
            cancellationToken);

        return null;
    }
}
