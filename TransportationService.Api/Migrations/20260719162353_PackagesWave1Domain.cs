using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PackagesWave1Domain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PackageNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PackageNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValue: "PKG-");

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadingStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BarcodeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BarcodeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalBarcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalPackageReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UnitType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitTypeLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    VolumeM3 = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    LengthCm = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    WidthCm = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    ParentPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                    IsFragile = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresTemperatureControl = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresSignature = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentLifecycleStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrentExceptionStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_packages_packages_ParentPackageId",
                        column: x => x.ParentPackageId,
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_packages_transport_order_stops_DeliveryStopId",
                        column: x => x.DeliveryStopId,
                        principalTable: "transport_order_stops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_packages_transport_order_stops_LoadingStopId",
                        column: x => x.LoadingStopId,
                        principalTable: "transport_order_stops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_packages_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_barcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetireReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_package_barcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_barcodes_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OldStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportOrderStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BarcodeUsed = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ScanEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExceptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsOverride = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    ClientEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_package_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_events_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_package_barcodes_PackageId",
                table: "package_barcodes",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_barcodes_TenantId_Value",
                table: "package_barcodes",
                columns: new[] { "TenantId", "Value" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_package_events_PackageId",
                table: "package_events",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_events_TenantId_PackageId_OccurredAt",
                table: "package_events",
                columns: new[] { "TenantId", "PackageId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_package_events_TenantId_TripId",
                table: "package_events",
                columns: new[] { "TenantId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_packages_DeliveryStopId",
                table: "packages",
                column: "DeliveryStopId");

            migrationBuilder.CreateIndex(
                name: "IX_packages_LoadingStopId",
                table: "packages",
                column: "LoadingStopId");

            migrationBuilder.CreateIndex(
                name: "IX_packages_ParentPackageId",
                table: "packages",
                column: "ParentPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_packages_TenantId_CurrentLifecycleStatus",
                table: "packages",
                columns: new[] { "TenantId", "CurrentLifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_packages_TenantId_PackageNumber",
                table: "packages",
                columns: new[] { "TenantId", "PackageNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_packages_TenantId_TransportOrderId",
                table: "packages",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_packages_TransportOrderId",
                table: "packages",
                column: "TransportOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "package_barcodes");

            migrationBuilder.DropTable(
                name: "package_events");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropColumn(
                name: "PackageNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "PackageNumberPrefix",
                table: "tenant_settings");
        }
    }
}
