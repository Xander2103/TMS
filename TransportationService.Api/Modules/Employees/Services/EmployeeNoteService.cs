using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record EmployeeNoteDto(
    Guid Id,
    Guid EmployeeId,
    string Text,
    bool IsPinnedToDashboard,
    DateTime CreatedAt,
    Guid? CreatedByUserId,
    DateTime UpdatedAt,
    Guid? UpdatedByUserId);

public interface IEmployeeNoteService
{
    /// <summary>Newest first. Null = employee unknown for this tenant.</summary>
    Task<IReadOnlyList<EmployeeNoteDto>?> ListAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<EmployeeNoteDto?> CreateAsync(Guid employeeId, string text, CancellationToken cancellationToken);
    Task<EmployeeNoteDto?> UpdateAsync(Guid employeeId, Guid noteId, string text, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid employeeId, Guid noteId, CancellationToken cancellationToken);
    Task<EmployeeNoteDto?> SetPinnedAsync(Guid employeeId, Guid noteId, bool pinned, CancellationToken cancellationToken);
}

/// <summary>
/// Multiple free-text notes per employee (corrections wave §4), replacing the legacy single
/// Employee.Notes field. Every note can be individually pinned to the company dashboard.
/// </summary>
public class EmployeeNoteService : IEmployeeNoteService
{
    private const string EntityType = "EmployeeNote";
    private const int MaxTextLength = 4000;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public EmployeeNoteService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<EmployeeNoteDto>?> ListAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        var notes = await _dbContext.EmployeeNotes.AsNoTracking()
            .Where(n => n.TenantId == _tenantContext.TenantId && n.EmployeeId == employeeId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        return notes.Select(Map).ToList();
    }

    public async Task<EmployeeNoteDto?> CreateAsync(Guid employeeId, string text, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        var trimmed = Validate(text);

        var note = new EmployeeNote
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            Text = trimmed,
            IsPinnedToDashboard = false,
        };
        _dbContext.EmployeeNotes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), "Created", null,
            new { note.EmployeeId, note.Text }, cancellationToken);

        return Map(note);
    }

    public async Task<EmployeeNoteDto?> UpdateAsync(Guid employeeId, Guid noteId, string text, CancellationToken cancellationToken)
    {
        var note = await FindAsync(employeeId, noteId, cancellationToken);
        if (note is null)
        {
            return null;
        }

        var trimmed = Validate(text);
        var before = note.Text;
        note.Text = trimmed;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), "Updated",
            new { Text = before }, new { Text = trimmed }, cancellationToken);

        return Map(note);
    }

    public async Task<bool> DeleteAsync(Guid employeeId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await FindAsync(employeeId, noteId, cancellationToken);
        if (note is null)
        {
            return false;
        }

        _dbContext.Remove(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), "Deleted",
            new { note.EmployeeId, note.Text }, null, cancellationToken);

        return true;
    }

    public async Task<EmployeeNoteDto?> SetPinnedAsync(Guid employeeId, Guid noteId, bool pinned, CancellationToken cancellationToken)
    {
        var note = await FindAsync(employeeId, noteId, cancellationToken);
        if (note is null)
        {
            return null;
        }

        if (note.IsPinnedToDashboard == pinned)
        {
            return Map(note);
        }

        note.IsPinnedToDashboard = pinned;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), pinned ? "Pinned" : "Unpinned",
            null, null, cancellationToken);

        return Map(note);
    }

    private static string Validate(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("text", "Notitietekst is verplicht.");
        }

        if (trimmed.Length > MaxTextLength)
        {
            throw new DomainValidationException("text", $"Notitietekst mag maximaal {MaxTextLength} tekens bevatten.");
        }

        return trimmed;
    }

    private Task<EmployeeNote?> FindAsync(Guid employeeId, Guid noteId, CancellationToken cancellationToken) =>
        _dbContext.EmployeeNotes.FirstOrDefaultAsync(
            n => n.TenantId == _tenantContext.TenantId && n.EmployeeId == employeeId && n.Id == noteId, cancellationToken)!;

    private Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        _dbContext.Employees.AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId, cancellationToken);

    private static EmployeeNoteDto Map(EmployeeNote n) => new(
        n.Id, n.EmployeeId, n.Text, n.IsPinnedToDashboard, n.CreatedAt, n.CreatedByUserId, n.UpdatedAt, n.UpdatedByUserId);
}
