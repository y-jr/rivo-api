using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionIdleExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Os valores por omissão que o EF gerou eram `0` segundos de
            // tolerância e `0001-01-01` de última actividade — o que **matava
            // todas as sessões abertas no instante do deploy**, e deixava uma
            // coluna com um valor sem sentido. Substituídos por estes:
            //
            //   - 1800 segundos, que é o valor por omissão da configuração;
            //   - `SYSDATETIMEOFFSET()`, que é a leitura honesta de «não sabemos
            //     quando esta sessão foi usada pela última vez, portanto conta a
            //     partir de agora».
            //
            // A generosidade é limitada: nenhuma sessão anterior a esta migração
            // vive mais de 60 minutos, porque era esse o tecto absoluto antigo, e
            // esse tecto continua a valer para as que já existem.
            migrationBuilder.AddColumn<int>(
                name: "idle_timeout_seconds",
                schema: "identity",
                table: "user_session",
                type: "int",
                nullable: false,
                defaultValue: 1800);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                schema: "identity",
                table: "user_session",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSDATETIMEOFFSET()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "idle_timeout_seconds",
                schema: "identity",
                table: "user_session");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                schema: "identity",
                table: "user_session");
        }
    }
}
