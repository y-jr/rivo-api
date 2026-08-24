using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "document_series",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    next_sequence = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number_type = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    number_series = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    number_sequence = table.Column<int>(type: "int", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_point_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    customer_tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    customer_address_detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    customer_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    customer_country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    net_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_invoice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice_line",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("pk_sales_invoice_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_invoice_line_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalSchema: "finance",
                        principalTable: "sales_invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_series_type_code",
                schema: "finance",
                table: "document_series",
                columns: new[] { "type", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_customer_id",
                schema: "finance",
                table: "sales_invoice",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_issued_on",
                schema: "finance",
                table: "sales_invoice",
                column: "issued_on");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_number_type_number_series_number_sequence",
                schema: "finance",
                table: "sales_invoice",
                columns: new[] { "number_type", "number_series", "number_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_line_sales_invoice_id_line_number",
                schema: "finance",
                table: "sales_invoice_line",
                columns: new[] { "sales_invoice_id", "line_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_series",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "sales_invoice_line",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "sales_invoice",
                schema: "finance");
        }
    }
}
