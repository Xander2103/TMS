using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// Normalisation + validation for person-level fields (IBAN, BIC, Belgian national
/// register number). Throws <see cref="DomainValidationException"/> with a Dutch message
/// on invalid input; empty input always normalises to null. The optional field path binds
/// the error to the caller's request field for field-level display.
/// </summary>
public static class EmployeePersonValidators
{
    // IBAN/BIC live in Common.Validation.BankingValidators (shared with the customer
    // module); these delegates keep the historic employee-module call sites stable.
    public static string? NormalizeIban(string? input, string field = "iban") =>
        Common.Validation.BankingValidators.NormalizeIban(input, field);

    public static string? NormalizeBic(string? input, string field = "bic") =>
        Common.Validation.BankingValidators.NormalizeBic(input, field);

    public static string? NormalizeNationalRegisterNumber(string? input, string field = "nationalRegisterNumber")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 11)
        {
            throw new DomainValidationException(field, "Rijksregisternummer moet uit 11 cijfers bestaan.");
        }

        // Belgian checksum: 97 - (first 9 digits % 97); people born in/after 2000 prefix a 2.
        var body = long.Parse(digits[..9]);
        var check = int.Parse(digits[9..]);
        var validPre2000 = check == 97 - (int)(body % 97);
        var validPost2000 = check == 97 - (int)((2000000000L + body) % 97);
        if (!validPre2000 && !validPost2000)
        {
            throw new DomainValidationException(field, "Rijksregisternummer heeft een ongeldig controlegetal.");
        }

        return digits;
    }
}
