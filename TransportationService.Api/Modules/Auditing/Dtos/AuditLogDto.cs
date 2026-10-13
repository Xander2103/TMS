namespace TransportationService.Api.Modules.Auditing.Dtos;

/// <summary><paramref name="UserName"/>: the acting person, resolved from the tenant's users; null when no person acted (a pipeline event) or the user is unknown.</summary>
public record AuditLogDto(Guid Id, Guid? UserId, string EntityType, string EntityId, string Action, string? OldValuesJson, string? NewValuesJson, DateTime Timestamp, string? UserName = null);
public record AuditLogPagedResult(IReadOnlyList<AuditLogDto> Items, int TotalCount, int Page, int PageSize);
