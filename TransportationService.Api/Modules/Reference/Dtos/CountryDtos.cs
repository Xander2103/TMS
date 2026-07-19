namespace TransportationService.Api.Modules.Reference.Dtos;

/// <summary>Combobox option for country selection; <c>Code</c> is the stored value.</summary>
public sealed record CountryOptionDto(string Code, string Alpha3, string Name, bool IsEuMember);
