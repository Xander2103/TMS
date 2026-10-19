namespace TransportationService.Api.Common.Storage;

/// <summary>Where the resolved storage root came from (drives validation strictness).</summary>
public enum StorageRootSource
{
    /// <summary><c>Storage:RootPath</c> is set (the only production-grade source).</summary>
    Configured,

    /// <summary>Development without configuration: today's <c>&lt;ContentRoot&gt;/App_Data</c>.</summary>
    DevelopmentDefault,

    /// <summary>Outside Development via the explicit <c>Storage:AllowContentRootStorage</c> opt-in.</summary>
    LegacyContentRoot,
}

/// <summary>
/// The resolved durable storage root — registered as a singleton and the ONLY way any component
/// finds an on-disk location (file storage, backups default directory, Development message sink).
/// Nothing may combine <c>ContentRootPath</c>/<c>AppContext.BaseDirectory</c> with "App_Data"
/// on its own again: that is how the deploy-wipe (B1) and the sink-retention mismatch (BG-09) arose.
/// </summary>
public sealed class StorageRoot
{
    public const string LegacyFolderName = "App_Data";
    public const string BackupsFolderName = "backups";
    public const string MessageSinkFolderName = "message-sink";

    public StorageRoot(string rootPath, StorageRootSource source)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        Source = source;
    }

    public string RootPath { get; }
    public StorageRootSource Source { get; }

    /// <summary>Default in-app backup directory (unless <c>Backups:Directory</c> overrides it).</summary>
    public string BackupsDirectory => Path.Combine(RootPath, BackupsFolderName);

    /// <summary>Development-only message sink (raw tokens; never archived in backups).</summary>
    public string MessageSinkDirectory => Path.Combine(RootPath, MessageSinkFolderName);

    /// <summary>Today's Development location, also the fallback for code constructed without DI.</summary>
    public static StorageRoot DevelopmentDefault(IHostEnvironment environment) =>
        new(Path.Combine(environment.ContentRootPath, LegacyFolderName), StorageRootSource.DevelopmentDefault);

    /// <summary>
    /// Resolves the root from configuration. Fails fast (InvalidOperationException with an
    /// actionable message) on a relative path, or when nothing is configured outside Development
    /// without the explicit legacy opt-in. Deeper checks (writable, outside the release dir) live
    /// in <see cref="StorageRootValidator"/>.
    /// </summary>
    public static StorageRoot Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var configured = options.RootPath?.Trim();

        if (!string.IsNullOrEmpty(configured))
        {
            // A relative path resolves against the process working directory — exactly the
            // accident B1 is about — so it is refused in every environment.
            if (!Path.IsPathFullyQualified(configured))
            {
                throw new InvalidOperationException(
                    "Storage:RootPath must be an absolute path (got a relative path). Set it to a dedicated "
                    + "persistent directory outside the release directory, e.g. /var/lib/transportationservice/storage. "
                    + "See docs/storage.md (Bestandsopslag).");
            }

            return new StorageRoot(configured, StorageRootSource.Configured);
        }

        if (environment.IsDevelopment())
        {
            return DevelopmentDefault(environment);
        }

        if (options.AllowContentRootStorage)
        {
            return new StorageRoot(
                Path.Combine(environment.ContentRootPath, LegacyFolderName), StorageRootSource.LegacyContentRoot);
        }

        throw new InvalidOperationException(
            "Storage:RootPath is not configured. Outside Development the durable file storage (documents, POD "
            + "signatures, Peppol payloads, backups) must live in an absolute directory OUTSIDE the release "
            + "directory, which the deploy script overwrites with rsync --delete. Set Storage__RootPath (e.g. "
            + "/var/lib/transportationservice/storage) after moving the existing App_Data per docs/storage.md "
            + "(Bestandsopslag → Migratie), or set Storage__AllowContentRootStorage=true to keep the legacy "
            + "<ContentRoot>/App_Data location temporarily. Refusing to start.");
    }

    /// <summary>
    /// Maps a storage key ("tenant-…/category/file") to its absolute path, refusing traversal and
    /// any key that escapes the root. Shared by <c>LocalFileStorageService</c> and the integrity
    /// verifier so both agree on what a key means.
    /// </summary>
    public static string ResolveKeyPath(string rootPath, string storageKey)
    {
        var normalized = storageKey.Replace('\\', '/');
        if (normalized.Contains("..")) throw new ArgumentException("Invalid storage key.", nameof(storageKey));

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
        // Compare with a trailing separator (L4): a bare StartsWith would accept a SIBLING
        // directory whose name merely starts with the root ("C:\data" matching "C:\database").
        if (!IsSameOrInside(fullPath, rootPath) || PathsEqual(fullPath, rootPath))
        {
            throw new ArgumentException("Storage key escapes the storage root.", nameof(storageKey));
        }

        return fullPath;
    }

    /// <summary>True when <paramref name="path"/> equals <paramref name="directory"/> or lies below it.</summary>
    public static bool IsSameOrInside(string path, string directory)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return PathsEqual(fullPath, fullDirectory)
               || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);

    // Linux file systems are case-sensitive; macOS/Windows defaults are not.
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
