using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableIssuedTransportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SnapshotFrozenAt",
                table: "issued_transport_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SnapshotIsBackfilled",
                table: "issued_transport_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotJson",
                table: "issued_transport_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotVersion",
                table: "issued_transport_documents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SnapshotFrozenAt",
                table: "issued_transport_documents");

            migrationBuilder.DropColumn(
                name: "SnapshotIsBackfilled",
                table: "issued_transport_documents");

            migrationBuilder.DropColumn(
                name: "SnapshotJson",
                table: "issued_transport_documents");

            migrationBuilder.DropColumn(
                name: "SnapshotVersion",
                table: "issued_transport_documents");
        }
    }
}
