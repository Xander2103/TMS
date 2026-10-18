namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// WH-H2 (warehouse acceptance 2026-10-18): SER-TEST-001 was issued to employee A and then to B,
/// and both became current holders. A serial number is the identity of ONE physical item.
/// <para>
/// Product decision: the serial identity is created AT ISSUE (there is no asset register; a
/// serialized item's stock is counted per template/variant). What the model does guarantee:
/// the serial is stored normalized next to the typed value, and per tenant + item (template) at most
/// ONE open issuance may carry a given normalized serial — enforced by the service (Dutch refusal)
/// and by a filtered unique index (the race). The full lifecycle (A → return → B) stays readable as
/// separate issuance rows with the same normalized serial.
/// </para>
/// </summary>
public static class SerialNumbers
{
    public const int MaxLength = 100;

    /// <summary>"  ser-gold 001 " → "SER-GOLD001": upper-case, all whitespace removed. Null when empty.</summary>
    public static string? Normalize(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        var compact = new string(serial.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return compact.ToUpperInvariant();
    }
}
