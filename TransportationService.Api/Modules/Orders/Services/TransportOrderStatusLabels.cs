using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Dutch labels for <see cref="TransportOrderStatus"/> in user-facing texts (readiness checks,
/// timelines). The enum name is a code; a planner reads "Gepland", never "Planned" (B32).
/// </summary>
public static class TransportOrderStatusLabels
{
    public static string Dutch(TransportOrderStatus status) => status switch
    {
        TransportOrderStatus.Draft => "Concept",
        TransportOrderStatus.Submitted => "Ingediend",
        TransportOrderStatus.Confirmed => "Bevestigd",
        TransportOrderStatus.Planned => "Gepland",
        TransportOrderStatus.InProgress => "In uitvoering",
        TransportOrderStatus.Completed => "Afgerond",
        TransportOrderStatus.Invoiced => "Gefactureerd",
        TransportOrderStatus.Cancelled => "Geannuleerd",
        _ => status.ToString(),
    };
}
