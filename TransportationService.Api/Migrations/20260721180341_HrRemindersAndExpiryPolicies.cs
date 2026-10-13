using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class HrRemindersAndExpiryPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expiry_reminder_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    RepeatIntervalDays = table.Column<int>(type: "integer", nullable: true),
                    NotifyEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyHr = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyPlanner = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyFleetManager = table.Column<bool>(type: "boolean", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_expiry_reminder_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hr_reminder_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BirthdayEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BirthdayDaysBefore = table.Column<int>(type: "integer", nullable: false),
                    BirthdayEmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BirthdayRecipientRoleCodes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeniorityEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SeniorityMilestoneYears = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeniorityWarningDays = table.Column<int>(type: "integer", nullable: false),
                    SeniorityEmployeeEmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmploymentEndEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmploymentEndDaysBefore = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_hr_reminder_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reminder_dispatch_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_reminder_dispatch_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expiry_reminder_policies_TenantId_TargetKind_TargetCode",
                table: "expiry_reminder_policies",
                columns: new[] { "TenantId", "TargetKind", "TargetCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_hr_reminder_settings_TenantId",
                table: "hr_reminder_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reminder_dispatch_logs_TenantId_DedupeKey",
                table: "reminder_dispatch_logs",
                columns: new[] { "TenantId", "DedupeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expiry_reminder_policies");

            migrationBuilder.DropTable(
                name: "hr_reminder_settings");

            migrationBuilder.DropTable(
                name: "reminder_dispatch_logs");
        }
    }
}
