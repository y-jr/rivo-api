using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContasAPagarETesouraria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_account",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    bank = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    iban = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_request",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    payee_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payee_tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_on = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    executed_from_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    executed_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    executed_method = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    execution_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    supplier_tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    net_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_account_iban",
                schema: "finance",
                table: "bank_account",
                column: "iban",
                unique: true,
                filter: "[iban] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_request_approval_request_id",
                schema: "finance",
                table: "payment_request",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_request_executed_by_employee_id",
                schema: "finance",
                table: "payment_request",
                column: "executed_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_request_purchase_invoice_id_status",
                schema: "finance",
                table: "payment_request",
                columns: new[] { "purchase_invoice_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_due_on",
                schema: "finance",
                table: "purchase_invoice",
                column: "due_on");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_invoice_supplier_number",
                schema: "finance",
                table: "purchase_invoice",
                columns: new[] { "supplier_tax_id", "supplier_invoice_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_account",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_request",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "purchase_invoice",
                schema: "finance");
        }
    }
}
