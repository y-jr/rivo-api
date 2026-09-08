using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CodigoEUnidadeNaLinhaDaNotaDeCredito : Migration
    {
        /// <summary>
        /// ⚠ As omissões não são as que o EF gerou — ele propôs cadeia vazia, e
        /// os dois tipos têm <c>minLength</c> 1 no XSD. Quarta migração seguida
        /// em que isto acontece.
        ///
        /// <para>
        /// Sem estes dois campos a nota de crédito não pode ir no SAF-T, e
        /// omiti-la <strong>sobredeclara a receita</strong>: a nota reduz o que
        /// a factura pede, e um ficheiro que a esconde diz à AGT que se recebeu
        /// mais do que se recebeu.
        /// </para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "product_code",
                schema: "finance",
                table: "credit_note_line",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Desconhecido");

            migrationBuilder.AddColumn<string>(
                name: "unit_of_measure",
                schema: "finance",
                table: "credit_note_line",
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
                table: "credit_note_line");

            migrationBuilder.DropColumn(
                name: "unit_of_measure",
                schema: "finance",
                table: "credit_note_line");
        }
    }
}
