using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class HrDossierReminderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DossierEscalationDays",
                table: "hr_reminder_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "DossierReminderDays",
                table: "hr_reminder_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<bool>(
                name: "DossierRemindersEnabled",
                table: "hr_reminder_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DossierEscalationDays",
                table: "hr_reminder_settings");

            migrationBuilder.DropColumn(
                name: "DossierReminderDays",
                table: "hr_reminder_settings");

            migrationBuilder.DropColumn(
                name: "DossierRemindersEnabled",
                table: "hr_reminder_settings");
        }
    }
}
