namespace TransportationService.Api.Modules.Security;

public enum UploadScanVerdict
{
    Clean,
    Infected,
}

public sealed record UploadScanResult(UploadScanVerdict Verdict, string? Detail = null)
{
    public static readonly UploadScanResult Clean = new(UploadScanVerdict.Clean);
}

/// <summary>
/// Malware-scan seam for uploaded content (L10). The storage layer refuses to persist anything an
/// implementation flags as infected. Production plugs in a real engine (ClamAV/ICAP/vendor API —
/// operational checklist #18); until then <see cref="PassThroughUploadScanner"/> documents
/// explicitly that no scanning happens, rather than scanning being silently absent.
/// </summary>
public interface IUploadScanner
{
    Task<UploadScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken);
}

/// <summary>No engine attached: every file passes. Deliberately visible in DI registration.</summary>
public sealed class PassThroughUploadScanner : IUploadScanner
{
    public Task<UploadScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken)
        => Task.FromResult(UploadScanResult.Clean);
}

/// <summary>Thrown when a scanner flags an upload; surfaces as a 400 through the domain-validation flow.</summary>
public sealed class InfectedUploadException : Exception
{
    public InfectedUploadException(string fileName, string? detail)
        : base($"Upload '{fileName}' is geweigerd door de malwarescanner{(detail is null ? "." : $": {detail}")}")
    {
    }
}
