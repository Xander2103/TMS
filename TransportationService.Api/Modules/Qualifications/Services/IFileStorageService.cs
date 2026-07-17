namespace TransportationService.Api.Modules.Qualifications.Services;

public interface IFileStorageService
{
    /// <returns>An opaque storage key. Never a client-supplied path.</returns>
    Task<string> SaveAsync(Guid tenantId, string category, string fileName, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
