using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Dtos;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Auditing.Services;

public interface IAuditHistoryReader
{
    /// <summary>One page of the given audit rows, newest first, with actor names and id references
    /// resolved. <paramref name="redactedKeys"/> are removed from the before/after values (a domain
    /// history for operational roles must not leak fields their role cannot see elsewhere).</summary>
    Task<AuditLogPagedResult> ReadAsync(
        IQueryable<AuditLog> rows, int page, int pageSize, IReadOnlyCollection<string>? redactedKeys, CancellationToken cancellationToken);
}

/// <summary>
/// The audit-log page builder shared by the raw audit log (audit_logs.view) and the domain
/// histories (incident / afwijking: when, who, what) that operational roles may read.
/// </summary>
public class AuditHistoryReader : IAuditHistoryReader
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditReferenceResolver _references;

    public AuditHistoryReader(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditReferenceResolver references)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _references = references;
    }

    public async Task<AuditLogPagedResult> ReadAsync(
        IQueryable<AuditLog> rows, int page, int pageSize, IReadOnlyCollection<string>? redactedKeys, CancellationToken cancellationToken)
    {
        // PERF-H5: the raw audit log of a tenant runs into the hundreds of thousands of rows;
        // `pageSize=1000000` used to stream the whole table (97 MB). Every page request goes
        // through the shared normaliser: 1..200 rows, default 25, page >= 1. The returned
        // `pageSize` tells the caller what was really applied.
        var pageRequest = PageRequest.Of(page, pageSize);
        page = pageRequest.Page;
        pageSize = pageRequest.PageSize;

        var query = rows.AsNoTracking().Where(a => a.TenantId == _tenantContext.TenantId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            // Stable order: rows written in the same tick (one SaveChanges) keep a fixed
            // position across pages, so paging never skips or repeats an entry.
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip(pageRequest.Skip)
            .Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.UserId, a.EntityType, a.EntityId, a.Action, a.OldValuesJson, a.NewValuesJson, a.Timestamp))
            .ToListAsync(cancellationToken);

        if (redactedKeys is { Count: > 0 })
        {
            items = items.Select(i => i with
            {
                OldValuesJson = Redact(i.OldValuesJson, redactedKeys),
                NewValuesJson = Redact(i.NewValuesJson, redactedKeys),
            }).ToList();
        }

        // Actor names (product acceptance 2026-10-07): the history must say WHO confirmed, reopened or
        // cancelled. Resolved within the tenant only; a foreign or deleted user stays nameless.
        var userIds = items.Where(i => i.UserId is not null).Select(i => i.UserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var tenantId = _tenantContext.TenantId;
            var names = await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FirstName + u.LastName) ? u.Email : (u.FirstName + " " + u.LastName).Trim(), cancellationToken);
            items = items.Select(i => i.UserId is { } id && names.TryGetValue(id, out var name) ? i with { UserName = name } : i).ToList();
        }

        // B25: the ids inside the values become references a planner recognises (per entry, only its own ids).
        var resolved = await _references.ResolveAsync(items.SelectMany(i => new[] { i.OldValuesJson, i.NewValuesJson }), cancellationToken);
        if (resolved.Count > 0)
        {
            items = items.Select(i =>
            {
                var own = AuditReferenceResolver.CollectIds([i.OldValuesJson, i.NewValuesJson])
                    .Where(resolved.ContainsKey)
                    .ToDictionary(id => id.ToString(), id => resolved[id]);
                return own.Count > 0 ? i with { References = own } : i;
            }).ToList();
        }

        return new AuditLogPagedResult(items, totalCount, page, pageSize);
    }

    private static string? Redact(string? json, IReadOnlyCollection<string> keys)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject values)
            {
                return json;
            }

            foreach (var key in values.Select(p => p.Key).Where(k => keys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                values.Remove(key);
            }

            return values.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
