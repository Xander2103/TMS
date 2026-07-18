using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;

namespace TransportationService.Api.Modules.Reporting.Dtos;

public record RecentOrderDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    string CustomerName,
    TransportOrderStatus Status,
    string GoodsDescription);

/// <summary>One-call aggregate for the company dashboard; heavier module views have their own endpoints.</summary>
public record DashboardDto(
    int OrdersOpenCount,
    int OrdersInExecutionCount,
    int OrdersCompletedThisMonth,
    int TripsTodayTotal,
    int TripsTodayInProgress,
    int TripsTodayWithConflicts,
    decimal RevenueInvoicedThisMonth,
    decimal OutstandingAmount,
    int OverdueInvoiceCount,
    int DriversAbsentToday,
    int VehiclesAvailable,
    int MaintenanceDueCount,
    int InspectionsDueCount,
    int DocumentsExpiringCount,
    int OpenDamageCount,
    IReadOnlyList<RecentOrderDto> RecentOrders,
    IReadOnlyList<TripListItemDto> TripsToday);
