using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CommercialWaveAuditFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Audit fix — fail loudly, never truncate: the new bounds must hold for existing rows
            // (CostCentre is copied into invoice_lines.CostCentreSnapshot varchar(40) at Send).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM sales_categories
                               WHERE length("CostCentre") > 40 OR length("DefaultPricingBasis") > 40
                                  OR length("InvoiceDescriptionFr") > 300 OR length("InvoiceDescriptionEn") > 300
                                  OR length("InvoiceDescriptionDe") > 300 OR length("Notes") > 1000) THEN
                        RAISE EXCEPTION 'CommercialWaveAuditFixes: een verkoopcategorie heeft een kostenplaats/omschrijving/notitie die langer is dan de nieuwe limiet (40/300/1000). Kort die eerst in.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_customer_communication_rule_contacts_TenantId_RuleId_Contac~",
                table: "customer_communication_rule_contacts");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "sales_categories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceDescriptionFr",
                table: "sales_categories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceDescriptionEn",
                table: "sales_categories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceDescriptionDe",
                table: "sales_categories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultPricingBasis",
                table: "sales_categories",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CostCentre",
                table: "sales_categories",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "pricing_import_runs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "pricing_import_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Succeeded");

            migrationBuilder.CreateIndex(
                name: "IX_sales_category_ledger_mappings_LedgerAccountId",
                table: "sales_category_ledger_mappings",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_category_ledger_mappings_LegalEntityId",
                table: "sales_category_ledger_mappings",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rule_contacts_TenantId_RuleId_Contac~",
                table: "customer_communication_rule_contacts",
                columns: new[] { "TenantId", "RuleId", "ContactId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_category_ledger_mappings_ledger_accounts_LedgerAccoun~",
                table: "sales_category_ledger_mappings",
                column: "LedgerAccountId",
                principalTable: "ledger_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_category_ledger_mappings_legal_entities_LegalEntityId",
                table: "sales_category_ledger_mappings",
                column: "LegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Audit fix (rule F): the diesel-surcharge base is now decided per sales code. Before
            // this, the surcharge ran over the whole order amount (transport + service lines), so
            // the transport and supplement roles are flagged to preserve today's effective base;
            // operators untick what should not count. Idempotent: only flips rows still at false.
            migrationBuilder.Sql("""
                UPDATE sales_categories
                SET "IncludeInDieselBase" = TRUE
                WHERE "IncludeInDieselBase" = FALSE
                  AND "IsDeleted" = FALSE
                  AND "SystemRole" IN ('Transport', 'Surcharge');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_category_ledger_mappings_ledger_accounts_LedgerAccoun~",
                table: "sales_category_ledger_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_category_ledger_mappings_legal_entities_LegalEntityId",
                table: "sales_category_ledger_mappings");

            migrationBuilder.DropIndex(
                name: "IX_sales_category_ledger_mappings_LedgerAccountId",
                table: "sales_category_ledger_mappings");

            migrationBuilder.DropIndex(
                name: "IX_sales_category_ledger_mappings_LegalEntityId",
                table: "sales_category_ledger_mappings");

            migrationBuilder.DropIndex(
                name: "IX_customer_communication_rule_contacts_TenantId_RuleId_Contac~",
                table: "customer_communication_rule_contacts");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "pricing_import_runs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "pricing_import_runs");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "sales_categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceDescriptionFr",
                table: "sales_categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceDescriptionEn",
                table: "sales_categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceDescriptionDe",
                table: "sales_categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultPricingBasis",
                table: "sales_categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CostCentre",
                table: "sales_categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rule_contacts_TenantId_RuleId_Contac~",
                table: "customer_communication_rule_contacts",
                columns: new[] { "TenantId", "RuleId", "ContactId" },
                unique: true);
        }
    }
}
