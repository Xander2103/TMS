using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

public record PortalUpcomingDeliveryDto(Guid OrderId, string OrderNumber, DateTime PlannedAt, string? City);

public record PortalRecentInvoiceDto(Guid Id, string InvoiceNumber, DateOnly InvoiceDate, InvoiceStatus Status, decimal Total);

public record PortalDashboardDto(
    int ActiveOrders,
    IReadOnlyList<PortalUpcomingDeliveryDto> UpcomingDeliveries,
    int ProblemOrders,
    int UnreadMessages,
    IReadOnlyList<PortalRecentInvoiceDto> RecentInvoices,
    IReadOnlyList<PortalAnnouncementDto> Announcements);
