using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DocumentStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentPreference",
                table: "transport_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentStrategy",
                table: "customers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "tenant_document_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    MatchCrossBorder = table.Column<bool>(type: "boolean", nullable: true),
                    MatchAdr = table.Column<bool>(type: "boolean", nullable: true),
                    MatchActivityTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_tenant_document_rules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_document_rules_TenantId_Priority",
                table: "tenant_document_rules",
                columns: new[] { "TenantId", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_document_rules");

            migrationBuilder.DropColumn(
                name: "DocumentPreference",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "DocumentStrategy",
                table: "customers");
        }
    }
}
