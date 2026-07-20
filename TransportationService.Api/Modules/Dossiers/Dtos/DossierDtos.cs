namespace TransportationService.Api.Modules.Dossiers.Dtos;

public record DossierListItemDto(
    Guid Id,
    string DossierNumber,
    string Title,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    string? ResponsibleName,
    int OrderCount,
    int OpenIncidentCount,
    DateTime CreatedAt);

public record DossierOrderDto(
    Guid LinkId,
    Guid OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    string Status,
    string? GoodsDescription,
    decimal? AgreedPrice);

/// <summary>One relation seen from the dossier being viewed; Other* describes the far end.</summary>
public record DossierRelationDto(
    Guid Id,
    string RelationType,
    string? Notes,
    bool IsOutgoing,
    Guid OtherDossierId,
    string OtherDossierNumber,
    string OtherDossierTitle);

public record DossierIncidentDto(
    Guid Id,
    string Title,
    string IncidentType,
    string Status,
    string Severity,
    DateOnly? DueDate);

/// <summary>
/// Money view of the dossier: agreed order revenue, what was actually invoiced for those
/// orders, and the incident cost estimate/actuals booked against the dossier.
/// </summary>
public record DossierFinancialSummaryDto(
    decimal AgreedOrderTotal,
    decimal InvoicedTotal,
    decimal EstimatedIncidentCost,
    decimal ActualIncidentCost);

public record DossierDetailDto(
    Guid Id,
    string DossierNumber,
    string Title,
    string? Description,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    Guid? ResponsibleUserId,
    string? ResponsibleName,
    DateTime? ClosedAt,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<DossierOrderDto> Orders,
    IReadOnlyList<DossierRelationDto> Relations,
    IReadOnlyList<DossierIncidentDto> Incidents,
    DossierFinancialSummaryDto Financials);

public record SaveDossierRequest(
    string Title,
    string? Description = null,
    Guid? CustomerId = null,
    Guid? ResponsibleUserId = null,
    string? Notes = null);

public record LinkDossierOrderRequest(Guid TransportOrderId);

public record AddDossierRelationRequest(
    Guid TargetDossierId,
    string RelationType,
    string? Notes = null);
