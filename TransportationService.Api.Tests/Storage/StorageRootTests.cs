using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Common.Storage;

namespace TransportationService.Api.Tests.Storage;

/// <summary>
/// B1 (production readiness 2026-10-18): the durable storage root. Pins the per-environment
/// contract — Development keeps &lt;ContentRoot&gt;/App_Data, everything else needs an absolute,
/// writable Storage:RootPath outside the release directory (or the explicit legacy opt-in) — and
/// the readiness probe that reports an unwritable root without leaking its path.
/// </summary>
public class StorageRootTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"tms-storage-root-{Guid.NewGuid():N}");

    public StorageRootTests()
    {
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(_sandbox, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(_sandbox, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class TestEnvironment(string name, string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private string ContentRoot => Path.Combine(_sandbox, "release", "bin");
    private string AppBase => Path.Combine(_sandbox, "release", "bin");

    // --- resolver -------------------------------------------------------------------------

    [Fact]
    public void Resolve_Development_WithoutConfig_KeepsContentRootAppData()
    {
        var root = StorageRoot.Resolve(Config(), new TestEnvironment("Development", ContentRoot));

        Assert.Equal(StorageRootSource.DevelopmentDefault, root.Source);
        Assert.Equal(Path.Combine(ContentRoot, "App_Data"), root.RootPath);
        Assert.Equal(Path.Combine(ContentRoot, "App_Data", "backups"), root.BackupsDirectory);
        Assert.Equal(Path.Combine(ContentRoot, "App_Data", "message-sink"), root.MessageSinkDirectory);
    }

    [Fact]
    public void Resolve_Production_WithoutConfig_RefusesWithActionableMessage()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StorageRoot.Resolve(Config(), new TestEnvironment("Production", ContentRoot)));

        Assert.Contains("Storage:RootPath", error.Message);
        Assert.Contains("docs/storage.md", error.Message);
        Assert.Contains("AllowContentRootStorage", error.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Resolve_RelativePath_IsRefusedInEveryEnvironment(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StorageRoot.Resolve(Config(("Storage:RootPath", "App_Data")), new TestEnvironment(environment, ContentRoot)));

        Assert.Contains("absolute", error.Message);
    }

    [Fact]
    public void Resolve_Production_ConfiguredAbsolutePath_IsUsed()
    {
        var path = Path.Combine(_sandbox, "storage");

        var root = StorageRoot.Resolve(Config(("Storage:RootPath", path)), new TestEnvironment("Production", ContentRoot));

        Assert.Equal(StorageRootSource.Configured, root.Source);
        Assert.Equal(path, root.RootPath);
    }

    [Fact]
    public void Resolve_Production_LegacyOptIn_KeepsContentRootAppData()
    {
        var root = StorageRoot.Resolve(
            Config(("Storage:AllowContentRootStorage", "true")), new TestEnvironment("Production", ContentRoot));

        Assert.Equal(StorageRootSource.LegacyContentRoot, root.Source);
        Assert.Equal(Path.Combine(ContentRoot, "App_Data"), root.RootPath);
    }

    [Fact]
    public void ResolveKeyPath_RefusesTraversalAndSiblingEscape()
    {
        var root = Path.Combine(_sandbox, "data");

        Assert.Equal(Path.Combine(root, "tenant-1", "x.pdf"), StorageRoot.ResolveKeyPath(root, "tenant-1/x.pdf"));
        Assert.Throws<ArgumentException>(() => StorageRoot.ResolveKeyPath(root, "../data2/x.pdf"));
        Assert.Throws<ArgumentException>(() => StorageRoot.ResolveKeyPath(root, Path.Combine(_sandbox, "database", "x")));
    }

    // --- validator ------------------------------------------------------------------------

    [Fact]
    public void Validate_Production_AcceptsWritablePathOutsideRelease_AndCreatesIt()
    {
        var path = Path.Combine(_sandbox, "var-lib", "storage");
        var root = new StorageRoot(path, StorageRootSource.Configured);

        StorageRootValidator.Validate(root, new TestEnvironment("Production", ContentRoot), Config(), AppBase);

        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(path)); // the probe cleans up after itself
    }

    [Fact]
    public void Validate_Production_RefusesPathInsideContentRoot()
    {
        var root = new StorageRoot(Path.Combine(ContentRoot, "App_Data"), StorageRootSource.Configured);

        var error = Assert.Throws<InvalidOperationException>(() =>
            StorageRootValidator.Validate(root, new TestEnvironment("Production", ContentRoot), Config(), AppBase));

        Assert.Contains("inside the application directory", error.Message);
    }

    [Fact]
    public void Validate_Production_RefusesPathInsideApplicationBaseDirectory()
    {
        var contentRoot = Path.Combine(_sandbox, "workdir");
        var root = new StorageRoot(Path.Combine(AppBase, "data"), StorageRootSource.Configured);

        Assert.Throws<InvalidOperationException>(() =>
            StorageRootValidator.Validate(root, new TestEnvironment("Production", contentRoot), Config(), AppBase));
    }

    [Fact]
    public void Validate_Production_RefusesBackupsDirectoryInsideRelease()
    {
        var root = new StorageRoot(Path.Combine(_sandbox, "storage"), StorageRootSource.Configured);

        var error = Assert.Throws<InvalidOperationException>(() =>
            StorageRootValidator.Validate(root, new TestEnvironment("Production", ContentRoot),
                Config(("Backups:Directory", Path.Combine(ContentRoot, "App_Data", "backups"))), AppBase));

        Assert.Contains("Backups:Directory", error.Message);
    }

    [Fact]
    public void Validate_Production_RefusesUnwritableRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permissions only
        }

        var path = Path.Combine(_sandbox, "readonly");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var root = new StorageRoot(path, StorageRootSource.Configured);

        var error = Assert.Throws<InvalidOperationException>(() =>
            StorageRootValidator.Validate(root, new TestEnvironment("Production", ContentRoot), Config(), AppBase));

        Assert.Contains("not usable", error.Message);
    }

    [Fact]
    public void Validate_Production_LegacyOptIn_AllowsContentRoot()
    {
        var root = new StorageRoot(Path.Combine(ContentRoot, "App_Data"), StorageRootSource.LegacyContentRoot);

        StorageRootValidator.Validate(root, new TestEnvironment("Production", ContentRoot), Config(), AppBase);

        Assert.True(Directory.Exists(root.RootPath));
    }

    [Fact]
    public void Validate_Development_IsLenient()
    {
        var root = StorageRoot.DevelopmentDefault(new TestEnvironment("Development", ContentRoot));

        // Inside the content root by design; nothing is created or probed in Development.
        StorageRootValidator.Validate(root, new TestEnvironment("Development", ContentRoot), Config(), AppBase);
        Assert.False(Directory.Exists(root.RootPath));
    }

    // --- readiness health check -----------------------------------------------------------

    private static HealthCheckContext ContextFor(IHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration("storage", check, HealthStatus.Unhealthy, ["ready"]),
    };

    [Fact]
    public async Task HealthCheck_WritableRoot_IsHealthy()
    {
        var check = new StorageWritableHealthCheck(new StorageRoot(Path.Combine(_sandbox, "ok"), StorageRootSource.Configured));

        var result = await check.CheckHealthAsync(ContextFor(check));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HealthCheck_UnwritableRoot_IsUnhealthy_WithoutLeakingThePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_sandbox, "locked");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var check = new StorageWritableHealthCheck(new StorageRoot(path, StorageRootSource.Configured));

        var result = await check.CheckHealthAsync(ContextFor(check));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain(path, result.Description);
        Assert.DoesNotContain(_sandbox, result.Description);
    }
}
