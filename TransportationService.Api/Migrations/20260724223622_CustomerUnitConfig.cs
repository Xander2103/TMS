using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerUnitConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerLabel",
                table: "customer_preferred_units",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EdiCode",
                table: "customer_preferred_units",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcelCode",
                table: "customer_preferred_units",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavourite",
                table: "customer_preferred_units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing preferred units were always shown first; keep that behaviour by
            // marking them as favourites under the new model.
            migrationBuilder.Sql("UPDATE customer_preferred_units SET \"IsFavourite\" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerLabel",
                table: "customer_preferred_units");

            migrationBuilder.DropColumn(
                name: "EdiCode",
                table: "customer_preferred_units");

            migrationBuilder.DropColumn(
                name: "ExcelCode",
                table: "customer_preferred_units");

            migrationBuilder.DropColumn(
                name: "IsFavourite",
                table: "customer_preferred_units");
        }
    }
}
