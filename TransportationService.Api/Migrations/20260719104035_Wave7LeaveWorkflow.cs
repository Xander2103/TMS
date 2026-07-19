using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class Wave7LeaveWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentFileName",
                table: "absences",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "absences",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "absences",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartDay",
                table: "absences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentFileName",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "PartDay",
                table: "absences");
        }
    }
}
