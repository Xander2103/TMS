using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PackageCargoLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CargoItemId",
                table: "packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_packages_CargoItemId",
                table: "packages",
                column: "CargoItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_packages_order_cargo_items_CargoItemId",
                table: "packages",
                column: "CargoItemId",
                principalTable: "order_cargo_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_packages_order_cargo_items_CargoItemId",
                table: "packages");

            migrationBuilder.DropIndex(
                name: "IX_packages_CargoItemId",
                table: "packages");

            migrationBuilder.DropColumn(
                name: "CargoItemId",
                table: "packages");
        }
    }
}
