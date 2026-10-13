using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerContactsAndAddressRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultBillingLocation",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "customer_contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "customer_contacts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "customer_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MobilePhone",
                table: "customer_contacts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "customer_contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguageCode",
                table: "customer_contacts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contact_departments",
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
                    table.PrimaryKey("PK_contact_departments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_locations_default_billing_per_customer",
                table: "locations",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "\"IsDefaultBillingLocation\" = true AND \"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_customer_contacts_DepartmentId",
                table: "customer_contacts",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_contact_departments_TenantId",
                table: "contact_departments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_contact_departments_TenantId_Code",
                table: "contact_departments",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_contact_departments_TenantId_IsActive",
                table: "contact_departments",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_customer_contacts_contact_departments_DepartmentId",
                table: "customer_contacts",
                column: "DepartmentId",
                principalTable: "contact_departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_contacts_contact_departments_DepartmentId",
                table: "customer_contacts");

            migrationBuilder.DropTable(
                name: "contact_departments");

            migrationBuilder.DropIndex(
                name: "IX_locations_default_billing_per_customer",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_customer_contacts_DepartmentId",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "IsDefaultBillingLocation",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "MobilePhone",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "PreferredLanguageCode",
                table: "customer_contacts");
        }
    }
}
