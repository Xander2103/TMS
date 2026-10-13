using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimeAndAttendanceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LookupHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_attendance_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_credentials_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SelfPunchEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KioskEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PinLength = table.Column<int>(type: "integer", nullable: false),
                    ForgottenClockOutAfterHours = table.Column<int>(type: "integer", nullable: false),
                    AutoCloseEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoCloseAfterHours = table.Column<int>(type: "integer", nullable: false),
                    PlannedNotClockedInGraceMinutes = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_attendance_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kiosk_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPunchAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_kiosk_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kiosk_devices_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attendance_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClockInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClockInSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClockOutSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    KioskDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    HasCorrections = table.Column<bool>(type: "boolean", nullable: false),
                    ForgottenClockOutNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_attendance_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_sessions_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_sessions_kiosk_devices_KioskDeviceId",
                        column: x => x.KioskDeviceId,
                        principalTable: "kiosk_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_attendance_sessions_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "attendance_breaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_attendance_breaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_breaks_attendance_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "attendance_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_corrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BreakId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OldValue = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewValue = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_attendance_corrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_corrections_attendance_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "attendance_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    KioskDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attendance_events_attendance_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "attendance_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_breaks_SessionId",
                table: "attendance_breaks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_breaks_TenantId_EmployeeId_StartedAt",
                table: "attendance_breaks",
                columns: new[] { "TenantId", "EmployeeId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_attendance_breaks_open_per_session",
                table: "attendance_breaks",
                columns: new[] { "TenantId", "SessionId" },
                unique: true,
                filter: "\"EndedAt\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_corrections_SessionId",
                table: "attendance_corrections",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_corrections_TenantId_EmployeeId_CreatedAt",
                table: "attendance_corrections",
                columns: new[] { "TenantId", "EmployeeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_corrections_TenantId_SessionId",
                table: "attendance_corrections",
                columns: new[] { "TenantId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_credentials_EmployeeId",
                table: "attendance_credentials",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "UX_attendance_credentials_employee",
                table: "attendance_credentials",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_attendance_credentials_lookup",
                table: "attendance_credentials",
                columns: new[] { "TenantId", "LookupHash" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_events_SessionId",
                table: "attendance_events",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_events_TenantId_EmployeeId_OccurredAt",
                table: "attendance_events",
                columns: new[] { "TenantId", "EmployeeId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_events_TenantId_SessionId_OccurredAt",
                table: "attendance_events",
                columns: new[] { "TenantId", "SessionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sessions_EmployeeId",
                table: "attendance_sessions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sessions_KioskDeviceId",
                table: "attendance_sessions",
                column: "KioskDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sessions_LocationId",
                table: "attendance_sessions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sessions_TenantId_ClockInAt",
                table: "attendance_sessions",
                columns: new[] { "TenantId", "ClockInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sessions_TenantId_EmployeeId_ClockInAt",
                table: "attendance_sessions",
                columns: new[] { "TenantId", "EmployeeId", "ClockInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sessions_TenantId_Status",
                table: "attendance_sessions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_attendance_sessions_active_per_employee",
                table: "attendance_sessions",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "\"ClockOutAt\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_settings_TenantId",
                table: "attendance_settings",
                column: "TenantId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_devices_LocationId",
                table: "kiosk_devices",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_devices_TenantId_Name",
                table: "kiosk_devices",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_breaks");

            migrationBuilder.DropTable(
                name: "attendance_corrections");

            migrationBuilder.DropTable(
                name: "attendance_credentials");

            migrationBuilder.DropTable(
                name: "attendance_events");

            migrationBuilder.DropTable(
                name: "attendance_settings");

            migrationBuilder.DropTable(
                name: "attendance_sessions");

            migrationBuilder.DropTable(
                name: "kiosk_devices");
        }
    }
}
