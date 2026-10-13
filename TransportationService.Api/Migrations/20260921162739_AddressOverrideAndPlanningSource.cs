using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddressOverrideAndPlanningSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TailLiftCapacityKg",
                table: "vehicles",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleSelectionSource",
                table: "trips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AddressOverridden",
                table: "transport_order_stops",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TailLiftCapacityKg",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "VehicleSelectionSource",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "AddressOverridden",
                table: "transport_order_stops");
        }
    }
}
