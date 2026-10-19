using Microsoft.Extensions.Configuration;
using TransportationService.Api.Modules.Security;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// CFG-06: a non-Development host refuses to start with missing, non-https, localhost or demo
/// public URLs, or with proxy entries that do not parse (they used to be skipped silently).
/// </summary>
public class DeploymentConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Frontend:BaseUrl"] = "https://tms.transport-janssens.be",
            ["Cors:AllowedOrigins:0"] = "https://tms.transport-janssens.be",
            ["Network:KnownProxies:0"] = "127.0.0.1",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void ValidProductionConfiguration_Passes()
    {
        DeploymentConfigurationValidator.Validate(Config(("Network:KnownNetworks:0", "10.20.0.0/16")));
        Assert.Empty(DeploymentConfigurationValidator.Warnings(Config()));
    }

    [Theory]
    [InlineData("Frontend:BaseUrl", null, "Frontend:BaseUrl is missing")]
    [InlineData("Frontend:BaseUrl", "tms.transport-janssens.be", "not an absolute http(s) URL")]
    [InlineData("Frontend:BaseUrl", "http://tms.transport-janssens.be", "must use https")]
    [InlineData("Frontend:BaseUrl", "https://localhost:5173", "localhost")]
    [InlineData("Frontend:BaseUrl", "https://127.0.0.1", "localhost")]
    [InlineData("Frontend:BaseUrl", "https://tms-demo.vanmalderstudio.be", "demo domain")]
    [InlineData("Frontend:BaseUrl", "https://tms.transport-janssens.be/?x=1", "query string")]
    [InlineData("Cors:AllowedOrigins:0", "https://tms-demo.vanmalderstudio.be", "demo domain")]
    [InlineData("Cors:AllowedOrigins:0", "https://tms.transport-janssens.be/app", "without a path")]
    [InlineData("Cors:AllowedOrigins:0", "http://localhost:5173", "must use https")]
    [InlineData("Network:KnownProxies:0", "127.0.0.l", "not a valid IP address")]
    [InlineData("Network:KnownProxies:1", "nginx", "not a valid IP address")]
    [InlineData("Network:KnownNetworks:0", "10.0.0.0/33", "not a valid CIDR")]
    [InlineData("Network:KnownNetworks:0", "10.0.0.0", "not a valid CIDR")]
    public void InvalidValues_RefuseToStart_NamingTheKey(string key, string? value, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DeploymentConfigurationValidator.Validate(Config((key, value))));

        Assert.Contains(expected, exception.Message);
        Assert.Contains("refusing to start", exception.Message);
    }

    [Fact]
    public void MissingCorsOrigins_RefuseToStart()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Frontend:BaseUrl"] = "https://tms.transport-janssens.be",
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => DeploymentConfigurationValidator.Validate(config));
        Assert.Contains("Cors:AllowedOrigins is empty", exception.Message);
    }

    [Fact]
    public void ShippedProductionSettings_NoLongerCarryTheDemoDomain()
    {
        var path = Path.Combine(FindRepoRoot(), "TransportationService.Api", "appsettings.Production.json");
        var production = File.ReadAllText(path);

        Assert.DoesNotContain("tms-demo.vanmalderstudio.be", production);
        Assert.Contains("\"Microsoft.EntityFrameworkCore.Database.Command\": \"Warning\"", production);
    }

    [Fact]
    public void AllowNonProductionUrls_RelaxesOnlyTheUrlRules_AndWarns()
    {
        var relaxed = Config(
            (DeploymentConfigurationValidator.AllowNonProductionUrlsKey, "true"),
            ("Frontend:BaseUrl", "http://localhost:5185"),
            ("Cors:AllowedOrigins:0", "https://tms-demo.vanmalderstudio.be"));

        DeploymentConfigurationValidator.Validate(relaxed);
        Assert.Contains(DeploymentConfigurationValidator.Warnings(relaxed), w => w.Contains("AllowNonProductionUrls"));

        // Proxy parsing is never relaxed.
        Assert.Throws<InvalidOperationException>(() => DeploymentConfigurationValidator.Validate(Config(
            (DeploymentConfigurationValidator.AllowNonProductionUrlsKey, "true"),
            ("Network:KnownProxies:0", "not-an-ip"))));
    }

    [Fact]
    public void NoKnownProxies_IsAWarning()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Frontend:BaseUrl"] = "https://tms.transport-janssens.be",
            ["Cors:AllowedOrigins:0"] = "https://tms.transport-janssens.be",
        }).Build();
        DeploymentConfigurationValidator.Validate(config);
        Assert.Contains(DeploymentConfigurationValidator.Warnings(config), w => w.Contains("KnownProxies is empty"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TransportationService.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
