using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InventoryControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NegativeStockRequiresReason",
                table: "issued_item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReorderQuantity",
                table: "issued_item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetStockLevel",
                table: "issued_item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StockSnapshot = table.Column<int>(type: "integer", nullable: false),
                    WarningSnapshot = table.Column<int>(type: "integer", nullable: true),
                    MinimumSnapshot = table.Column<int>(type: "integer", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_inventory_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_alerts_issued_item_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "issued_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_alerts_issued_item_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "issued_item_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_alerts_TemplateId",
                table: "inventory_alerts",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_alerts_TenantId_Status",
                table: "inventory_alerts",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_alerts_TenantId_TemplateId",
                table: "inventory_alerts",
                columns: new[] { "TenantId", "TemplateId" },
                unique: true,
                filter: "\"VariantId\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_alerts_TenantId_TemplateId_VariantId",
                table: "inventory_alerts",
                columns: new[] { "TenantId", "TemplateId", "VariantId" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_alerts_VariantId",
                table: "inventory_alerts",
                column: "VariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_alerts");

            migrationBuilder.DropColumn(
                name: "NegativeStockRequiresReason",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "ReorderQuantity",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "TargetStockLevel",
                table: "issued_item_templates");
        }
    }
}
