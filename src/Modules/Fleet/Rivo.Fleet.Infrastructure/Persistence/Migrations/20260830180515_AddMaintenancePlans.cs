using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenancePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_plan",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    interval_days = table.Column<int>(type: "int", nullable: false),
                    next_due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plan", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_plan_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_is_active_next_due_on",
                schema: "fleet",
                table: "maintenance_plan",
                columns: new[] { "is_active", "next_due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_vehicle_id",
                schema: "fleet",
                table: "maintenance_plan",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_plan",
                schema: "fleet");
        }
    }
}
