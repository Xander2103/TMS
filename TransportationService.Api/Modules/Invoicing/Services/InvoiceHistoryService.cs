using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>One readable step of an invoice's financial story (WHEN / WHO / WHAT).</summary>
public record InvoiceHistoryEventDto(
    string Id,
    DateTime Timestamp,
    /// <summary><c>User</c> (a person acted), <c>System</c> (automatic: dispatcher, provider) or <c>Unknown</c>.</summary>
    string ActorKind,
    string? ActorName,
    /// <summary>Stable machine code ("invoice.created", "invoice.issued", "peppol.delivered", …).</summary>
    string Type,
    /// <summary>The Dutch sentence the user reads.</summary>
    string Title,
    string? Detail);

public interface IInvoiceHistoryService
{
    /// <summary>The invoice's financial story, oldest first; null when the invoice is not visible to the tenant.</summary>
    Task<IReadOnlyList<InvoiceHistoryEventDto>?> GetAsync(Guid invoiceId, CancellationToken cancellationToken);
}

/// <summary>
/// ACC-M7 — read model over what is ALREADY recorded: the invoice's audit rows (created, updated,
/// period, issued/status, number override, credit note, ledger snapshots, deleted) and the Peppol
/// transmission events of the invoice. Nothing is written. Every code becomes a Dutch sentence;
/// automatic steps (dispatcher, provider callbacks) are attributed to <c>System</c>, never to the
/// person who happened to queue the transmission. The raw audit log (audit_logs.view) stays untouched.
/// </summary>
public class InvoiceHistoryService : IInvoiceHistoryService
{
    private const string InvoiceEntity = "Invoice";
    private const string TransmissionEntity = "PeppolTransmission";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public InvoiceHistoryService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    private sealed record Row(string Id, DateTime At, Guid? UserId, bool System, string Type, string Title, string? Detail, int Rank);

    public async Task<IReadOnlyList<InvoiceHistoryEventDto>?> GetAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var invoice = await _dbContext.Invoices.AsNoTracking().IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Id == invoiceId)
            .Select(i => new { i.Id, i.Kind })
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var isCreditNote = invoice.Kind == Entities.InvoiceKind.CreditNote;
        var entityId = invoiceId.ToString();
        var audits = await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EntityType == InvoiceEntity && a.EntityId == entityId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);

        var rows = new List<Row>();
        foreach (var audit in audits)
        {
            var before = Parse(audit.OldValuesJson);
            var after = Parse(audit.NewValuesJson);
            var (type, title, detail) = Describe(audit.Action, before, after, isCreditNote);
            rows.Add(new Row($"audit:{audit.Id}", audit.Timestamp, audit.UserId, false, type, title, detail, 5));
        }

        // Peppol: the transmissions of this invoice and their event trail. Queueing, retrying and
        // cancelling are user actions (audited on the transmission); everything else is the
        // dispatcher or the provider → System.
        var transmissions = await _dbContext.PeppolTransmissions.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.InvoiceId == invoiceId)
            .Include(t => t.Events)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        if (transmissions.Count > 0)
        {
            var transmissionIds = transmissions.Select(t => t.Id.ToString()).ToList();
            var transmissionAudits = await _dbContext.AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.EntityType == TransmissionEntity && transmissionIds.Contains(a.EntityId))
                .ToListAsync(cancellationToken);
            foreach (var transmission in transmissions)
            {
                var ownAudits = transmissionAudits.Where(a => a.EntityId == transmission.Id.ToString()).ToList();
                var queuedBy = ownAudits.FirstOrDefault(a => a.Action is "Queued" or "Retried")?.UserId;
                var cancelledBy = ownAudits.FirstOrDefault(a => a.Action == "Cancelled")?.UserId;
                var version = transmission.PayloadVersion;
                var environment = transmission.Environment == PeppolEnvironment.Live ? "live" : "sandbox";
                foreach (var e in transmission.Events.OrderBy(e => e.Timestamp))
                {
                    var (type, title, system, rank) = e.Status switch
                    {
                        PeppolTransmissionStatus.Queued when IsAttemptFailure(e.Detail) =>
                            ("peppol.attempt_failed", "Peppol-verzending: poging mislukt, wordt opnieuw geprobeerd", true, 6),
                        PeppolTransmissionStatus.Queued => ("peppol.queued", $"Peppol-verzending in wachtrij geplaatst ({environment}, versie {version})", false, 5),
                        PeppolTransmissionStatus.SubmittedToProvider => ("peppol.submitted", "Peppol-verzending aangeboden aan de provider", true, 6),
                        PeppolTransmissionStatus.AcceptedByProvider => ("peppol.accepted", "Peppol-verzending aanvaard door de provider", true, 6),
                        PeppolTransmissionStatus.Delivered => ("peppol.delivered", "Factuur afgeleverd via Peppol", true, 6),
                        PeppolTransmissionStatus.Failed => ("peppol.failed", "Peppol-verzending definitief mislukt", true, 6),
                        PeppolTransmissionStatus.Rejected => ("peppol.rejected", "Peppol-verzending geweigerd door de ontvanger", true, 6),
                        PeppolTransmissionStatus.Cancelled => ("peppol.cancelled", "Peppol-verzending geannuleerd", false, 5),
                        _ => ("peppol." + e.Status.ToString().ToLowerInvariant(), $"Peppol-verzending: {e.Status}", true, 6),
                    };
                    var userId = system ? null
                        : e.Status == PeppolTransmissionStatus.Cancelled ? cancelledBy : queuedBy;
                    var detail = e.Detail;
                    if (e.Status == PeppolTransmissionStatus.Delivered && !string.IsNullOrWhiteSpace(transmission.ProviderMessageId))
                    {
                        detail = string.IsNullOrWhiteSpace(detail)
                            ? $"Provider-referentie {transmission.ProviderMessageId}"
                            : $"{detail} · provider-referentie {transmission.ProviderMessageId}";
                    }

                    rows.Add(new Row($"peppol:{e.Id}", e.Timestamp, userId, system || userId is null, type, title, Clean(detail), rank));
                }
            }
        }

        // The issue step writes two audits (the status transition and the numbered "Issued" record);
        // the reader wants ONE line for that moment — the one that carries the number (ACC-M7).
        var numberedIssues = rows.Where(r => r.Type == "invoice.issued" && r.Detail is not null).ToList();
        if (numberedIssues.Count > 0)
        {
            rows.RemoveAll(r => r.Type == "invoice.issued" && r.Detail is null
                                && numberedIssues.Any(n => Math.Abs((n.At - r.At).TotalSeconds) < 5));
        }

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        return rows
            .OrderBy(r => r.At).ThenBy(r => r.Rank)
            .Select(r =>
            {
                var kind = r.System ? "System" : r.UserId is { } id && names.TryGetValue(id, out _) ? "User" : r.UserId is null ? "System" : "Unknown";
                var name = r.UserId is { } uid && names.TryGetValue(uid, out var n) && n.Length > 0 ? n : null;
                return new InvoiceHistoryEventDto(r.Id, r.At, kind, kind == "User" ? name : null, r.Type, r.Title, r.Detail);
            })
            .ToList();
    }

    private static bool IsAttemptFailure(string? detail) =>
        detail is not null && detail.Contains("mislukt", StringComparison.OrdinalIgnoreCase);

    private static (string Type, string Title, string? Detail) Describe(
        string action, JsonElement? before, JsonElement? after, bool isCreditNote)
    {
        var doc = isCreditNote ? "Creditnota" : "Factuur";
        switch (action)
        {
            case "Created":
            {
                var number = Str(after, "InvoiceNumber");
                var lines = Int(after, "LineCount");
                var title = number is null ? $"Concept{doc.ToLowerInvariant()} aangemaakt" : $"Concept{doc.ToLowerInvariant()} {number} aangemaakt";
                return ("invoice.created", title, lines is { } n ? $"{n} {(n == 1 ? "lijn" : "lijnen")}" : null);
            }
            case "CreditNoteCreated":
            {
                var credited = Str(before, "CreditedInvoiceNumber");
                var number = Str(after, "InvoiceNumber");
                var title = credited is null
                    ? "Creditnota aangemaakt"
                    : $"Creditnota {(number is null ? "" : number + " ")}aangemaakt voor factuur {credited}".Replace("  ", " ");
                return ("creditnote.created", title.Trim(), null);
            }
            case "Credited":
            {
                var number = Str(after, "CreditNoteNumber");
                return ("invoice.credited", number is null ? "Gecrediteerd door een creditnota" : $"Gecrediteerd door creditnota {number}", null);
            }
            case "Updated":
            {
                var linesBefore = Int(before, "LineCount");
                var linesAfter = Int(after, "LineCount");
                var date = Str(after, "InvoiceDate");
                var parts = new List<string>();
                if (date is not null) parts.Add($"factuurdatum {FormatDate(date)}");
                if (linesAfter is { } la)
                {
                    parts.Add(linesBefore is { } lb && lb != la ? $"lijnen {lb} → {la}" : $"{la} {(la == 1 ? "lijn" : "lijnen")}");
                }

                var title = linesBefore is { } b && linesAfter is { } a && b != a ? $"{doc}lijnen gewijzigd" : $"{doc}gegevens gewijzigd";
                return ("invoice.updated", title, parts.Count > 0 ? string.Join(" · ", parts) : null);
            }
            case "PeriodChanged":
            {
                var year = Int(after, "InvoicePeriodYear");
                var month = Int(after, "InvoicePeriodMonth");
                var period = year is { } y && month is { } m ? $"{m:00}-{y}" : null;
                return ("invoice.period_changed", period is null ? "Factuurperiode gewijzigd" : $"Factuurperiode gewijzigd naar {period}", null);
            }
            case "StatusChanged":
            case "Issued":
            case "Paid":
            {
                var status = Str(after, "Status") ?? (action == "Issued" ? "Sent" : action == "Paid" ? "Paid" : null);
                return status switch
                {
                    "Sent" => ("invoice.issued", $"{doc} definitief gemaakt en verzonden", Str(after, "InvoiceNumber") is { } n ? $"Nummer {n}" : null),
                    "Paid" => ("invoice.paid", "Gemarkeerd als betaald", null),
                    "Cancelled" => ("invoice.cancelled", $"{doc} geannuleerd", Str(after, "Reason")),
                    "Draft" => ("invoice.reopened", "Teruggezet naar concept", null),
                    _ => ("invoice.status_changed", "Status gewijzigd", status),
                };
            }
            case "Deleted":
                return ("invoice.deleted", $"Concept{doc.ToLowerInvariant()} verwijderd", null);
            case "NumberOverridden":
            {
                var oldNumber = Str(before, "InvoiceNumber");
                var newNumber = Str(after, "InvoiceNumber");
                var title = oldNumber is null || newNumber is null
                    ? "Nummer handmatig gecorrigeerd"
                    : $"Nummer handmatig gecorrigeerd: {oldNumber} → {newNumber}";
                return ("invoice.number_overridden", title, Str(after, "Reason"));
            }
            case "LedgerSnapshotsCompleted":
            {
                var b = Int(before, "MissingSnapshots");
                var a = Int(after, "MissingSnapshots");
                return ("invoice.ledger_completed", "Grootboekgegevens aangevuld",
                    b is { } mb && a is { } ma ? $"ontbrekende rekeningen {mb} → {ma}" : null);
            }
            default:
                return ("invoice." + action.ToLowerInvariant(), Humanize(action), null);
        }
    }

    private static string Humanize(string action)
    {
        var chars = new List<char>();
        foreach (var c in action)
        {
            if (char.IsUpper(c) && chars.Count > 0) chars.Add(' ');
            chars.Add(chars.Count == 0 ? c : char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private static string FormatDate(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : iso;

    private static string? Clean(string? detail) => string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } obj) return null;
        foreach (var property in obj.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }

        return null;
    }

    private static int? Int(JsonElement? element, string name) =>
        int.TryParse(Str(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
