using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using TransportationService.Api.Common.Storage;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

/// <summary>
/// H4: the file half of a backup set. A backup is <c>&lt;base&gt;.dump</c> (pg_dump -Fc) +
/// <c>&lt;base&gt;.files.tar.gz</c> (the durable storage root) + <c>&lt;base&gt;.sha256</c>
/// (sha256sum-compatible manifest, so <c>sha256sum -c</c> works in the backup directory). The
/// archive holds paths relative to the storage root: restore is
/// <c>tar -C &lt;Storage:RootPath&gt; -xzf &lt;base&gt;.files.tar.gz</c>.
/// Encryption keys are NOT in the storage root and therefore never in the archive — they belong in
/// the vault (docs/operations/backup-restore.md).
/// </summary>
public static class BackupArtifacts
{
    public static string ArchiveFileName(string dumpFileName) =>
        Path.GetFileNameWithoutExtension(dumpFileName) + ".files.tar.gz";

    public static string ManifestFileName(string dumpFileName) =>
        Path.GetFileNameWithoutExtension(dumpFileName) + ".sha256";

    /// <summary>
    /// Streams every regular file under <paramref name="rootPath"/> into a gzip'ed tar, skipping
    /// the excluded directories (backups, message sink), symlinks (never follow out of the root)
    /// and health-probe files. Returns the number of files written. Unreadable files FAIL the
    /// archive — a silently incomplete file backup is worse than a reported failure.
    /// </summary>
    public static async Task<int> CreateArchiveAsync(
        string rootPath, string archivePath, IReadOnlyList<string> excludedDirectories,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException("The storage root does not exist.");
        }

        var count = 0;
        await using var file = new FileStream(
            archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        await using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var path in EnumerateFiles(rootPath, excludedDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
            await tar.WriteEntryAsync(path, entryName, cancellationToken);
            count++;
        }

        return count;
    }

    /// <summary>Reads the whole archive back (every entry and the gzip trailer/CRC) and returns
    /// the number of regular-file entries. Throws on any corruption.</summary>
    public static async Task<int> CountArchiveEntriesAsync(string archivePath, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var reader = new TarReader(gzip, leaveOpen: true);
        var count = 0;
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                if (entry.DataStream is { } data)
                {
                    await data.CopyToAsync(Stream.Null, cancellationToken);
                }

                count++;
            }
        }

        await reader.DisposeAsync();
        // Drain past the tar end-of-archive blocks so the gzip trailer (CRC32 + length) is checked.
        await gzip.CopyToAsync(Stream.Null, cancellationToken);
        return count;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Writes "&lt;sha256&gt;  &lt;file name&gt;" lines (GNU sha256sum format).</summary>
    public static Task WriteManifestAsync(
        string manifestPath, IEnumerable<(string FileName, string Sha256)> entries, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            manifestPath,
            string.Concat(entries.Select(e => $"{e.Sha256}  {e.FileName}\n")),
            cancellationToken);

    public static async Task<IReadOnlyDictionary<string, string>> ReadManifestAsync(
        string manifestPath, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync(manifestPath, cancellationToken))
        {
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator > 0)
            {
                result[line[(separator + 2)..]] = line[..separator];
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateFiles(string directory, IReadOnlyList<string> excludedDirectories)
    {
        // AttributesToSkip = 0: dot-files count as "hidden" on Unix and must still be archived.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        foreach (var file in Directory.EnumerateFiles(directory, "*", options))
        {
            var info = new FileInfo(file);
            if (info.LinkTarget is not null || StorageProbe.IsProbeFile(info.Name))
            {
                continue;
            }

            yield return file;
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", options))
        {
            if (new DirectoryInfo(subdirectory).LinkTarget is not null
                || excludedDirectories.Any(excluded => StorageRoot.IsSameOrInside(subdirectory, excluded)))
            {
                continue;
            }

            foreach (var file in EnumerateFiles(subdirectory, excludedDirectories))
            {
                yield return file;
            }
        }
    }
}
