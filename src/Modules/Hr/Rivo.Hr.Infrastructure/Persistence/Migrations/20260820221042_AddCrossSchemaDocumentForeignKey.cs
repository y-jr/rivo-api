using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Chave estrangeira entre schemas: <c>hr.employee_document.document_id</c>
    /// para <c>documents.document(id)</c>.
    ///
    /// <para>
    /// Migração própria, e não parte da inicial, porque a tabela referida
    /// pertence a outro módulo: quando `hr` migra, `documents` já tem de estar
    /// migrado. Separar torna a dependência visível em vez de a esconder no
    /// meio da criação das tabelas de `hr`.
    /// </para>
    /// </summary>
    public partial class AddCrossSchemaDocumentForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrita em SQL porque o EF Core não a consegue exprimir: as duas
            // tabelas pertencem a DbContext distintos, e `hr` não pode
            // referenciar a entidade Document de `documents` (ADR-017).
            //
            // É esta restrição que devolve a integridade referencial que a
            // chave polimórfica do desenho inicial não dava (ADR-009), e é o
            // único caso de FK entre schemas permitido: para a chave primária
            // do contexto dono (ADR-010).
            //
            // `NO ACTION` é o `RESTRICT` do SQL Server: um documento ligado a
            // um colaborador não pode ser eliminado sem antes se desfazer a
            // ligação.
            migrationBuilder.Sql("""
                ALTER TABLE hr.employee_document
                ADD CONSTRAINT fk_employee_document_document
                FOREIGN KEY (document_id)
                REFERENCES documents.document (id)
                ON DELETE NO ACTION;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE hr.employee_document DROP CONSTRAINT IF EXISTS fk_employee_document_document;");
        }
    }
}
