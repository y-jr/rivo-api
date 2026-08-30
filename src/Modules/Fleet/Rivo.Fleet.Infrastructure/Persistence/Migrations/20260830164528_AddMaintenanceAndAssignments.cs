using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceAndAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_record",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_record", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_record_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_assignment",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicle_assignment_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_record_vehicle_id",
                schema: "fleet",
                table: "maintenance_record",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_assignment_employee_id",
                schema: "fleet",
                table: "vehicle_assignment",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_assignment_vehicle_id",
                schema: "fleet",
                table: "vehicle_assignment",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_record",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "vehicle_assignment",
                schema: "fleet");
        }
    }
}
