using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReturnsAndReorders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConditionAtIssue",
                table: "employee_issued_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedReturnDate",
                table: "employee_issued_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverdueNotifiedAt",
                table: "employee_issued_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnDisposition",
                table: "employee_issued_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reorder_proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStockSnapshot = table.Column<int>(type: "integer", nullable: false),
                    TargetStockSnapshot = table.Column<int>(type: "integer", nullable: true),
                    SuggestedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ApprovedQuantity = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_reorder_proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reorder_proposals_issued_item_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "issued_item_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reorder_proposals_issued_item_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "issued_item_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_issued_items_TenantId_Status_ExpectedReturnDate",
                table: "employee_issued_items",
                columns: new[] { "TenantId", "Status", "ExpectedReturnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_reorder_proposals_TemplateId",
                table: "reorder_proposals",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_reorder_proposals_TenantId_Status",
                table: "reorder_proposals",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_reorder_proposals_TenantId_TemplateId",
                table: "reorder_proposals",
                columns: new[] { "TenantId", "TemplateId" },
                unique: true,
                filter: "\"VariantId\" IS NULL AND \"IsDeleted\" = false AND \"Status\" IN ('Proposed','Reviewed','Approved','Ordered')");

            migrationBuilder.CreateIndex(
                name: "IX_reorder_proposals_TenantId_TemplateId_VariantId",
                table: "reorder_proposals",
                columns: new[] { "TenantId", "TemplateId", "VariantId" },
                unique: true,
                filter: "\"VariantId\" IS NOT NULL AND \"IsDeleted\" = false AND \"Status\" IN ('Proposed','Reviewed','Approved','Ordered')");

            migrationBuilder.CreateIndex(
                name: "IX_reorder_proposals_VariantId",
                table: "reorder_proposals",
                column: "VariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reorder_proposals");

            migrationBuilder.DropIndex(
                name: "IX_employee_issued_items_TenantId_Status_ExpectedReturnDate",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "ConditionAtIssue",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "ExpectedReturnDate",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "OverdueNotifiedAt",
                table: "employee_issued_items");

            migrationBuilder.DropColumn(
                name: "ReturnDisposition",
                table: "employee_issued_items");
        }
    }
}
