using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// WH-M1 (warehouse acceptance 2026-10-18): items had no article number, so two items with the same
/// name were indistinguishable in every picker. Each item now carries a short, stable, visible
/// warehouse code ("BM-0007"), unique per tenant over ALL rows (a code that history printed is
/// never handed out again, deleted items included). Names may still legitimately repeat.
/// New items get the next free number; a code can be typed explicitly (letters, digits, '-', '_', '.').
/// </summary>
public static class IssuedItemCodes
{
    public const string Prefix = "BM-";
    public const int MaxLength = 30;

    public static string Format(int number) => $"{Prefix}{number:0000}";

    /// <summary>Trim + upper-case; null when empty. Throws a Dutch validation error on bad characters.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > MaxLength || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new DomainValidationException("code",
                $"Ongeldige artikelcode. Gebruik letters, cijfers, '-', '_' of '.' (maximaal {MaxLength} tekens).");
        }

        return normalized;
    }

    /// <summary>The next "BM-nnnn" after the highest BM-number this tenant ever used (deleted rows included).</summary>
    public static async Task<string> NextAsync(TransportationDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var codes = await db.IssuedItemTemplates.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Code != null && t.Code.StartsWith(Prefix))
            .Select(t => t.Code!)
            .ToListAsync(cancellationToken);
        var highest = codes
            .Select(c => int.TryParse(c.AsSpan(Prefix.Length), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return Format(highest + 1);
    }

    public static Task<bool> IsTakenAsync(
        TransportationDbContext db, Guid tenantId, string code, Guid? exceptTemplateId, CancellationToken cancellationToken) =>
        db.IssuedItemTemplates.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId && t.Code == code && t.Id != exceptTemplateId, cancellationToken);
}
