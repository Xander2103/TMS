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

    private string ResolveFullPath(string storageKey)
    {
        var normalized = storageKey.Replace('\\', '/');
        if (normalized.Contains("..")) throw new ArgumentException("Invalid storage key.", nameof(storageKey));

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        // Compare with a trailing separator (L4): a bare StartsWith would accept a SIBLING
        // directory whose name merely starts with the root ("C:\data" matching "C:\database").
        var rootWithSeparator = Path.GetFullPath(_rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Storage key escapes the storage root.", nameof(storageKey));
        }

        return fullPath;
    }

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
