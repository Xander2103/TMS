using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class BracketDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BracketMode",
                table: "price_rules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Absolute");

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumAmount",
                table: "price_rules",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LoadingMetersTo",
                table: "price_rule_brackets",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeToM3",
                table: "price_rule_brackets",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightToKg",
                table: "price_rule_brackets",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BracketMode",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "MaximumAmount",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "LoadingMetersTo",
                table: "price_rule_brackets");

            migrationBuilder.DropColumn(
                name: "VolumeToM3",
                table: "price_rule_brackets");

            migrationBuilder.DropColumn(
                name: "WeightToKg",
                table: "price_rule_brackets");
        }
    }
}
