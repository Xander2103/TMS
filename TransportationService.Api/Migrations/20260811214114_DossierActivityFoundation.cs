using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DossierActivityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "transport_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CustomerReference",
                table: "transport_dossiers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DossierDate",
                table: "transport_dossiers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "transport_dossiers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginTransportOrderId",
                table: "transport_dossiers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "transport_dossiers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "activity_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    KpiCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HasStops = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsGoods = table.Column<bool>(type: "boolean", nullable: false),
                    PlanningRelevant = table.Column<bool>(type: "boolean", nullable: false),
                    WarehouseRelevant = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsDuration = table.Column<bool>(type: "boolean", nullable: false),
                    IsQuickStart = table.Column<bool>(type: "boolean", nullable: false),
                    QuickStartOrder = table.Column<int>(type: "integer", nullable: false),
                    IsSystemDefaultTransport = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_activity_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dossier_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LinkedTransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedActivityId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DurationHours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_dossier_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dossier_activities_activity_types_ActivityTypeId",
                        column: x => x.ActivityTypeId,
                        principalTable: "activity_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dossier_activities_dossier_activities_LinkedActivityId",
                        column: x => x.LinkedActivityId,
                        principalTable: "dossier_activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dossier_activities_transport_dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "transport_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dossier_activities_transport_orders_LinkedTransportOrderId",
                        column: x => x.LinkedTransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_LegalEntityId",
                table: "transport_dossiers",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "UX_transport_dossiers_origin_order",
                table: "transport_dossiers",
                column: "OriginTransportOrderId",
                unique: true,
                filter: "\"OriginTransportOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_activity_types_TenantId_Code",
                table: "activity_types",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_activity_types_default_transport",
                table: "activity_types",
                column: "TenantId",
                unique: true,
                filter: "\"IsSystemDefaultTransport\" = true AND \"IsActive\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_ActivityTypeId",
                table: "dossier_activities",
                column: "ActivityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_DossierId",
                table: "dossier_activities",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_LinkedActivityId",
                table: "dossier_activities",
                column: "LinkedActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_LinkedTransportOrderId",
                table: "dossier_activities",
                column: "LinkedTransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_TenantId_ActivityTypeId",
                table: "dossier_activities",
                columns: new[] { "TenantId", "ActivityTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_TenantId_DossierId",
                table: "dossier_activities",
                columns: new[] { "TenantId", "DossierId" });

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_TenantId_LinkedTransportOrderId",
                table: "dossier_activities",
                columns: new[] { "TenantId", "LinkedTransportOrderId" },
                filter: "\"LinkedTransportOrderId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_transport_dossiers_legal_entities_LegalEntityId",
                table: "transport_dossiers",
                column: "LegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transport_dossiers_transport_orders_OriginTransportOrderId",
                table: "transport_dossiers",
                column: "OriginTransportOrderId",
                principalTable: "transport_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transport_dossiers_legal_entities_LegalEntityId",
                table: "transport_dossiers");

            migrationBuilder.DropForeignKey(
                name: "FK_transport_dossiers_transport_orders_OriginTransportOrderId",
                table: "transport_dossiers");

            migrationBuilder.DropTable(
                name: "dossier_activities");

            migrationBuilder.DropTable(
                name: "activity_types");

            migrationBuilder.DropIndex(
                name: "IX_transport_dossiers_LegalEntityId",
                table: "transport_dossiers");

            migrationBuilder.DropIndex(
                name: "UX_transport_dossiers_origin_order",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CustomerReference",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "DossierDate",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "OriginTransportOrderId",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "transport_dossiers");
        }
    }
}
