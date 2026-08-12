using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Wave 2: one-time derivation of the typed <c>CoverageStatus</c> column from the frozen
/// <c>CoverageJson</c> blobs of pre-wave pricing snapshots. Idempotent (only rows with json
/// but no status), provider-neutral, batched; the JSON itself stays untouched — history is
/// never rewritten, only indexed.
/// </summary>
public static class CoverageStatusBackfillSeeder
{
    private const int BatchSize = 500;

    private sealed record CoverageEntry(string? Status);

    public static async Task<int> SyncAsync(TransportationDbContext db, CancellationToken cancellationToken = default)
    {
        var updated = 0;
        while (true)
        {
            var batch = await db.TransportOrderPricingSnapshots
                .Where(s => s.CoverageJson != null && s.CoverageStatus == null)
                .OrderBy(s => s.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return updated;
            }

            foreach (var snapshot in batch)
            {
                snapshot.CoverageStatus = Derive(snapshot.CoverageJson!);
            }

            await db.SaveChangesAsync(cancellationToken);
            updated += batch.Count;
        }
    }

    /// <summary>Worst entry wins: None &gt; Partial &gt; Full; no entries = NotApplicable.</summary>
    public static string Derive(string coverageJson)
    {
        List<CoverageEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<CoverageEntry>>(
                coverageJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return "NotApplicable"; // malformed legacy blob: honest fallback, never a crash
        }

        if (entries is not { Count: > 0 })
        {
            return "NotApplicable";
        }

        var statuses = entries.Select(e => e.Status).ToList();
        if (statuses.Contains("None"))
        {
            return "None";
        }

        return statuses.Contains("Partial") ? "Partial" : "Full";
    }
}
