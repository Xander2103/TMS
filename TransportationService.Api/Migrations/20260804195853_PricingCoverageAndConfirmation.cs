using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricingCoverageAndConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "order_pricing_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedByName",
                table: "order_pricing_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "order_pricing_snapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedWithUnpricedGoodsReason",
                table: "order_pricing_snapshots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverageJson",
                table: "order_pricing_snapshots",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "ConfirmedByName",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "ConfirmedWithUnpricedGoodsReason",
                table: "order_pricing_snapshots");

            migrationBuilder.DropColumn(
                name: "CoverageJson",
                table: "order_pricing_snapshots");
        }
    }
}
