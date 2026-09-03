using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadeDeGestao : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Idempotente por construção — cada passo verifica se o objecto já
        /// existe antes de o criar. Não é o desenho normal de uma migração
        /// EF Core (que assume o histórico como única fonte de verdade), mas
        /// a produção chegou a este ponto com <c>finance.accounting_rule</c>
        /// já criada fisicamente sem o registo correspondente em
        /// <c>__ef_migrations_history</c> — origem não confirmada (nenhum dos
        /// dois lados desta conversa tem acesso à VPS para investigar), e sem
        /// isso não há como corrigir o histórico directamente. Tornar a
        /// migração segura contra qualquer estado prévio dos objectos que ela
        /// cria resolve sem precisar desse acesso.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[finance].[accounting_rule]') IS NULL
                BEGIN
                    CREATE TABLE [finance].[accounting_rule] (
                        [id] uniqueidentifier NOT NULL,
                        [code] nvarchar(30) NOT NULL,
                        [name] nvarchar(200) NOT NULL,
                        [source_type] nvarchar(40) NOT NULL,
                        [source] nvarchar(200) NOT NULL,
                        [effective_from] date NOT NULL,
                        [effective_to] date NULL,
                        [is_active] bit NOT NULL,
                        [lines] nvarchar(max) NOT NULL,
                        [version] int NOT NULL,
                        CONSTRAINT [pk_accounting_rule] PRIMARY KEY ([id])
                    );
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[finance].[chart_of_accounts_version]') IS NULL
                BEGIN
                    CREATE TABLE [finance].[chart_of_accounts_version] (
                        [id] uniqueidentifier NOT NULL,
                        [jurisdiction] nvarchar(30) NOT NULL,
                        [name] nvarchar(60) NOT NULL,
                        [revision] nvarchar(30) NOT NULL,
                        [source] nvarchar(300) NOT NULL,
                        [effective_from] date NOT NULL,
                        [effective_to] date NULL,
                        [is_active] bit NOT NULL,
                        [version] int NOT NULL,
                        CONSTRAINT [pk_chart_of_accounts_version] PRIMARY KEY ([id])
                    );
                END
                """);

            // Versão-placeholder com o Guid vazio. `LedgerAccount.Open` ainda não
            // recebe nem atribui uma versão real (nada em `finance` liga contas a
            // versões hoje) — todas as contas, já abertas ou por abrir, têm
            // `ChartOfAccountsVersionId` = Guid.Empty por omissão do tipo. Sem
            // esta linha a FK abaixo rejeitava toda a tabela `ledger_account`
            // existente e todo o `POST /finance/ledger/accounts` seguinte.
            // Ver .claude/state/pending-decisions.md — a atribuição real de
            // versão a conta não está desenhada.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [finance].[chart_of_accounts_version] WHERE [id] = '00000000-0000-0000-0000-000000000000')
                BEGIN
                    INSERT INTO [finance].[chart_of_accounts_version]
                        ([id], [jurisdiction], [name], [revision], [source], [effective_from], [effective_to], [is_active], [version])
                    VALUES
                        ('00000000-0000-0000-0000-000000000000', N'AO', N'Sem versão atribuída', N'0',
                         N'Placeholder de migração — contas existentes sem versão real do plano de contas.',
                         '2026-01-01', NULL, 1, 0);
                END
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('finance.ledger_account', 'chart_of_accounts_version_id') IS NULL
                BEGIN
                    ALTER TABLE [finance].[ledger_account]
                        ADD [chart_of_accounts_version_id] uniqueidentifier NOT NULL
                        CONSTRAINT [df_ledger_account_chart_of_accounts_version_id] DEFAULT '00000000-0000-0000-0000-000000000000';
                    ALTER TABLE [finance].[ledger_account]
                        DROP CONSTRAINT [df_ledger_account_chart_of_accounts_version_id];
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_ledger_account_chart_of_accounts_version_id' AND object_id = OBJECT_ID('finance.ledger_account'))
                BEGIN
                    CREATE INDEX [ix_ledger_account_chart_of_accounts_version_id] ON [finance].[ledger_account] ([chart_of_accounts_version_id]);
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_accounting_rule_code_effective_from' AND object_id = OBJECT_ID('finance.accounting_rule'))
                BEGIN
                    CREATE UNIQUE INDEX [ix_accounting_rule_code_effective_from] ON [finance].[accounting_rule] ([code], [effective_from]);
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_chart_of_accounts_version_jurisdiction_name_revision' AND object_id = OBJECT_ID('finance.chart_of_accounts_version'))
                BEGIN
                    CREATE UNIQUE INDEX [ix_chart_of_accounts_version_jurisdiction_name_revision] ON [finance].[chart_of_accounts_version] ([jurisdiction], [name], [revision]);
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_ledger_account_chart_of_accounts_version_chart_of_accounts_version_id')
                BEGIN
                    ALTER TABLE [finance].[ledger_account] ADD CONSTRAINT [fk_ledger_account_chart_of_accounts_version_chart_of_accounts_version_id]
                        FOREIGN KEY ([chart_of_accounts_version_id]) REFERENCES [finance].[chart_of_accounts_version] ([id]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_ledger_account_chart_of_accounts_version_chart_of_accounts_version_id')
                    ALTER TABLE [finance].[ledger_account] DROP CONSTRAINT [fk_ledger_account_chart_of_accounts_version_chart_of_accounts_version_id];
                """);

            migrationBuilder.Sql("IF OBJECT_ID(N'[finance].[accounting_rule]') IS NOT NULL DROP TABLE [finance].[accounting_rule];");

            migrationBuilder.Sql("IF OBJECT_ID(N'[finance].[chart_of_accounts_version]') IS NOT NULL DROP TABLE [finance].[chart_of_accounts_version];");

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_ledger_account_chart_of_accounts_version_id' AND object_id = OBJECT_ID('finance.ledger_account'))
                    DROP INDEX [ix_ledger_account_chart_of_accounts_version_id] ON [finance].[ledger_account];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('finance.ledger_account', 'chart_of_accounts_version_id') IS NOT NULL
                    ALTER TABLE [finance].[ledger_account] DROP COLUMN [chart_of_accounts_version_id];
                """);
        }
    }
}
