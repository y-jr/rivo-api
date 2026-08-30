using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeTaxSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "income_tax_schedule",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_income_tax_schedule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "income_tax_schedule_version",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    legal_instrument = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    income_tax_schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_income_tax_schedule_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_income_tax_schedule_version_income_tax_schedule_income_tax_schedule_id",
                        column: x => x.income_tax_schedule_id,
                        principalSchema: "fiscal",
                        principalTable: "income_tax_schedule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "income_tax_bracket",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lower_bound = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    fixed_portion = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    rate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    income_tax_schedule_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_income_tax_bracket", x => x.id);
                    table.ForeignKey(
                        name: "fk_income_tax_bracket_income_tax_schedule_version_income_tax_schedule_version_id",
                        column: x => x.income_tax_schedule_version_id,
                        principalSchema: "fiscal",
                        principalTable: "income_tax_schedule_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_income_tax_bracket_income_tax_schedule_version_id",
                schema: "fiscal",
                table: "income_tax_bracket",
                column: "income_tax_schedule_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_income_tax_schedule_version_effective_from",
                schema: "fiscal",
                table: "income_tax_schedule_version",
                column: "effective_from");

            migrationBuilder.CreateIndex(
                name: "ix_income_tax_schedule_version_income_tax_schedule_id",
                schema: "fiscal",
                table: "income_tax_schedule_version",
                column: "income_tax_schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "income_tax_bracket",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "income_tax_schedule_version",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "income_tax_schedule",
                schema: "fiscal");
        }
    }
}
