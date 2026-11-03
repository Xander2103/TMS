using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class LegalEntityHeaderImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeaderImageContentType",
                table: "legal_entities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderImageFileName",
                table: "legal_entities",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderImageStorageKey",
                table: "legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeaderImageContentType",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "HeaderImageFileName",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "HeaderImageStorageKey",
                table: "legal_entities");
        }
    }
}
