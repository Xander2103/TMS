namespace TransportationService.Api.Modules.OrderImport.Services;

/// <summary>
/// One importable TMS target field of the order Excel import. <see cref="Key"/> is the STABLE
/// identifier stored in a profile's MappingJson columns (exactly the importer's KnownFields —
/// the catalog never offers a field the engine cannot process). <see cref="Group"/> groups the
/// picker (dossier / loading / unloading / goods); labels are translated client-side from the
/// key, the backend stays language-neutral.
/// </summary>
public sealed record OrderImportField(
    string Key,
    string Group,
    /// <summary>Normalized header texts that identify this field with HIGH confidence.</summary>
    IReadOnlyList<string> Aliases,
    /// <summary>Ambiguous/abbreviated headers: suggested with MEDIUM confidence ("Controleren").</summary>
    IReadOnlyList<string> WeakAliases);

/// <summary>
/// The field catalog + deterministic header recognition of the order Excel import. Recognition
/// is alias-based and explainable — normalized exact matches only, no fuzzy scoring and no
/// model: an exact alias is "Herkend" (95), an exact weak alias "Controleren" (70), anything
/// else stays unmapped. Headers that are genuinely ambiguous on their own ("Postcode", "Plaats")
/// are deliberately NOT aliases of either side — guessing the wrong stop is worse than asking.
/// </summary>
public static class OrderImportFields
{
    public const int HighConfidence = 95;
    public const int MediumConfidence = 70;

    public static IReadOnlyList<OrderImportField> All { get; } =
    [
        new("customerReference", "dossier",
            ["klantreferentie", "referentie", "reference", "ref", "customerreference", "customerref", "ordernummer", "orderreference"],
            ["ordernr", "referentienummer", "refnr"]),
        new("orderDate", "dossier",
            ["orderdatum", "orderdate", "dossierdatum", "datum", "date"],
            []),
        new("goodsDescription", "goods",
            ["omschrijving", "omschrijvinggoederen", "goederen", "description", "goodsdescription", "goods", "descriptiondesmarchandises"],
            ["inhoud", "contents"]),
        new("quantity", "goods",
            ["aantal", "quantity", "qty", "hoeveelheid"],
            ["pal", "pallets", "colli", "stuks", "pieces", "pcs"]),
        new("quantityUnit", "goods",
            ["eenheid", "unit", "uom", "unite"],
            []),
        new("weightKg", "goods",
            ["gewicht", "gewichtkg", "weight", "weightkg", "kg", "poids"],
            ["brutogewicht", "nettogewicht", "grossweight", "netweight"]),
        new("loadingLocation", "loading",
            ["laadadres", "laadlocatie", "loadinglocation", "laadplaatsnaam", "afzender", "pickupaddress", "adressechargement"],
            ["from", "pickup", "sender"]),
        new("loadingPostalCode", "loading",
            ["laadpostcode", "postcodeladen", "postcodelaadadres", "loadingpostalcode", "loadingzip", "fromzip", "fromzipcode", "frompostalcode", "cpchargement"],
            []),
        new("loadingCity", "loading",
            ["laadplaats", "laadgemeente", "plaatsladen", "gemeenteladen", "loadingcity", "fromcity", "villechargement"],
            []),
        new("loadingCountry", "loading",
            ["laadland", "landladen", "loadingcountry", "fromcountry", "payschargement"],
            []),
        new("unloadingLocation", "unloading",
            ["losadres", "loslocatie", "unloadinglocation", "bestemming", "ontvanger", "destination", "deliveryaddress", "adresselivraison"],
            ["to", "delivery", "receiver"]),
        new("unloadingPostalCode", "unloading",
            ["lospostcode", "postcodelossen", "postcodelevering", "unloadingpostalcode", "deliverypostcode", "deliveryzip", "destinationzip", "destzip", "tozip", "cplivraison"],
            []),
        new("unloadingCity", "unloading",
            ["losplaats", "losgemeente", "plaatslossen", "plaatslevering", "unloadingcity", "deliverycity", "destinationcity", "tocity", "villelivraison"],
            []),
        new("unloadingCountry", "unloading",
            ["losland", "landlossen", "landlevering", "unloadingcountry", "deliverycountry", "destinationcountry", "tocountry", "payslivraison"],
            []),
        new("adr", "goods",
            ["adr", "gevaarlijkegoederen", "dangerousgoods", "hazmat"],
            ["gevaarlijk"]),
    ];

    /// <summary>Lowercase, letters+digits only: "Destination ZIP" → "destinationzip".</summary>
    public static string NormalizeHeader(string header) =>
        new([.. header.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    /// <summary>
    /// Deterministic suggestion for one header: (field key, confidence), or null when nothing
    /// matches exactly. First catalog entry wins; the catalog owns the priority order.
    /// </summary>
    public static (string Field, int Confidence)? Suggest(string header)
    {
        var normalized = NormalizeHeader(header);
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var field in All)
        {
            if (field.Aliases.Contains(normalized))
            {
                return (field.Key, HighConfidence);
            }
        }

        foreach (var field in All)
        {
            if (field.WeakAliases.Contains(normalized))
            {
                return (field.Key, MediumConfidence);
            }
        }

        return null;
    }
}
