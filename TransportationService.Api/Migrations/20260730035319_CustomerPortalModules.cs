using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPortalModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_message_reads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_customer_message_reads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorIsStaff = table.Column<bool>(type: "boolean", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
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
                    table.PrimaryKey("PK_customer_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portal_announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ActiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActiveUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_portal_announcements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_message_reads_TenantId_UserId_CustomerId",
                table: "customer_message_reads",
                columns: new[] { "TenantId", "UserId", "CustomerId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"TransportOrderId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_message_reads_TenantId_UserId_CustomerId_Transport~",
                table: "customer_message_reads",
                columns: new[] { "TenantId", "UserId", "CustomerId", "TransportOrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"TransportOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_messages_TenantId_CustomerId_TransportOrderId_Crea~",
                table: "customer_messages",
                columns: new[] { "TenantId", "CustomerId", "TransportOrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_portal_announcements_TenantId_IsActive_ActiveFrom_ActiveUnt~",
                table: "portal_announcements",
                columns: new[] { "TenantId", "IsActive", "ActiveFrom", "ActiveUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_message_reads");

            migrationBuilder.DropTable(
                name: "customer_messages");

            migrationBuilder.DropTable(
                name: "portal_announcements");
        }
    }
}
