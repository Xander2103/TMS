using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

/// <summary>Optional context for a staged custody event; every field maps 1:1 onto PackageEvent.</summary>
public sealed record PackageEventContext(
    Guid? TripId = null,
    Guid? StopId = null,
    Guid? OrderId = null,
    Guid? DriverId = null,
    string? DeviceInfo = null,
    string? BarcodeUsed = null,
    string? Result = null,
    Guid? ScanEventId = null,
    Guid? ExceptionId = null,
    bool IsOverride = false,
    string? Notes = null,
    Guid? ClientEventId = null);

public interface IPackageEventWriter
{
    /// <summary>
    /// STAGES an immutable custody event on the shared DbContext (never saves) so it commits
    /// atomically with the mutation that caused it. There is deliberately no update/delete
    /// counterpart anywhere — corrections append new events.
    /// </summary>
    PackageEvent Stage(
        Package package, PackageEventType type,
        PackageLifecycleStatus? oldStatus, PackageLifecycleStatus? newStatus,
        PackageEventContext? context = null);
}

public class PackageEventWriter : IPackageEventWriter
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly TimeProvider _timeProvider;

    public PackageEventWriter(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _timeProvider = timeProvider;
    }

    public PackageEvent Stage(
        Package package, PackageEventType type,
        PackageLifecycleStatus? oldStatus, PackageLifecycleStatus? newStatus,
        PackageEventContext? context = null)
    {
        var packageEvent = new PackageEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            PackageId = package.Id,
            EventType = type,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            TripId = context?.TripId,
            TransportOrderStopId = context?.StopId,
            TransportOrderId = context?.OrderId ?? package.TransportOrderId,
            UserId = _currentUserContext.CurrentUserId,
            DriverId = context?.DriverId,
            DeviceInfo = context?.DeviceInfo,
            BarcodeUsed = context?.BarcodeUsed,
            Result = context?.Result,
            ScanEventId = context?.ScanEventId,
            ExceptionId = context?.ExceptionId,
            IsOverride = context?.IsOverride ?? false,
            Notes = context?.Notes,
            ClientEventId = context?.ClientEventId,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _dbContext.Add(packageEvent);
        return packageEvent;
    }
}
