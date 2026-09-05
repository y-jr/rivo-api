using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Commercial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerAccountLinkHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_account_link",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    linked_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    linked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    unlinked_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    unlinked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_account_link", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_account_link_aberto",
                schema: "commercial",
                table: "customer_account_link",
                column: "customer_id",
                unique: true,
                filter: "[unlinked_on] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customer_account_link_customer",
                schema: "commercial",
                table: "customer_account_link",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_account_link_user",
                schema: "commercial",
                table: "customer_account_link",
                column: "user_id");

            // Retroactivo: abre um episódio por cada vínculo que já existe.
            // Sem isto, um cliente com conta ligada apareceria no histórico
            // como "nunca teve conta", e desligá-lo não fecharia episódio
            // nenhum.
            //
            // ⚠ `linked_on` é uma SENTINELA, não uma data.
            //
            // Em `hr` (ADR-053) a data veio de `hired_on`, e era exacta: até ao
            // ADR-051 o vínculo só se podia criar na admissão. Aqui não há
            // equivalente — `commercial.customer` não tem coluna de data
            // nenhuma, e o vínculo criava-se a qualquer momento.
            //
            // Usar a data da migração seria pior do que não saber: diria que a
            // conta não podia agir antes de hoje, o que é falso, e uma consulta
            // forense concluiria o contrário do que se passou. `0001-01-01`
            // diz "desde sempre, até melhor informação" — erra para o lado de
            // não excluir ninguém indevidamente, e é visivelmente uma
            // sentinela e não uma data real.
            //
            // `linked_by_user_id` fica NULL, que se lê como desconhecido.
            migrationBuilder.Sql("""
                INSERT INTO commercial.customer_account_link
                    (id, version, customer_id, user_id, linked_on, linked_by_user_id, unlinked_on, unlinked_by_user_id)
                SELECT NEWID(), 0, c.id, c.user_id, '0001-01-01T00:00:00+00:00', NULL, NULL, NULL
                FROM commercial.customer AS c
                WHERE c.user_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_account_link",
                schema: "commercial");
        }
    }
}
