using TransportationService.Api.Common;
using TransportationService.Api.Modules.Dossiers.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// Product acceptance sprint 2026-10-07 — ONE answer to "may this dossier still be edited?".
/// Only an Open dossier is editable. A confirmed (status Closed) and a cancelled dossier are both
/// read-only until someone with <c>dossiers.reopen</c> reopens them: the UI already hid every
/// editor for both, but the services only refused Closed, so a cancelled dossier could still be
/// changed through the API. Hiding is not security; every write path now asks here.
/// </summary>
public static class DossierStateRules
{
    public static bool IsEditable(DossierStatus status) => status == DossierStatus.Open;

    /// <summary>User-facing reason, naming the real state ("bevestigd" / "geannuleerd"), never "gesloten".</summary>
    public static string ReadOnlyMessage(DossierStatus status, string action = "bewerkt") =>
        status == DossierStatus.Cancelled
            ? $"Dit dossier is geannuleerd en kan niet worden {action}. Heropen het dossier eerst."
            : $"Dit dossier is bevestigd en kan niet worden {action}. Heropen het dossier eerst.";

    /// <summary>Throws the validation error a write on a confirmed or cancelled dossier gets.</summary>
    public static void RequireEditable(TransportDossier dossier, string action = "bewerkt")
    {
        if (!IsEditable(dossier.Status))
        {
            throw new DomainValidationException(ReadOnlyMessage(dossier.Status, action));
        }
    }
}
