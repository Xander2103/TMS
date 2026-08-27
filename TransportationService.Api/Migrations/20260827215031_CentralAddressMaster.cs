using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CentralAddressMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressExactKey",
                table: "locations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressStreetKey",
                table: "locations",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_location_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Alias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDefaultLoading = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultUnloading = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultBilling = table.Column<bool>(type: "boolean", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_customer_location_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_location_links_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_location_links_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_AddressExactKey",
                table: "locations",
                columns: new[] { "TenantId", "AddressExactKey" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_AddressStreetKey",
                table: "locations",
                columns: new[] { "TenantId", "AddressStreetKey" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_CustomerId",
                table: "customer_location_links",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_default_billing",
                table: "customer_location_links",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDefaultBilling\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_default_loading",
                table: "customer_location_links",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDefaultLoading\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_default_unloading",
                table: "customer_location_links",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDefaultUnloading\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_LocationId",
                table: "customer_location_links",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_TenantId",
                table: "customer_location_links",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_TenantId_CustomerId",
                table: "customer_location_links",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_TenantId_CustomerId_LocationId",
                table: "customer_location_links",
                columns: new[] { "TenantId", "CustomerId", "LocationId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_customer_location_links_TenantId_LocationId",
                table: "customer_location_links",
                columns: new[] { "TenantId", "LocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_location_links");

            migrationBuilder.DropIndex(
                name: "IX_locations_TenantId_AddressExactKey",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_TenantId_AddressStreetKey",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "AddressExactKey",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "AddressStreetKey",
                table: "locations");
        }
    }
}
