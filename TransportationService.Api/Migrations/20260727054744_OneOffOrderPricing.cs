using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OneOffOrderPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OneOffExtraHourlyRate",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OneOffFixedAmount",
                table: "transport_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OneOffIncludedCombinedMinutes",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OneOffIncludedLoadingMinutes",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OneOffIncludedUnloadingMinutes",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OneOffNotes",
                table: "transport_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // Existing rows must read back as a valid OrderPricingSource value.
            migrationBuilder.AddColumn<string>(
                name: "PricingSource",
                table: "transport_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Contract");

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraHourlyRate",
                table: "pricing_agreements",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncludedCombinedMinutes",
                table: "pricing_agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncludedLoadingMinutes",
                table: "pricing_agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncludedUnloadingMinutes",
                table: "pricing_agreements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Proposed",
                table: "order_pricing_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OneOffExtraHourlyRate",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OneOffFixedAmount",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OneOffIncludedCombinedMinutes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OneOffIncludedLoadingMinutes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OneOffIncludedUnloadingMinutes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OneOffNotes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PricingSource",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "ExtraHourlyRate",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "IncludedCombinedMinutes",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "IncludedLoadingMinutes",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "IncludedUnloadingMinutes",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "Proposed",
                table: "order_pricing_lines");
        }
    }
}
