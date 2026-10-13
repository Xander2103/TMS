using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Locations.Services;

namespace TransportationService.Api.Tests.Locations;

/// <summary>D3: the two pure address helpers — address-line split and postal-code format.</summary>
public class AddressHelpersTests
{
    [Theory]
    [InlineData("Noorderlaan 10", "Noorderlaan", "10")]
    [InlineData("Kerkstraat 12A", "Kerkstraat", "12A")]
    [InlineData("Kerkstraat 12 a", "Kerkstraat", "12 a")]
    [InlineData("Kerkstraat 12 bus 3", "Kerkstraat", "12 bus 3")]
    [InlineData("Kerkstraat 12 b 3", "Kerkstraat", "12 b 3")]
    [InlineData("Rue de la Loi 16-18", "Rue de la Loi", "16-18")]
    [InlineData("Stationsplein 5/2", "Stationsplein", "5/2")]
    [InlineData("Avenue Louise 54 bte 7", "Avenue Louise", "54 bte 7")]
    [InlineData("Dorpsstraat, 7", "Dorpsstraat", "7")]
    [InlineData("  Kaai   1742  ", "Kaai", "1742")]
    public void Split_TrailingHouseNumber_IsSeparated(string line, string street, string houseNumber)
    {
        var (actualStreet, actualNumber) = AddressLineSplitter.Split(line);

        Assert.Equal(street, actualStreet);
        Assert.Equal(houseNumber, actualNumber);
    }

    [Theory]
    [InlineData("Industrieweg")]            // no number at all
    [InlineData("10 Downing Street")]       // leading number: not confidently splittable
    [InlineData("Laan 1945 12")]            // a year inside the street name: ambiguous, keep whole
    [InlineData("Haven 1742 - kaai noord")] // number in the middle
    public void Split_WhenItCannotSplitWithConfidence_KeepsEverythingAsStreet(string line)
    {
        var (street, houseNumber) = AddressLineSplitter.Split(line);

        Assert.Equal(line, street);
        Assert.Null(houseNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_Blank_ReturnsNothing(string? line)
    {
        Assert.Equal((null, null), AddressLineSplitter.Split(line));
    }

    [Theory]
    [InlineData("BE", "3080")]
    [InlineData("be", " 2030 ")]
    [InlineData("BE", "B-2030")]
    [InlineData("NL", "1234 AB")]
    [InlineData("NL", "1234AB")]
    [InlineData("NL", "1234 ab")]
    [InlineData("FR", "75001")]
    [InlineData("DE", "10115")]
    [InlineData("LU", "1009")]
    [InlineData("LU", "L-1009")]
    [InlineData("GB", "SW1A 1AA")]   // unknown country: no format check
    [InlineData("XX", "whatever")]
    [InlineData(null, "30800")]      // no country: nothing to check against
    [InlineData("BE", null)]         // no postal code: not required here
    [InlineData("BE", "")]
    public void PostalCode_Accepted(string? country, string? postalCode)
    {
        Assert.Null(PostalCodeValidator.Validate(country, postalCode));
    }

    [Theory]
    [InlineData("BE", "30800", "Een Belgische postcode heeft 4 cijfers.")]
    [InlineData("BE", "308", "Een Belgische postcode heeft 4 cijfers.")]
    [InlineData("BE", "30A0", "Een Belgische postcode heeft 4 cijfers.")]
    [InlineData("NL", "1234", "Een Nederlandse postcode heeft 4 cijfers en 2 letters (bv. 1234 AB).")]
    [InlineData("NL", "12345 AB", "Een Nederlandse postcode heeft 4 cijfers en 2 letters (bv. 1234 AB).")]
    [InlineData("FR", "7500", "Een Franse postcode heeft 5 cijfers.")]
    [InlineData("DE", "101150", "Een Duitse postcode heeft 5 cijfers.")]
    [InlineData("LU", "10090", "Een Luxemburgse postcode heeft 4 cijfers.")]
    public void PostalCode_Rejected_WithDutchMessage(string country, string postalCode, string message)
    {
        Assert.Equal(message, PostalCodeValidator.Validate(country, postalCode));

        var exception = Assert.Throws<DomainValidationException>(
            () => PostalCodeValidator.EnsureValid(country, postalCode, "postalCode"));
        Assert.Equal(message, exception.FieldErrors!["postalCode"][0]);
    }
}
