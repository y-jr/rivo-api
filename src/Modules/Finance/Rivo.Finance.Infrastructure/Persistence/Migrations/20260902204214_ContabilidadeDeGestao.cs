using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadeDeGestao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_rule",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    lines = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chart_of_accounts_version",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    jurisdiction = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    version = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_of_accounts_version", x => x.id);
                });

            // Versão-placeholder com o Guid vazio. `LedgerAccount.Open` ainda não
            // recebe nem atribui uma versão real (nada em `finance` liga contas a
            // versões hoje) — todas as contas, já abertas ou por abrir, têm
            // `ChartOfAccountsVersionId` = Guid.Empty por omissão do tipo. Sem
            // esta linha a FK abaixo rejeitava toda a tabela `ledger_account`
            // existente e todo o `POST /finance/ledger/accounts` seguinte.
            // Ver .claude/state/pending-decisions.md — a atribuição real de
            // versão a conta não está desenhada.
            migrationBuilder.InsertData(
                schema: "finance",
                table: "chart_of_accounts_version",
                columns: new[] { "id", "jurisdiction", "name", "version", "source", "effective_from", "effective_to", "is_active" },
                values: new object[]
                {
                    new Guid("00000000-0000-0000-0000-000000000000"),
                    "AO",
                    "Sem versão atribuída",
                    "0",
                    "Placeholder de migração — contas existentes sem versão real do plano de contas.",
                    new DateOnly(2026, 1, 1),
                    null,
                    true,
                });

            migrationBuilder.AddColumn<Guid>(
                name: "chart_of_accounts_version_id",
                schema: "finance",
                table: "ledger_account",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_ledger_account_chart_of_accounts_version_id",
                schema: "finance",
                table: "ledger_account",
                column: "chart_of_accounts_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounting_rule_code_effective_from",
                schema: "finance",
                table: "accounting_rule",
                columns: new[] { "code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chart_of_accounts_version_jurisdiction_name_version",
                schema: "finance",
                table: "chart_of_accounts_version",
                columns: new[] { "jurisdiction", "name", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ledger_account_chart_of_accounts_version_chart_of_accounts_version_id",
                schema: "finance",
                table: "ledger_account",
                column: "chart_of_accounts_version_id",
                principalSchema: "finance",
                principalTable: "chart_of_accounts_version",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ledger_account_chart_of_accounts_version_chart_of_accounts_version_id",
                schema: "finance",
                table: "ledger_account");

            migrationBuilder.DropTable(
                name: "accounting_rule",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "chart_of_accounts_version",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_ledger_account_chart_of_accounts_version_id",
                schema: "finance",
                table: "ledger_account");

            migrationBuilder.DropColumn(
                name: "chart_of_accounts_version_id",
                schema: "finance",
                table: "ledger_account");
        }
    }
}
