using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class GlobalCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The table changes meaning: per-tenant lookup rows become one global ISO list.
            // Old rows would carry duplicate codes across tenants (breaking the new unique
            // index) and bogus IsEuMember/Alpha3 values, so they are purged here; the
            // CountrySeeder repopulates the full ISO list at application startup.
            migrationBuilder.Sql("DELETE FROM countries;");

            migrationBuilder.DropIndex(
                name: "IX_countries_TenantId",
                table: "countries");

            migrationBuilder.DropIndex(
                name: "IX_countries_TenantId_Code",
                table: "countries");

            migrationBuilder.DropIndex(
                name: "IX_countries_TenantId_IsActive",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "countries");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "countries",
                newName: "IsEuMember");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "countries",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "Alpha3",
                table: "countries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_countries_Code",
                table: "countries",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_countries_Code",
                table: "countries");

            migrationBuilder.DropColumn(
                name: "Alpha3",
                table: "countries");

            migrationBuilder.RenameColumn(
                name: "IsEuMember",
                table: "countries",
                newName: "IsDeleted");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "countries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2)",
                oldMaxLength: 2);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "countries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "countries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "countries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "countries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "countries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "countries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "countries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "countries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_countries_TenantId",
                table: "countries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_countries_TenantId_Code",
                table: "countries",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_countries_TenantId_IsActive",
                table: "countries",
                columns: new[] { "TenantId", "IsActive" });
        }
    }
}
