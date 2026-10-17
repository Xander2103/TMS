namespace TransportationService.Api.Modules.Fleet.Dtos;

/// <summary>
/// FL-H3: one line of the vehicle/trailer timeline. <c>ActorKind</c> is "User" or "System";
/// <c>Category</c> groups the event (lifecycle, status, readiness, document, inspection,
/// maintenance, damage, fuel, leasing, tachograph, assignment, planning); <c>Financial</c> marks
/// lines that carry amounts (only served to fleet_finance.view holders); <c>Technical</c> holds the
/// raw before/after detail for the expandable technical view.
/// </summary>
public record FleetHistoryEventDto(
    string Id,
    DateTime At,
    string ActorKind,
    string? ActorName,
    string Category,
    string Title,
    string? Detail,
    bool Financial = false,
    string? LinkPath = null,
    string? Technical = null);
