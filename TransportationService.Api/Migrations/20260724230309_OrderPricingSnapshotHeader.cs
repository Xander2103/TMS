using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderPricingSnapshotHeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualQuantity",
                table: "order_pricing_lines",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgreementName",
                table: "order_pricing_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BillableQuantity",
                table: "order_pricing_lines",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleName",
                table: "order_pricing_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_pricing_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ZoneCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ZoneName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AgreementNames = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnitSummary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CalculatedTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    OverrideAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    OverrideReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OverriddenByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverriddenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_order_pricing_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_pricing_snapshots_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_pricing_snapshots_TenantId_TransportOrderId",
                table: "order_pricing_snapshots",
                columns: new[] { "TenantId", "TransportOrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_order_pricing_snapshots_TransportOrderId",
                table: "order_pricing_snapshots",
                column: "TransportOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "ActualQuantity",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "AgreementName",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "BillableQuantity",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "RuleName",
                table: "order_pricing_lines");
        }
    }
}
