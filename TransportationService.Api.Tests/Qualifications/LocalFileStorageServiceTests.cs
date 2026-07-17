using System.Text;
using TransportationService.Api.Modules.Qualifications.Services;
using Xunit;

namespace TransportationService.Api.Tests.Qualifications;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tms-tests-" + Guid.NewGuid());

    [Fact]
    public async Task SaveAndOpenRead_RoundTripsContent()
    {
        var sut = new LocalFileStorageService(_root);
        var tenantId = Guid.NewGuid();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var key = await sut.SaveAsync(tenantId, "qualifications", "license.pdf", content, CancellationToken.None);

        await using var readBack = await sut.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(readBack);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAsync_RejectsPathTraversalKeys()
    {
        var sut = new LocalFileStorageService(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.OpenReadAsync("../../etc/passwd", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
