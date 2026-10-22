using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// Performance/concurrency final remediation 2026-10-21 (additive, one migration):
    /// <list type="bullet">
    /// <item>PERF-B4: <c>TransportDossier.Version</c> and <c>TransportOrder.Version</c> are EF
    /// concurrency tokens (model annotation only — no SQL; the snapshot carries it);</item>
    /// <item>PERF-B2: <c>Invoice.Status</c> is an EF concurrency token (model annotation only);</item>
    /// <item>PERF-H1: partial unique index <c>IX_issued_transport_documents_Live</c> — one LIVE
    /// document per (tenant, order, kind). Existing data may already hold duplicate live documents
    /// from the race this fixes, so BEFORE the index is created the newest (<c>IssuedAt</c>) of each
    /// duplicate set stays live and the older ones are marked replaced by it, with a reason that
    /// names this migration. The cleanup is PostgreSQL SQL and is skipped on other providers (the
    /// SQLite test databases are created with EnsureCreated, never migrated).</item>
    /// </list>
    /// </summary>
    public partial class PerformanceConcurrencyRemediation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") == true)
            {
                migrationBuilder.Sql("""
                    WITH live AS (
                        SELECT "Id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "TenantId", "TransportOrderId", "Kind"
                                   ORDER BY "IssuedAt" DESC, "CreatedAt" DESC, "Id" DESC) AS rn,
                               FIRST_VALUE("Id") OVER (
                                   PARTITION BY "TenantId", "TransportOrderId", "Kind"
                                   ORDER BY "IssuedAt" DESC, "CreatedAt" DESC, "Id" DESC) AS newest_id
                        FROM issued_transport_documents
                        WHERE "ReplacedByDocumentId" IS NULL AND "IsDeleted" = false
                    )
                    UPDATE issued_transport_documents AS d
                    SET "ReplacedByDocumentId" = live.newest_id,
                        "ReplacedAt" = NOW(),
                        "ReplacementReason" = 'Dubbel actief document opgeruimd (PERF-H1 migratie)',
                        "UpdatedAt" = NOW()
                    FROM live
                    WHERE d."Id" = live."Id" AND live.rn > 1;
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_Live",
                table: "issued_transport_documents",
                columns: new[] { "TenantId", "TransportOrderId", "Kind" },
                unique: true,
                filter: "\"ReplacedByDocumentId\" IS NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_issued_transport_documents_Live",
                table: "issued_transport_documents");
        }
    }
}
