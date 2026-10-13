using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CargoItemRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdrDetails",
                table: "order_cargo_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AdrRequired",
                table: "order_cargo_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightMeters",
                table: "order_cargo_items",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthMeters",
                table: "order_cargo_items",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LoadingStopId",
                table: "order_cargo_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "order_cargo_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Stackable",
                table: "order_cargo_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWeightKg",
                table: "order_cargo_items",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitType",
                table: "order_cargo_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitTypeLabel",
                table: "order_cargo_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnloadingStopId",
                table: "order_cargo_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VolumeIsManual",
                table: "order_cargo_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeM3",
                table: "order_cargo_items",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightPerUnitKg",
                table: "order_cargo_items",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthMeters",
                table: "order_cargo_items",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_cargo_items_LoadingStopId",
                table: "order_cargo_items",
                column: "LoadingStopId");

            migrationBuilder.CreateIndex(
                name: "IX_order_cargo_items_UnloadingStopId",
                table: "order_cargo_items",
                column: "UnloadingStopId");

            migrationBuilder.AddForeignKey(
                name: "FK_order_cargo_items_transport_order_stops_LoadingStopId",
                table: "order_cargo_items",
                column: "LoadingStopId",
                principalTable: "transport_order_stops",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_order_cargo_items_transport_order_stops_UnloadingStopId",
                table: "order_cargo_items",
                column: "UnloadingStopId",
                principalTable: "transport_order_stops",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_cargo_items_transport_order_stops_LoadingStopId",
                table: "order_cargo_items");

            migrationBuilder.DropForeignKey(
                name: "FK_order_cargo_items_transport_order_stops_UnloadingStopId",
                table: "order_cargo_items");

            migrationBuilder.DropIndex(
                name: "IX_order_cargo_items_LoadingStopId",
                table: "order_cargo_items");

            migrationBuilder.DropIndex(
                name: "IX_order_cargo_items_UnloadingStopId",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "AdrDetails",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "AdrRequired",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "HeightMeters",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "LengthMeters",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "LoadingStopId",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "Stackable",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "TotalWeightKg",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "UnitTypeLabel",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "UnloadingStopId",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "VolumeIsManual",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "VolumeM3",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "WeightPerUnitKg",
                table: "order_cargo_items");

            migrationBuilder.DropColumn(
                name: "WidthMeters",
                table: "order_cargo_items");
        }
    }
}
