using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class IncidentDossierScopeBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EX-04 data repair: incidents registered with only an order (legacy forms, other producers)
            // get the dossier and customer the server now derives on every create/update — the same
            // order → dossier resolution as ProblemScope (origin order, then link, then activity).
            migrationBuilder.Sql(@"
UPDATE incidents i
SET ""DossierId"" = COALESCE(
    (SELECT d.""Id"" FROM transport_dossiers d
     WHERE d.""OriginTransportOrderId"" = i.""TransportOrderId"" AND d.""TenantId"" = i.""TenantId"" AND NOT d.""IsDeleted""
     ORDER BY d.""CreatedAt"" LIMIT 1),
    (SELECT l.""DossierId"" FROM dossier_orders l
     WHERE l.""TransportOrderId"" = i.""TransportOrderId"" AND l.""TenantId"" = i.""TenantId"" AND NOT l.""IsDeleted""
     ORDER BY l.""CreatedAt"" LIMIT 1),
    (SELECT a.""DossierId"" FROM dossier_activities a
     WHERE a.""LinkedTransportOrderId"" = i.""TransportOrderId"" AND a.""TenantId"" = i.""TenantId"" AND NOT a.""IsDeleted""
     ORDER BY a.""CreatedAt"" LIMIT 1))
WHERE i.""DossierId"" IS NULL AND i.""TransportOrderId"" IS NOT NULL;

UPDATE incidents i
SET ""CustomerId"" = o.""CustomerId""
FROM transport_orders o
WHERE i.""CustomerId"" IS NULL AND o.""Id"" = i.""TransportOrderId"" AND o.""TenantId"" = i.""TenantId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair only: the derived links are correct and stay.
        }
    }
}
