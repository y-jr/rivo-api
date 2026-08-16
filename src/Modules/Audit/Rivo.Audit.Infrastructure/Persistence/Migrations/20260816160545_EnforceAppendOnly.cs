using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Audit.Infrastructure.Persistence.Migrations;

/// <summary>
/// Impõe append-only na trilha de auditoria ao nível da base de dados — K9.
///
/// <para>
/// <c>AuditEvent</c> já era imutável em código: sem setters públicos, sem
/// métodos que alterem estado, e há um teste de arquitectura que o verifica por
/// reflexão (ADR-024). Mas nada impedia um <c>UPDATE</c> ou <c>DELETE</c>
/// directo na tabela, e BR-10 exige a garantia — não a boa intenção.
/// </para>
///
/// <para>
/// A imutabilidade em código só vale enquanto a aplicação for o único caminho
/// de escrita. Deixa de valer no dia em que alguém abrir um cliente de SQL
/// para "corrigir" um registo — que é precisamente o momento em que a
/// auditoria mais importa.
/// </para>
/// </summary>
public partial class EnforceAppendOnly : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Gatilho em vez de permissões de base de dados.
        //
        // Revogar UPDATE/DELETE ao utilizador aplicacional seria mais limpo,
        // mas exige um segundo utilizador — um para a aplicação, outro para
        // retenção — e a decisão sobre utilizadores de base de dados por
        // módulo continua em aberto. Um gatilho não depende dessa decisão e
        // aplica-se a *qualquer* ligação, incluindo a de um administrador
        // distraído.
        //
        // Contrapartida aceite: quem for dono da tabela pode remover o
        // gatilho. Protege contra o erro, não contra o adversário com
        // privilégios totais — para esse, a resposta é retenção fora da base
        // de dados, que é outra decisão.
        migrationBuilder.Sql("""
            create or replace function audit.reject_mutation()
            returns trigger
            language plpgsql
            as $$
            begin
                raise exception
                    'A trilha de auditoria e append-only (BR-10). % em audit.audit_event foi recusado.',
                    tg_op
                    using errcode = 'restrict_violation';
            end;
            $$;
            """);

        migrationBuilder.Sql("""
            create trigger audit_event_append_only
            before update or delete on audit.audit_event
            for each row execute function audit.reject_mutation();
            """);

        // `truncate` não dispara gatilhos de linha — precisa do seu próprio,
        // ao nível da instrução. Sem isto, um `truncate` apagava a trilha
        // inteira sem encontrar resistência.
        migrationBuilder.Sql("""
            create trigger audit_event_no_truncate
            before truncate on audit.audit_event
            for each statement execute function audit.reject_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop trigger if exists audit_event_no_truncate on audit.audit_event;");
        migrationBuilder.Sql("drop trigger if exists audit_event_append_only on audit.audit_event;");
        migrationBuilder.Sql("drop function if exists audit.reject_mutation();");
    }
}
