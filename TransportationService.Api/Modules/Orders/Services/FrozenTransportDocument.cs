using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// MD-B1: the content of an issued CMR / delivery note / work order as frozen at issue — the
/// printed document model (<see cref="TransportDocumentSnapshot"/>, without the reference block,
/// whose number is the document's own immutable identity) plus the dossier number at issue.
/// </summary>
public sealed record FrozenTransportDocument(int Version, string? DossierNumber, TransportDocumentSnapshot Document);

/// <summary>Serialization of <see cref="FrozenTransportDocument"/> — one stable shape for every reader.</summary>
public static class FrozenTransportDocumentJson
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(string? dossierNumber, TransportDocumentSnapshot snapshot) =>
        JsonSerializer.Serialize(
            new FrozenTransportDocument(CurrentVersion, dossierNumber, snapshot with { Reference = null }), Options);

    public static bool TryDeserialize(string? json, [NotNullWhen(true)] out FrozenTransportDocument? frozen)
    {
        frozen = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            frozen = JsonSerializer.Deserialize<FrozenTransportDocument>(json, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        return frozen?.Document is not null;
    }
}

/// <summary>
/// MD-B1 legacy policy: documents issued BEFORE the remediation stored no content and were
/// re-rendered from live master data on every download. They are frozen ONCE, at the first
/// startup after deployment, from the data as it stands then — the only source that exists; no
/// older value can be reconstructed. They are flagged <c>SnapshotIsBackfilled</c> and from then on
/// are exactly as immutable as a document frozen at issue. Idempotent: only rows without a
/// snapshot are touched, an existing snapshot is never rewritten.
/// </summary>
public static class IssuedTransportDocumentSnapshotBackfill
{
    private const int BatchSize = 200;

    public static async Task<int> SyncAsync(TransportationDbContext db, ILogger? logger = null, TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        var frozenCount = 0;
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        while (true)
        {
            var batch = await db.IssuedTransportDocuments.IgnoreQueryFilters()
                .Where(d => d.SnapshotJson == null)
                .OrderBy(d => d.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var document in batch)
            {
                if (!await FreezeAsync(db, document, now, cancellationToken))
                {
                    // The order is gone: keep an explicit empty marker so the row is not retried forever.
                    document.SnapshotJson = "{}";
                    document.SnapshotVersion = 0;
                    document.SnapshotFrozenAt = now;
                    document.SnapshotIsBackfilled = true;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            frozenCount += batch.Count;
        }

        if (frozenCount > 0)
        {
            logger?.LogInformation("MD-B1: froze {Count} legacy issued transport document(s) from current master data.", frozenCount);
        }

        return frozenCount;
    }

    /// <summary>Freezes one legacy document in place (not saved). False = its order no longer exists.</summary>
    public static async Task<bool> FreezeAsync(
        TransportationDbContext db, IssuedTransportDocument document, DateTime now, CancellationToken cancellationToken)
    {
        var tenant = new DevTenantContext(document.TenantId);
        var builder = new TransportDocumentService(db, tenant);
        var snapshot = await builder.BuildCurrentSnapshotAsync(document.TransportOrderId, document.Kind.ToString(), cancellationToken);
        if (snapshot is null)
        {
            return false;
        }

        var dossierNumber = document.DossierId is { } dossierId
            ? await db.TransportDossiers.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.TenantId == document.TenantId && d.Id == dossierId)
                .Select(d => d.DossierNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : (await OwningDossierResolver.ResolveAsync(db, document.TenantId, document.TransportOrderId, cancellationToken))?.DossierNumber;

        document.SnapshotJson = FrozenTransportDocumentJson.Serialize(dossierNumber, snapshot);
        document.SnapshotVersion = FrozenTransportDocumentJson.CurrentVersion;
        document.SnapshotFrozenAt = now;
        document.SnapshotIsBackfilled = true;
        return true;
    }
}
