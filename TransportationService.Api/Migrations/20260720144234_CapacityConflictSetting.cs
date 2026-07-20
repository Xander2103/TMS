using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CapacityConflictSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapacityConflictSeverity",
                table: "tenant_settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Warning");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapacityConflictSeverity",
                table: "tenant_settings");
        }
    }
}
