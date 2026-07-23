using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeaveBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaveTypeId",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leave_balance_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_leave_balance_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "leave_entitlement_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultAnnualEntitlementDays = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    PendingReservesBalance = table.Column<bool>(type: "boolean", nullable: false),
                    AllowNegativeBalance = table.Column<bool>(type: "boolean", nullable: false),
                    CarryOverEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxCarryOverDays = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_leave_entitlement_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employee_leave_balances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarYear = table.Column<int>(type: "integer", nullable: false),
                    BalanceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseEntitlementDays = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CarryOverDays = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_employee_leave_balances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_leave_balances_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_employee_leave_balances_leave_balance_types_BalanceTypeId",
                        column: x => x.BalanceTypeId,
                        principalTable: "leave_balance_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    DeductsFromBalance = table.Column<bool>(type: "boolean", nullable: false),
                    BalanceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AbsenceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsHalfDays = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresReason = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresAttachment = table.Column<bool>(type: "boolean", nullable: false),
                    VisibleInSelfService = table.Column<bool>(type: "boolean", nullable: false),
                    Colour = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_leave_types", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leave_types_leave_balance_types_BalanceTypeId",
                        column: x => x.BalanceTypeId,
                        principalTable: "leave_balance_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_balance_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeLeaveBalanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_leave_balance_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leave_balance_adjustments_employee_leave_balances_EmployeeL~",
                        column: x => x.EmployeeLeaveBalanceId,
                        principalTable: "employee_leave_balances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_absences_LeaveTypeId",
                table: "absences",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_absences_TenantId_LeaveTypeId",
                table: "absences",
                columns: new[] { "TenantId", "LeaveTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_leave_balances_BalanceTypeId",
                table: "employee_leave_balances",
                column: "BalanceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_leave_balances_EmployeeId",
                table: "employee_leave_balances",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_leave_balances_TenantId_EmployeeId_CalendarYear_Ba~",
                table: "employee_leave_balances",
                columns: new[] { "TenantId", "EmployeeId", "CalendarYear", "BalanceTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_balance_adjustments_EmployeeLeaveBalanceId",
                table: "leave_balance_adjustments",
                column: "EmployeeLeaveBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_leave_balance_adjustments_TenantId_EmployeeLeaveBalanceId",
                table: "leave_balance_adjustments",
                columns: new[] { "TenantId", "EmployeeLeaveBalanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_balance_types_TenantId_Code",
                table: "leave_balance_types",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_entitlement_settings_TenantId",
                table: "leave_entitlement_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_types_BalanceTypeId",
                table: "leave_types",
                column: "BalanceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_leave_types_TenantId_Code",
                table: "leave_types",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_absences_leave_types_LeaveTypeId",
                table: "absences",
                column: "LeaveTypeId",
                principalTable: "leave_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_absences_leave_types_LeaveTypeId",
                table: "absences");

            migrationBuilder.DropTable(
                name: "leave_balance_adjustments");

            migrationBuilder.DropTable(
                name: "leave_entitlement_settings");

            migrationBuilder.DropTable(
                name: "leave_types");

            migrationBuilder.DropTable(
                name: "employee_leave_balances");

            migrationBuilder.DropTable(
                name: "leave_balance_types");

            migrationBuilder.DropIndex(
                name: "IX_absences_LeaveTypeId",
                table: "absences");

            migrationBuilder.DropIndex(
                name: "IX_absences_TenantId_LeaveTypeId",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "LeaveTypeId",
                table: "absences");
        }
    }
}
