using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderPricingAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedPrice",
                table: "transport_orders",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PriceIsManual",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PriceOverrideReason",
                table: "transport_orders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomTypeName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_order_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_documents_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_pricing_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Informational = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_order_pricing_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_pricing_lines_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_service_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceOptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_order_service_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_service_lines_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_documents_TenantId_TransportOrderId",
                table: "order_documents",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_order_documents_TransportOrderId",
                table: "order_documents",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_pricing_lines_TenantId_TransportOrderId",
                table: "order_pricing_lines",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_order_pricing_lines_TransportOrderId",
                table: "order_pricing_lines",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_service_lines_TenantId_TransportOrderId",
                table: "order_service_lines",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_order_service_lines_TransportOrderId",
                table: "order_service_lines",
                column: "TransportOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_documents");

            migrationBuilder.DropTable(
                name: "order_pricing_lines");

            migrationBuilder.DropTable(
                name: "order_service_lines");

            migrationBuilder.DropColumn(
                name: "CalculatedPrice",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PriceIsManual",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PriceOverrideReason",
                table: "transport_orders");
        }
    }
}
