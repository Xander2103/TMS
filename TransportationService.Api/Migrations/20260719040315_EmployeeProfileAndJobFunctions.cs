using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeProfileAndJobFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the old Country/PrimaryFunction columns are dropped at the END of this
            // migration, after their data has been copied into the new shape.
            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "employees",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmergencyContactPhone",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmergencyContactName",
                table: "employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bic",
                table: "employees",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContractTypeId",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "employees",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "employees",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobilePhone",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalRegisterNumber",
                table: "employees",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalityCode",
                table: "employees",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfBirth",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguageCode",
                table: "employees",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_job_functions",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobFunctionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_job_functions", x => new { x.EmployeeId, x.JobFunctionId });
                    table.ForeignKey(
                        name: "FK_employee_job_functions_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_employee_job_functions_job_functions_JobFunctionId",
                        column: x => x.JobFunctionId,
                        principalTable: "job_functions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employees_TenantId_DepartmentId",
                table: "employees",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_job_functions_JobFunctionId",
                table: "employee_job_functions",
                column: "JobFunctionId");

            // Address country: two-letter values are already ISO codes; known names are mapped.
            migrationBuilder.Sql("""
                UPDATE employees SET "CountryCode" = CASE
                    WHEN length(trim("Country")) = 2 THEN upper(trim("Country"))
                    WHEN "Country" IN ('België', 'Belgie', 'Belgium') THEN 'BE'
                    WHEN "Country" IN ('Nederland', 'Netherlands') THEN 'NL'
                    WHEN "Country" IN ('Duitsland', 'Germany') THEN 'DE'
                    WHEN "Country" IN ('Frankrijk', 'France') THEN 'FR'
                    WHEN "Country" IN ('Luxemburg', 'Luxembourg') THEN 'LU'
                    WHEN "Country" IN ('Polen', 'Poland') THEN 'PL'
                    ELSE NULL END;
                """);

            // Legacy single PrimaryFunction enum → multi job-function links, matched on the
            // tenant's seeded JobFunction lookup codes. Unmatched values are skipped.
            migrationBuilder.Sql("""
                INSERT INTO employee_job_functions ("EmployeeId", "JobFunctionId")
                SELECT e."Id", jf."Id"
                FROM employees e
                JOIN job_functions jf ON jf."TenantId" = e."TenantId" AND jf."IsDeleted" = false
                    AND jf."Code" = CASE e."PrimaryFunction"
                        WHEN 'DriverB' THEN 'CHAUF'
                        WHEN 'DriverC' THEN 'CHAUF'
                        WHEN 'DriverCE' THEN 'CHAUF'
                        WHEN 'CraneOperator' THEN 'KRAAN'
                        WHEN 'WarehouseWorker' THEN 'MAGM'
                        WHEN 'Planner' THEN 'PLAN'
                        WHEN 'Dispatcher' THEN 'DISP'
                        WHEN 'OfficeWorker' THEN 'ADMM'
                        WHEN 'Mechanic' THEN 'MONT'
                        ELSE NULL END
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.DropColumn(
                name: "Country",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "PrimaryFunction",
                table: "employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_job_functions");

            migrationBuilder.DropIndex(
                name: "IX_employees_TenantId_DepartmentId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "Bic",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "ContractTypeId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "MobilePhone",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "NationalRegisterNumber",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "NationalityCode",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "PlaceOfBirth",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "PreferredLanguageCode",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "employees");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmergencyContactPhone",
                table: "employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmergencyContactName",
                table: "employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryFunction",
                table: "employees",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
