using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class SalesCodeFiscalMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostCentre",
                table: "sales_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPricingBasis",
                table: "sales_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultUnitPrice",
                table: "sales_categories",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeInDieselBase",
                table: "sales_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDescriptionDe",
                table: "sales_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDescriptionEn",
                table: "sales_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDescriptionFr",
                table: "sales_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "sales_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VatTreatmentOverride",
                table: "sales_categories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCentreSnapshot",
                table: "invoice_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionLanguageSnapshot",
                table: "invoice_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesCodeSnapshot",
                table: "invoice_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatLegalTextSnapshot",
                table: "invoice_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentOverride",
                table: "invoice_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentOverrideReason",
                table: "invoice_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentSnapshot",
                table: "invoice_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentSourceSnapshot",
                table: "invoice_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_category_ledger_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCentre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_category_ledger_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_category_ledger_mappings_sales_categories_SalesCatego~",
                        column: x => x.SalesCategoryId,
                        principalTable: "sales_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_category_ledger_mappings_SalesCategoryId",
                table: "sales_category_ledger_mappings",
                column: "SalesCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_category_ledger_mappings_TenantId_SalesCategoryId_Leg~",
                table: "sales_category_ledger_mappings",
                columns: new[] { "TenantId", "SalesCategoryId", "LegalEntityId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_category_ledger_mappings");

            migrationBuilder.DropColumn(
                name: "CostCentre",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "DefaultPricingBasis",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "DefaultUnitPrice",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "IncludeInDieselBase",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "InvoiceDescriptionDe",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "InvoiceDescriptionEn",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "InvoiceDescriptionFr",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "VatTreatmentOverride",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "CostCentreSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "DescriptionLanguageSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "SalesCodeSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "VatLegalTextSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "VatTreatmentOverride",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "VatTreatmentOverrideReason",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "VatTreatmentSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "VatTreatmentSourceSnapshot",
                table: "invoice_lines");
        }
    }
}
