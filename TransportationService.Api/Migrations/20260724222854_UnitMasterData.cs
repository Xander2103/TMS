using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class UnitMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "unit_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Decimals",
                table: "unit_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultHeightCm",
                table: "unit_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultLengthCm",
                table: "unit_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultLoadingMeters",
                table: "unit_types",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultPalletPlaces",
                table: "unit_types",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultVolumeM3",
                table: "unit_types",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultWeightKg",
                table: "unit_types",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultWidthCm",
                table: "unit_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DimensionBehavior",
                table: "unit_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeightKg",
                table: "unit_types",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Symbol",
                table: "unit_types",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "Decimals",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultHeightCm",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultLengthCm",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultLoadingMeters",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultPalletPlaces",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultVolumeM3",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultWeightKg",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DefaultWidthCm",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "DimensionBehavior",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "MaxWeightKg",
                table: "unit_types");

            migrationBuilder.DropColumn(
                name: "Symbol",
                table: "unit_types");
        }
    }
}
