using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CommercialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceReadiness",
                table: "transport_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceReadinessReasons",
                table: "transport_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCategoryId",
                table: "service_options",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultUnitCode",
                table: "sales_categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDescriptionNl",
                table: "sales_categories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatCategoryOverride",
                table: "sales_categories",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCategoryId",
                table: "pricing_agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCategoryId",
                table: "price_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCategoryId",
                table: "order_service_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverageStatus",
                table: "order_pricing_snapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStale",
                table: "order_pricing_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCategoryId",
                table: "order_pricing_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LanguageCode",
                table: "invoices",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceGrouping",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "customer_allowed_legal_entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_customer_allowed_legal_entities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_allowed_legal_entities_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_allowed_legal_entities_legal_entities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "legal_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_TenantId_InvoiceReadiness",
                table: "transport_orders",
                columns: new[] { "TenantId", "InvoiceReadiness" });

            migrationBuilder.CreateIndex(
                name: "IX_service_options_SalesCategoryId",
                table: "service_options",
                column: "SalesCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreements_SalesCategoryId",
                table: "pricing_agreements",
                column: "SalesCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_SalesCategoryId",
                table: "price_rules",
                column: "SalesCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_allowed_legal_entities_LegalEntityId",
                table: "customer_allowed_legal_entities",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_allowed_legal_entities_TenantId_CustomerId",
                table: "customer_allowed_legal_entities",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "UX_customer_allowed_entities_active",
                table: "customer_allowed_legal_entities",
                columns: new[] { "CustomerId", "LegalEntityId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_price_rules_sales_categories_SalesCategoryId",
                table: "price_rules",
                column: "SalesCategoryId",
                principalTable: "sales_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pricing_agreements_sales_categories_SalesCategoryId",
                table: "pricing_agreements",
                column: "SalesCategoryId",
                principalTable: "sales_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_service_options_sales_categories_SalesCategoryId",
                table: "service_options",
                column: "SalesCategoryId",
                principalTable: "sales_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_price_rules_sales_categories_SalesCategoryId",
                table: "price_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_pricing_agreements_sales_categories_SalesCategoryId",
                table: "pricing_agreements");

            migrationBuilder.DropForeignKey(
                name: "FK_service_options_sales_categories_SalesCategoryId",
                table: "service_options");

            migrationBuilder.DropTable(
                name: "customer_allowed_legal_entities");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_TenantId_InvoiceReadiness",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_service_options_SalesCategoryId",
                table: "service_options");

            migrationBuilder.DropIndex(
                name: "IX_pricing_agreements_SalesCategoryId",
                table: "pricing_agreements");

            migrationBuilder.DropIndex(
                name: "IX_price_rules_SalesCategoryId",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "InvoiceReadiness",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "InvoiceReadinessReasons",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "SalesCategoryId",
                table: "service_options");

            migrationBuilder.DropColumn(
                name: "DefaultUnitCode",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "InvoiceDescriptionNl",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "VatCategoryOverride",
                table: "sales_categories");

            migrationBuilder.DropColumn(
                name: "SalesCategoryId",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "SalesCategoryId",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "SalesCategoryId",
                table: "order_service_lines");

            migrationBuilder.DropColumn(
                name: "CoverageStatus",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "IsStale",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "SalesCategoryId",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "LanguageCode",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceGrouping",
                table: "customers");
        }
    }
}
