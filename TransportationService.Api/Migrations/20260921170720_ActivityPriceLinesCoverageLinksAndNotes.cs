using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ActivityPriceLinesCoverageLinksAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FreeConfirmed",
                table: "dossier_activity_pricings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "dossier_activity_price_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierActivityPricingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SalesCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_dossier_activity_price_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dossier_activity_price_lines_dossier_activity_pricings_Doss~",
                        column: x => x.DossierActivityPricingId,
                        principalTable: "dossier_activity_pricings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dossier_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierActivityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
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
                    table.PrimaryKey("PK_dossier_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dossier_notes_dossier_activities_DossierActivityId",
                        column: x => x.DossierActivityId,
                        principalTable: "dossier_activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dossier_notes_transport_dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "transport_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_price_line_cargo_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CargoItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_price_line_cargo_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_price_line_cargo_links_order_cargo_items_CargoItemId",
                        column: x => x.CargoItemId,
                        principalTable: "order_cargo_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_price_line_cargo_links_transport_orders_TransportOrde~",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activity_price_lines_DossierActivityPricingId",
                table: "dossier_activity_price_lines",
                column: "DossierActivityPricingId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activity_price_lines_TenantId_DossierActivityPricin~",
                table: "dossier_activity_price_lines",
                columns: new[] { "TenantId", "DossierActivityPricingId" });

            migrationBuilder.CreateIndex(
                name: "IX_dossier_notes_DossierActivityId",
                table: "dossier_notes",
                column: "DossierActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_notes_DossierId",
                table: "dossier_notes",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_notes_TenantId_DossierActivityId",
                table: "dossier_notes",
                columns: new[] { "TenantId", "DossierActivityId" },
                filter: "\"DossierActivityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_notes_TenantId_DossierId_CreatedAt",
                table: "dossier_notes",
                columns: new[] { "TenantId", "DossierId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_price_line_cargo_links_CargoItemId",
                table: "order_price_line_cargo_links",
                column: "CargoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_order_price_line_cargo_links_TenantId_TransportOrderId_Line~",
                table: "order_price_line_cargo_links",
                columns: new[] { "TenantId", "TransportOrderId", "LineKey", "CargoItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_price_line_cargo_links_TransportOrderId",
                table: "order_price_line_cargo_links",
                column: "TransportOrderId");

            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") != true)
            {
                return;
            }

            // D7 data step: every non-blank legacy free-text note becomes the FIRST note of its
            // dossier / activity (author = the row's creator, time = the row's last change). The
            // legacy columns are only READ here — never cleared, never written again by the note
            // endpoints. Idempotent: a row whose text already exists as a note at that level (also
            // a since-deleted one) is skipped, so re-running this statement copies nothing twice.
            migrationBuilder.Sql("""
                INSERT INTO dossier_notes
                    ("Id", "TenantId", "DossierId", "DossierActivityId", "Text",
                     "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId", "IsDeleted")
                SELECT gen_random_uuid(), d."TenantId", d."Id", NULL, btrim(d."Notes"),
                       GREATEST(d."UpdatedAt", d."CreatedAt"), GREATEST(d."UpdatedAt", d."CreatedAt"),
                       d."CreatedByUserId", d."CreatedByUserId", false
                FROM transport_dossiers d
                WHERE d."IsDeleted" = false
                  AND d."Notes" IS NOT NULL AND btrim(d."Notes") <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM dossier_notes n
                      WHERE n."DossierId" = d."Id" AND n."DossierActivityId" IS NULL
                        AND n."Text" = btrim(d."Notes"));
                """);

            migrationBuilder.Sql("""
                INSERT INTO dossier_notes
                    ("Id", "TenantId", "DossierId", "DossierActivityId", "Text",
                     "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId", "IsDeleted")
                SELECT gen_random_uuid(), a."TenantId", a."DossierId", a."Id", btrim(a."Notes"),
                       GREATEST(a."UpdatedAt", a."CreatedAt"), GREATEST(a."UpdatedAt", a."CreatedAt"),
                       a."CreatedByUserId", a."CreatedByUserId", false
                FROM dossier_activities a
                JOIN transport_dossiers d ON d."Id" = a."DossierId" AND d."IsDeleted" = false
                WHERE a."IsDeleted" = false
                  AND a."Notes" IS NOT NULL AND btrim(a."Notes") <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM dossier_notes n
                      WHERE n."DossierActivityId" = a."Id"
                        AND n."Text" = btrim(a."Notes"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dossier_activity_price_lines");

            migrationBuilder.DropTable(
                name: "dossier_notes");

            migrationBuilder.DropTable(
                name: "order_price_line_cargo_links");

            migrationBuilder.DropColumn(
                name: "FreeConfirmed",
                table: "dossier_activity_pricings");
        }
    }
}
