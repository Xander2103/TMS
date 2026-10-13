using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerContactTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactType",
                table: "customer_contacts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Algemeen");

            migrationBuilder.CreateIndex(
                name: "ix_customer_contacts_primary_per_type",
                table: "customer_contacts",
                columns: new[] { "TenantId", "CustomerId", "ContactType" },
                unique: true,
                filter: "\"IsPrimary\" = true AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customer_contacts_primary_per_type",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "ContactType",
                table: "customer_contacts");
        }
    }
}
