using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerCommunicationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_communication_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomTypeLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CcEmail = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    FallbackContactId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_customer_communication_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_communication_rules_customer_contacts_FallbackCont~",
                        column: x => x.FallbackContactId,
                        principalTable: "customer_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_customer_communication_rules_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_communication_rule_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_customer_communication_rule_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_communication_rule_contacts_customer_communication~",
                        column: x => x.RuleId,
                        principalTable: "customer_communication_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_communication_rule_contacts_customer_contacts_Cont~",
                        column: x => x.ContactId,
                        principalTable: "customer_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rule_contacts_ContactId",
                table: "customer_communication_rule_contacts",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rule_contacts_RuleId",
                table: "customer_communication_rule_contacts",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rule_contacts_TenantId_RuleId_Contac~",
                table: "customer_communication_rule_contacts",
                columns: new[] { "TenantId", "RuleId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rules_CustomerId",
                table: "customer_communication_rules",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rules_FallbackContactId",
                table: "customer_communication_rules",
                column: "FallbackContactId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rules_TenantId_CustomerId",
                table: "customer_communication_rules",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_communication_rules_TenantId_CustomerId_Type",
                table: "customer_communication_rules",
                columns: new[] { "TenantId", "CustomerId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_communication_rule_contacts");

            migrationBuilder.DropTable(
                name: "customer_communication_rules");
        }
    }
}
