using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeAndPortalMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguageCode",
                table: "users",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "internal_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailRequested",
                table: "internal_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "internal_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAt",
                table: "internal_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "internal_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAcknowledgement",
                table: "internal_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisibleFrom",
                table: "internal_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                table: "internal_message_recipients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmailOutboxMessageId",
                table: "internal_message_recipients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "portal_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleNl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleFr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TitleEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BodyNl = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    BodyFr = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    BodyEn = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequiresAcknowledgement = table.Column<bool>(type: "boolean", nullable: false),
                    VisibleFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmailRequested = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_portal_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portal_message_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PortalMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portal_message_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_portal_message_receipts_portal_messages_PortalMessageId",
                        column: x => x.PortalMessageId,
                        principalTable: "portal_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portal_message_recipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PortalMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portal_message_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_portal_message_recipients_portal_messages_PortalMessageId",
                        column: x => x.PortalMessageId,
                        principalTable: "portal_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_internal_messages_TenantId_VisibleFrom",
                table: "internal_messages",
                columns: new[] { "TenantId", "VisibleFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_portal_message_receipts_PortalMessageId_UserId",
                table: "portal_message_receipts",
                columns: new[] { "PortalMessageId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_portal_message_receipts_TenantId_UserId_ReadAt",
                table: "portal_message_receipts",
                columns: new[] { "TenantId", "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_portal_message_recipients_PortalMessageId",
                table: "portal_message_recipients",
                column: "PortalMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_portal_message_recipients_TenantId_CustomerId_PortalMessage~",
                table: "portal_message_recipients",
                columns: new[] { "TenantId", "CustomerId", "PortalMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_portal_messages_TenantId_VisibleFrom_ExpiresAt",
                table: "portal_messages",
                columns: new[] { "TenantId", "VisibleFrom", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portal_message_receipts");

            migrationBuilder.DropTable(
                name: "portal_message_recipients");

            migrationBuilder.DropTable(
                name: "portal_messages");

            migrationBuilder.DropIndex(
                name: "IX_internal_messages_TenantId_VisibleFrom",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "PreferredLanguageCode",
                table: "users");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "EmailRequested",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "RequiresAcknowledgement",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "VisibleFrom",
                table: "internal_messages");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "internal_message_recipients");

            migrationBuilder.DropColumn(
                name: "EmailOutboxMessageId",
                table: "internal_message_recipients");
        }
    }
}
