using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeAccountLinkHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_account_link",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    linked_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    linked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    unlinked_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    unlinked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_account_link", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_account_link_aberto",
                schema: "hr",
                table: "employee_account_link",
                column: "employee_id",
                unique: true,
                filter: "[unlinked_on] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_account_link_employee",
                schema: "hr",
                table: "employee_account_link",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_account_link_user",
                schema: "hr",
                table: "employee_account_link",
                column: "user_id");

            // Retroactivo: abre um episódio para cada vínculo que já existe.
            //
            // Sem isto, um colaborador com conta ligada apareceria no histórico
            // como "nunca teve conta" — que é pior do que não ter histórico
            // nenhum, porque parece uma resposta.
            //
            // `linked_on` vem de `hired_on` porque, até 2026-09-05, o vínculo
            // **só** se podia criar na admissão: não havia rota para o criar
            // depois (foi essa lacuna que o ADR-051 fechou). Para a
            // esmagadora maioria dos episódios a data é exacta, não estimada.
            // A excepção são os poucos criados entre o ADR-051 e esta
            // migração, para os quais fica adiantada.
            //
            // `linked_by_user_id` fica NULL, que se lê como **desconhecido** e
            // não como "ninguém": o vínculo existia antes de haver quem o
            // registasse, e inventar um autor seria pior do que admitir que
            // não se sabe.
            migrationBuilder.Sql("""
                INSERT INTO hr.employee_account_link
                    (id, version, employee_id, user_id, linked_on, linked_by_user_id, unlinked_on, unlinked_by_user_id)
                SELECT NEWID(), 0, e.id, e.user_id, e.hired_on, NULL, NULL, NULL
                FROM hr.employee AS e
                WHERE e.user_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_account_link",
                schema: "hr");
        }
    }
}
