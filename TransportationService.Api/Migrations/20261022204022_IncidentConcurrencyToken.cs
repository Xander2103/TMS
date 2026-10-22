using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// Performance/concurrency final remediation 2026-10-21, concurrency matrix #10 (PERF-M5): two
    /// simultaneous edits of one incident were last-write-wins. <c>incidents.Version</c> is an EF
    /// concurrency token; additive — every existing row gets its own fresh token (PostgreSQL
    /// <c>gen_random_uuid()</c>, built in since PG 13) so no two rows share a value from day one.
    /// </summary>
    public partial class IncidentConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "incidents",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "incidents");
        }
    }
}
