using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Exceptions.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Exceptions.Services;

public class ExecutionExceptionService : IExecutionExceptionService
{
    private const string EntityType = "ExecutionException";

    /// <summary>Controlled resolution workflow; Resolved and Rejected are final.</summary>
    private static readonly IReadOnlyDictionary<ExecutionExceptionStatus, ExecutionExceptionStatus[]> Transitions =
        new Dictionary<ExecutionExceptionStatus, ExecutionExceptionStatus[]>
        {
            [ExecutionExceptionStatus.Open] =
                [ExecutionExceptionStatus.Investigating, ExecutionExceptionStatus.Resolved, ExecutionExceptionStatus.Rejected, ExecutionExceptionStatus.CustomerActionRequired],
            [ExecutionExceptionStatus.Investigating] =
                [ExecutionExceptionStatus.Resolved, ExecutionExceptionStatus.Rejected, ExecutionExceptionStatus.CustomerActionRequired],
            [ExecutionExceptionStatus.CustomerActionRequired] =
                [ExecutionExceptionStatus.Investigating, ExecutionExceptionStatus.Resolved, ExecutionExceptionStatus.Rejected],
            [ExecutionExceptionStatus.Resolved] = [],
            [ExecutionExceptionStatus.Rejected] = [],
        };

    private static readonly string[] AllowedPhotoExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly IFileStorageService _fileStorageService;
    private readonly TimeProvider _timeProvider;

    public ExecutionExceptionService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        INotificationService notificationService,
        IFileStorageService fileStorageService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _notificationService = notificationService;
        _fileStorageService = fileStorageService;
        _timeProvider = timeProvider;
    }

    public async Task<ExceptionOperationResult> ReportAsync(
        Guid tripId, ReportExceptionRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return ExceptionOperationResult.Invalid("Een omschrijving van het probleem is verplicht.");
        }

        if (request.Quantity is < 0)
        {
            return ExceptionOperationResult.Invalid("De hoeveelheid kan niet negatief zijn.");
        }

        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return ExceptionOperationResult.NotFound;
        }

        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (restrictToOwnDriver && (driverId is null || trip.DriverId != driverId))
        {
            return ExceptionOperationResult.NotYourTrip;
        }

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();

        Guid? orderId = null;
        if (request.TransportOrderStopId is { } stopId)
        {
            var stop = await _dbContext.TransportOrderStops.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == stopId && s.TenantId == _tenantContext.TenantId
                                          && orderIds.Contains(s.TransportOrderId), cancellationToken);
            if (stop is null)
            {
                return ExceptionOperationResult.NotFound;
            }

            orderId = stop.TransportOrderId;
        }

        if (request.CargoItemId is { } cargoId)
        {
            var cargoBelongs = await _dbContext.CargoItems.AsNoTracking()
                .AnyAsync(c => c.Id == cargoId && c.TenantId == _tenantContext.TenantId
                               && orderIds.Contains(c.TransportOrderId), cancellationToken);
            if (!cargoBelongs)
            {
                return ExceptionOperationResult.NotFound;
            }
        }

        var exception = new ExecutionException
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = tripId,
            TransportOrderId = orderId,
            TransportOrderStopId = request.TransportOrderStopId,
            CargoItemId = request.CargoItemId,
            Type = request.Type,
            Severity = request.Severity,
            Status = ExecutionExceptionStatus.Open,
            Description = request.Description.Trim(),
            Quantity = request.Quantity,
            ReportedByUserId = _currentUserContext.CurrentUserId,
            DriverId = driverId,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
        };
        _dbContext.Add(exception);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, exception.Id.ToString(), "Reported", null,
            new { exception.TripId, exception.Type, exception.Severity, exception.Description }, cancellationToken);

        // Everyone who can resolve exceptions hears about new ones.
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.ExceptionsResolve,
            "exception_reported",
            "Nieuwe afwijking gemeld",
            $"{trip.TripNumber}: {ExceptionTypeLabel(exception.Type)} — {exception.Description}",
            $"/exceptions/{exception.Id}",
            cancellationToken);

        return ExceptionOperationResult.Success((await MapDetailAsync(exception.Id, cancellationToken))!);
    }

    public async Task<PagedResult<ExceptionListItemDto>> SearchAsync(
        ExecutionExceptionStatus? status, ExecutionExceptionType? type, ExceptionSeverity? severity,
        int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var pageRequest = PageRequest.Of(page, pageSize);

        var query = _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId);
        if (status is { } s) query = query.Where(e => e.Status == s);
        if (type is { } t) query = query.Where(e => e.Type == t);
        if (severity is { } sev) query = query.Where(e => e.Severity == sev);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .ToListAsync(cancellationToken);

        var items = new List<ExceptionListItemDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await MapListItemAsync(row, cancellationToken));
        }

        return new PagedResult<ExceptionListItemDto>(items, totalCount, pageRequest.Page, pageRequest.PageSize);
    }

    public Task<ExceptionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        MapDetailAsync(id, cancellationToken);

    public async Task<ExceptionListResult> ListForTripAsync(
        Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var trip = await _dbContext.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return ExceptionListResult.NotFound;
        }

        if (restrictToOwnDriver)
        {
            var driverId = await CurrentDriverIdAsync(cancellationToken);
            if (driverId is null || trip.DriverId != driverId)
            {
                return ExceptionListResult.NotYourTrip;
            }
        }

        var rows = await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.TripId == tripId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

        var items = new List<ExceptionListItemDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await MapListItemAsync(row, cancellationToken));
        }

        return ExceptionListResult.Success(items);
    }

    public async Task<ExceptionOperationResult> ChangeStatusAsync(
        Guid id, ChangeExceptionStatusRequest request, CancellationToken cancellationToken)
    {
        var exception = await Scoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (exception is null)
        {
            return ExceptionOperationResult.NotFound;
        }

        if (!Transitions[exception.Status].Contains(request.Status))
        {
            return ExceptionOperationResult.InvalidState(
                $"Een afwijking met status '{exception.Status}' kan niet naar '{request.Status}'.");
        }

        var note = Trim(request.Note);
        var isTerminal = request.Status is ExecutionExceptionStatus.Resolved or ExecutionExceptionStatus.Rejected;
        if (isTerminal && note is null)
        {
            return ExceptionOperationResult.Invalid("Een afhandelingsnotitie is verplicht bij het afsluiten van een afwijking.");
        }

        var before = new { exception.Status };
        exception.Status = request.Status;
        if (isTerminal)
        {
            exception.ResolutionNote = note;
            exception.ResolvedByUserId = _currentUserContext.CurrentUserId;
            exception.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }
        else if (note is not null)
        {
            exception.DispatcherNotes = string.IsNullOrWhiteSpace(exception.DispatcherNotes)
                ? note
                : $"{exception.DispatcherNotes}\n{note}";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, exception.Id.ToString(), "StatusChanged", before,
            new { exception.Status, Note = note }, cancellationToken);

        // The reporter hears every decision on their report.
        await _notificationService.NotifyAsync(
            exception.ReportedByUserId,
            "exception_decided",
            "Afwijking bijgewerkt",
            note is null
                ? $"Je melding ({ExceptionTypeLabel(exception.Type)}) is nu: {StatusLabel(request.Status)}."
                : $"Je melding ({ExceptionTypeLabel(exception.Type)}): {StatusLabel(request.Status)} — {note}",
            $"/my-trips/{exception.TripId}",
            cancellationToken);

        return ExceptionOperationResult.Success((await MapDetailAsync(exception.Id, cancellationToken))!);
    }

    public async Task<ExceptionOperationResult> UpdateAsync(
        Guid id, UpdateExceptionRequest request, CancellationToken cancellationToken)
    {
        var exception = await Scoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (exception is null)
        {
            return ExceptionOperationResult.NotFound;
        }

        var before = new { exception.Severity, exception.DispatcherNotes, exception.CustomerVisible };
        exception.Severity = request.Severity;
        exception.DispatcherNotes = Trim(request.DispatcherNotes);
        exception.CustomerVisible = request.CustomerVisible;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, exception.Id.ToString(), "Updated", before,
            new { exception.Severity, exception.DispatcherNotes, exception.CustomerVisible }, cancellationToken);

        return ExceptionOperationResult.Success((await MapDetailAsync(exception.Id, cancellationToken))!);
    }

    public async Task<ExceptionOperationResult> AttachPhotoAsync(
        Guid id, string fileName, string contentType, Stream content, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var exception = await Scoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (exception is null)
        {
            return ExceptionOperationResult.NotFound;
        }

        if (restrictToOwnDriver)
        {
            var driverId = await CurrentDriverIdAsync(cancellationToken);
            var tripDriverId = await _dbContext.Trips.AsNoTracking()
                .Where(t => t.Id == exception.TripId && t.TenantId == _tenantContext.TenantId)
                .Select(t => t.DriverId)
                .FirstOrDefaultAsync(cancellationToken);
            if (driverId is null || tripDriverId != driverId)
            {
                return ExceptionOperationResult.NotYourTrip;
            }
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedPhotoExtensions.Contains(extension))
        {
            return ExceptionOperationResult.Invalid("Alleen foto's (.jpg, .jpeg, .png, .webp) zijn toegestaan.");
        }

        var storagePath = await _fileStorageService.SaveAsync(
            _tenantContext.TenantId, "exception-photos", fileName, content, cancellationToken);

        var photo = new ExceptionPhoto
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            ExecutionExceptionId = exception.Id,
            FileName = fileName,
            ContentType = contentType,
            StoragePath = storagePath,
        };
        _dbContext.Add(photo);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, exception.Id.ToString(), "PhotoAttached", null,
            new { photo.FileName }, cancellationToken);

        return ExceptionOperationResult.Success((await MapDetailAsync(exception.Id, cancellationToken))!);
    }

    public async Task<(Stream Content, string ContentType, string FileName)?> OpenPhotoAsync(
        Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await _dbContext.ExceptionPhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ExecutionExceptionId == id
                                      && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var stream = await _fileStorageService.OpenReadAsync(photo.StoragePath, cancellationToken);
        return (stream, photo.ContentType, photo.FileName);
    }

    public async Task<ExceptionOperationResult> DeletePhotoAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        var exception = await Scoped().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (exception is null)
        {
            return ExceptionOperationResult.NotFound;
        }

        var photo = await _dbContext.ExceptionPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ExecutionExceptionId == id
                                      && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (photo is null)
        {
            return ExceptionOperationResult.NotFound;
        }

        await _fileStorageService.DeleteAsync(photo.StoragePath, cancellationToken);
        _dbContext.Remove(photo); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, exception.Id.ToString(), "PhotoDeleted", new { photo.FileName }, null, cancellationToken);

        return ExceptionOperationResult.Success((await MapDetailAsync(exception.Id, cancellationToken))!);
    }

    private IQueryable<ExecutionException> Scoped() =>
        _dbContext.ExecutionExceptions.Where(e => e.TenantId == _tenantContext.TenantId);

    private async Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.EmployeeId != null)
            .Join(_dbContext.Drivers.AsNoTracking().Where(d => d.TenantId == _tenantContext.TenantId),
                u => u.EmployeeId, d => d.EmployeeId, (u, d) => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ExceptionListItemDto> MapListItemAsync(ExecutionException row, CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(row, cancellationToken);
        var photoCount = await _dbContext.ExceptionPhotos.AsNoTracking()
            .CountAsync(p => p.ExecutionExceptionId == row.Id && p.TenantId == _tenantContext.TenantId, cancellationToken);
        return new ExceptionListItemDto(
            row.Id, row.OccurredAt, row.Type, row.Severity, row.Status, row.Description,
            context.TripNumber, context.OrderNumber, context.StopLabel, context.ReporterName,
            photoCount, row.CustomerVisible);
    }

    private sealed record ExceptionContext(
        string TripNumber, string? OrderNumber, string? StopLabel, string? CargoDescription,
        string? ReporterName, string? DriverName, string? ResolvedByName);

    private async Task<ExceptionContext> LoadContextAsync(ExecutionException row, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var tripNumber = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.Id == row.TripId && t.TenantId == tenantId)
            .Select(t => t.TripNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var orderNumber = row.TransportOrderId is { } orderId
            ? await _dbContext.TransportOrders.AsNoTracking().IgnoreQueryFilters()
                .Where(o => o.Id == orderId && o.TenantId == tenantId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        string? stopLabel = null;
        if (row.TransportOrderStopId is { } stopId)
        {
            stopLabel = await _dbContext.TransportOrderStops.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.Id == stopId && s.TenantId == tenantId)
                .Select(s => s.LocationName ?? s.City)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var cargoDescription = row.CargoItemId is { } cargoId
            ? await _dbContext.CargoItems.AsNoTracking().IgnoreQueryFilters()
                .Where(c => c.Id == cargoId && c.TenantId == tenantId)
                .Select(c => c.Description)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var reporterName = await UserNameAsync(row.ReportedByUserId, cancellationToken);
        var resolvedByName = await UserNameAsync(row.ResolvedByUserId, cancellationToken);

        string? driverName = null;
        if (row.DriverId is { } driverId)
        {
            driverName = await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.Id == driverId && d.TenantId == tenantId)
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new ExceptionContext(tripNumber, orderNumber, stopLabel, cargoDescription, reporterName, driverName, resolvedByName);
    }

    private async Task<string?> UserNameAsync(Guid? userId, CancellationToken cancellationToken) =>
        userId is { } id
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == id && u.TenantId == _tenantContext.TenantId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

    private async Task<ExceptionDetailDto?> MapDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _dbContext.ExecutionExceptions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var context = await LoadContextAsync(row, cancellationToken);
        var photos = await _dbContext.ExceptionPhotos.AsNoTracking()
            .Where(p => p.ExecutionExceptionId == row.Id && p.TenantId == _tenantContext.TenantId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ExceptionPhotoDto(p.Id, p.FileName, p.ContentType, p.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ExceptionDetailDto(
            row.Id, row.Type, row.Severity, row.Status, row.Description, row.Quantity,
            row.TripId, context.TripNumber,
            row.TransportOrderId, context.OrderNumber,
            row.TransportOrderStopId, context.StopLabel,
            row.CargoItemId, context.CargoDescription,
            context.ReporterName, context.DriverName,
            row.OccurredAt, row.Latitude, row.Longitude,
            row.DispatcherNotes, row.CustomerVisible,
            row.ResolutionNote, context.ResolvedByName, row.ResolvedAt,
            photos, Transitions[row.Status]);
    }

    private static string ExceptionTypeLabel(ExecutionExceptionType type) => type switch
    {
        ExecutionExceptionType.MissingPackage => "Ontbrekend collo",
        ExecutionExceptionType.WrongPackage => "Verkeerd collo",
        ExecutionExceptionType.DamagedPackage => "Beschadigd collo",
        ExecutionExceptionType.RejectedDelivery => "Levering geweigerd",
        ExecutionExceptionType.PartialDelivery => "Gedeeltelijke levering",
        ExecutionExceptionType.AddressIssue => "Adresprobleem",
        ExecutionExceptionType.CustomerUnavailable => "Klant niet aanwezig",
        ExecutionExceptionType.AccessDenied => "Geen toegang",
        ExecutionExceptionType.VehicleIssue => "Voertuigprobleem",
        ExecutionExceptionType.Delay => "Vertraging",
        _ => "Andere afwijking",
    };

    private static string StatusLabel(ExecutionExceptionStatus status) => status switch
    {
        ExecutionExceptionStatus.Open => "Open",
        ExecutionExceptionStatus.Investigating => "In onderzoek",
        ExecutionExceptionStatus.Resolved => "Opgelost",
        ExecutionExceptionStatus.Rejected => "Afgewezen",
        _ => "Actie klant vereist",
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
