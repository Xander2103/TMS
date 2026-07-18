using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceTransportOrderScaffold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The legacy scaffold rows carry no tenant or customer linkage; the new non-nullable
            // columns and FK constraints below require the table to be empty. Demo data is discarded.
            migrationBuilder.Sql("DELETE FROM transport_orders;");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_Reference",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PickupAddress",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "transport_orders");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "transport_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<bool>(
                name: "AdrRequired",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AgreedPrice",
                table: "transport_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CraneRequired",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "transport_orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "transport_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CustomerReference",
                table: "transport_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "transport_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodsDescription",
                table: "transport_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "transport_orders",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OrderDate",
                table: "transport_orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "transport_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PalletCount",
                table: "transport_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "transport_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUnit",
                table: "transport_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "transport_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "transport_orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeM3",
                table: "transport_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "transport_orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transport_order_stops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    StopType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    PlannedFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_order_stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_order_stops_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transport_order_stops_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_CustomerId",
                table: "transport_orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_TenantId_CustomerId",
                table: "transport_orders",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_TenantId_OrderDate",
                table: "transport_orders",
                columns: new[] { "TenantId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_TenantId_OrderNumber",
                table: "transport_orders",
                columns: new[] { "TenantId", "OrderNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_TenantId_Status",
                table: "transport_orders",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_order_stops_LocationId",
                table: "transport_order_stops",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_order_stops_TransportOrderId_Sequence",
                table: "transport_order_stops",
                columns: new[] { "TransportOrderId", "Sequence" });

            migrationBuilder.AddForeignKey(
                name: "FK_transport_orders_customers_CustomerId",
                table: "transport_orders",
                column: "CustomerId",
                principalTable: "customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transport_orders_customers_CustomerId",
                table: "transport_orders");

            migrationBuilder.DropTable(
                name: "transport_order_stops");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_CustomerId",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_TenantId_CustomerId",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_TenantId_OrderDate",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_TenantId_OrderNumber",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_TenantId_Status",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "AdrRequired",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "AgreedPrice",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CraneRequired",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CustomerReference",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "GoodsDescription",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OrderDate",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PalletCount",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "QuantityUnit",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "VolumeM3",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "transport_orders");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "transport_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "transport_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "transport_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PickupAddress",
                table: "transport_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "transport_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_Reference",
                table: "transport_orders",
                column: "Reference",
                unique: true);
        }
    }
}
