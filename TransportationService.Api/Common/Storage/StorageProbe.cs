namespace TransportationService.Api.Common.Storage;

/// <summary>
/// Write/read/delete probe of a directory: the startup validator and the readiness health check
/// use the same test, so "ready" means exactly what startup demanded. The failure text names only
/// the exception type — never the path or OS details — because the health endpoint is anonymous.
/// </summary>
public static class StorageProbe
{
    private const string ProbePrefix = ".tms-storage-probe-";

    /// <summary>True when probe files belong to this class (the backup archive skips them).</summary>
    public static bool IsProbeFile(string fileName) => fileName.StartsWith(ProbePrefix, StringComparison.Ordinal);

    public static async Task<string?> TryProbeAsync(string directory, CancellationToken cancellationToken)
    {
        var probePath = Path.Combine(directory, $"{ProbePrefix}{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var payload = Guid.NewGuid().ToByteArray();
            await File.WriteAllBytesAsync(probePath, payload, cancellationToken);
            var readBack = await File.ReadAllBytesAsync(probePath, cancellationToken);
            if (!readBack.AsSpan().SequenceEqual(payload))
            {
                return "probe file read back different content";
            }

            File.Delete(probePath);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(probePath);
            return $"write/delete probe failed ({ex.GetType().Name})";
        }
    }

    public static string? TryProbe(string directory) =>
        TryProbeAsync(directory, CancellationToken.None).GetAwaiter().GetResult();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort; the probe already reported the failure
        }
    }
}
