using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Diagnostics;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Common;

/// <summary>
/// PERF-H2 (performance/concurrency final remediation 2026-10-21): an expected race on a KNOWN
/// unique constraint answers 409 with a domain sentence; everything else stays a 500 so a genuine
/// defect is never hidden behind a friendly message. The constraint name comes from PostgreSQL's
/// error itself and, on the SQLite test harness, from the EF model behind the failed entries.
/// </summary>
public class KnownUniqueConflictExceptionFilterTests
{
    private static ExceptionContext BuildContext(Exception exception)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = exception };
    }

    /// <summary>What Npgsql raises for a 23505: the constraint travels on the exception.</summary>
    private static DbUpdateException Postgres(string sqlState, string? constraint) =>
        new("An error occurred while saving the entity changes.",
            new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", sqlState,
                tableName: "user_roles", constraintName: constraint));

    private static string Body(ExceptionContext context) =>
        JsonSerializer.Serialize(Assert.IsType<ConflictObjectResult>(context.Result).Value);

    [Fact]
    public void KnownPostgresConstraint_Answers409_WithTheDomainSentence()
    {
        var context = BuildContext(Postgres("23505", "PK_user_roles"));

        new KnownUniqueConflictExceptionFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var body = Body(context);
        Assert.Contains("De rollen van deze gebruiker zijn intussen door iemand anders gewijzigd. Herlaad en probeer opnieuw.", body);
        Assert.Contains(ErrorCodes.ConcurrencyConflict, body);
        // Final integration 2026-10-24: a 409 carries the same correlation id as every other error.
        Assert.Contains($"\"correlationId\":\"{context.HttpContext.TraceIdentifier}\"", body);
    }

    /// <summary>
    /// Final integration 2026-10-24: the 409 filter (performance) and the 503 handler (production
    /// hardening) never claim each other's failures, and neither claims a genuine defect (500).
    /// </summary>
    [Theory]
    [InlineData("23505", "PK_user_roles", 409)]
    [InlineData("23505", "IX_issued_transport_documents_Live", 409)]
    [InlineData("23505", "IX_customers_TenantId_NormalizedCustomerNumber", 500)]
    [InlineData("23503", "FK_user_roles_users_UserId", 500)]
    [InlineData("42P01", null, 500)]
    [InlineData("40P01", null, 500)]
    [InlineData("57014", null, 500)]
    [InlineData("08006", null, 503)]
    [InlineData("57P01", null, 503)]
    [InlineData("53300", null, 503)]
    public void PostgresFailures_MapToExactlyOneStatus(string sqlState, string? constraint, int expected)
    {
        var failure = Postgres(sqlState, constraint);

        var status = KnownUniqueConflictExceptionFilter.TryMap(failure, out _) ? 409
            : DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(failure) ? 503
            : 500;

        Assert.Equal(expected, status);
    }

    [Fact]
    public void UnknownConstraint_IsLeftUnhandled_SoItStaysA500()
    {
        var context = BuildContext(Postgres("23505", "IX_customers_TenantId_NormalizedCustomerNumber"));

        new KnownUniqueConflictExceptionFilter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
        Assert.False(KnownUniqueConflictExceptionFilter.TryMap(context.Exception, out _));
    }

    [Fact]
    public void OtherDatabaseErrors_AreNoUniqueViolation_AndStayUnhandled()
    {
        var foreignKey = Postgres("23503", "FK_user_roles_users_UserId");
        Assert.Null(UniqueViolations.ConstraintName(foreignKey));
        Assert.False(KnownUniqueConflictExceptionFilter.TryMap(foreignKey, out _));

        // A stale token belongs to ConcurrencyConflictExceptionFilter, not to this one.
        Assert.False(KnownUniqueConflictExceptionFilter.TryMap(new DbUpdateConcurrencyException("stale"), out _));
        Assert.False(KnownUniqueConflictExceptionFilter.TryMap(new InvalidOperationException("boom"), out _));

        var context = BuildContext(foreignKey);
        new KnownUniqueConflictExceptionFilter().OnException(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void ConstraintName_ReadsThePostgresConstraint_WithoutAProviderReference()
    {
        Assert.Equal("IX_invoices_TenantId_InvoiceNumber",
            UniqueViolations.ConstraintName(Postgres("23505", "IX_invoices_TenantId_InvoiceNumber")));
        Assert.True(UniqueViolations.IsUniqueViolation(Postgres("23505", null)));
        Assert.Null(UniqueViolations.ConstraintName(Postgres("23505", null)));
    }

    [Fact]
    public void TheRegistry_CoversTheAuditedRaces_WithDutchSentences()
    {
        var registry = KnownUniqueConflictExceptionFilter.KnownConstraints;
        foreach (var required in new[]
                 {
                     "IX_trip_planning_entries_TenantId_TripId", "PK_user_roles",
                     "IX_issued_transport_documents_TenantId_DocumentNumber", "IX_issued_transport_documents_Live",
                     "IX_invoices_TenantId_InvoiceNumber",
                 })
        {
            Assert.True(registry.ContainsKey(required), required);
        }

        Assert.All(registry.Values, sentence => Assert.EndsWith(".", sentence));
        // Number claims of orders, dossiers and trips are serialized by TenantNumbering: deliberately not mapped.
        Assert.DoesNotContain("IX_transport_orders_TenantId_OrderNumber", registry.Keys);
        Assert.DoesNotContain("UX_transport_dossiers_origin_order", registry.Keys);
    }

    /// <summary>SQLite says only "UNIQUE constraint failed: user_roles.UserId, user_roles.RoleId" — resolved to the PK.</summary>
    [Fact]
    public async Task Sqlite_PrimaryKeyViolation_ResolvesToThePrimaryKeyName_AndMapsTo409()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planner", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "p@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await db.Context.SaveChangesAsync();

        await using var other = db.CreateContextForTenant(tenantId);
        other.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        Assert.Equal("PK_user_roles", UniqueViolations.ConstraintName(exception));
        Assert.True(KnownUniqueConflictExceptionFilter.TryMap(exception, out var message));
        Assert.Contains("rollen van deze gebruiker", message);
        var context = BuildContext(exception);
        new KnownUniqueConflictExceptionFilter().OnException(context);
        Assert.IsType<ConflictObjectResult>(context.Result);
    }

    [Fact]
    public async Task Sqlite_UniqueIndexViolation_ResolvesToTheIndexName()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        TripPlanningEntry Entry() => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, EmployeeId = Guid.NewGuid(), DriverId = Guid.NewGuid(),
            TripNumber = "RIT-0001", Date = new DateOnly(2026, 10, 21),
        };
        db.Context.TripPlanningEntries.Add(Entry());
        await db.Context.SaveChangesAsync();

        await using var other = db.CreateContextForTenant(tenantId);
        other.TripPlanningEntries.Add(Entry());
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        Assert.Equal("IX_trip_planning_entries_TenantId_TripId", UniqueViolations.ConstraintName(exception));
        Assert.True(KnownUniqueConflictExceptionFilter.TryMap(exception, out var message));
        Assert.StartsWith("De rit is intussen door een andere gebruiker gewijzigd", message);
    }
}
