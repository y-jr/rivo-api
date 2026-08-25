using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// O extracto de conta — a linha por movimento que faltava para o saldo ser
    /// reconciliável.
    ///
    /// <para>
    /// Traz três coisas além da tabela: o gatilho que impede alterar o passado,
    /// a sentinela que impede truncá-lo, e um movimento de abertura para as
    /// contas que já existiam.
    /// </para>
    /// </summary>
    public partial class ExtractoDeConta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_movement",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_movement", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_movement_bank_account_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "finance",
                        principalTable: "bank_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_movement_bank_account_id_occurred_at",
                schema: "finance",
                table: "bank_movement",
                columns: new[] { "bank_account_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_movement_source_type_source_id",
                schema: "finance",
                table: "bank_movement",
                columns: new[] { "source_type", "source_id" },
                filter: "[source_id] IS NOT NULL");

            // **Append-only, imposto pelo motor.** Mesma peça que a trilha de
            // auditoria usa desde o K9, e pela mesma razão: um extracto que se
            // pode editar não serve para reconciliar nada. A diferença entre o
            // Rivo e o banco tem de ser explicável por movimentos que ninguém
            // reescreveu.
            //
            // `INSTEAD OF` recusa antes de escrever; o `THROW` aborta a
            // transacção de quem tentou.
            //
            // Corrigir um movimento errado faz-se como na contabilidade — com
            // outro movimento em sentido contrário, que também fica no
            // extracto. É isso que se quer.
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER finance.bank_movement_append_only
                ON finance.bank_movement
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 50020, 'O extracto de conta e append-only. UPDATE ou DELETE em finance.bank_movement foi recusado - corrija com um movimento em sentido contrario.', 1;
                END;
                """);

            // `TRUNCATE TABLE` não dispara gatilhos em SQL Server (ADR-029). O
            // que o impede é uma regra do motor: uma tabela referenciada por
            // chave estrangeira não pode ser truncada, mesmo que quem a
            // referencia esteja vazio. Esta tabela nunca leva linhas — existe
            // só para ser essa referência.
            migrationBuilder.Sql("""
                CREATE TABLE finance.bank_movement_truncate_guard
                (
                    bank_movement_id uniqueidentifier NOT NULL,
                    CONSTRAINT pk_bank_movement_truncate_guard PRIMARY KEY (bank_movement_id),
                    CONSTRAINT fk_bank_movement_truncate_guard_bank_movement
                        FOREIGN KEY (bank_movement_id) REFERENCES finance.bank_movement (id)
                );
                """);

            // **Movimento de abertura para as contas que já existiam.**
            //
            // Sem isto, uma conta com saldo e sem histórico daria um extracto
            // que fecha a zero contra um saldo que não é zero — e pareceria um
            // defeito do extracto quando é apenas o que aconteceu: os
            // movimentos anteriores a esta migração nunca foram registados.
            //
            // Uma linha explícita a dizê-lo é mais honesta do que uma
            // divergência por explicar. `NEWID()` e não um Guid v7: é uma linha
            // por conta, escrita de uma vez, e a ordenação do extracto começa
            // nela.
            migrationBuilder.Sql("""
                INSERT INTO finance.bank_movement
                    (id, bank_account_id, occurred_at, direction, amount, balance_after,
                     description, source_type, source_id)
                SELECT
                    NEWID(),
                    a.id,
                    SYSDATETIMEOFFSET(),
                    CASE WHEN a.balance >= 0 THEN 'Credit' ELSE 'Debit' END,
                    ABS(a.balance),
                    a.balance,
                    N'Saldo de abertura do extracto. Os movimentos anteriores a esta data nao foram registados.',
                    NULL,
                    NULL
                FROM finance.bank_account AS a
                WHERE a.balance <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A sentinela e o gatilho primeiro: com eles de pé, a tabela não
            // se larga.
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.bank_movement_truncate_guard;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS finance.bank_movement_append_only;");

            migrationBuilder.DropTable(
                name: "bank_movement",
                schema: "finance");
        }
    }
}
