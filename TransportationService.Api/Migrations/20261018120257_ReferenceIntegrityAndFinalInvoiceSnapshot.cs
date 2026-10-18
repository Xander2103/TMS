using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceIntegrityAndFinalInvoiceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customers_TenantId_CustomerNumber",
                table: "customers");

            migrationBuilder.AddColumn<bool>(
                name: "PartySnapshotBackfilled",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartySnapshotFrozenAt",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerLogoStorageKey",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedCustomerNumber",
                table: "customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            // MD-M1/MD-B2: deterministic backfill (trim + upper), then refuse to continue when two
            // customers — deleted ones included — would share a number. Nothing is merged or renamed
            // silently: the operator resolves the listed numbers and re-runs the migration.
            migrationBuilder.Sql("""UPDATE customers SET "NormalizedCustomerNumber" = upper(btrim("CustomerNumber"));""");
            migrationBuilder.Sql("""
                DO $$
                DECLARE conflicts text;
                BEGIN
                    SELECT string_agg(format('tenant %s: %s (%s rijen)', "TenantId", "NormalizedCustomerNumber", n), '; ')
                      INTO conflicts
                      FROM (SELECT "TenantId", "NormalizedCustomerNumber", count(*) AS n
                              FROM customers GROUP BY 1, 2 HAVING count(*) > 1) d;
                    IF conflicts IS NOT NULL THEN
                        RAISE EXCEPTION 'Klantnummers botsen hoofdletterongevoelig (ook verwijderde klanten tellen mee): %. Hernoem de dubbele nummers en voer de migratie opnieuw uit.', conflicts;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_customers_TenantId_CustomerNumber",
                table: "customers",
                columns: new[] { "TenantId", "CustomerNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_TenantId_NormalizedCustomerNumber",
                table: "customers",
                columns: new[] { "TenantId", "NormalizedCustomerNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customers_TenantId_CustomerNumber",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "IX_customers_TenantId_NormalizedCustomerNumber",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PartySnapshotBackfilled",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PartySnapshotFrozenAt",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerLogoStorageKey",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "NormalizedCustomerNumber",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "IX_customers_TenantId_CustomerNumber",
                table: "customers",
                columns: new[] { "TenantId", "CustomerNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
