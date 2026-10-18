using System.Globalization;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// T-H6: the Dutch words of the dossier timeline. Every code that used to leak into the history
/// (confirmation checks such as <c>order.not_completed</c>, readiness reasons such as
/// <c>pod.missing</c>, the auto-confirmation trigger <c>trip:RIT-0023</c>, enum names) is read
/// here; an unknown future code reads neutrally instead of as a raw code.
/// </summary>
public static class DossierTimelineLabels
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-BE");

    /// <summary>
    /// Confirmation checks (<see cref="DossierLifecycleService"/> evaluation) and the readiness /
    /// invoice-readiness reasons they borrow from (<see cref="DossierReadinessService"/>,
    /// <c>InvoiceReadinessEvaluator</c>).
    /// </summary>
    public static string ConfirmationCheck(string code) => code switch
    {
        "dossier.not_open" => "Dossier was niet open",
        "incident.open" => "Open incidenten",
        "exception.open.info" => "Open afwijking (ter info)",
        "exception.open.warning" => "Open afwijking (waarschuwing)",
        "exception.open.hold" => "Open blokkerende afwijking",
        "activity.none" => "Geen activiteiten",
        "activity.not_executed" => "Activiteit nog niet uitgevoerd",
        "route.order_missing" => "Transportactiviteit zonder transportopdracht",
        "route.date_missing" => "Route zonder datum",
        "order.not_completed" => "Opdracht nog niet uitgevoerd",
        "order.awaiting_review" => "Klantaanvraag nog niet beoordeeld",
        "delivery.not_completed" => "Levering niet volledig afgerond",
        "delivery.pod_missing" or "pod.missing" => "Afleverbewijs (POD) ontbreekt",
        "nothing.completed" => "Nog niets uitgevoerd",
        "documents.cmr_missing" => "CMR ontbreekt",
        "pricing.missing" => "Verkoopprijs ontbreekt",
        "pricing.zero" => "Verkoopprijs van € 0,00",
        "pricing.none" => "Nog geen verkooplijnen of prijs",
        "pricing.incomplete" => "Prijs onvolledig",
        "pricing.stale" => "Prijs moet herberekend worden",
        _ when code.StartsWith("pricing.coverage.", StringComparison.Ordinal) => "Tarief dekt niet alle goederen",
        _ when code.StartsWith("pricing.", StringComparison.Ordinal) => "Aandachtspunt bij de prijs",
        _ => "Ander aandachtspunt",
    };

    /// <summary>"trip:RIT-0023" → "na voltooiing van rit RIT-0023"; null when the trigger says nothing readable.</summary>
    public static string? AutoConfirmTrigger(string? trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger)) return null;
        var separator = trigger.IndexOf(':');
        if (separator <= 0 || separator == trigger.Length - 1) return null;
        var reference = trigger[(separator + 1)..].Trim();
        return trigger[..separator] switch
        {
            "trip" => $"na voltooiing van rit {reference}",
            "order" => $"na voltooiing van opdracht {reference}",
            "activity" => $"na afhandeling van activiteit {reference}",
            _ => null,
        };
    }

    public static string OrderStatus(TransportOrderStatus status) => Orders.Services.TransportOrderStatusLabels.Dutch(status);

    public static string DossierStatus(Entities.DossierStatus status) => status switch
    {
        Entities.DossierStatus.Open => "Open",
        Entities.DossierStatus.Closed => "Bevestigd",
        Entities.DossierStatus.Cancelled => "Geannuleerd",
        _ => "Onbekend",
    };

    public static string StopType(StopType type) => type switch
    {
        Orders.Entities.StopType.Loading => "laden",
        Orders.Entities.StopType.Unloading => "lossen",
        Orders.Entities.StopType.Site => "werf",
        _ => "stop",
    };

    /// <summary>Terminal stop outcomes only — intermediate steps (aangekomen, laden…) stay out of the dossier story.</summary>
    public static string? StopOutcome(StopExecutionStatus status) => status switch
    {
        StopExecutionStatus.Completed => "afgerond",
        StopExecutionStatus.PartiallyCompleted => "gedeeltelijk afgerond",
        StopExecutionStatus.Failed => "mislukt",
        StopExecutionStatus.Skipped => "overgeslagen",
        _ => null,
    };

    public static string IssuedDocumentKind(IssuedTransportDocumentKind kind) => kind switch
    {
        IssuedTransportDocumentKind.Cmr => "CMR",
        IssuedTransportDocumentKind.DeliveryNote => "Leveringsbon",
        IssuedTransportDocumentKind.WorkOrder => "Werkbon",
        _ => "Transportdocument",
    };

    public static string DocumentType(TransportOrderDocumentType type, string? customName) => type switch
    {
        TransportOrderDocumentType.CustomerDeliveryNote => "Leveringsbon van de klant",
        TransportOrderDocumentType.DeliveryNote => "Leveringsbon",
        TransportOrderDocumentType.Cmr => "CMR",
        _ => string.IsNullOrWhiteSpace(customName) ? "Document" : customName.Trim(),
    };

    public static string PodOutcome(PodOutcome outcome) => outcome switch
    {
        Pod.Entities.PodOutcome.Complete => "Volledig geleverd",
        Pod.Entities.PodOutcome.Partial => "Gedeeltelijk geleverd",
        Pod.Entities.PodOutcome.Refused => "Levering geweigerd",
        _ => "Onbekend",
    };

    public static string IncidentType(IncidentType type, string? customName) => type switch
    {
        Incidents.Entities.IncidentType.Damage => "Schade",
        Incidents.Entities.IncidentType.Delay => "Vertraging",
        Incidents.Entities.IncidentType.Theft => "Diefstal",
        Incidents.Entities.IncidentType.Accident => "Ongeval",
        Incidents.Entities.IncidentType.WrongDelivery => "Verkeerde levering",
        Incidents.Entities.IncidentType.MissingGoods => "Ontbrekende goederen",
        Incidents.Entities.IncidentType.CustomerComplaint => "Klacht van de klant",
        Incidents.Entities.IncidentType.VehicleBreakdown => "Voertuigpanne",
        Incidents.Entities.IncidentType.Administrative => "Administratief",
        _ => string.IsNullOrWhiteSpace(customName) ? "Overig" : customName.Trim(),
    };

    public static string IncidentSeverity(IncidentSeverity severity) => severity switch
    {
        Incidents.Entities.IncidentSeverity.Low => "Laag",
        Incidents.Entities.IncidentSeverity.Medium => "Gemiddeld",
        Incidents.Entities.IncidentSeverity.High => "Hoog",
        Incidents.Entities.IncidentSeverity.Critical => "Kritiek",
        _ => "Onbekend",
    };

    public static string ExceptionType(ExecutionExceptionType type) => type switch
    {
        ExecutionExceptionType.MissingPackage => "Ontbrekend collo",
        ExecutionExceptionType.WrongPackage => "Verkeerd collo",
        ExecutionExceptionType.DamagedPackage => "Beschadigd collo",
        ExecutionExceptionType.UnknownPackage => "Onbekend collo",
        ExecutionExceptionType.WrongRoutePackage => "Collo voor een andere rit",
        ExecutionExceptionType.WrongStopPackage => "Collo voor een andere stop",
        ExecutionExceptionType.IncompleteQuantity => "Onvolledige hoeveelheid",
        ExecutionExceptionType.UnreadableBarcode => "Onleesbare barcode",
        ExecutionExceptionType.LabelMissing => "Etiket ontbreekt",
        ExecutionExceptionType.RejectedBeforeLoading => "Geweigerd voor het laden",
        ExecutionExceptionType.RejectedDelivery => "Levering geweigerd",
        ExecutionExceptionType.PartialDelivery => "Gedeeltelijke levering",
        ExecutionExceptionType.AddressIssue => "Probleem met het adres",
        ExecutionExceptionType.CustomerUnavailable => "Klant niet aanwezig",
        ExecutionExceptionType.AccessDenied => "Geen toegang",
        ExecutionExceptionType.VehicleIssue => "Probleem met het voertuig",
        ExecutionExceptionType.Delay => "Vertraging",
        _ => "Andere afwijking",
    };

    public static string ExceptionSeverity(ExceptionSeverity severity) => severity switch
    {
        Exceptions.Entities.ExceptionSeverity.Low => "Laag",
        Exceptions.Entities.ExceptionSeverity.Medium => "Gemiddeld",
        Exceptions.Entities.ExceptionSeverity.High => "Hoog",
        Exceptions.Entities.ExceptionSeverity.Critical => "Kritiek",
        _ => "Onbekend",
    };

    public static string OrderPricingStatus(string? status) => status switch
    {
        "Draft" or "0" => "concept",
        "Reviewed" or "1" => "nagekeken",
        "Locked" or "2" => "vergrendeld",
        "Invoiced" or "3" => "gefactureerd",
        _ => "gewijzigd",
    };

    public static string Euro(decimal amount) => "€ " + amount.ToString("N2", Nl);

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateTime date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string YesNo(bool value) => value ? "Ja" : "Nee";
}
