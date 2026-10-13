using TransportationService.Api.Modules.Operations.Dtos;

namespace TransportationService.Api.Modules.Operations.Services;

public interface IAlertService
{
    Task<IReadOnlyList<OperationalAlertDto>> ListAsync(AlertQuery query, CancellationToken cancellationToken);

    /// <summary>Marks the alert as seen-by-a-human; idempotent on an already acknowledged alert.</summary>
    Task<OperationalAlertDto?> AcknowledgeAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationalAlertDto?> ResolveAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationalAlertDto?> AssignAsync(Guid id, Guid? userId, CancellationToken cancellationToken);
}
