using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class LocationOperationalData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                table: "locations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AdrAllowed",
                table: "locations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactMobile",
                table: "locations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CraneRequired",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerContactId",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultLoadingMinutes",
                table: "locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultUnloadingMinutes",
                table: "locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryByAppointmentOnly",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Dock",
                table: "locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverInstructions",
                table: "locations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EarliestArrival",
                table: "locations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForkliftAvailable",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Gate",
                table: "locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightRestrictionMeters",
                table: "locations",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalMemo",
                table: "locations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "LatestArrival",
                table: "locations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PreferredArrivalFrom",
                table: "locations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PreferredArrivalTo",
                table: "locations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceptionPoint",
                table: "locations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteDescription",
                table: "locations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightRestrictionTons",
                table: "locations",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "location_opening_intervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    FromTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ToTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_opening_intervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_location_opening_intervals_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_locations_CustomerContactId",
                table: "locations",
                column: "CustomerContactId");

            migrationBuilder.CreateIndex(
                name: "IX_location_opening_intervals_LocationId",
                table: "location_opening_intervals",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_location_opening_intervals_TenantId_LocationId",
                table: "location_opening_intervals",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_locations_customer_contacts_CustomerContactId",
                table: "locations",
                column: "CustomerContactId",
                principalTable: "customer_contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_locations_customer_contacts_CustomerContactId",
                table: "locations");

            migrationBuilder.DropTable(
                name: "location_opening_intervals");

            migrationBuilder.DropIndex(
                name: "IX_locations_CustomerContactId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "AccessCode",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "AdrAllowed",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "ContactMobile",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "CraneRequired",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "CustomerContactId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "DefaultLoadingMinutes",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "DefaultUnloadingMinutes",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "DeliveryByAppointmentOnly",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "Dock",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "DriverInstructions",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "EarliestArrival",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "ForkliftAvailable",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "Gate",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "HeightRestrictionMeters",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "InternalMemo",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "LatestArrival",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "PreferredArrivalFrom",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "PreferredArrivalTo",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "ReceptionPoint",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "RouteDescription",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "WeightRestrictionTons",
                table: "locations");
        }
    }
}
