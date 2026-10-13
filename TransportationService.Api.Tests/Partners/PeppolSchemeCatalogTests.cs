using TransportationService.Api.Modules.Partners.Services;
using Xunit;

namespace TransportationService.Api.Tests.Partners;

public class PeppolSchemeCatalogTests
{
    [Fact]
    public void All_contains_known_belgian_and_dutch_schemes()
    {
        Assert.Contains(PeppolSchemeCatalog.All, s => s.Code == "0208"); // BE enterprise number
        Assert.Contains(PeppolSchemeCatalog.All, s => s.Code == "9925"); // BE VAT
        Assert.Contains(PeppolSchemeCatalog.All, s => s.Code == "0106"); // NL KvK
    }

    [Fact]
    public void All_codes_are_four_ascii_digits()
    {
        Assert.All(PeppolSchemeCatalog.All, s =>
            Assert.Matches("^[0-9]{4}$", s.Code));
    }

    [Fact]
    public void IsKnown_matches_catalog_membership()
    {
        Assert.True(PeppolSchemeCatalog.IsKnown("0208"));
        Assert.False(PeppolSchemeCatalog.IsKnown("0000"));
    }

    [Fact]
    public void InferSchemeForCountry_returns_belgian_enterprise_scheme_for_BE()
    {
        Assert.Equal("0208", PeppolSchemeCatalog.InferSchemeForCountry("be"));
        Assert.Null(PeppolSchemeCatalog.InferSchemeForCountry("ZZ"));
        Assert.Null(PeppolSchemeCatalog.InferSchemeForCountry(null));
    }
}
