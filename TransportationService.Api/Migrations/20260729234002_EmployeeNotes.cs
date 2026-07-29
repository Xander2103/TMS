using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsPinnedToDashboard = table.Column<bool>(type: "boolean", nullable: false),
                    PinnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PinnedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_employee_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_notes_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_notes_EmployeeId",
                table: "employee_notes",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_notes_TenantId_EmployeeId",
                table: "employee_notes",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_notes_TenantId_IsPinnedToDashboard",
                table: "employee_notes",
                columns: new[] { "TenantId", "IsPinnedToDashboard" },
                filter: "\"IsDeleted\" = false AND \"IsPinnedToDashboard\" = true");

            // One-time backfill: every non-blank legacy Employee.Notes value becomes the
            // employee's first note. The legacy column is left untouched (read-only from now
            // on) — this INSERT runs once, by nature of the migration, and is never repeated.
            // Backfilled notes start unpinned (PinnedAt/PinnedByUserId stay NULL, the column
            // default) — nothing was ever pinned before this feature existed.
            migrationBuilder.Sql("""
                INSERT INTO employee_notes
                    ("Id", "TenantId", "EmployeeId", "Text", "IsPinnedToDashboard",
                     "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId", "IsDeleted")
                SELECT gen_random_uuid(), e."TenantId", e."Id", trim(e."Notes"), false,
                       now(), now(), NULL, NULL, false
                FROM employees e
                WHERE e."Notes" IS NOT NULL AND btrim(e."Notes") <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_notes");
        }
    }
}
