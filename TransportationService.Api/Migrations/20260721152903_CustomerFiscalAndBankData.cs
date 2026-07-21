using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerFiscalAndBankData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bic",
                table: "customers",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyNumber",
                table: "customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "customers",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "Bic",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "CompanyNumber",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "customers");
        }
    }
}
