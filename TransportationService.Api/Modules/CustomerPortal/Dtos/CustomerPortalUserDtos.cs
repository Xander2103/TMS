namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

/// <summary>
/// The three OPTIONAL portal capabilities layered on top of the mandatory base "klantportaal"
/// role (which every portal user always has — Opdrachten + Berichten). Each flag maps 1:1 to
/// one of the klantportaal_* add-on role templates; there is no other mechanism.
/// </summary>
public record PortalUserGrantsDto(bool Documents, bool Invoices, bool ManageUsers);

public record PortalUserListItemDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    bool IsBlocked,
    /// <summary>True while an activation/reset token is outstanding and unused.</summary>
    bool HasPendingActivation,
    PortalUserGrantsDto Grants);

public record PortalInviteUserRequest(string FirstName, string LastName, string Email, PortalUserGrantsDto Grants);

/// <summary>
/// The raw activation token is returned exactly once — same contract as the internal admin
/// "start activation" flow — but ONLY while the resolved
/// <see cref="TransportationService.Api.Modules.Messaging.Services.IEmailProvider"/> is the
/// development sink (see <c>CustomerPortalUserService.IsRawTokenSafeToReturn</c>, an explicit
/// runtime type check, not just a doc comment). The moment a real SMTP/SendGrid provider is
/// registered, this field is <see langword="null"/> on every response — enforced, not aspirational.
/// </summary>
public record PortalUserInviteResultDto(PortalUserListItemDto User, string? ActivationToken, DateTime ActivationTokenExpiresAtUtc);

public record PortalUserGrantsRequest(PortalUserGrantsDto Grants);

public enum PortalUserOperationOutcome
{
    Success,
    NotFound,
    ValidationFailed,
}

public record PortalUserOperationResult<T>(PortalUserOperationOutcome Outcome, T? Value, string? Error = null) where T : class
{
    public static PortalUserOperationResult<T> Success(T value) => new(PortalUserOperationOutcome.Success, value);
    public static PortalUserOperationResult<T> NotFound() => new(PortalUserOperationOutcome.NotFound, null);
    public static PortalUserOperationResult<T> Invalid(string error) => new(PortalUserOperationOutcome.ValidationFailed, null, error);
}
