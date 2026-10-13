namespace TransportationService.Api.Common.Abstractions;

/// <summary>
/// Optimistic-concurrency token maintained centrally by the auditing interceptor: every
/// modification gets a fresh Guid, so services never bump by hand and no mutation path can
/// forget. Clients echo the token; a mismatch yields HTTP 409 carrying the current state
/// (Trip pattern — Trip itself predates this interface and keeps its manual bumps).
/// </summary>
public interface IVersionedEntity
{
    Guid Version { get; set; }
}
