using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Custo médio ponderado (decisão de negócio do utilizador, 2026-08-31 —
    /// sem fonte fiscal verificada para decidir por conta própria, mesma
    /// disciplina de IRT/INSS).
    ///
    /// <para>
    /// <c>unit_cost</c> em <c>stock_movement</c> e <c>average_cost</c> em
    /// <c>item</c> nascem a zero para os movimentos e itens já existentes na
    /// base local — não há custo de compra capturado antes desta migração, e
    /// zero é a leitura honesta disso ("sem dado", não um valor inventado).
    /// A valorização só passa a ser real a partir da próxima Recepção de
    /// cada item, quando um custo unitário é finalmente indicado. Mesmo
    /// padrão do <c>defaultValue</c> usado no retrofit de subsídios de
    /// `payroll` e no seed de Armazém de `inventory` (ambos 2026-08-31).
    /// </para>
    /// </summary>
    public partial class AddStockValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "average_cost",
                schema: "inventory",
                table: "item",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropColumn(
                name: "average_cost",
                schema: "inventory",
                table: "item");
        }
    }
}
