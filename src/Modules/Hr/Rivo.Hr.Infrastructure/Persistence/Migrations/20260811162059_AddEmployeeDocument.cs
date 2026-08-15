using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_document",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_document_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_document_id",
                schema: "hr",
                table: "employee_document",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_employee_id",
                schema: "hr",
                table: "employee_document",
                column: "employee_id");

            // Chave estrangeira entre schemas para documents.document(id).
            //
            // Escrita em SQL porque o EF Core não a consegue exprimir: as duas
            // tabelas pertencem a DbContext distintos, e `hr` não pode
            // referenciar a entidade Document de `documents` (ADR-017).
            //
            // É esta restrição que devolve a integridade referencial que a
            // chave polimórfica do desenho inicial não dava (ADR-009), e é o
            // único caso de FK entre schemas permitido: para a chave primária
            // do contexto dono (ADR-010).
            //
            // RESTRICT: um documento ligado a um colaborador não pode ser
            // eliminado sem antes se desfazer a ligação.
            migrationBuilder.Sql("""
                ALTER TABLE hr.employee_document
                ADD CONSTRAINT fk_employee_document_document
                FOREIGN KEY (document_id)
                REFERENCES documents.document (id)
                ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE hr.employee_document DROP CONSTRAINT IF EXISTS fk_employee_document_document;");

            migrationBuilder.DropTable(
                name: "employee_document",
                schema: "hr");
        }
    }
}
