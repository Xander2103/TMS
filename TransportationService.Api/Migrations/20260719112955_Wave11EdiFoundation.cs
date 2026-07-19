using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class Wave11EdiFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trading_partners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalCustomerIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MappingProfile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_trading_partners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trading_partners_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "edi_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TradingPartnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorDetail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ValidationErrorsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResultEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResultEntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_edi_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edi_messages_trading_partners_TradingPartnerId",
                        column: x => x.TradingPartnerId,
                        principalTable: "trading_partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edi_partner_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingPartnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalLocationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_edi_partner_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edi_partner_locations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_edi_partner_locations_trading_partners_TradingPartnerId",
                        column: x => x.TradingPartnerId,
                        principalTable: "trading_partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_edi_messages_TenantId_Status",
                table: "edi_messages",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_edi_messages_TenantId_TradingPartnerId_ExternalReference",
                table: "edi_messages",
                columns: new[] { "TenantId", "TradingPartnerId", "ExternalReference" });

            migrationBuilder.CreateIndex(
                name: "IX_edi_messages_TradingPartnerId",
                table: "edi_messages",
                column: "TradingPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_edi_partner_locations_LocationId",
                table: "edi_partner_locations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_edi_partner_locations_TradingPartnerId_ExternalLocationCode",
                table: "edi_partner_locations",
                columns: new[] { "TradingPartnerId", "ExternalLocationCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trading_partners_CustomerId",
                table: "trading_partners",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_trading_partners_TenantId_Code",
                table: "trading_partners",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edi_messages");

            migrationBuilder.DropTable(
                name: "edi_partner_locations");

            migrationBuilder.DropTable(
                name: "trading_partners");
        }
    }
}
