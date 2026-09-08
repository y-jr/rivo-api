using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Procurement.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// O Fornecedor ganha morada de facturação, por exigência do SAF-T
    /// (<c>Supplier.BillingAddress</c>, sem <c>minOccurs="0"</c>).
    ///
    /// <para>
    /// ⚠ <strong>As omissões não são as que o EF gerou.</strong> Ele propôs
    /// cadeia vazia, e cadeia vazia produz um ficheiro <em>inválido</em>:
    /// <c>AddressDetail</c> e <c>City</c> são
    /// <c>SAFAOtextTypeMandatoryMax…Car</c>, com <c>minLength</c> de 1. Uma
    /// migração que corre sem erro e deixa a exportação partida é pior do que
    /// uma que falha.
    /// </para>
    ///
    /// <para>
    /// <strong>"Desconhecido" é escolha deliberada e visível.</strong> É o
    /// termo que o XSD usa para o mesmo efeito em <c>AccountID</c>, passa a
    /// validação, e é procurável — quem quiser saber que fornecedores ficaram
    /// por completar pesquisa por ele. Inventar uma rua seria pior: pareceria
    /// dado a sério.
    /// </para>
    ///
    /// <para>
    /// <strong>O país fica <c>AO</c>, e isso é uma suposição.</strong> Ao
    /// contrário dos outros dois, <c>Country</c> tem lista fechada no XSD e não
    /// admite "desconhecido". Para a carteira de fornecedores de uma PME
    /// angolana é a suposição certa na esmagadora maioria dos casos, mas é uma
    /// suposição — quem tiver fornecedores estrangeiros registados antes desta
    /// data tem de os corrigir.
    /// </para>
    /// </summary>
    public partial class MoradaDeFacturacaoDoFornecedor : Migration
    {
        /// <summary>
        /// Ver o resumo da classe: cadeia vazia invalidaria a exportação.
        /// </summary>
        private const string PorPreencher = "Desconhecido";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_detail",
                schema: "procurement",
                table: "supplier",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: PorPreencher);

            migrationBuilder.AddColumn<string>(
                name: "billing_city",
                schema: "procurement",
                table: "supplier",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: PorPreencher);

            migrationBuilder.AddColumn<string>(
                name: "billing_country",
                schema: "procurement",
                table: "supplier",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "AO");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_detail",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "billing_city",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "billing_country",
                schema: "procurement",
                table: "supplier");
        }
    }
}
