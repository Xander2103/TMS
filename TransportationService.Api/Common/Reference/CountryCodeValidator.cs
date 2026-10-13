using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Common.Reference;

public sealed class CountryCodeValidator : ICountryCodeValidator
{
    private readonly TransportationDbContext _dbContext;

    public CountryCodeValidator(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string?> NormalizeAndValidateAsync(string? code, string fieldLabel, CancellationToken cancellationToken, string? field = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        var exists = await _dbContext.Countries.AnyAsync(c => c.Code == normalized, cancellationToken);
        if (!exists)
        {
            var message = $"Onbekende landcode '{normalized}' voor {fieldLabel}.";
            throw field is null
                ? new Common.DomainValidationException(message)
                : new Common.DomainValidationException(field, message);
        }

        return normalized;
    }
}
