namespace TransportationService.Api.Common;

/// <summary>
/// Enum parsing that refuses undefined values. <c>Enum.TryParse</c> happily accepts a numeric
/// string ("7") and returns an undefined enum value, which — with string-stored enum columns —
/// persists literal garbage. Every parse of client-supplied enum text must go through here.
/// </summary>
public static class EnumParsing
{
    /// <summary>True only when the text maps to a DEFINED member of <typeparamref name="TEnum"/>.</summary>
    public static bool TryParseDefined<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result);
    }

    /// <summary>Parses or throws a Dutch <see cref="DomainValidationException"/> (mapped to 400).</summary>
    public static TEnum ParseDefinedOrThrow<TEnum>(string? value, string fieldName, string dutchLabel)
        where TEnum : struct, Enum
    {
        if (TryParseDefined<TEnum>(value, out var parsed))
        {
            return parsed;
        }

        throw new DomainValidationException(fieldName, $"Kies een geldige {dutchLabel}.");
    }
}
