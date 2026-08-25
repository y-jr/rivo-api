using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostagemAutomatica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "posting_rule",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    @event = table.Column<string>(name: "event", type: "nvarchar(40)", maxLength: 40, nullable: false),
                    journal_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "posting_rule_line",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    posting_rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    line_number = table.Column<int>(type: "int", nullable: false),
                    account_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    side = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    amount = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_rule_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_posting_rule_line_posting_rules_posting_rule_id",
                        column: x => x.posting_rule_id,
                        principalSchema: "finance",
                        principalTable: "posting_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posting_rule_event",
                schema: "finance",
                table: "posting_rule",
                column: "event",
                unique: true,
                filter: "[is_active] = 1");

            migrationBuilder.CreateIndex(
                name: "ix_posting_rule_line_posting_rule_id_line_number",
                schema: "finance",
                table: "posting_rule_line",
                columns: new[] { "posting_rule_id", "line_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "posting_rule_line",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "posting_rule",
                schema: "finance");
        }
    }
}
