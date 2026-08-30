using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Payroll.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Chave estrangeira entre schemas: <c>payroll.payroll_item_document.document_id</c>
    /// para <c>documents.document(id)</c>.
    ///
    /// <para>
    /// Migração própria, e não parte da anterior, porque a tabela referida
    /// pertence a outro módulo: quando `payroll` migra, `documents` já tem de
    /// estar migrado (`Program.cs` migra `documents` antes de `payroll`).
    /// Separar torna a dependência visível em vez de a esconder no meio da
    /// criação da tabela — mesmo desenho de `hr` (`AddCrossSchemaDocumentForeignKey`).
    /// </para>
    /// </summary>
    public partial class AddCrossSchemaDocumentForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrita em SQL porque o EF Core não a consegue exprimir: as duas
            // tabelas pertencem a DbContext distintos, e `payroll` não pode
            // referenciar a entidade Document de `documents` (ADR-017).
            //
            // É esta restrição que dá a integridade referencial que a chave
            // polimórfica evitaria (ADR-009), e é o único caso de FK entre
            // schemas permitido: para a chave primária do contexto dono
            // (ADR-010).
            //
            // `NO ACTION` é o `RESTRICT` do SQL Server: um documento ligado a
            // um item de folha não pode ser eliminado sem antes se desfazer a
            // ligação.
            migrationBuilder.Sql("""
                ALTER TABLE payroll.payroll_item_document
                ADD CONSTRAINT fk_payroll_item_document_document
                FOREIGN KEY (document_id)
                REFERENCES documents.document (id)
                ON DELETE NO ACTION;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE payroll.payroll_item_document DROP CONSTRAINT IF EXISTS fk_payroll_item_document_document;");
        }
    }
}
