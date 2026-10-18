using System.Text.Json;

namespace TransportationService.Api.Common.Integrity;

/// <summary>
/// MD-M10 (master-data acceptance 2026-10-18): configuration audits were either too thin (a legal
/// entity "Updated" without IBAN, address or Peppol) or noise ("Updated" with nothing changed).
/// Callers pass a purpose-built snapshot of the auditable fields before and after; only the fields
/// that actually changed are kept, and no change = no audit record. Never pass secrets.
/// </summary>
public static class AuditDiff
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Null when nothing changed; otherwise the changed fields, old and new side.</summary>
    public static (Dictionary<string, JsonElement> Old, Dictionary<string, JsonElement> New)? Changes(object before, object after)
    {
        var oldFields = Fields(before);
        var newFields = Fields(after);
        var changedOld = new Dictionary<string, JsonElement>();
        var changedNew = new Dictionary<string, JsonElement>();
        foreach (var (name, value) in newFields)
        {
            var previous = oldFields.GetValueOrDefault(name);
            if (previous.ValueKind == JsonValueKind.Undefined || previous.GetRawText() != value.GetRawText())
            {
                changedOld[name] = previous.ValueKind == JsonValueKind.Undefined ? Null : previous;
                changedNew[name] = value;
            }
        }

        return changedNew.Count == 0 ? null : (changedOld, changedNew);
    }

    private static readonly JsonElement Null = JsonDocument.Parse("null").RootElement.Clone();

    private static Dictionary<string, JsonElement> Fields(object snapshot)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, Options));
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
