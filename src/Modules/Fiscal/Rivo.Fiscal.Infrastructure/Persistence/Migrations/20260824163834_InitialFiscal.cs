using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fiscal");

            migrationBuilder.CreateTable(
                name: "tax_rate_schedule",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rate_schedule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_rate_version",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    legal_instrument = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tax_rate_schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rate_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_rate_version_tax_rate_schedule_tax_rate_schedule_id",
                        column: x => x.tax_rate_schedule_id,
                        principalSchema: "fiscal",
                        principalTable: "tax_rate_schedule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_rate_schedule_kind_code",
                schema: "fiscal",
                table: "tax_rate_schedule",
                columns: new[] { "kind", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rate_version_effective_from",
                schema: "fiscal",
                table: "tax_rate_version",
                column: "effective_from");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rate_version_tax_rate_schedule_id",
                schema: "fiscal",
                table: "tax_rate_version",
                column: "tax_rate_schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_rate_version",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "tax_rate_schedule",
                schema: "fiscal");
        }
    }
}
