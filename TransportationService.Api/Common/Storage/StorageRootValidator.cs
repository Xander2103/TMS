namespace TransportationService.Api.Common.Storage;

/// <summary>
/// B1 fail-fast, in the style of <c>StartupSecurityValidator</c>: run once after the host is built
/// and before it serves. Outside Development the storage root must be absolute (already enforced by
/// <see cref="StorageRoot.Resolve"/>), not a filesystem root, exist or be creatable, pass a write/
/// delete probe, and lie OUTSIDE the application's content root and binary directory — the release
/// directory the deploy script rsyncs with --delete. The legacy opt-in skips only that last check.
/// Development keeps today's lenient behaviour (the directory is created lazily).
/// </summary>
public static class StorageRootValidator
{
    public static void Validate(
        StorageRoot root, IHostEnvironment environment, IConfiguration configuration, ILogger? logger = null) =>
        Validate(root, environment, configuration, AppContext.BaseDirectory, logger);

    /// <summary>Test seam: <paramref name="applicationBaseDirectory"/> stands in for AppContext.BaseDirectory.</summary>
    public static void Validate(
        StorageRoot root, IHostEnvironment environment, IConfiguration configuration,
        string applicationBaseDirectory, ILogger? logger = null)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (string.Equals(Path.GetPathRoot(root.RootPath), root.RootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Storage:RootPath points at a filesystem root. Use a dedicated directory such as "
                + "/var/lib/transportationservice/storage. Refusing to start.");
        }

        if (root.Source == StorageRootSource.LegacyContentRoot)
        {
            logger?.LogWarning(
                "Storage:AllowContentRootStorage is enabled: files are stored in {StorageRoot}, inside the release "
                + "directory. A deploy (rsync --delete) can erase them. Migrate per docs/storage.md and set "
                + "Storage:RootPath.", root.RootPath);
        }
        else
        {
            RefuseInsideApplication(root.RootPath, "Storage:RootPath", environment, applicationBaseDirectory);
        }

        // An explicit Backups:Directory obeys the same placement rules: in-app dumps inside the
        // release directory are wiped by the same rsync.
        var backupsDirectory = configuration["Backups:Directory"]?.Trim();
        if (!string.IsNullOrEmpty(backupsDirectory))
        {
            if (!Path.IsPathFullyQualified(backupsDirectory))
            {
                throw new InvalidOperationException(
                    "Backups:Directory must be an absolute path (got a relative path). Leave it unset to use "
                    + "<Storage:RootPath>/backups. Refusing to start.");
            }

            if (root.Source != StorageRootSource.LegacyContentRoot)
            {
                RefuseInsideApplication(backupsDirectory, "Backups:Directory", environment, applicationBaseDirectory);
            }
        }

        if (StorageProbe.TryProbe(root.RootPath) is { } failure)
        {
            throw new InvalidOperationException(
                $"The storage root {root.RootPath} is not usable: {failure}. Create the directory and give the "
                + "service account read/write access (e.g. chown -R <service-user> and chmod 750). Refusing to start.");
        }

        logger?.LogInformation("Durable storage root: {StorageRoot} ({Source}).", root.RootPath, root.Source);
    }

    private static void RefuseInsideApplication(
        string path, string key, IHostEnvironment environment, string applicationBaseDirectory)
    {
        foreach (var appDirectory in new[] { environment.ContentRootPath, applicationBaseDirectory })
        {
            if (!string.IsNullOrWhiteSpace(appDirectory) && StorageRoot.IsSameOrInside(path, appDirectory))
            {
                throw new InvalidOperationException(
                    $"{key} ({path}) lies inside the application directory ({appDirectory}). The deploy script "
                    + "replaces that directory with rsync --delete, which would erase the stored files. Use a "
                    + "directory outside the release, e.g. /var/lib/transportationservice/storage. Refusing to start.");
            }
        }
    }
}
