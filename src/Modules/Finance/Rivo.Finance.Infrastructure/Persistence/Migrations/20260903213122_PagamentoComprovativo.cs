using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PagamentoComprovativo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_claim",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    receipt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_claim", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_claim_customer_id_status",
                schema: "finance",
                table: "payment_claim",
                columns: new[] { "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_claim_document_id",
                schema: "finance",
                table: "payment_claim",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_claim_sales_invoice_id",
                schema: "finance",
                table: "payment_claim",
                column: "sales_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_claim",
                schema: "finance");
        }
    }
}
