using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DriverNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultCurrency",
                table: "tenant_settings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AddColumn<string>(
                name: "CompanyNumber",
                table: "tenant_settings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DateFormat",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "dd-MM-yyyy");

            migrationBuilder.AddColumn<string>(
                name: "DecimalSeparator",
                table: "tenant_settings",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: ",");

            migrationBuilder.AddColumn<string>(
                name: "DefaultDistanceUnit",
                table: "tenant_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "km");

            migrationBuilder.AddColumn<int>(
                name: "DefaultLoadingMinutes",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "DefaultUnloadingMinutes",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultVatRatePercent",
                table: "tenant_settings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 21m);

            migrationBuilder.AddColumn<string>(
                name: "DefaultWeightUnit",
                table: "tenant_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "kg");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceEmail",
                table: "tenant_settings",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoReference",
                table: "tenant_settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalCity",
                table: "tenant_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalCountryCode",
                table: "tenant_settings",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalHouseNumber",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalPostalCode",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalStreet",
                table: "tenant_settings",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "OrderNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermDays",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "TradingName",
                table: "tenant_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TripNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "TripNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyNumber",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DateFormat",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DecimalSeparator",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DefaultDistanceUnit",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DefaultLoadingMinutes",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DefaultUnloadingMinutes",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DefaultVatRatePercent",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "DefaultWeightUnit",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "InvoiceEmail",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "InvoiceNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "InvoiceNumberPrefix",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "LogoReference",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OperationalCity",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OperationalCountryCode",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OperationalHouseNumber",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OperationalPostalCode",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OperationalStreet",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OrderNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "OrderNumberPrefix",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "PaymentTermDays",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "TradingName",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "TripNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "TripNumberPrefix",
                table: "tenant_settings");

            migrationBuilder.AlterColumn<string>(
                name: "DriverNumberPrefix",
                table: "tenant_settings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultCurrency",
                table: "tenant_settings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldDefaultValue: "EUR");
        }
    }
}
