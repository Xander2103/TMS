namespace TransportationService.Api.Modules.Gdpr;

/// <summary>
/// Configurable retention policy (H13), bound to the "Retention" configuration section. Windows
/// are deliberately conservative defaults — shorten or lengthen per legal advice, never in code.
/// <see cref="LegalHold"/> freezes every automated purge (litigation hold): nothing is deleted
/// while it is on. Audit logs are NOT purged by the application at all — the append-only trigger
/// blocks it by construction; audit retention is a deliberate maintenance-role operation
/// (operational checklist #29).
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>True = every automated purge is suspended (litigation/legal hold).</summary>
    public bool LegalHold { get; set; }

    /// <summary>Delivered/failed/suppressed outbox mails older than this are deleted.</summary>
    public int OutboxRetentionDays { get; set; } = 365;

    /// <summary>Used/revoked/expired activation- and reset-token rows older than this are deleted.</summary>
    public int SecurityTokenRetentionDays { get; set; } = 30;

    /// <summary>Development message-sink files older than this are deleted on sweep.</summary>
    public int SinkFileRetentionDays { get; set; } = 14;
}
