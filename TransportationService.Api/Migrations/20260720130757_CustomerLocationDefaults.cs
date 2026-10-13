using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerLocationDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultLoadingLocation",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultUnloadingLocation",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_locations_default_loading_per_customer",
                table: "locations",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDefaultLoadingLocation\" = true AND \"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_locations_default_unloading_per_customer",
                table: "locations",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDefaultUnloadingLocation\" = true AND \"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_locations_default_loading_per_customer",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_default_unloading_per_customer",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "IsDefaultLoadingLocation",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "IsDefaultUnloadingLocation",
                table: "locations");
        }
    }
}
