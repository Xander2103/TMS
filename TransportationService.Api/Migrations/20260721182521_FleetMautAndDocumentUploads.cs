using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class FleetMautAndDocumentUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AxleCount",
                table: "vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LoadingMeters",
                table: "vehicles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RequiredLicenceCode",
                table: "vehicles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AxleCount",
                table: "trailers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LoadingMeters",
                table: "trailers",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "DocumentPath",
                table: "fleet_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "fleet_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "fleet_documents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuingAuthority",
                table: "fleet_documents",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AxleCount",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "LoadingMeters",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "RequiredLicenceCode",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "AxleCount",
                table: "trailers");

            migrationBuilder.DropColumn(
                name: "LoadingMeters",
                table: "trailers");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "fleet_documents");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "fleet_documents");

            migrationBuilder.DropColumn(
                name: "IssuingAuthority",
                table: "fleet_documents");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentPath",
                table: "fleet_documents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
