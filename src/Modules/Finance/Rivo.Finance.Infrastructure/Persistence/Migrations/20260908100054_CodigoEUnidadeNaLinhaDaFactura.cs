using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A linha de factura ganha <c>ProductCode</c> e <c>UnitOfMeasure</c>, que
    /// o SAF-T exige em cada linha de <c>SourceDocuments</c>.
    ///
    /// <para>
    /// ⚠ <strong>As omissões que o EF gerou eram cadeia vazia</strong>, e
    /// cadeia vazia produz um ficheiro inválido — os dois tipos têm
    /// <c>minLength</c> de 1. É a terceira migração seguida em que isto
    /// acontece; o EF propõe o que satisfaz a base de dados, não o que
    /// satisfaz o esquema.
    /// </para>
    ///
    /// <para>
    /// <strong>As linhas anteriores ficam com <c>"Desconhecido"</c> e
    /// <c>"UN"</c>.</strong> O primeiro é o termo que o XSD usa para o mesmo
    /// efeito noutros campos, e é procurável — quem quiser saber que linhas
    /// ficaram por completar pesquisa por ele. O segundo é a unidade do caso
    /// corrente de uma linha de venda, e não é invenção pelo mesmo motivo que
    /// não é na emissão: quem factura três consultorias factura três unidades.
    /// </para>
    ///
    /// <para>
    /// ⚠ <strong>"Desconhecido" não estará na tabela de produtos do
    /// ficheiro.</strong> O SAF-T quer que cada <c>ProductCode</c> de uma
    /// linha apareça em <c>MasterFiles</c>, e isso só passa a importar quando
    /// <c>SourceDocuments</c> for emitido — é lá que a junção tem de
    /// acontecer, e fica registado aqui para não se descobrir tarde.
    /// </para>
    /// </summary>
    public partial class CodigoEUnidadeNaLinhaDaFactura : Migration
    {
        /// <summary>Ver o resumo da classe: cadeia vazia invalidaria o ficheiro.</summary>
        private const string PorPreencher = "Desconhecido";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "product_code",
                schema: "finance",
                table: "sales_invoice_line",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: PorPreencher);

            migrationBuilder.AddColumn<string>(
                name: "unit_of_measure",
                schema: "finance",
                table: "sales_invoice_line",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "product_code",
                schema: "finance",
                table: "sales_invoice_line");

            migrationBuilder.DropColumn(
                name: "unit_of_measure",
                schema: "finance",
                table: "sales_invoice_line");
        }
    }
}
