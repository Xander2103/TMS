namespace TransportationService.Api.Data;

/// <summary>
/// Standard nationality and language sets seeded into every tenant's lookups. Unlike the
/// starter lookups (seed-if-empty), these are converged add-if-missing by code so existing
/// tenants also receive the full list; names a tenant edited are left untouched.
/// Frequently used entries get a low sort order, the long tail shares 100.
/// </summary>
public static class NationalityLanguageSeedData
{
    private static (string Code, string Name, int SortOrder) N(string code, string name, int sortOrder = 100)
        => (code, name, sortOrder);

    /// <summary>Nationalities keyed by ISO 3166-1 alpha-2 country code, Dutch demonyms.</summary>
    public static IReadOnlyList<(string Code, string Name, int SortOrder)> Nationalities { get; } =
    [
        N("BE", "Belgisch", 0),
        N("NL", "Nederlands", 1),
        N("DE", "Duits", 2),
        N("FR", "Frans", 3),
        N("LU", "Luxemburgs", 4),
        N("PL", "Pools", 5),
        N("GB", "Brits", 6),

        N("AL", "Albanees"), N("AT", "Oostenrijks"), N("BA", "Bosnisch"), N("BG", "Bulgaars"),
        N("BY", "Belarussisch"), N("CH", "Zwitsers"), N("CY", "Cypriotisch"), N("CZ", "Tsjechisch"),
        N("DK", "Deens"), N("EE", "Ests"), N("ES", "Spaans"), N("FI", "Fins"),
        N("GR", "Grieks"), N("HR", "Kroatisch"), N("HU", "Hongaars"), N("IE", "Iers"),
        N("IS", "IJslands"), N("IT", "Italiaans"), N("LT", "Litouws"), N("LV", "Lets"),
        N("MD", "Moldavisch"), N("ME", "Montenegrijns"), N("MK", "Noord-Macedonisch"), N("MT", "Maltees"),
        N("NO", "Noors"), N("PT", "Portugees"), N("RO", "Roemeens"), N("RS", "Servisch"),
        N("SE", "Zweeds"), N("SI", "Sloveens"), N("SK", "Slowaaks"), N("UA", "Oekraïens"),

        N("CN", "Chinees"), N("IN", "Indiaas"), N("JP", "Japans"), N("KR", "Zuid-Koreaans"),
        N("MA", "Marokkaans"), N("PH", "Filipijns"), N("RU", "Russisch"), N("SY", "Syrisch"),
        N("TN", "Tunesisch"), N("TR", "Turks"), N("US", "Amerikaans"), N("VN", "Vietnamees"),
    ];

    /// <summary>Languages keyed by ISO 639-1 code, Dutch names.</summary>
    public static IReadOnlyList<(string Code, string Name, int SortOrder)> Languages { get; } =
    [
        N("nl", "Nederlands", 0),
        N("fr", "Frans", 1),
        N("en", "Engels", 2),
        N("de", "Duits", 3),
        N("pl", "Pools", 4),

        N("bg", "Bulgaars"), N("bs", "Bosnisch"), N("cs", "Tsjechisch"), N("da", "Deens"),
        N("el", "Grieks"), N("es", "Spaans"), N("et", "Ests"), N("fi", "Fins"),
        N("hr", "Kroatisch"), N("hu", "Hongaars"), N("it", "Italiaans"), N("lt", "Litouws"),
        N("lv", "Lets"), N("mk", "Macedonisch"), N("no", "Noors"), N("pt", "Portugees"),
        N("ro", "Roemeens"), N("ru", "Russisch"), N("sk", "Slowaaks"), N("sl", "Sloveens"),
        N("sq", "Albanees"), N("sr", "Servisch"), N("sv", "Zweeds"), N("tr", "Turks"),
        N("uk", "Oekraïens"),

        N("ar", "Arabisch"), N("fa", "Perzisch"), N("hi", "Hindi"), N("ja", "Japans"),
        N("ko", "Koreaans"), N("tl", "Filipijns (Tagalog)"), N("vi", "Vietnamees"), N("zh", "Chinees"),
    ];
}
