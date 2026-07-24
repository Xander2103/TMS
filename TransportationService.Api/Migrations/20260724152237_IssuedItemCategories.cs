using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class IssuedItemCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "issued_item_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "issued_item_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issued_item_categories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_templates_CategoryId",
                table: "issued_item_templates",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_templates_TenantId_CategoryId",
                table: "issued_item_templates",
                columns: new[] { "TenantId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_categories_TenantId",
                table: "issued_item_categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_categories_TenantId_Code",
                table: "issued_item_categories",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_categories_TenantId_IsActive",
                table: "issued_item_categories",
                columns: new[] { "TenantId", "IsActive" });

            // Backfill: promote each tenant's distinct free-text template categories to master
            // data and link the templates, so existing data keeps working without manual work.
            migrationBuilder.Sql("""
                INSERT INTO issued_item_categories
                    ("Id", "TenantId", "CreatedAt", "UpdatedAt", "IsDeleted", "Code", "Name", "IsActive", "SortOrder")
                SELECT gen_random_uuid(), s."TenantId", now(), now(), false,
                       left(regexp_replace(upper(trim(s."Category")), '[^A-Z0-9]+', '_', 'g'), 50),
                       left(trim(s."Category"), 150), true, 0
                FROM (SELECT DISTINCT "TenantId", "Category"
                      FROM issued_item_templates
                      WHERE "IsDeleted" = false AND trim(coalesce("Category", '')) <> '') s
                WHERE NOT EXISTS (
                    SELECT 1 FROM issued_item_categories c
                    WHERE c."TenantId" = s."TenantId" AND c."IsDeleted" = false
                      AND c."Code" = left(regexp_replace(upper(trim(s."Category")), '[^A-Z0-9]+', '_', 'g'), 50));

                UPDATE issued_item_templates t
                SET "CategoryId" = c."Id"
                FROM issued_item_categories c
                WHERE c."TenantId" = t."TenantId" AND c."IsDeleted" = false
                  AND c."Name" = trim(t."Category")
                  AND t."CategoryId" IS NULL AND t."IsDeleted" = false;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_issued_item_templates_issued_item_categories_CategoryId",
                table: "issued_item_templates",
                column: "CategoryId",
                principalTable: "issued_item_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_issued_item_templates_issued_item_categories_CategoryId",
                table: "issued_item_templates");

            migrationBuilder.DropTable(
                name: "issued_item_categories");

            migrationBuilder.DropIndex(
                name: "IX_issued_item_templates_CategoryId",
                table: "issued_item_templates");

            migrationBuilder.DropIndex(
                name: "IX_issued_item_templates_TenantId_CategoryId",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "issued_item_templates");
        }
    }
}
