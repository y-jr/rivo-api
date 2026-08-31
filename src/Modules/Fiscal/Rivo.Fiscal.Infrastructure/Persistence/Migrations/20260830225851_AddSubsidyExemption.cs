using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubsidyExemption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subsidy_exemption_schedule",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subsidy_exemption_schedule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subsidy_exemption_version",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    legal_instrument = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    subsidy_exemption_schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subsidy_exemption_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_subsidy_exemption_version_subsidy_exemption_schedule_subsidy_exemption_schedule_id",
                        column: x => x.subsidy_exemption_schedule_id,
                        principalSchema: "fiscal",
                        principalTable: "subsidy_exemption_schedule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subsidy_exemption_schedule_kind",
                schema: "fiscal",
                table: "subsidy_exemption_schedule",
                column: "kind",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subsidy_exemption_version_effective_from",
                schema: "fiscal",
                table: "subsidy_exemption_version",
                column: "effective_from");

            migrationBuilder.CreateIndex(
                name: "ix_subsidy_exemption_version_subsidy_exemption_schedule_id",
                schema: "fiscal",
                table: "subsidy_exemption_version",
                column: "subsidy_exemption_schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subsidy_exemption_version",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "subsidy_exemption_schedule",
                schema: "fiscal");
        }
    }
}
