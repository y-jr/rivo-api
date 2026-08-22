using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Impõe append-only na trilha de auditoria ao nível da base de dados — K9.
    ///
    /// <para>
    /// <c>AuditEvent</c> já era imutável em código: sem setters públicos, sem
    /// métodos que alterem estado, e há um teste de arquitectura que o verifica
    /// por reflexão (ADR-024). Mas nada impedia um <c>UPDATE</c> ou
    /// <c>DELETE</c> directo na tabela, e BR-10 exige a garantia — não a boa
    /// intenção.
    /// </para>
    ///
    /// <para>
    /// A imutabilidade em código só vale enquanto a aplicação for o único
    /// caminho de escrita. Deixa de valer no dia em que alguém abrir um cliente
    /// de SQL para "corrigir" um registo — que é precisamente o momento em que
    /// a auditoria mais importa.
    /// </para>
    /// </summary>
    public partial class EnforceAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Gatilho em vez de permissões de base de dados.
            //
            // Revogar UPDATE/DELETE ao utilizador aplicacional seria mais
            // limpo, mas exige um segundo utilizador — um para a aplicação,
            // outro para retenção — e a decisão sobre utilizadores de base de
            // dados por módulo continua em aberto. Um gatilho não depende dessa
            // decisão e aplica-se a *qualquer* ligação, incluindo a de um
            // administrador distraído.
            //
            // `INSTEAD OF` e não `AFTER`: recusa antes de escrever, em vez de
            // escrever e desfazer. O `THROW` aborta a transacção do chamador.
            //
            // Contrapartida aceite: quem for dono da tabela pode remover o
            // gatilho. Protege contra o erro, não contra o adversário com
            // privilégios totais — para esse, a resposta é retenção fora da
            // base de dados, que é outra decisão.
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER audit.audit_event_append_only
                ON audit.audit_event
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 50010, 'A trilha de auditoria e append-only (BR-10). UPDATE ou DELETE em audit.audit_event foi recusado.', 1;
                END;
                """);

            // `TRUNCATE TABLE` não dispara gatilhos em SQL Server — nem de
            // linha, nem de instrução. Ao contrário do PostgreSQL, onde havia
            // um gatilho `BEFORE TRUNCATE`, aqui não existe forma de o
            // interceptar (ADR-029).
            //
            // O que existe é uma regra do motor: **uma tabela referenciada por
            // uma chave estrangeira não pode ser truncada**, mesmo que a tabela
            // que a referencia esteja vazia. É esta tabela sentinela, e é para
            // isso que serve — nunca leva linhas.
            //
            // Um `DELETE FROM` continua a ser recusado pelo gatilho acima, por
            // isso as duas peças juntas cobrem os três caminhos de destruição.
            migrationBuilder.Sql("""
                CREATE TABLE audit.audit_event_truncate_guard
                (
                    audit_event_id uniqueidentifier NOT NULL,
                    CONSTRAINT pk_audit_event_truncate_guard PRIMARY KEY (audit_event_id),
                    CONSTRAINT fk_audit_event_truncate_guard_audit_event
                        FOREIGN KEY (audit_event_id) REFERENCES audit.audit_event (id)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.audit_event_truncate_guard;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit.audit_event_append_only;");
        }
    }
}
