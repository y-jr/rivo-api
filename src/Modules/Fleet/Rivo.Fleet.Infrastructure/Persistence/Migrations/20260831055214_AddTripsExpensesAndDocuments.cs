using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fleet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripsExpensesAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fleet_expense",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fleet_expense", x => x.id);
                    table.ForeignKey(
                        name: "fk_fleet_expense_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_document",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicle_document_vehicle_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_trip",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    driver_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: false),
                    start_odometer = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    end_odometer = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_trip", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicle_trip_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fleet_expense_vehicle_id",
                schema: "fleet",
                table: "fleet_expense",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_document_document_id",
                schema: "fleet",
                table: "vehicle_document",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_document_vehicle_id",
                schema: "fleet",
                table: "vehicle_document",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_trip_driver_id",
                schema: "fleet",
                table: "vehicle_trip",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_trip_vehicle_id",
                schema: "fleet",
                table: "vehicle_trip",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fleet_expense",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "vehicle_document",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "vehicle_trip",
                schema: "fleet");
        }
    }
}
