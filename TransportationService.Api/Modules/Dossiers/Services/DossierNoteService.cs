using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierNoteService
{
    /// <summary>
    /// Newest first. Without <paramref name="activityId"/>: every note of the dossier (dossier-level
    /// notes have a null activity). Null = dossier unknown for this tenant (→ 404).
    /// </summary>
    Task<IReadOnlyList<DossierNoteDto>?> ListAsync(Guid dossierId, Guid? activityId, CancellationToken cancellationToken);

    Task<DossierNoteDto?> CreateAsync(Guid dossierId, CreateDossierNoteRequest request, CancellationToken cancellationToken);

    /// <summary>Null = the note is not a note of THIS dossier in this tenant (→ 404).</summary>
    Task<DossierNoteDto?> UpdateAsync(Guid dossierId, Guid noteId, UpdateDossierNoteRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid dossierId, Guid noteId, CancellationToken cancellationToken);
}

/// <summary>
/// D7 (master sprint 2026-09-21): any number of notes per dossier, optionally about one of its
/// activities — the <c>EmployeeNote</c> pattern. The legacy free-text columns
/// (<c>TransportDossier.Notes</c>, <c>DossierActivity.Notes</c>) are never written here.
/// <para>
/// Closed dossier: reading stays possible, writing is refused with the same message as every
/// other dossier child mutation (activities, order links, header) — reopen first. A note is not
/// an audit line, but create/update/delete ARE audited; the audit values carry the length and a
/// short preview only, never the full text (a note may hold anything a user types).
/// </para>
/// </summary>
public class DossierNoteService : IDossierNoteService
{
    public const int MaxTextLength = 4000;
    public const int PreviewLength = 160;
    private const int AuditPreviewLength = 40;
    private const string EntityType = "DossierNote";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IPermissionAuthorizationService? _permissionService;

    public DossierNoteService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        ICurrentUserContext? currentUser = null,
        IPermissionAuthorizationService? permissionService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<DossierNoteDto>?> ListAsync(Guid dossierId, Guid? activityId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        var query = _dbContext.DossierNotes.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.DossierId == dossierId);
        if (activityId is { } aid)
        {
            query = query.Where(n => n.DossierActivityId == aid);
        }

        var notes = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .ToListAsync(cancellationToken);

        return await MapAsync(notes, dossier, cancellationToken);
    }

    public async Task<DossierNoteDto?> CreateAsync(Guid dossierId, CreateDossierNoteRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        RequireOpen(dossier);
        var text = Validate(request.Text);

        // An activity id from the client must be an activity OF THIS DOSSIER (and tenant) — never
        // a note attached across dossiers.
        if (request.DossierActivityId is { } activityId
            && !await _dbContext.DossierActivities.AnyAsync(
                a => a.TenantId == tenantId && a.DossierId == dossierId && a.Id == activityId, cancellationToken))
        {
            throw new DomainValidationException("dossierActivityId", "Deze activiteit hoort niet bij dit dossier.");
        }

        var note = new DossierNote
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DossierId = dossierId,
            DossierActivityId = request.DossierActivityId,
            Text = text,
            // The author IS the creator. The auditing interceptor stamps the same user for a request;
            // setting it here keeps the author correct outside an HTTP request too (it only fills a null).
            CreatedByUserId = _currentUser?.CurrentUserId,
        };
        _dbContext.DossierNotes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), "Created", null,
            AuditValues(note), cancellationToken);

        return (await MapAsync([note], dossier, cancellationToken))[0];
    }

    public async Task<DossierNoteDto?> UpdateAsync(
        Guid dossierId, Guid noteId, UpdateDossierNoteRequest request, CancellationToken cancellationToken)
    {
        var dossier = await FindDossierAsync(dossierId, cancellationToken);
        var note = dossier is null ? null : await FindNoteAsync(dossierId, noteId, cancellationToken);
        if (dossier is null || note is null)
        {
            return null;
        }

        RequireOpen(dossier);
        var text = Validate(request.Text);
        var before = AuditValues(note);
        note.Text = text;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), "Updated", before, AuditValues(note), cancellationToken);

        return (await MapAsync([note], dossier, cancellationToken))[0];
    }

    public async Task<bool> DeleteAsync(Guid dossierId, Guid noteId, CancellationToken cancellationToken)
    {
        var dossier = await FindDossierAsync(dossierId, cancellationToken);
        var note = dossier is null ? null : await FindNoteAsync(dossierId, noteId, cancellationToken);
        if (dossier is null || note is null)
        {
            return false;
        }

        RequireOpen(dossier);
        var before = AuditValues(note);
        _dbContext.Remove(note); // soft delete via the auditing interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, note.Id.ToString(), "Deleted", before, null, cancellationToken);
        return true;
    }

    /// <summary>
    /// Note summary of one dossier for <c>DossierDetailDto</c>: counts per activity + the newest
    /// note of each, in TWO queries whatever the number of activities or notes (ids and
    /// timestamps first, then the text of the few newest notes only).
    /// </summary>
    public static async Task<DossierNoteSummary> SummarizeAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid dossierId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.DossierNotes.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.DossierId == dossierId)
            .Select(n => new { n.Id, n.DossierActivityId, n.CreatedAt })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return DossierNoteSummary.Empty;
        }

        var latestByActivity = rows
            .Where(r => r.DossierActivityId is not null)
            .GroupBy(r => r.DossierActivityId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(), Latest: g.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).First()));

        var latestIds = latestByActivity.Values.Select(v => v.Latest.Id).ToList();
        var texts = latestIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.DossierNotes.AsNoTracking()
                .Where(n => n.TenantId == tenantId && latestIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Text })
                .ToDictionaryAsync(n => n.Id, n => n.Text, cancellationToken);

        return new DossierNoteSummary(
            rows.Count(r => r.DossierActivityId is null),
            latestByActivity.ToDictionary(
                kv => kv.Key,
                kv => new ActivityNoteSummary(
                    kv.Value.Count,
                    BuildPreview(texts.GetValueOrDefault(kv.Value.Latest.Id), PreviewLength),
                    kv.Value.Latest.CreatedAt)));
    }

    /// <summary>First <paramref name="maxLength"/> characters on a single line (line breaks and runs of whitespace collapse to one space).</summary>
    public static string? BuildPreview(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var singleLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length > maxLength ? singleLine[..maxLength] : singleLine;
    }

    private async Task<List<DossierNoteDto>> MapAsync(
        IReadOnlyList<DossierNote> notes, TransportDossier dossier, CancellationToken cancellationToken)
    {
        // Author names in ONE query for the whole list; a note without author (migrated legacy
        // text, system) simply has no name.
        var authorIds = notes.Where(n => n.CreatedByUserId is not null).Select(n => n.CreatedByUserId!.Value).Distinct().ToList();
        var authors = authorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId && authorIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name.Trim(), cancellationToken);

        // Fail-closed: no wired authorization service / no user means no write affordance. A closed
        // dossier refuses note writes, so the flags are false there too (the UI shows no buttons).
        var canWrite = dossier.Status != DossierStatus.Closed
            && _permissionService is not null
            && _currentUser?.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.DossiersManage, cancellationToken);

        return notes
            .Select(n => new DossierNoteDto(
                n.Id, n.DossierId, n.DossierActivityId, n.Text,
                n.CreatedByUserId is { } authorId ? authors.GetValueOrDefault(authorId) : null,
                n.CreatedAt, n.UpdatedAt, canWrite, canWrite))
            .ToList();
    }

    /// <summary>What the audit trail keeps of a note: where it hangs, its length and a short preview — never the full text.</summary>
    private static object AuditValues(DossierNote note) => new
    {
        note.DossierId,
        note.DossierActivityId,
        TextLength = note.Text.Length,
        TextPreview = BuildPreview(note.Text, AuditPreviewLength),
    };

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

    private static void RequireOpen(TransportDossier dossier)
    {
        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Een gesloten dossier kan niet worden bewerkt. Heropen het dossier eerst.");
        }
    }

    private Task<TransportDossier?> FindDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        _dbContext.TransportDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == _tenantContext.TenantId && d.Id == dossierId, cancellationToken);

    private Task<DossierNote?> FindNoteAsync(Guid dossierId, Guid noteId, CancellationToken cancellationToken) =>
        _dbContext.DossierNotes.FirstOrDefaultAsync(
            n => n.TenantId == _tenantContext.TenantId && n.DossierId == dossierId && n.Id == noteId, cancellationToken);
}

/// <summary>D7: note roll-up of one dossier (see <see cref="DossierNoteService.SummarizeAsync"/>).</summary>
public sealed record DossierNoteSummary(int DossierLevelCount, IReadOnlyDictionary<Guid, ActivityNoteSummary> ByActivity)
{
    public static readonly DossierNoteSummary Empty = new(0, new Dictionary<Guid, ActivityNoteSummary>());
}

public sealed record ActivityNoteSummary(int Count, string? LatestPreview, DateTime LatestAt);
