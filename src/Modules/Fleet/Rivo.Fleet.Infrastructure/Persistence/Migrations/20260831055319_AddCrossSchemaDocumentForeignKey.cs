using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fleet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Chave estrangeira entre schemas: <c>fleet.vehicle_document.document_id</c>
    /// para <c>documents.document(id)</c>.
    ///
    /// <para>
    /// Migração própria, e não parte da anterior, porque a tabela referida
    /// pertence a outro módulo: quando `fleet` migra, `documents` já tem de
    /// estar migrado. Mesmo padrão de `hr.AddCrossSchemaDocumentForeignKey`
    /// (2026-08-20).
    /// </para>
    /// </summary>
    public partial class AddCrossSchemaDocumentForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrita em SQL porque o EF Core não a consegue exprimir: as duas
            // tabelas pertencem a DbContext distintos, e `fleet` não pode
            // referenciar a entidade Document de `documents` (ADR-017).
            //
            // `NO ACTION` é o `RESTRICT` do SQL Server: um documento ligado a
            // uma viatura não pode ser eliminado sem antes se desfazer a
            // ligação.
            migrationBuilder.Sql("""
                ALTER TABLE fleet.vehicle_document
                ADD CONSTRAINT fk_vehicle_document_document
                FOREIGN KEY (document_id)
                REFERENCES documents.document (id)
                ON DELETE NO ACTION;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE fleet.vehicle_document DROP CONSTRAINT IF EXISTS fk_vehicle_document_document;");
        }
    }
}
