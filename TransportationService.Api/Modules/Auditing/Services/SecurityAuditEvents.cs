namespace TransportationService.Api.Modules.Auditing.Services;

/// <summary>
/// Canonical action names for security-relevant audit records. Centralised so that SIEM rules and
/// alerting can key off stable identifiers instead of ad-hoc strings scattered through services.
/// Never record password or token material alongside these events.
/// </summary>
public static class SecurityAuditEvents
{
    public const string EntityType = "Security";

    // Authentication
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string AccountLocked = "AccountLocked";
    public const string LoginBlockedWhileLocked = "LoginBlockedWhileLocked";
    public const string LoginAmbiguousTenant = "LoginAmbiguousTenant";
    public const string TokenRefreshed = "TokenRefreshed";
    public const string RefreshRejected = "RefreshRejected";
    public const string RefreshReuseDetected = "RefreshReuseDetected";
    public const string Logout = "Logout";

    // Account & authorization changes (complements the entity-scoped audits in the services)
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordResetByAdmin = "PasswordResetByAdmin";
    public const string UserBlocked = "UserBlocked";
    public const string UserDeactivated = "UserDeactivated";
    public const string SessionsRevoked = "SessionsRevoked";
}
