using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InventoryStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowNegativeStock",
                table: "issued_item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CurrentStock",
                table: "issued_item_templates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "issued_item_templates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LowStockThreshold",
                table: "issued_item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumStock",
                table: "issued_item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "issued_item_templates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StockTrackingEnabled",
                table: "issued_item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StorageLocation",
                table: "issued_item_templates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "issued_item_templates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VariantsEnabled",
                table: "issued_item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "issued_item_templates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "employee_issued_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantSnapshot",
                table: "employee_issued_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "issued_item_attribute_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AllowCustomValues = table.Column<bool>(type: "boolean", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_issued_item_attribute_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "issued_item_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrentStock = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_issued_item_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issued_item_variants_issued_item_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "issued_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issued_item_attribute_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_issued_item_attribute_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issued_item_attribute_options_issued_item_attribute_definit~",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "issued_item_attribute_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issued_item_template_attributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_issued_item_template_attributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issued_item_template_attributes_issued_item_attribute_defin~",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "issued_item_attribute_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_issued_item_template_attributes_issued_item_templates_Templ~",
                        column: x => x.TemplateId,
                        principalTable: "issued_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovementType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ResultingStock = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_movements_issued_item_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "issued_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_movements_issued_item_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "issued_item_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "issued_item_variant_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AttributeOptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_issued_item_variant_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issued_item_variant_values_issued_item_attribute_options_At~",
                        column: x => x.AttributeOptionId,
                        principalTable: "issued_item_attribute_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_issued_item_variant_values_issued_item_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "issued_item_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_issued_items_VariantId",
                table: "employee_issued_items",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_attribute_definitions_TenantId_IsActive",
                table: "issued_item_attribute_definitions",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_attribute_options_AttributeDefinitionId",
                table: "issued_item_attribute_options",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_attribute_options_TenantId_AttributeDefinitionId",
                table: "issued_item_attribute_options",
                columns: new[] { "TenantId", "AttributeDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_template_attributes_AttributeDefinitionId",
                table: "issued_item_template_attributes",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_template_attributes_TemplateId",
                table: "issued_item_template_attributes",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_template_attributes_TenantId_TemplateId_Attribu~",
                table: "issued_item_template_attributes",
                columns: new[] { "TenantId", "TemplateId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variant_values_AttributeOptionId",
                table: "issued_item_variant_values",
                column: "AttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variant_values_TenantId_VariantId_AttributeDefi~",
                table: "issued_item_variant_values",
                columns: new[] { "TenantId", "VariantId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variant_values_VariantId",
                table: "issued_item_variant_values",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variants_TemplateId",
                table: "issued_item_variants",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variants_TenantId_TemplateId",
                table: "issued_item_variants",
                columns: new[] { "TenantId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TemplateId",
                table: "stock_movements",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_TemplateId_Timestamp",
                table: "stock_movements",
                columns: new[] { "TenantId", "TemplateId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_VariantId",
                table: "stock_movements",
                columns: new[] { "TenantId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_VariantId",
                table: "stock_movements",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_employee_issued_items_issued_item_variants_VariantId",
                table: "employee_issued_items",
                column: "VariantId",
                principalTable: "issued_item_variants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_issued_items_issued_item_variants_VariantId",
                table: "employee_issued_items");

            migrationBuilder.DropTable(
                name: "issued_item_template_attributes");

            migrationBuilder.DropTable(
                name: "issued_item_variant_values");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "issued_item_attribute_options");

            migrationBuilder.DropTable(
                name: "issued_item_variants");

            migrationBuilder.DropTable(
                name: "issued_item_attribute_definitions");

            migrationBuilder.DropIndex(
                name: "IX_employee_issued_items_VariantId",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "AllowNegativeStock",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "CurrentStock",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "LowStockThreshold",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "MinimumStock",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "StockTrackingEnabled",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "StorageLocation",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "VariantsEnabled",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "issued_item_templates");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "VariantSnapshot",
                table: "employee_issued_items");
        }
    }
}
