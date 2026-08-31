using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseAndTransfer : Migration
    {
        // Armazém "seed" de retrocompatibilidade — os movimentos já
        // existentes na base local não têm armazém, e o retrofit torna
        // warehouse_id obrigatório. Não há um armazém "certo" para dados
        // históricos: nasce um Principal e fica documentado aqui como
        // artefacto da migração, não como escolha de negócio (ver
        // `modules/inventory.md`).
        private static readonly Guid SeedWarehouseId = new("8892383d-e16d-4399-8012-e9f8b9d01ea5");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "warehouse",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouse", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_code",
                schema: "inventory",
                table: "warehouse",
                column: "code",
                unique: true);

            migrationBuilder.InsertData(
                schema: "inventory",
                table: "warehouse",
                columns: new[] { "id", "code", "name", "status", "version" },
                values: new object[] { SeedWarehouseId, "PRINCIPAL", "Armazém Principal", "Active", 0 });

            migrationBuilder.AddColumn<Guid>(
                name: "related_warehouse_id",
                schema: "inventory",
                table: "stock_movement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                schema: "inventory",
                table: "stock_movement",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: SeedWarehouseId);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_related_warehouse_id",
                schema: "inventory",
                table: "stock_movement",
                column: "related_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_warehouse_id",
                schema: "inventory",
                table: "stock_movement",
                column: "warehouse_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movement_warehouses_related_warehouse_id",
                schema: "inventory",
                table: "stock_movement",
                column: "related_warehouse_id",
                principalSchema: "inventory",
                principalTable: "warehouse",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movement_warehouses_warehouse_id",
                schema: "inventory",
                table: "stock_movement",
                column: "warehouse_id",
                principalSchema: "inventory",
                principalTable: "warehouse",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_movement_warehouses_related_warehouse_id",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movement_warehouses_warehouse_id",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropTable(
                name: "warehouse",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "ix_stock_movement_related_warehouse_id",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropIndex(
                name: "ix_stock_movement_warehouse_id",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropColumn(
                name: "related_warehouse_id",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                schema: "inventory",
                table: "stock_movement");
        }
    }
}
