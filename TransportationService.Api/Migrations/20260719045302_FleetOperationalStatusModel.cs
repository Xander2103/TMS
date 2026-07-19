using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class FleetOperationalStatusModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "vehicles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "trailers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Enum members were renamed so no operational status reads as a second "Actief":
            // Active → Available; Decommissioned → OutOfService + administratively inactive.
            foreach (var table in new[] { "vehicles", "trailers" })
            {
                migrationBuilder.Sql($"""
                    UPDATE {table} SET "OperationalStatus" = 'Available' WHERE "OperationalStatus" = 'Active';
                    """);
                migrationBuilder.Sql($"""
                    UPDATE {table}
                    SET "OperationalStatus" = 'OutOfService',
                        "IsActive" = false,
                        "StatusReason" = COALESCE("StatusReason", 'Uit dienst genomen')
                    WHERE "OperationalStatus" = 'Decommissioned';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "trailers");
        }
    }
}
