using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderServiceQuantities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceDescriptionSnapshot",
                table: "order_service_lines",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "order_service_lines",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceDescriptionSnapshot",
                table: "order_service_lines");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "order_service_lines");
        }
    }
}
