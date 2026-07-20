using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Dtos;

/// <summary>Driver home: what am I doing now, what comes next, what needs my attention.</summary>
public record MyDashboardDto(
    MyTripDto? CurrentTrip,
    MyTripDto? NextTrip,
    /// <summary>Next open stop of the current trip.</summary>
    string? NextStopCity,
    string? NextStopLocationName,
    DateTime? NextStopPlannedFrom,
    DateTime? NextStopEta,
    EtaSource? NextStopEtaSource,
    int OpenStopCount,
    int UnresolvedExceptionCount,
    int ActiveIncidentCount,
    int TodayTripCount);

/// <summary>A document the driver may see: papers of the assets on their active trips.</summary>
public record DriverDocumentDto(
    Guid Id,
    string Source,
    string AssetNumber,
    FleetDocumentType DocumentType,
    string? CustomTypeName,
    string? DocumentNumber,
    DateOnly? ExpiryDate,
    bool FileAvailable);

public record CreateDriverIncidentRequest(
    string Title,
    string Description,
    string IncidentType,
    string Severity,
    string? CustomTypeName = null,
    Guid? TripId = null,
    Guid? TransportOrderId = null,
    Guid? VehicleId = null,
    Guid? TrailerId = null,
    Guid? ClientRequestId = null);
