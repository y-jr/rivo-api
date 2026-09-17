using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                schema: "inventory",
                table: "inventory_count",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "settled_at",
                schema: "inventory",
                table: "inventory_count",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                schema: "inventory",
                table: "inventory_count",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "submitted_variance_value",
                schema: "inventory",
                table: "inventory_count",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_count_approval_request",
                schema: "inventory",
                table: "inventory_count",
                column: "approval_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_count_approval_request",
                schema: "inventory",
                table: "inventory_count");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                schema: "inventory",
                table: "inventory_count");

            migrationBuilder.DropColumn(
                name: "settled_at",
                schema: "inventory",
                table: "inventory_count");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                schema: "inventory",
                table: "inventory_count");

            migrationBuilder.DropColumn(
                name: "submitted_variance_value",
                schema: "inventory",
                table: "inventory_count");
        }
    }
}
