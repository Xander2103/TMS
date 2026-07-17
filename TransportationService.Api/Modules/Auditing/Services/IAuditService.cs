namespace TransportationService.Api.Modules.Auditing.Services;

public interface IAuditService
{
    Task RecordAsync(string entityType, string entityId, string action, object? oldValues, object? newValues, CancellationToken cancellationToken);
}
