using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class Wave1StopExecutionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessInstructions",
                table: "transport_order_stops",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppointmentReference",
                table: "transport_order_stops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AppointmentRequired",
                table: "transport_order_stops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedFrom",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedTo",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EarliestAllowed",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LatestAllowed",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadingInstructions",
                table: "transport_order_stops",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedFrom",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedTo",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnloadingInstructions",
                table: "transport_order_stops",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepartedAt",
                table: "stop_executions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateArrivalReason",
                table: "stop_executions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "stop_executions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stop_status_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StopExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_stop_status_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stop_status_histories_stop_executions_StopExecutionId",
                        column: x => x.StopExecutionId,
                        principalTable: "stop_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stop_status_histories_StopExecutionId_OccurredAt",
                table: "stop_status_histories",
                columns: new[] { "StopExecutionId", "OccurredAt" });

            // The status model was widened; the old initial status 'Pending' is now 'Planned'.
            migrationBuilder.Sql("UPDATE stop_executions SET \"Status\" = 'Planned' WHERE \"Status\" = 'Pending';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE stop_executions SET \"Status\" = 'Pending' WHERE \"Status\" = 'Planned';");

            migrationBuilder.DropTable(
                name: "stop_status_histories");

            migrationBuilder.DropColumn(
                name: "AccessInstructions",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "AppointmentReference",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "AppointmentRequired",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "ConfirmedFrom",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "ConfirmedTo",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "EarliestAllowed",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "LatestAllowed",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "LoadingInstructions",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "RequestedFrom",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "RequestedTo",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "UnloadingInstructions",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "DepartedAt",
                table: "stop_executions");

            migrationBuilder.DropColumn(
                name: "LateArrivalReason",
                table: "stop_executions");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "stop_executions");
        }
    }
}
