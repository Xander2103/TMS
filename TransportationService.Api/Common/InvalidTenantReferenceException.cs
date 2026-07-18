namespace TransportationService.Api.Common;

/// <summary>
/// Thrown when a request references a record (by id) that does not exist within the current
/// tenant — either a guessed/foreign GUID or a stale id. Controllers translate this into a
/// 400 so cross-tenant ids are indistinguishable from non-existent ones.
/// </summary>
public class InvalidTenantReferenceException : Exception
{
    public InvalidTenantReferenceException(string referenceName)
        : base($"De gekoppelde referentie '{referenceName}' bestaat niet.")
    {
        ReferenceName = referenceName;
    }

    public string ReferenceName { get; }
}
