using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class FleetReadinessIdentityAndDamageLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedLicensePlate",
                table: "vehicles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedLicensePlate",
                table: "trailers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ScheduledEndDate",
                table: "maintenance_records",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "damage_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentFileName",
                table: "damage_reports",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TripId",
                table: "damage_reports",
                type: "uuid",
                nullable: true);

            // FL-M1: existing rows get their normalised identity BEFORE the unique indexes exist —
            // plate = upper-case letters and digits; VIN trimmed + upper-cased, empty = unknown (null).
            // A tenant that already holds the same physical plate/VIN twice (live rows) gets a clear
            // error naming the duplicates instead of an anonymous unique-index failure.
            migrationBuilder.Sql(
                """
                UPDATE vehicles SET "NormalizedLicensePlate" = upper(regexp_replace("LicensePlate", '[^A-Za-z0-9]', '', 'g'));
                UPDATE trailers SET "NormalizedLicensePlate" = upper(regexp_replace("LicensePlate", '[^A-Za-z0-9]', '', 'g'));
                UPDATE vehicles SET "Vin" = NULLIF(upper(btrim("Vin")), '') WHERE "Vin" IS NOT NULL;
                UPDATE trailers SET "Vin" = NULLIF(upper(btrim("Vin")), '') WHERE "Vin" IS NOT NULL;
                DO $$
                DECLARE dup text;
                BEGIN
                  SELECT string_agg(x, '; ') INTO dup FROM (
                    SELECT 'voertuigkenteken ' || "NormalizedLicensePlate" || ': ' || string_agg("InternalNumber", ', ') AS x
                      FROM vehicles WHERE NOT "IsDeleted" GROUP BY "TenantId", "NormalizedLicensePlate" HAVING count(*) > 1
                    UNION ALL
                    SELECT 'voertuig-VIN ' || "Vin" || ': ' || string_agg("InternalNumber", ', ')
                      FROM vehicles WHERE NOT "IsDeleted" AND "Vin" IS NOT NULL GROUP BY "TenantId", "Vin" HAVING count(*) > 1
                    UNION ALL
                    SELECT 'opleggerkenteken ' || "NormalizedLicensePlate" || ': ' || string_agg("InternalNumber", ', ')
                      FROM trailers WHERE NOT "IsDeleted" GROUP BY "TenantId", "NormalizedLicensePlate" HAVING count(*) > 1
                    UNION ALL
                    SELECT 'oplegger-VIN ' || "Vin" || ': ' || string_agg("InternalNumber", ', ')
                      FROM trailers WHERE NOT "IsDeleted" AND "Vin" IS NOT NULL GROUP BY "TenantId", "Vin" HAVING count(*) > 1
                  ) d;
                  IF dup IS NOT NULL THEN
                    RAISE EXCEPTION 'Dubbele wagenpark-identiteiten gevonden; los deze eerst op: %', dup;
                  END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_NormalizedLicensePlate",
                table: "vehicles",
                columns: new[] { "TenantId", "NormalizedLicensePlate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_Vin",
                table: "vehicles",
                columns: new[] { "TenantId", "Vin" },
                unique: true,
                filter: "\"Vin\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId_NormalizedLicensePlate",
                table: "trailers",
                columns: new[] { "TenantId", "NormalizedLicensePlate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId_Vin",
                table: "trailers",
                columns: new[] { "TenantId", "Vin" },
                unique: true,
                filter: "\"Vin\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vehicles_TenantId_NormalizedLicensePlate",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_TenantId_Vin",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_trailers_TenantId_NormalizedLicensePlate",
                table: "trailers");

            migrationBuilder.DropIndex(
                name: "IX_trailers_TenantId_Vin",
                table: "trailers");

            migrationBuilder.DropColumn(
                name: "NormalizedLicensePlate",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "NormalizedLicensePlate",
                table: "trailers");

            migrationBuilder.DropColumn(
                name: "ScheduledEndDate",
                table: "maintenance_records");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "damage_reports");

            migrationBuilder.DropColumn(
                name: "AttachmentFileName",
                table: "damage_reports");

            migrationBuilder.DropColumn(
                name: "TripId",
                table: "damage_reports");
        }
    }
}
