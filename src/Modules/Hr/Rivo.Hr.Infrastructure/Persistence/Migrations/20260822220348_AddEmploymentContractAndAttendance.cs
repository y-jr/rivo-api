using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmploymentContractAndAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_record",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    checked_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    justification = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_record", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_record_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employment_contract",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    monthly_salary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_contract", x => x.id);
                    table.ForeignKey(
                        name: "fk_employment_contract_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_record_day_status",
                schema: "hr",
                table: "attendance_record",
                columns: new[] { "day", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_record_employee_id_day",
                schema: "hr",
                table: "attendance_record",
                columns: new[] { "employee_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_contract_employee_id_starts_on",
                schema: "hr",
                table: "employment_contract",
                columns: new[] { "employee_id", "starts_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_record",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employment_contract",
                schema: "hr");
        }
    }
}
