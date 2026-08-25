using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadeEPlaneamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_centre_id",
                schema: "finance",
                table: "payment_request",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounting_period",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closed_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reopened_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reopen_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_period", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_centre",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    responsible_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_centre", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_forecast",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    month = table.Column<int>(type: "int", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    operational_costs = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    fixed_costs = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_forecast", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_account",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    parent_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_account", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_account_ledger_account_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "finance",
                        principalTable: "ledger_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cost_centre_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    annual_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    approved_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_cost_centre_cost_centre_id",
                        column: x => x.cost_centre_id,
                        principalSchema: "finance",
                        principalTable: "cost_centre",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entry",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journal_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    archival_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    type = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    total_debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    total_credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_voided = table.Column<bool>(type: "bit", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    void_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_entry_journal_journal_id",
                        column: x => x.journal_id,
                        principalSchema: "finance",
                        principalTable: "journal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_line",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    budget_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    month = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_line_budgets_budget_id",
                        column: x => x.budget_id,
                        principalSchema: "finance",
                        principalTable: "budget",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "journal_entry_line",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    record_number = table.Column<int>(type: "int", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    side = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    cost_centre_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_document_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    system_entry_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entry_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_entry_line_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalSchema: "finance",
                        principalTable: "journal_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_request_cost_centre_id_requested_on_status",
                schema: "finance",
                table: "payment_request",
                columns: new[] { "cost_centre_id", "requested_on", "status" },
                filter: "[cost_centre_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_accounting_period_fiscal_year_number",
                schema: "finance",
                table: "accounting_period",
                columns: new[] { "fiscal_year", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budget_cost_centre_id_fiscal_year",
                schema: "finance",
                table: "budget",
                columns: new[] { "cost_centre_id", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budget_line_budget_id_month",
                schema: "finance",
                table: "budget_line",
                columns: new[] { "budget_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_centre_code",
                schema: "finance",
                table: "cost_centre",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_centre_department_id",
                schema: "finance",
                table: "cost_centre",
                column: "department_id",
                filter: "[department_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cost_forecast_department_id_fiscal_year_month",
                schema: "finance",
                table: "cost_forecast",
                columns: new[] { "department_id", "fiscal_year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_code",
                schema: "finance",
                table: "journal",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_journal_id",
                schema: "finance",
                table: "journal_entry",
                column: "journal_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_transaction_date_journal_code_archival_number",
                schema: "finance",
                table: "journal_entry",
                columns: new[] { "transaction_date", "journal_code", "archival_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_transaction_date_period",
                schema: "finance",
                table: "journal_entry",
                columns: new[] { "transaction_date", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_line_account_id",
                schema: "finance",
                table: "journal_entry_line",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_line_cost_centre_id",
                schema: "finance",
                table: "journal_entry_line",
                column: "cost_centre_id",
                filter: "[cost_centre_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entry_line_journal_entry_id_record_number",
                schema: "finance",
                table: "journal_entry_line",
                columns: new[] { "journal_entry_id", "record_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_account_code",
                schema: "finance",
                table: "ledger_account",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_account_parent_id",
                schema: "finance",
                table: "ledger_account",
                column: "parent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_period",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "budget_line",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "cost_forecast",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "journal_entry_line",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ledger_account",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "budget",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "journal_entry",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "cost_centre",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "journal",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_payment_request_cost_centre_id_requested_on_status",
                schema: "finance",
                table: "payment_request");

            migrationBuilder.DropColumn(
                name: "cost_centre_id",
                schema: "finance",
                table: "payment_request");
        }
    }
}
