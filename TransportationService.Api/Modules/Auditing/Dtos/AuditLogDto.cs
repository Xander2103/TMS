namespace TransportationService.Api.Modules.Auditing.Dtos;

public record AuditLogDto(Guid Id, Guid? UserId, string EntityType, string EntityId, string Action, string? OldValuesJson, string? NewValuesJson, DateTime Timestamp);
public record AuditLogPagedResult(IReadOnlyList<AuditLogDto> Items, int TotalCount, int Page, int PageSize);
