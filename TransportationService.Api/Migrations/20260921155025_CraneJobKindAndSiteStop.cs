using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CraneJobKindAndSiteStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CraneJobKind",
                table: "transport_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                // Existing orders are ordinary transports: CraneJobKind.None (enum stored as string).
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "LiftConditions",
                table: "transport_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiftEquipment",
                table: "transport_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LiftHeightMeters",
                table: "transport_orders",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiftLoadDimensions",
                table: "transport_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LiftLoadWeightKg",
                table: "transport_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LiftRadiusMeters",
                table: "transport_orders",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkDescription",
                table: "transport_orders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PlannedToIsManual",
                table: "transport_order_stops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsOnSiteWork",
                table: "activity_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // One-off data fix (D2): tenants seeded before this flag existed get it on their
            // Kraantransport type — the same value the seeder now gives new tenants. This is the
            // ONLY place the code string is matched; runtime logic drives on the flag alone, so a
            // tenant can move or clear it afterwards through the activity-type settings.
            migrationBuilder.Sql(
                "UPDATE activity_types SET \"SupportsOnSiteWork\" = TRUE WHERE \"Code\" = 'KRAANTRANSPORT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CraneJobKind",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LiftConditions",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LiftEquipment",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LiftHeightMeters",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LiftLoadDimensions",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LiftLoadWeightKg",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LiftRadiusMeters",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "WorkDescription",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PlannedToIsManual",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "SupportsOnSiteWork",
                table: "activity_types");
        }
    }
}
