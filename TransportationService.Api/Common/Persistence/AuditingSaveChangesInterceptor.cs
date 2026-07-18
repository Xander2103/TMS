using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// Centralises audit stamping and soft-delete handling for every entity that opts in
/// via <see cref="IAuditableEntity"/> / <see cref="ISoftDeletable"/>. Registered on the
/// DbContext options so the DbContext constructor stays dependency-free (keeps the
/// test harness simple). The current user is read from the request-scoped context that
/// <c>TenantContextMiddleware</c> publishes; outside a request (e.g. seeding) it is null.
/// </summary>
public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public AuditingSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var userId = ResolveCurrentUserId();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ISoftDeletable soft && entry.State == EntityState.Deleted)
            {
                // Convert a physical delete into a soft delete.
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAt = now;
                soft.DeletedByUserId = userId;
            }

            if (entry.Entity is IAuditableEntity auditable)
            {
                ApplyAuditStamps(entry, auditable, now, userId);
            }
        }
    }

    private static void ApplyAuditStamps(EntityEntry entry, IAuditableEntity auditable, DateTime now, Guid? userId)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedAt = now;
                auditable.UpdatedAt = now;
                auditable.CreatedByUserId ??= userId;
                auditable.UpdatedByUserId = userId;
                break;
            case EntityState.Modified:
                auditable.UpdatedAt = now;
                auditable.UpdatedByUserId = userId;
                // Never let a modification rewrite the original creation stamps.
                entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                entry.Property(nameof(IAuditableEntity.CreatedByUserId)).IsModified = false;
                break;
        }
    }

    private Guid? ResolveCurrentUserId()
    {
        var items = _httpContextAccessor.HttpContext?.Items;
        if (items is not null && items.TryGetValue(nameof(ICurrentUserContext), out var value)
            && value is ICurrentUserContext currentUser)
        {
            return currentUser.CurrentUserId;
        }

        return null;
    }
}
