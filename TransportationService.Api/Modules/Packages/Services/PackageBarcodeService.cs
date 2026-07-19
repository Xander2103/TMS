using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

public sealed record BarcodeResolution(Package Package, PackageBarcode Barcode, bool IsRetired);

public interface IPackageBarcodeService
{
    /// <summary>New internal barcode value for a package number: "{number}-{8 random base32 chars}".</summary>
    string GenerateInternalValue(string packageNumber);

    /// <summary>Validates a manually supplied (external) barcode value: printable ASCII, 4..100 chars.</summary>
    string? ValidateExternalValue(string value);

    /// <summary>Stages the registry rows for a new package (internal + optional external). Never saves.</summary>
    void StageInitialBarcodes(Package package, string? externalBarcode);

    /// <summary>
    /// Retires the active internal barcode and stages a fresh one (immutable package number
    /// kept). Old values stay in the registry and keep resolving as retired.
    /// </summary>
    Task<(string OldValue, string NewValue)> RelabelAsync(Package package, string reason, CancellationToken cancellationToken);

    /// <summary>Resolves a scanned value tenant-wide against the registry (active or retired).</summary>
    Task<BarcodeResolution?> ResolveAsync(string value, CancellationToken cancellationToken);
}

public class PackageBarcodeService : IPackageBarcodeService
{
    // Crockford base32: no I/L/O/U — unambiguous on labels, Code128-encodable.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int SuffixLength = 8;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly TimeProvider _timeProvider;

    public PackageBarcodeService(
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

    public string GenerateInternalValue(string packageNumber)
    {
        Span<byte> random = stackalloc byte[SuffixLength];
        RandomNumberGenerator.Fill(random);
        Span<char> suffix = stackalloc char[SuffixLength];
        for (var i = 0; i < SuffixLength; i++)
        {
            suffix[i] = Alphabet[random[i] % Alphabet.Length];
        }

        // The random suffix makes values practically globally unique and non-guessable;
        // knowing a barcode still grants nothing — every endpoint is permission-gated.
        return $"{packageNumber}-{new string(suffix)}";
    }

    public string? ValidateExternalValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 4 or > 100)
        {
            return "Een barcode moet tussen 4 en 100 tekens lang zijn.";
        }

        if (trimmed.Any(c => c < 0x20 || c > 0x7E))
        {
            return "Een barcode mag alleen afdrukbare ASCII-tekens bevatten.";
        }

        return null;
    }

    public void StageInitialBarcodes(Package package, string? externalBarcode)
    {
        _dbContext.Add(new PackageBarcode
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            PackageId = package.Id,
            Value = package.BarcodeValue,
            Type = package.BarcodeType,
            IsActive = true,
        });

        if (!string.IsNullOrWhiteSpace(externalBarcode))
        {
            _dbContext.Add(new PackageBarcode
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                PackageId = package.Id,
                Value = externalBarcode.Trim(),
                Type = PackageBarcodeType.External,
                IsActive = true,
            });
        }
    }

    public async Task<(string OldValue, string NewValue)> RelabelAsync(
        Package package, string reason, CancellationToken cancellationToken)
    {
        var active = await _dbContext.PackageBarcodes
            .Where(b => b.TenantId == _tenantContext.TenantId && b.PackageId == package.Id
                        && b.IsActive && b.Type != PackageBarcodeType.External)
            .ToListAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var barcode in active)
        {
            barcode.IsActive = false;
            barcode.RetiredAt = now;
            barcode.RetiredByUserId = _currentUserContext.CurrentUserId;
            barcode.RetireReason = reason;
        }

        var oldValue = package.BarcodeValue;
        var newValue = GenerateInternalValue(package.PackageNumber);
        package.BarcodeValue = newValue;
        _dbContext.Add(new PackageBarcode
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            PackageId = package.Id,
            Value = newValue,
            Type = package.BarcodeType,
            IsActive = true,
        });

        return (oldValue, newValue);
    }

    public async Task<BarcodeResolution?> ResolveAsync(string value, CancellationToken cancellationToken)
    {
        var trimmed = value.Trim();
        var row = await _dbContext.PackageBarcodes.AsNoTracking()
            .Where(b => b.TenantId == _tenantContext.TenantId && b.Value == trimmed)
            .OrderByDescending(b => b.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var package = await _dbContext.Packages
            .FirstOrDefaultAsync(p => p.Id == row.PackageId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        return package is null ? null : new BarcodeResolution(package, row, !row.IsActive);
    }
}
