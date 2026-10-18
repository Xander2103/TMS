using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

public record PortalUpcomingDeliveryDto(Guid OrderId, string OrderNumber, DateTime PlannedAt, string? City);

public record PortalRecentInvoiceDto(Guid Id, string InvoiceNumber, DateOnly InvoiceDate, InvoiceStatus Status, decimal Total)
{
    /// <summary>KP-09: a credit note is shown as such (and with a minus sign) on the dashboard too.</summary>
    public InvoiceKind Kind { get; init; } = InvoiceKind.Invoice;
    public string Currency { get; init; } = "EUR";
}

/// <summary>A request that waits for the customer (an unanswered "Info opvragen" question).</summary>
public record PortalActionItemDto(Guid OrderId, string OrderNumber);

public record PortalDashboardDto(
    int ActiveOrders,
    IReadOnlyList<PortalUpcomingDeliveryDto> UpcomingDeliveries,
    int ProblemOrders,
    int UnreadMessages,
    IReadOnlyList<PortalRecentInvoiceDto> RecentInvoices,
    IReadOnlyList<PortalAnnouncementDto> Announcements)
{
    public IReadOnlyList<PortalActionItemDto> AwaitingYourReply { get; init; } = [];
}
