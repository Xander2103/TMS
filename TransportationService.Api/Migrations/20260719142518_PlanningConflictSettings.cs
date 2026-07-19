using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlanningConflictSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShiftOverlapConflictSeverity",
                table: "tenant_settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Warning");

            migrationBuilder.AddColumn<string>(
                name: "TrainingConflictSeverity",
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
                name: "ShiftOverlapConflictSeverity",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "TrainingConflictSeverity",
                table: "tenant_settings");
        }
    }
}
