using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderIncludedTimeOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExtraTimeHourlyRateOverride",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraTimeMinimumBillableMinutes",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraTimeRoundingStepMinutes",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncludedLoadingMinutesOverride",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncludedUnloadingMinutesOverride",
                table: "transport_orders",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraTimeHourlyRateOverride",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "ExtraTimeMinimumBillableMinutes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "ExtraTimeRoundingStepMinutes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "IncludedLoadingMinutesOverride",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "IncludedUnloadingMinutesOverride",
                table: "transport_orders");
        }
    }
}
