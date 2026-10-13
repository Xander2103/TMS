using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Security;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IKioskDeviceService
{
    Task<IReadOnlyList<KioskDeviceDto>> ListAsync(CancellationToken cancellationToken);
    Task<KioskProvisionResult?> CreateAsync(SaveKioskDeviceRequest request, CancellationToken cancellationToken);
    Task<KioskDeviceDto?> UpdateAsync(Guid id, SaveKioskDeviceRequest request, CancellationToken cancellationToken);
    Task<KioskProvisionResult?> RotateSecretAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Beheer van prikklok-devices. Het device-secret bestaat alleen als hash; de volledige
/// deviceKey wordt uitsluitend éénmalig bij provisioning of rotatie geretourneerd.
/// Elke beheeractie (aanmaken, wijzigen, uitschakelen, rotatie) wordt geauditeerd —
/// zonder secret of hash in de auditpayload.
/// </summary>
public class KioskDeviceService : IKioskDeviceService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public KioskDeviceService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<KioskDeviceDto>> ListAsync(CancellationToken cancellationToken)
    {
        var devices = await Scoped().AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken);
        var locationNames = await LocationNamesAsync(devices.Select(d => d.LocationId), cancellationToken);
        return devices.Select(d => ToDto(d, locationNames)).ToList();
    }

    public async Task<KioskProvisionResult?> CreateAsync(SaveKioskDeviceRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var secret = KioskDeviceSecrets.GenerateSecret();
        var device = new KioskDevice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Name = name,
            LocationId = request.LocationId,
            IsActive = request.IsActive,
            DefaultLanguage = Common.SupportedLanguages.Normalize(request.DefaultLanguage),
            SecretHash = KioskDeviceSecrets.Hash(secret),
        };
        _dbContext.KioskDevices.Add(device);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("KioskDevice", device.Id.ToString(), "Created",
            null, new { device.Name, device.LocationId, device.IsActive, device.DefaultLanguage }, cancellationToken);

        var locationNames = await LocationNamesAsync([device.LocationId], cancellationToken);
        return new KioskProvisionResult(ToDto(device, locationNames), KioskDeviceSecrets.BuildDeviceKey(device.Id, secret));
    }

    public async Task<KioskDeviceDto?> UpdateAsync(Guid id, SaveKioskDeviceRequest request, CancellationToken cancellationToken)
    {
        var device = await Scoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var old = new { device.Name, device.LocationId, device.IsActive, device.DefaultLanguage };
        device.Name = request.Name.Trim();
        device.LocationId = request.LocationId;
        device.IsActive = request.IsActive;
        device.DefaultLanguage = Common.SupportedLanguages.Normalize(request.DefaultLanguage);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var action = old.IsActive && !device.IsActive ? "Disabled" : "Updated";
        await _auditService.RecordAsync("KioskDevice", device.Id.ToString(), action,
            old, new { device.Name, device.LocationId, device.IsActive, device.DefaultLanguage }, cancellationToken);

        var locationNames = await LocationNamesAsync([device.LocationId], cancellationToken);
        return ToDto(device, locationNames);
    }

    public async Task<KioskProvisionResult?> RotateSecretAsync(Guid id, CancellationToken cancellationToken)
    {
        var device = await Scoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var secret = KioskDeviceSecrets.GenerateSecret();
        device.SecretHash = KioskDeviceSecrets.Hash(secret);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("KioskDevice", device.Id.ToString(), "SecretRotated", null,
            new { device.Name }, cancellationToken);

        var locationNames = await LocationNamesAsync([device.LocationId], cancellationToken);
        return new KioskProvisionResult(ToDto(device, locationNames), KioskDeviceSecrets.BuildDeviceKey(device.Id, secret));
    }

    private IQueryable<KioskDevice> Scoped() =>
        _dbContext.KioskDevices.Where(d => d.TenantId == _tenantContext.TenantId);

    private async Task<Dictionary<Guid, string>> LocationNamesAsync(
        IEnumerable<Guid?> locationIds, CancellationToken cancellationToken)
    {
        var ids = locationIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && ids.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
    }

    private static KioskDeviceDto ToDto(KioskDevice device, IReadOnlyDictionary<Guid, string> locationNames) =>
        new(device.Id, device.Name, device.LocationId,
            device.LocationId is { } locId && locationNames.TryGetValue(locId, out var name) ? name : null,
            device.IsActive, device.LastSeenAt, device.LastPunchAt, device.CreatedAt, device.DefaultLanguage);
}
