using TransportationService.Api.Common.Storage;
using TransportationService.Api.Modules.Security;

namespace TransportationService.Api.Modules.Qualifications.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly IUploadScanner? _scanner;

    public LocalFileStorageService(string rootPath, IUploadScanner? scanner = null)
    {
        _rootPath = rootPath;
        _scanner = scanner;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Guid tenantId, string category, string fileName, Stream content, CancellationToken cancellationToken)
    {
        // L10: the storage layer is the chokepoint every upload passes through, so the malware
        // scan lives here — an infected file is refused before a single byte hits disk.
        if (_scanner is not null)
        {
            var scan = await _scanner.ScanAsync(fileName, content, cancellationToken);
            if (scan.Verdict == UploadScanVerdict.Infected)
            {
                throw new InfectedUploadException(fileName, scan.Detail);
            }

            if (content.CanSeek)
            {
                content.Position = 0;
            }
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        var storageKey = $"tenant-{tenantId}/{SanitizeSegment(category)}/{Guid.NewGuid()}-{sanitizedFileName}";
        var fullPath = ResolveFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storageKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(storageKey);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(storageKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    // Traversal/escape guard shared with the integrity verifier (Common/Storage/StorageRoot).
    private string ResolveFullPath(string storageKey) => StorageRoot.ResolveKeyPath(_rootPath, storageKey);

    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(segment.Where(c => !invalid.Contains(c)).ToArray());
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
