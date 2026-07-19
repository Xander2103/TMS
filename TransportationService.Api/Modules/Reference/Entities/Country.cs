namespace TransportationService.Api.Modules.Reference.Entities;

/// <summary>
/// Global ISO 3166-1 country reference. Not tenant data: every tenant sees the same list,
/// which is seeded/synchronised from <see cref="Data.CountrySeedData"/> at startup and is
/// not editable through the tenant master-data screens.
/// </summary>
public class Country
{
    public Guid Id { get; set; }

    /// <summary>ISO 3166-1 alpha-2 code (e.g. "BE").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-3 code (e.g. "BEL").</summary>
    public string Alpha3 { get; set; } = string.Empty;

    /// <summary>Dutch display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>EU membership, used for VAT-treatment suggestions.</summary>
    public bool IsEuMember { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Pins frequently used countries (BeNeLux etc.) to the top of comboboxes.</summary>
    public int SortOrder { get; set; }
}
