using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseInventoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IssuedItemId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "issued_item_templates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsumable",
                table: "issued_item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedSerialNumber",
                table: "employee_issued_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // WH-M1: every existing item gets a visible code BM-0001… per tenant in creation order
            // (deleted rows included — their numbers are never handed out again).
            migrationBuilder.Sql("""
                UPDATE issued_item_templates AS t
                SET "Code" = 'BM-' || lpad(x.rn::text, 4, '0')
                FROM (
                    SELECT "Id", row_number() OVER (PARTITION BY "TenantId" ORDER BY "CreatedAt", "Id") AS rn
                    FROM issued_item_templates
                ) AS x
                WHERE t."Id" = x."Id" AND t."Code" IS NULL;
                """);

            // WH-H2: the serial identity = typed serial, upper-cased, all whitespace removed.
            migrationBuilder.Sql("""
                UPDATE employee_issued_items
                SET "NormalizedSerialNumber" = NULLIF(upper(regexp_replace("SerialNumber", '\s', '', 'g')), '')
                WHERE "SerialNumber" IS NOT NULL;
                """);

            // Legacy data from before the rule (the acceptance run issued SER-TEST-001 twice) must not
            // stop the deploy: per item + serial the EARLIEST open issuance keeps the serial identity;
            // the later open duplicates keep their typed serial (still visible, flagged on the loans
            // screen) but give up the normalized identity so the unique index can be built. Returning
            // them is the way to clean up; a new issue of that serial stays blocked while one is open.
            migrationBuilder.Sql("""
                UPDATE employee_issued_items AS i
                SET "NormalizedSerialNumber" = NULL
                FROM (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "TenantId", "TemplateId", "NormalizedSerialNumber"
                        ORDER BY "IssuedDate" NULLS LAST, "CreatedAt", "Id") AS rn
                    FROM employee_issued_items
                    WHERE "NormalizedSerialNumber" IS NOT NULL AND "Status" = 'Issued' AND "IsDeleted" = false
                ) AS d
                WHERE i."Id" = d."Id" AND d.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_IssuedItemId",
                table: "stock_movements",
                columns: new[] { "TenantId", "IssuedItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_templates_TenantId_Code",
                table: "issued_item_templates",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_issued_items_TenantId_NormalizedSerialNumber",
                table: "employee_issued_items",
                columns: new[] { "TenantId", "NormalizedSerialNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_employee_issued_items_open_serial",
                table: "employee_issued_items",
                columns: new[] { "TenantId", "TemplateId", "NormalizedSerialNumber" },
                unique: true,
                filter: "\"NormalizedSerialNumber\" IS NOT NULL AND \"Status\" = 'Issued' AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_movements_TenantId_IssuedItemId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_issued_item_templates_TenantId_Code",
                table: "issued_item_templates");

            migrationBuilder.DropIndex(
                name: "IX_employee_issued_items_TenantId_NormalizedSerialNumber",
                table: "employee_issued_items");

            migrationBuilder.DropIndex(
                name: "UX_employee_issued_items_open_serial",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "IssuedItemId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "IsConsumable",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "NormalizedSerialNumber",
                table: "employee_issued_items");
        }
    }
}
