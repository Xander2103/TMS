using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderImportProfileCustomerAndHeaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "order_import_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceHeadersJson",
                table: "order_import_profiles",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_import_profiles_CustomerId",
                table: "order_import_profiles",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_order_import_profiles_customers_CustomerId",
                table: "order_import_profiles",
                column: "CustomerId",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_import_profiles_customers_CustomerId",
                table: "order_import_profiles");

            migrationBuilder.DropIndex(
                name: "IX_order_import_profiles_CustomerId",
                table: "order_import_profiles");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "order_import_profiles");

            migrationBuilder.DropColumn(
                name: "SourceHeadersJson",
                table: "order_import_profiles");
        }
    }
}
