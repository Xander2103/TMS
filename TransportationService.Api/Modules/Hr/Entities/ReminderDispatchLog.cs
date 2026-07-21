using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>
/// Authoritative de-duplication record for HR / expiry reminders. A reminder is only
/// produced when no log row with its DedupeKey exists for the tenant, so repeated sweeps
/// never re-send. Keys are stable per (kind, entity, occurrence), e.g.
/// "birthday:{employeeId}:{year}", "seniority-mail:{employeeId}:{milestone}".
/// </summary>
public class ReminderDispatchLog : AuditableTenantEntity
{
    public string DedupeKey { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
