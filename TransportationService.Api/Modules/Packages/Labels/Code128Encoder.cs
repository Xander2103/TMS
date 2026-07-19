namespace TransportationService.Api.Modules.Packages.Labels;

/// <summary>
/// Code 128 (subset B) encoder: turns printable-ASCII content into the module-width
/// sequence a renderer draws as alternating bar/space widths. Hand-written so labels have
/// zero licensing footprint; verified against published reference encodings in tests.
/// </summary>
public static class Code128Encoder
{
    private const int StartB = 104;
    private const int Stop = 106;

    // Widths per symbol id 0..106: 6 digits = 3 bars + 3 spaces (except stop: 7).
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    /// <summary>Symbol ids incl. start, content, checksum and stop; null when unencodable.</summary>
    public static IReadOnlyList<int>? Encode(string content)
    {
        if (content.Length is 0 or > 80 || content.Any(c => c < 0x20 || c > 0x7E))
        {
            return null;
        }

        var symbols = new List<int> { StartB };
        symbols.AddRange(content.Select(c => c - 0x20));

        var checksum = symbols[0];
        for (var position = 1; position < symbols.Count; position += 1)
        {
            checksum += symbols[position] * position;
        }

        symbols.Add(checksum % 103);
        symbols.Add(Stop);
        return symbols;
    }

    /// <summary>
    /// Module widths for the whole barcode, alternating bar/space starting with a bar.
    /// Sum × module width = total barcode width (quiet zones excluded).
    /// </summary>
    public static IReadOnlyList<int>? ModuleWidths(string content)
    {
        var symbols = Encode(content);
        if (symbols is null)
        {
            return null;
        }

        var widths = new List<int>();
        foreach (var symbol in symbols)
        {
            widths.AddRange(Patterns[symbol].Select(c => c - '0'));
        }

        return widths;
    }
}
