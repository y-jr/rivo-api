using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdemDeCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_order",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_purchase_requisition_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_requisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "procurement",
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_line_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_requisition_id",
                schema: "procurement",
                table: "purchase_order",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_status",
                schema: "procurement",
                table: "purchase_order",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_supplier_id",
                schema: "procurement",
                table: "purchase_order",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_line_purchase_order_id",
                schema: "procurement",
                table: "purchase_order_line",
                column: "purchase_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order",
                schema: "procurement");
        }
    }
}
