using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

public interface IPeppolIncomingService
{
    Task<PagedResult<PeppolIncomingDocumentDto>> SearchAsync(
        string? status, PageRequest page, CancellationToken cancellationToken);

    Task<PeppolIncomingDocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Marks the document as handled (Linked) with an optional reviewer note.</summary>
    Task<PeppolIncomingDocumentDto?> MarkReviewedAsync(Guid id, string? note, CancellationToken cancellationToken);

    /// <summary>Rejects the document (spam/irrelevant) with an optional reviewer note.</summary>
    Task<PeppolIncomingDocumentDto?> RejectAsync(Guid id, string? note, CancellationToken cancellationToken);
}

/// <summary>
/// Review queue over inbound Peppol documents. No supplier master exists, so "linking" is a
/// reviewer note plus the Linked status �?" a real supplier-invoice module can attach later.
/// </summary>
public class PeppolIncomingService : IPeppolIncomingService
{
    private const string EntityType = "PeppolIncomingDocument";

    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public PeppolIncomingService(TransportationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    private Guid TenantId => _tenant.TenantId;

    public async Task<PagedResult<PeppolIncomingDocumentDto>> SearchAsync(
        string? status, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.PeppolIncomingDocuments.AsNoTracking().Where(d => d.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(status)
            && TransportationService.Api.Common.EnumParsing.TryParseDefined<PeppolIncomingDocumentStatus>(status, out var parsed))
        {
            query = query.Where(d => d.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<PeppolIncomingDocumentDto>(
            rows.Select(Map).ToList(), total, page.Page, page.PageSize);
    }

    public async Task<PeppolIncomingDocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.PeppolIncomingDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == id, cancellationToken);
        return document is null ? null : Map(document);
    }

    public Task<PeppolIncomingDocumentDto?> MarkReviewedAsync(Guid id, string? note, CancellationToken cancellationToken)
        => DecideAsync(id, PeppolIncomingDocumentStatus.Linked, "Reviewed", note, cancellationToken);

    public Task<PeppolIncomingDocumentDto?> RejectAsync(Guid id, string? note, CancellationToken cancellationToken)
        => DecideAsync(id, PeppolIncomingDocumentStatus.Rejected, "Rejected", note, cancellationToken);

    private async Task<PeppolIncomingDocumentDto?> DecideAsync(
        Guid id, PeppolIncomingDocumentStatus target, string action, string? note, CancellationToken cancellationToken)
    {
        var document = await _db.PeppolIncomingDocuments
            .FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (document.Status is PeppolIncomingDocumentStatus.Linked or PeppolIncomingDocumentStatus.Rejected)
        {
            throw new DomainValidationException("Dit document is al beoordeeld.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > 2000 })
        {
            throw new DomainValidationException("reviewNote", "De notitie mag maximaal 2000 tekens lang zijn.");
        }

        var before = new { Status = document.Status.ToString(), document.ReviewNote };
        document.Status = target;
        document.ReviewNote = trimmedNote;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(EntityType, document.Id.ToString(), action, before,
            new { Status = document.Status.ToString(), document.ReviewNote }, cancellationToken);
        return Map(document);
    }

    private static PeppolIncomingDocumentDto Map(PeppolIncomingDocument d) => new(
        d.Id, d.DocumentKind.ToString(), d.SupplierParticipant, d.SupplierName,
        d.DocumentNumber, d.DocumentDate, d.Amount, d.Currency,
        d.Status.ToString(), d.ReviewNote, d.CreatedAt);
}
