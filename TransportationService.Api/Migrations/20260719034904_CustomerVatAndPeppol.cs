using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerVatAndPeppol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CustomerReferenceRequired",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultVatRatePercent",
                table: "customers",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceLanguageCode",
                table: "customers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeppolId",
                table: "customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeppolScheme",
                table: "customers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PurchaseOrderRequired",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SignedDeliveryNoteRequired",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VatCountryCode",
                table: "customers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatNotes",
                table: "customers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerReferenceRequired",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "DefaultVatRatePercent",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "InvoiceLanguageCode",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolId",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolScheme",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderRequired",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "SignedDeliveryNoteRequired",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "VatCountryCode",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "VatNotes",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "customers");
        }
    }
}
