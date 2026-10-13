using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Dtos;

public record BulkChangeStatusRequest(IReadOnlyList<Guid> OrderIds, TransportOrderStatus Status);

public record BulkStatusItemResultDto(Guid OrderId, bool Success, string? Error);

public record BulkStatusResultDto(int SucceededCount, int FailedCount, IReadOnlyList<BulkStatusItemResultDto> Results);
