using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Exceptions.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;

namespace TransportationService.Api.Modules.Exceptions.Services;

public interface IExecutionExceptionService
{
    /// <summary>Driver/dispatcher report anchored to a trip; stop/cargo context is validated against the trip.</summary>
    Task<ExceptionOperationResult> ReportAsync(
        Guid tripId, ReportExceptionRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<PagedResult<ExceptionListItemDto>> SearchAsync(
        ExecutionExceptionStatus? status, ExecutionExceptionType? type, ExceptionSeverity? severity,
        Guid? tripId, bool? packagesOnly, Guid? assignedToUserId,
        int? page, int? pageSize, CancellationToken cancellationToken);

    Task<ExceptionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Sets or clears the owning dispatcher for follow-up.</summary>
    Task<ExceptionOperationResult> AssignAsync(Guid id, Guid? userId, CancellationToken cancellationToken);

    Task<ExceptionListResult> ListForTripAsync(Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Controlled workflow; Resolved/Rejected demand a note and notify the reporter.</summary>
    Task<ExceptionOperationResult> ChangeStatusAsync(Guid id, ChangeExceptionStatusRequest request, CancellationToken cancellationToken);

    Task<ExceptionOperationResult> UpdateAsync(Guid id, UpdateExceptionRequest request, CancellationToken cancellationToken);

    Task<ExceptionOperationResult> AttachPhotoAsync(
        Guid id, string fileName, string contentType, Stream content, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Driver-workflow callers (restrictToOwnDriver) may only open media of their own trips.</summary>
    Task<(Stream Content, string ContentType, string FileName)?> OpenPhotoAsync(
        Guid id, Guid photoId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<ExceptionOperationResult> DeletePhotoAsync(Guid id, Guid photoId, CancellationToken cancellationToken);
}
