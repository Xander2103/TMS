namespace TransportationService.Api.Modules.Qualifications.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(string rootPath)
    {
        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Guid tenantId, string category, string fileName, Stream content, CancellationToken cancellationToken)
    {
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
        var rootFullPath = Path.GetFullPath(_rootPath);
        if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
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
