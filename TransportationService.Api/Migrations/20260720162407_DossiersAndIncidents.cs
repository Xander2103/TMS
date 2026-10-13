using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DossiersAndIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DossierNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DossierNumberPrefix",
                table: "tenant_settings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomTypeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Cause = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResponsibleUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerImpact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OperationalImpact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FinancialImpact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ActualCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transport_dossiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsibleUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_transport_dossiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dossier_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_dossier_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dossier_orders_transport_dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "transport_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dossier_orders_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dossier_relations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_dossier_relations", x => x.Id);
                    table.CheckConstraint("CK_dossier_relations_no_self_link", "\"SourceDossierId\" <> \"TargetDossierId\"");
                    table.ForeignKey(
                        name: "FK_dossier_relations_transport_dossiers_SourceDossierId",
                        column: x => x.SourceDossierId,
                        principalTable: "transport_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dossier_relations_transport_dossiers_TargetDossierId",
                        column: x => x.TargetDossierId,
                        principalTable: "transport_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dossier_orders_TenantId_TransportOrderId",
                table: "dossier_orders",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_dossier_orders_TransportOrderId",
                table: "dossier_orders",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "UX_dossier_orders_active_link",
                table: "dossier_orders",
                columns: new[] { "DossierId", "TransportOrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_relations_TargetDossierId",
                table: "dossier_relations",
                column: "TargetDossierId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_relations_TenantId_TargetDossierId",
                table: "dossier_relations",
                columns: new[] { "TenantId", "TargetDossierId" });

            migrationBuilder.CreateIndex(
                name: "UX_dossier_relations_active",
                table: "dossier_relations",
                columns: new[] { "SourceDossierId", "TargetDossierId", "RelationType" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_CustomerId",
                table: "incidents",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_DossierId",
                table: "incidents",
                columns: new[] { "TenantId", "DossierId" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_Status",
                table: "incidents",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_TransportOrderId",
                table: "incidents",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_TenantId_CustomerId",
                table: "transport_dossiers",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_TenantId_DossierNumber",
                table: "transport_dossiers",
                columns: new[] { "TenantId", "DossierNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_TenantId_Status",
                table: "transport_dossiers",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dossier_orders");

            migrationBuilder.DropTable(
                name: "dossier_relations");

            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropTable(
                name: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "DossierNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DossierNumberPrefix",
                table: "tenant_settings");
        }
    }
}
