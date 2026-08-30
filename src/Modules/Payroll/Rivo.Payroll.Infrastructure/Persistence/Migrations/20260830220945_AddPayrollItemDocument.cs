using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollItemDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_item_document",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payroll_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_item_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_item_document_payroll_item_payroll_item_id",
                        column: x => x.payroll_item_id,
                        principalSchema: "payroll",
                        principalTable: "payroll_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_item_document_document_id",
                schema: "payroll",
                table: "payroll_item_document",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_item_document_payroll_item_id",
                schema: "payroll",
                table: "payroll_item_document",
                column: "payroll_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_item_document",
                schema: "payroll");
        }
    }
}
