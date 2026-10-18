using TransportationService.Api.Modules.Exceptions.Entities;

namespace TransportationService.Api.Modules.Exceptions.Services;

/// <summary>How an OPEN afwijking weighs on confirming its dossier (EX-05).</summary>
public enum ExceptionConfirmationImpact
{
    /// <summary>"Ter info": shown, never blocks, auto-confirmation proceeds (the afwijking stays open).</summary>
    Info,

    /// <summary>"Waarschuwing": manual confirmation asks for a deliberate acknowledgement; auto-confirmation proceeds and records it.</summary>
    Warning,

    /// <summary>"Blokkerend": HOLDS automatic confirmation until reviewed/resolved; an authorised person may
    /// still confirm manually with an explicit acknowledgement AND a reason.</summary>
    Hold,
}

/// <summary>
/// EX-05: the single, central rule mapping an open afwijking's severity to its confirmation
/// impact. Product decision: manual confirmation with an open afwijking stays possible, but an
/// automatic confirmation must never silently swallow a serious one. Callers never compare
/// severity names themselves — they ask this policy.
/// </summary>
public static class ExceptionConfirmationPolicy
{
    public static ExceptionConfirmationImpact For(ExceptionSeverity severity) => severity switch
    {
        ExceptionSeverity.Low => ExceptionConfirmationImpact.Info,
        ExceptionSeverity.Medium => ExceptionConfirmationImpact.Warning,
        _ => ExceptionConfirmationImpact.Hold, // High, Critical
    };

    public static bool HoldsAutoConfirmation(ExceptionSeverity severity) => For(severity) == ExceptionConfirmationImpact.Hold;

    /// <summary>Manual confirmation over a held afwijking needs a written reason (audited).</summary>
    public static bool RequiresReason(ExceptionSeverity severity) => For(severity) == ExceptionConfirmationImpact.Hold;

    /// <summary>The confirmation-check severity the dossier dialog renders: Info | Warning | Hold.</summary>
    public static string CheckSeverity(ExceptionSeverity severity) => For(severity).ToString();
}
