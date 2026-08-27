using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProcurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.CreateTable(
                name: "purchase_requisition",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    requested_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closing_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_requisition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requisition_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    estimated_unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_line_purchase_requisition_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_requisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_approval_request_id",
                schema: "procurement",
                table: "purchase_requisition",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_requested_by_employee_id",
                schema: "procurement",
                table: "purchase_requisition",
                column: "requested_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_status",
                schema: "procurement",
                table: "purchase_requisition",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_line_requisition_id",
                schema: "procurement",
                table: "requisition_line",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_name",
                schema: "procurement",
                table: "supplier",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_tax_id",
                schema: "procurement",
                table: "supplier",
                column: "tax_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_requisition",
                schema: "procurement");
        }
    }
}
