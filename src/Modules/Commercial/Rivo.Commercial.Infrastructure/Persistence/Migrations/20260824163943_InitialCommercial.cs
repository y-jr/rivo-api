using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Commercial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "commercial");

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "commercial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    billing_detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    billing_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    billing_country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_name",
                schema: "commercial",
                table: "customer",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_customer_tax_id",
                schema: "commercial",
                table: "customer",
                column: "tax_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer",
                schema: "commercial");
        }
    }
}
