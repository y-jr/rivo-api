using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A cadeia de integridade da factura de venda (K7), mais os dois campos do
    /// SAF-T que não se reconstroem: <c>SystemEntryDate</c> e <c>SourceID</c>.
    ///
    /// <para>
    /// <strong>As facturas anteriores ficam sem elo, de propósito.</strong>
    /// <c>hash</c> e <c>previous_hash</c> são anuláveis e ficam nulos. Calcular
    /// um hash agora para documentos emitidos antes validaria exactamente
    /// aquilo que a cadeia existe para detectar — um elo produzido à
    /// posteriori não prova que ninguém lhes tocou entretanto. Nulo é a
    /// verdade: «este documento é anterior à cadeia».
    /// </para>
    ///
    /// <para>
    /// ⚠ <strong><c>system_entry_date</c> não usa a omissão que o EF
    /// gerou.</strong> Ele propôs <c>0001-01-01</c>, que é o mínimo de
    /// <c>DateTimeOffset</c> e não quer dizer nada. O XSD diz o que fazer
    /// quando este instante é desconhecido: «na exportação de dados relativos
    /// a exercícios anteriores em que esta informação seja desconhecida, este
    /// campo deverá ser preenchido com a data do documento e hora como
    /// 00:00:00». É o que a instrução SQL abaixo faz.
    /// </para>
    ///
    /// <para>
    /// <c>issued_by_user_id</c> fica nulo nas anteriores. Não há por onde
    /// deduzir quem emitiu — a trilha de auditoria tem o acto, mas ligá-los
    /// aqui seria inferência gravada como facto.
    /// </para>
    /// </summary>
    public partial class CadeiaDeIntegridadeNaFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hash",
                schema: "finance",
                table: "sales_invoice",
                type: "nvarchar(172)",
                maxLength: 172,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issued_by_user_id",
                schema: "finance",
                table: "sales_invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_hash",
                schema: "finance",
                table: "sales_invoice",
                type: "nvarchar(172)",
                maxLength: 172,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "system_entry_date",
                schema: "finance",
                table: "sales_invoice",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "last_document_hash",
                schema: "finance",
                table: "document_series",
                type: "nvarchar(172)",
                maxLength: 172,
                nullable: true);

            // Ver o resumo da classe: a data do documento às 00:00:00, que é o
            // que o XSD prescreve para este instante quando é desconhecido.
            // `issued_on` é `date`, por isso a conversão dá meia-noite sozinha.
            migrationBuilder.Sql(
                """
                UPDATE finance.sales_invoice
                SET system_entry_date = TODATETIMEOFFSET(CAST(issued_on AS datetime2), 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hash",
                schema: "finance",
                table: "sales_invoice");

            migrationBuilder.DropColumn(
                name: "issued_by_user_id",
                schema: "finance",
                table: "sales_invoice");

            migrationBuilder.DropColumn(
                name: "previous_hash",
                schema: "finance",
                table: "sales_invoice");

            migrationBuilder.DropColumn(
                name: "system_entry_date",
                schema: "finance",
                table: "sales_invoice");

            migrationBuilder.DropColumn(
                name: "last_document_hash",
                schema: "finance",
                table: "document_series");
        }
    }
}
