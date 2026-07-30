using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_templates_TenantId_Kind_Channel_Language",
                table: "message_templates");

            migrationBuilder.AddColumn<string>(
                name: "BodyHtml",
                table: "message_templates",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "message_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_notification_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: true),
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
                    table.PrimaryKey("PK_customer_notification_overrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    InAppEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SmsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecipientsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AllowCustomerOverride = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_notification_rules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_TenantId_CustomerId_Kind_Channel_Language",
                table: "message_templates",
                columns: new[] { "TenantId", "CustomerId", "Kind", "Channel", "Language" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"CustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_TenantId_Kind_Channel_Language",
                table: "message_templates",
                columns: new[] { "TenantId", "Kind", "Channel", "Language" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"CustomerId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_notification_overrides_TenantId_CustomerId_EventKey",
                table: "customer_notification_overrides",
                columns: new[] { "TenantId", "CustomerId", "EventKey" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_notification_rules_TenantId_EventKey",
                table: "notification_rules",
                columns: new[] { "TenantId", "EventKey" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_notification_overrides");

            migrationBuilder.DropTable(
                name: "notification_rules");

            migrationBuilder.DropIndex(
                name: "IX_message_templates_TenantId_CustomerId_Kind_Channel_Language",
                table: "message_templates");

            migrationBuilder.DropIndex(
                name: "IX_message_templates_TenantId_Kind_Channel_Language",
                table: "message_templates");

            migrationBuilder.DropColumn(
                name: "BodyHtml",
                table: "message_templates");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "message_templates");

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_TenantId_Kind_Channel_Language",
                table: "message_templates",
                columns: new[] { "TenantId", "Kind", "Channel", "Language" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
