using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotaDeCreditoERecibo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_note",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number_type = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    number_series = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    number_sequence = table.Column<int>(type: "int", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    corrected_invoice_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_point_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    customer_tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    customer_address_detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    customer_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    customer_country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    customer_is_final_consumer = table.Column<bool>(type: "bit", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    fiscal_notice = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    net_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipt",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number_type = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    number_series = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    number_sequence = table.Column<int>(type: "int", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    customer_tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    customer_address_detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    customer_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    customer_country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    customer_is_final_consumer = table.Column<bool>(type: "bit", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    fiscal_notice = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_line",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    line_number = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    tax_percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_note_line_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalSchema: "finance",
                        principalTable: "credit_note",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_line",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    line_number = table.Column<int>(type: "int", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipt_line_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "finance",
                        principalTable: "receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_number_type_number_series_number_sequence",
                schema: "finance",
                table: "credit_note",
                columns: new[] { "number_type", "number_series", "number_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_sales_invoice_id",
                schema: "finance",
                table: "credit_note",
                column: "sales_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_line_credit_note_id_line_number",
                schema: "finance",
                table: "credit_note_line",
                columns: new[] { "credit_note_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_customer_id",
                schema: "finance",
                table: "receipt",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_number_type_number_series_number_sequence",
                schema: "finance",
                table: "receipt",
                columns: new[] { "number_type", "number_series", "number_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_received_on",
                schema: "finance",
                table: "receipt",
                column: "received_on");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_line_receipt_id_line_number",
                schema: "finance",
                table: "receipt_line",
                columns: new[] { "receipt_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_line_sales_invoice_id",
                schema: "finance",
                table: "receipt_line",
                column: "sales_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_note_line",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipt_line",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "credit_note",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipt",
                schema: "finance");
        }
    }
}
