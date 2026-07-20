using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountFlowsAndRoleMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "job_function_role_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobFunctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_function_role_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_function_role_mappings_job_functions_JobFunctionId",
                        column: x => x.JobFunctionId,
                        principalTable: "job_functions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_function_role_mappings_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_security_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_security_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_security_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_function_role_mappings_JobFunctionId",
                table: "job_function_role_mappings",
                column: "JobFunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_job_function_role_mappings_RoleId",
                table: "job_function_role_mappings",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_job_function_role_mappings_TenantId_JobFunctionId_RoleId",
                table: "job_function_role_mappings",
                columns: new[] { "TenantId", "JobFunctionId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_security_tokens_TokenHash",
                table: "user_security_tokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_user_security_tokens_UserId_Kind",
                table: "user_security_tokens",
                columns: new[] { "UserId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_function_role_mappings");

            migrationBuilder.DropTable(
                name: "user_security_tokens");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "users");
        }
    }
}
