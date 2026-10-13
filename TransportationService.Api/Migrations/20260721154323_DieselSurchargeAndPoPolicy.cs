using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DieselSurchargeAndPoPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DieselSurchargeOverride",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DieselSurchargeOverrideReason",
                table: "transport_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DieselSurchargePercentOverride",
                table: "transport_orders",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderNumber",
                table: "invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderPolicy",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Backfill the policy from the legacy bool: true → Required, false → None.
            migrationBuilder.Sql(
                """
                UPDATE customers
                SET "PurchaseOrderPolicy" = CASE WHEN "PurchaseOrderRequired" THEN 'Required' ELSE 'None' END
                WHERE "PurchaseOrderPolicy" = '';
                """);

            migrationBuilder.CreateTable(
                name: "customer_diesel_surcharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Presentation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Rounding = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FormulaDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_customer_diesel_surcharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_diesel_surcharges_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_purchase_order_numbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_customer_purchase_order_numbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_purchase_order_numbers_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_diesel_surcharges_CustomerId",
                table: "customer_diesel_surcharges",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_diesel_surcharges_TenantId_CustomerId",
                table: "customer_diesel_surcharges",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_customer_purchase_order_numbers_CustomerId",
                table: "customer_purchase_order_numbers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_purchase_order_numbers_TenantId_CustomerId_ValidFr~",
                table: "customer_purchase_order_numbers",
                columns: new[] { "TenantId", "CustomerId", "ValidFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_diesel_surcharges");

            migrationBuilder.DropTable(
                name: "customer_purchase_order_numbers");

            migrationBuilder.DropColumn(
                name: "DieselSurchargeOverride",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "DieselSurchargeOverrideReason",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "DieselSurchargePercentOverride",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderNumber",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderPolicy",
                table: "customers");
        }
    }
}
