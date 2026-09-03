using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Commercial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "commercial",
                table: "customer",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_user_id",
                schema: "commercial",
                table: "customer",
                column: "user_id",
                unique: true,
                filter: "[user_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customer_user_id",
                schema: "commercial",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "commercial",
                table: "customer");
        }
    }
}
