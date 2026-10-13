namespace TransportationService.Api.Modules.Tarification.Dtos;

/// <summary>Commit target: update the source table in place, or land the file on a fresh version.</summary>
public enum PricingImportMode
{
    UpdateAgreement,
    DuplicateAsNewVersion,
}

/// <summary>One row-level problem or duplicate-row warning, 1-based including the header row.</summary>
public record PricingImportRowMessageDto(int Row, string Message);

/// <summary>
/// One classified rule change. Added rows carry a human Summary; Updated/Removed rows carry the
/// matched RuleId and (for Updated) the list of "Label: old → new" field differences.
/// </summary>
public record PricingImportRuleChangeDto(
    string Name, string? Summary = null, Guid? RuleId = null, IReadOnlyList<string>? FieldChanges = null);

public record PricingImportPreviewDto(
    int RowsFound, int RowsValid,
    IReadOnlyList<PricingImportRowMessageDto> Warnings,
    IReadOnlyList<PricingImportRowMessageDto> Errors,
    IReadOnlyList<PricingImportRuleChangeDto> Added,
    IReadOnlyList<PricingImportRuleChangeDto> Updated,
    IReadOnlyList<PricingImportRuleChangeDto> Removed,
    /// <summary>Sprint 4F: this exact file was already imported into this table before.</summary>
    bool AlreadyImported = false,
    DateTime? PreviousImportAt = null,
    string? PreviousImportFileName = null,
    /// <summary>
    /// Rows without a RegelId column that were matched to an existing rule on exact
    /// Naam + Basis + Eenheid + Zone and are therefore treated as updates, not duplicates.
    /// </summary>
    int MatchedByNameCount = 0,
    /// <summary>Canonical field keys the file/profile actually supplies; absent fields are left untouched on update.</summary>
    IReadOnlyList<string>? PresentFields = null);

/// <summary>
/// Commit form fields (multipart, alongside the file). NewName/NewEffectiveFrom are required
/// for DuplicateAsNewVersion and ignored for UpdateAgreement.
/// </summary>
public record PricingImportCommitRequest(
    PricingImportMode Mode, bool ApplyRemovals, string? NewName, DateOnly? NewEffectiveFrom);

/// <summary>
/// AgreementId is the agreement the import actually landed on: the same id for UpdateAgreement,
/// the freshly created copy's id for DuplicateAsNewVersion.
/// </summary>
public record PricingImportCommitResultDto(
    Guid AgreementId, PricingAgreementDto Agreement, int Added, int Updated, int Removed);
