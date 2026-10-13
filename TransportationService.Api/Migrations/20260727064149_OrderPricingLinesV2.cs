using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderPricingLinesV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LinesTotal",
                table: "order_pricing_snapshots",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            // Existing snapshots have never been reviewed/locked — Draft reads back correctly.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "order_pricing_snapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<string>(
                name: "AdjustReason",
                table: "order_pricing_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdjustedAtUtc",
                table: "order_pricing_lines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdjustedByUserId",
                table: "order_pricing_lines",
                type: "uuid",
                nullable: true);

            // Existing lines were all engine-produced and never manually edited — Auto reads back correctly.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "order_pricing_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<string>(
                name: "LineKey",
                table: "order_pricing_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "order_pricing_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalQuantity",
                table: "order_pricing_lines",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalUnitPrice",
                table: "order_pricing_lines",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "order_pricing_lines",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuleId",
                table: "order_pricing_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceOptionId",
                table: "order_pricing_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "order_pricing_lines",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinesTotal",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "AdjustReason",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "AdjustedAtUtc",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "AdjustedByUserId",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "LineKey",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "OriginalQuantity",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "OriginalUnitPrice",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "RuleId",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "ServiceOptionId",
                table: "order_pricing_lines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "order_pricing_lines");
        }
    }
}
