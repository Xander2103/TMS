using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class Warehousing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouses_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "docks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AllowsLoading = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsUnloading = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsAdr = table.Column<bool>(type: "boolean", nullable: false),
                    Refrigerated = table.Column<bool>(type: "boolean", nullable: false),
                    MaxVehicleLengthM = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    MaxVehicleHeightM = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_docks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_docks_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dock_appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DockId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlannedEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_dock_appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dock_appointments_docks_DockId",
                        column: x => x.DockId,
                        principalTable: "docks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dock_appointments_drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dock_appointments_trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dock_appointments_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dock_appointments_trips_TripId",
                        column: x => x.TripId,
                        principalTable: "trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dock_appointments_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dock_appointments_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_DockId",
                table: "dock_appointments",
                column: "DockId");

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_DriverId",
                table: "dock_appointments",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_TenantId_DockId_PlannedStart",
                table: "dock_appointments",
                columns: new[] { "TenantId", "DockId", "PlannedStart" });

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_TenantId_Status",
                table: "dock_appointments",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_TenantId_WarehouseId_PlannedStart",
                table: "dock_appointments",
                columns: new[] { "TenantId", "WarehouseId", "PlannedStart" });

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_TrailerId",
                table: "dock_appointments",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_TransportOrderId",
                table: "dock_appointments",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_TripId",
                table: "dock_appointments",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_VehicleId",
                table: "dock_appointments",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_dock_appointments_WarehouseId",
                table: "dock_appointments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_docks_TenantId_WarehouseId_Code",
                table: "docks",
                columns: new[] { "TenantId", "WarehouseId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_docks_WarehouseId",
                table: "docks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_LocationId",
                table: "warehouses",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_TenantId_Name",
                table: "warehouses",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dock_appointments");

            migrationBuilder.DropTable(
                name: "docks");

            migrationBuilder.DropTable(
                name: "warehouses");
        }
    }
}
