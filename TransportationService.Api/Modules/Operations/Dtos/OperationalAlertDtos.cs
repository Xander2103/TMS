using TransportationService.Api.Modules.Operations.Entities;

namespace TransportationService.Api.Modules.Operations.Dtos;

public record OperationalAlertDto(
    Guid Id,
    AlertSeverity Severity,
    string Category,
    string Source,
    string Title,
    string Message,
    string? LinkPath,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    AlertStatus Status,
    Guid? AssignedUserId,
    string? AssignedUserName,
    Guid? AcknowledgedByUserId,
    string? AcknowledgedByName,
    DateTime? AcknowledgedAt,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime LastSeenAt);

public record AlertQuery(
    AlertStatus? Status = null,
    AlertSeverity? Severity = null,
    string? Category = null,
    int Limit = 200);

public record AssignAlertRequest(Guid? UserId);
