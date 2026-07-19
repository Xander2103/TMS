using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class RoleTemplateTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TemplateCode",
                table: "roles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "role_template_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_template_states", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_roles_TenantId_TemplateCode",
                table: "roles",
                columns: new[] { "TenantId", "TemplateCode" },
                unique: true,
                filter: "\"TemplateCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_role_template_states_TenantId",
                table: "role_template_states",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_template_states");

            migrationBuilder.DropIndex(
                name: "IX_roles_TenantId_TemplateCode",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "TemplateCode",
                table: "roles");
        }
    }
}
