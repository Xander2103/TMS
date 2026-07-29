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

/// <summary>One personnel note pinned to the dashboard — only ever populated for callers
/// holding employee_notes.view (see DashboardService.GetAsync).</summary>
public record PinnedEmployeeNoteDto(
    Guid NoteId,
    Guid EmployeeId,
    string EmployeeName,
    string Excerpt,
    DateTime PinnedAt,
    string? AuthorName);

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
    int QualificationsExpiring30d,
    int QualificationsExpired,
    int OpenIncidentCount,
    int MissingPodCount,
    int FailedScanCount,
    int OverdueMaintenanceCount,
    IReadOnlyList<RecentOrderDto> RecentOrders,
    IReadOnlyList<TripListItemDto> TripsToday,
    IReadOnlyList<PinnedEmployeeNoteDto> PinnedEmployeeNotes);
