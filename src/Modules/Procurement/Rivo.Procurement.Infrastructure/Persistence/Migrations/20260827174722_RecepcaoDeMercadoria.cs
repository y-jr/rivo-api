using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecepcaoDeMercadoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "goods_receipt",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    received_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    delivery_note = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_purchase_order_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity_received = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_purchase_order_line_purchase_order_line_id",
                        column: x => x.purchase_order_line_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order_line",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_receipts_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalSchema: "procurement",
                        principalTable: "goods_receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_purchase_order_id",
                schema: "procurement",
                table: "goods_receipt",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_received_by_employee_id",
                schema: "procurement",
                table: "goods_receipt",
                column: "received_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_status",
                schema: "procurement",
                table: "goods_receipt",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_line_goods_receipt_id",
                schema: "procurement",
                table: "goods_receipt_line",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_line_purchase_order_line_id",
                schema: "procurement",
                table: "goods_receipt_line",
                column: "purchase_order_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_receipt_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "goods_receipt",
                schema: "procurement");
        }
    }
}
