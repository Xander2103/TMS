using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PackagesWave6Pod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageSummaryJson",
                table: "proofs_of_delivery",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PackagesAcknowledged",
                table: "proofs_of_delivery",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackageSummaryJson",
                table: "proofs_of_delivery");

            migrationBuilder.DropColumn(
                name: "PackagesAcknowledged",
                table: "proofs_of_delivery");
        }
    }
}
