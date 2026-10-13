using System.Globalization;
using System.Text;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// Pure helpers for auto-generated lookup codes (departments, contract types, job functions,
/// …). A code is derived from the name in the house style of the seeded values (short,
/// uppercase, alphanumeric: PLAN, VAST, CHAUF) and, when that base is taken, suffixed with the
/// next free ordinal (PLAN-2, PLAN-3). Uniqueness is decided by the caller against the
/// database and ultimately by the unique (TenantId, Code) index.
/// </summary>
public static class LookupCodeGenerator
{
    /// <summary>Longest generated base; leaves room for a "-NN" suffix inside the 50-char column.</summary>
    public const int MaxBaseLength = 10;

    /// <summary>Used when the name yields no usable characters (e.g. only punctuation).</summary>
    public const string FallbackBase = "CODE";

    /// <summary>Upper-case alphanumeric abbreviation of <paramref name="name"/>, diacritics stripped.</summary>
    public static string BaseFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FallbackBase;
        }

        var builder = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                builder.Append(char.ToUpperInvariant(ch));
                if (builder.Length == MaxBaseLength)
                {
                    break;
                }
            }
        }

        return builder.Length == 0 ? FallbackBase : builder.ToString();
    }

    /// <summary>
    /// First candidate not present in <paramref name="taken"/> (case-insensitive): the base
    /// itself, then base-2, base-3, … Never returns an existing code; existing codes are never
    /// touched.
    /// </summary>
    public static string NextFree(string baseCode, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseCode))
        {
            return baseCode;
        }

        for (var ordinal = 2; ; ordinal++)
        {
            var candidate = $"{baseCode}-{ordinal}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
