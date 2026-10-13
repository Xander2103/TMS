using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeHrAndDriverCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CivilStatus",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DependentChildren",
                table: "employees",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DimonaNumber",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityCardNumber",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "driver_driver_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_driver_driver_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_driver_driver_categories_driver_categories_DriverCategoryId",
                        column: x => x.DriverCategoryId,
                        principalTable: "driver_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_driver_driver_categories_drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_emergency_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    MobilePhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_employee_emergency_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_emergency_contacts_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_driver_driver_categories_DriverCategoryId",
                table: "driver_driver_categories",
                column: "DriverCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_driver_driver_categories_DriverId",
                table: "driver_driver_categories",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_driver_driver_categories_TenantId_DriverId_DriverCategoryId",
                table: "driver_driver_categories",
                columns: new[] { "TenantId", "DriverId", "DriverCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_emergency_contacts_EmployeeId",
                table: "employee_emergency_contacts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_emergency_contacts_TenantId_EmployeeId",
                table: "employee_emergency_contacts",
                columns: new[] { "TenantId", "EmployeeId" });

            // Backfill: migrate the legacy single emergency-contact pair into a priority-1
            // structured row so no information is lost when the pair becomes a collection.
            migrationBuilder.Sql(
                """
                INSERT INTO employee_emergency_contacts
                    ("Id", "EmployeeId", "Name", "Phone", "Priority", "TenantId",
                     "CreatedAt", "UpdatedAt", "IsDeleted")
                SELECT gen_random_uuid(), e."Id",
                       e."EmergencyContactName", e."EmergencyContactPhone", 1, e."TenantId",
                       now(), now(), false
                FROM employees e
                WHERE e."EmergencyContactName" IS NOT NULL
                  AND btrim(e."EmergencyContactName") <> '';
                """);

            // Backfill: migrate the legacy single driver category into the multi-category join.
            migrationBuilder.Sql(
                """
                INSERT INTO driver_driver_categories
                    ("Id", "DriverId", "DriverCategoryId", "SortOrder", "TenantId",
                     "CreatedAt", "UpdatedAt", "IsDeleted")
                SELECT gen_random_uuid(), d."Id", d."DriverCategoryId", 0, d."TenantId",
                       now(), now(), false
                FROM drivers d
                WHERE d."DriverCategoryId" IS NOT NULL
                  AND d."IsDeleted" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_driver_categories");

            migrationBuilder.DropTable(
                name: "employee_emergency_contacts");

            migrationBuilder.DropColumn(
                name: "CivilStatus",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "DependentChildren",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "DimonaNumber",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "IdentityCardNumber",
                table: "employees");
        }
    }
}
