using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OperationalFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "trips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing rows must read back as a valid OrderPriority value.
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "transport_orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "stop_status_histories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "proofs_of_delivery",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conflict_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConflictCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    table.PrimaryKey("PK_conflict_overrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operational_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    LinkPath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RelatedEntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_operational_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_resource_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Route = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TouchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_user_resource_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_resource_links_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stop_status_histories_TenantId_ClientRequestId",
                table: "stop_status_histories",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_proofs_of_delivery_TenantId_ClientRequestId",
                table: "proofs_of_delivery",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_ClientRequestId",
                table: "incidents",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_conflict_overrides_TenantId_EntityType_EntityId",
                table: "conflict_overrides",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_TenantId_DedupeKey",
                table: "operational_alerts",
                columns: new[] { "TenantId", "DedupeKey" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_TenantId_Status_Severity",
                table: "operational_alerts",
                columns: new[] { "TenantId", "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_user_resource_links_TenantId_UserId_Kind_EntityType_EntityId",
                table: "user_resource_links",
                columns: new[] { "TenantId", "UserId", "Kind", "EntityType", "EntityId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_user_resource_links_TenantId_UserId_Kind_TouchedAt",
                table: "user_resource_links",
                columns: new[] { "TenantId", "UserId", "Kind", "TouchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_resource_links_UserId",
                table: "user_resource_links",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conflict_overrides");

            migrationBuilder.DropTable(
                name: "operational_alerts");

            migrationBuilder.DropTable(
                name: "user_resource_links");

            migrationBuilder.DropIndex(
                name: "IX_stop_status_histories_TenantId_ClientRequestId",
                table: "stop_status_histories");

            migrationBuilder.DropIndex(
                name: "IX_proofs_of_delivery_TenantId_ClientRequestId",
                table: "proofs_of_delivery");

            migrationBuilder.DropIndex(
                name: "IX_incidents_TenantId_ClientRequestId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "stop_status_histories");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "proofs_of_delivery");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "incidents");
        }
    }
}
