using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InventoryUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowForInventory",
                table: "unit_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // One-time enablement of obvious stock units; runs only when the column is
            // introduced, so later admin choices are never overwritten.
            migrationBuilder.Sql(
                "UPDATE unit_types SET \"AllowForInventory\" = true WHERE \"Code\" IN ('PIECE', 'BOX', 'KG');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowForInventory",
                table: "unit_types");
        }
    }
}
