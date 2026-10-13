using TransportationService.Api.Modules.Locations.Services;

namespace TransportationService.Api.Tests.Locations;

/// <summary>
/// Sprint 2 — duplicate detection compares NORMALISED physical fields, never a display string.
/// </summary>
public class AddressNormalizerTests
{
    [Theory]
    // casing + surrounding/inner whitespace
    [InlineData("BE", "2030", "Antwerpen", "Noorderlaan", "10", "be", " 2030 ", "  ANTWERPEN", "noorderlaan  ", "10")]
    // punctuation in street and house number
    [InlineData("BE", "9000", "Gent", "Sint-Niklaasstraat", "10 A", "BE", "9000", "gent", "sint niklaasstraat", "10a")]
    // diacritics
    [InlineData("BE", "4000", "Liège", "Rue de l'Église", "5", "BE", "4000", "liege", "rue de l eglise", "5")]
    // separators inside the postcode
    [InlineData("NL", "1234 AB", "Amsterdam", "Damrak", "1", "nl", "1234ab", "AMSTERDAM", "Damrak", "1")]
    public void ExactKey_TreatsEquivalentSpellingsAsTheSameAddress(
        string c1, string p1, string city1, string s1, string h1,
        string c2, string p2, string city2, string s2, string h2)
    {
        Assert.Equal(
            AddressNormalizer.ExactKey(c1, p1, city1, s1, h1),
            AddressNormalizer.ExactKey(c2, p2, city2, s2, h2));
    }

    [Fact]
    public void ExactKey_DistinguishesHouseNumbers()
    {
        Assert.NotEqual(
            AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", "10"),
            AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", "12"));
    }

    [Fact]
    public void StreetKey_IgnoresTheHouseNumber()
    {
        Assert.Equal(
            AddressNormalizer.StreetKey("BE", "2030", "Antwerpen", "Noorderlaan"),
            AddressNormalizer.StreetKey("BE", "2030", "Antwerpen", "noorderlaan"));
    }

    [Fact]
    public void StreetKey_DistinguishesCities()
    {
        Assert.NotEqual(
            AddressNormalizer.StreetKey("BE", "2030", "Antwerpen", "Noorderlaan"),
            AddressNormalizer.StreetKey("BE", "9000", "Gent", "Noorderlaan"));
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("BE", "2030", null, null)]
    [InlineData("BE", null, "   ", "  ")]
    public void Keys_AreEmpty_WhenThereIsNothingToMatchOn(string? country, string? postal, string? city, string? street)
    {
        // An empty key must never be treated as equal to another empty key by the caller.
        Assert.Equal(string.Empty, AddressNormalizer.StreetKey(country, postal, city, street));
        Assert.Equal(string.Empty, AddressNormalizer.ExactKey(country, postal, city, street, "10"));
    }

    [Fact]
    public void StreetKey_IsEmpty_WhenOnlyTheCityIsKnown()
    {
        // A city-only record must not become a "same street" candidate for every address in
        // that city, so without a street there is nothing to compare on.
        Assert.Equal(string.Empty, AddressNormalizer.StreetKey("BE", "2030", "Antwerpen", null));
        Assert.Equal(string.Empty, AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", null, "10"));
    }

    // ---------------------------------------------------------- audit R2

    [Theory]
    [InlineData("1 bus 1", "1/1")]
    [InlineData("1/1", "1/1")]
    [InlineData("1-1", "1/1")]
    [InlineData("1 b 1", "1/1")]
    [InlineData("1b1", "1/1")]
    [InlineData("1 / 1", "1/1")]
    [InlineData("10a bte 2", "10a/2")]
    [InlineData("12 box 3", "12/3")]
    [InlineData("1A", "1a")]
    [InlineData("1 a", "1a")]
    [InlineData("10 B", "10b")]
    [InlineData("10", "10")]
    public void HouseNumber_CanonicalisesBusSeparators_AndKeepsLettersAdjacent(string input, string expectedPart)
    {
        var key = AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", input);
        Assert.EndsWith("|" + expectedPart, key);
    }

    [Theory]
    [InlineData("1/1", "11")]
    [InlineData("12/3", "123")]
    [InlineData("1 bus 1", "11")]
    public void HouseNumber_BusSeparatorIsNotDeleted_SoTheseAreDifferentDoors(string a, string b)
    {
        Assert.NotEqual(
            AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", a),
            AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", b));
    }

    [Fact]
    public void Fold_MapsSharpSToSs()
    {
        Assert.Equal(
            AddressNormalizer.StreetKey("DE", "50667", "Köln", "Große Straße"),
            AddressNormalizer.StreetKey("DE", "50667", "Koeln".Replace("oe", "o"), "grosse strasse"));
    }

    [Theory]
    [InlineData("B-2030", "2030")]
    [InlineData("NL-1234 AB", "1234ab")]
    [InlineData("L-1234", "1234")]
    [InlineData("1234 AB", "1234ab")]
    public void PostalCode_StripsLeadingIsoPrefix_AndSeparators(string input, string canonical)
    {
        Assert.Equal(
            AddressNormalizer.StreetKey("BE", input, "Stad", "Straat"),
            AddressNormalizer.StreetKey("BE", canonical, "Stad", "Straat"));
    }

    [Fact]
    public void Keys_AreDeterministic_AcrossCalls()
    {
        var first = AddressNormalizer.ExactKey("be", " B-2030 ", "ANTWERPEN", "Noorderlaan ", "1 bus 2");
        var second = AddressNormalizer.ExactKey("be", " B-2030 ", "ANTWERPEN", "Noorderlaan ", "1 bus 2");
        Assert.Equal(first, second);
        Assert.Equal("BE|2030|antwerpen|noorderlaan|1/2", first);
    }
}
