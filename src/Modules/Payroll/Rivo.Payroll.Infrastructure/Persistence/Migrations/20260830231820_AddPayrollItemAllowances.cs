using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Payroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollItemAllowances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "christmas_allowance",
                schema: "payroll",
                table: "payroll_item",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "food_allowance",
                schema: "payroll",
                table: "payroll_item",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "transport_allowance",
                schema: "payroll",
                table: "payroll_item",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "vacation_allowance",
                schema: "payroll",
                table: "payroll_item",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "christmas_allowance",
                schema: "payroll",
                table: "payroll_item");

            migrationBuilder.DropColumn(
                name: "food_allowance",
                schema: "payroll",
                table: "payroll_item");

            migrationBuilder.DropColumn(
                name: "transport_allowance",
                schema: "payroll",
                table: "payroll_item");

            migrationBuilder.DropColumn(
                name: "vacation_allowance",
                schema: "payroll",
                table: "payroll_item");
        }
    }
}
