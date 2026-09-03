using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Commercial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VendedorResponsavel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to_employee_id",
                schema: "commercial",
                table: "customer",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_assigned_to_employee_id",
                schema: "commercial",
                table: "customer",
                column: "assigned_to_employee_id",
                filter: "[assigned_to_employee_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customer_assigned_to_employee_id",
                schema: "commercial",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "assigned_to_employee_id",
                schema: "commercial",
                table: "customer");
        }
    }
}
