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

    // Reads of special-category or bulk data. Reads are normally not audited — these are the
    // exceptions where knowing *who looked* matters as much as knowing who changed something
    // (GDPR art. 9 data, personnel dossiers, and exports that leave the application entirely).
    public const string SensitiveDocumentDownloaded = "SensitiveDocumentDownloaded";
    public const string HealthDataViewed = "HealthDataViewed";
    public const string DataExported = "DataExported";

    /// <summary>
    /// One-liner for bulk-export read-audits (M6/M14): every file that leaves the application
    /// (XLSX and friends) records who exported what with which filter. Never include row data —
    /// the export itself is the data; the audit only needs the fact and the scope.
    /// </summary>
    public static Task RecordExportAsync(
        this IAuditService auditService, string export, object? filter, CancellationToken cancellationToken,
        string classification = Classification.Personal) =>
        auditService.RecordAsync(EntityType, export, DataExported, null,
            new { Export = export, Filter = filter, Classification = classification }, cancellationToken);

    /// <summary>
    /// Data classification stamped on read-audit records so a reviewer can filter the trail down to
    /// special-category access without re-deriving it from the entity type.
    /// </summary>
    public static class Classification
    {
        /// <summary>GDPR art. 9 special category (health, sick leave, medical certificates).</summary>
        public const string Health = "Health";

        /// <summary>Identifying/financial personal data (national register number, IBAN, ID card).</summary>
        public const string Confidential = "Confidential";

        /// <summary>Ordinary personal or business data leaving the application in bulk.</summary>
        public const string Personal = "Personal";
    }
}
