using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class VariantValueUniqueIndexFiltered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_issued_item_variant_values_TenantId_VariantId_AttributeDefi~",
                table: "issued_item_variant_values");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variant_values_TenantId_VariantId_AttributeDefi~",
                table: "issued_item_variant_values",
                columns: new[] { "TenantId", "VariantId", "AttributeDefinitionId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_issued_item_variant_values_TenantId_VariantId_AttributeDefi~",
                table: "issued_item_variant_values");

            migrationBuilder.CreateIndex(
                name: "IX_issued_item_variant_values_TenantId_VariantId_AttributeDefi~",
                table: "issued_item_variant_values",
                columns: new[] { "TenantId", "VariantId", "AttributeDefinitionId" },
                unique: true);
        }
    }
}
