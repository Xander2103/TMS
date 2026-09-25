using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Dossiers.Entities;

/// <summary>
/// D7 (master sprint 2026-09-21): one free-text note on a dossier, optionally about ONE of its
/// activities (<see cref="DossierActivityId"/> null = dossier-level). Follows the
/// <c>EmployeeNote</c> pattern: author = <c>CreatedByUserId</c>, time = <c>CreatedAt</c>, soft
/// delete. Replaces the legacy single <c>TransportDossier.Notes</c> / <c>DossierActivity.Notes</c>
/// fields going forward — those columns stay (read-only history), were copied into a first note
/// by the introducing migration and are never written by the note endpoints. A note is not an
/// audit line; creating/changing/deleting one IS audited (without the full text).
/// </summary>
public class DossierNote : AuditableTenantEntity
{
    public Guid DossierId { get; set; }

    /// <summary>The activity the note is about; must belong to <see cref="DossierId"/>. Null = dossier-level.</summary>
    public Guid? DossierActivityId { get; set; }

    public string Text { get; set; } = string.Empty;
}
