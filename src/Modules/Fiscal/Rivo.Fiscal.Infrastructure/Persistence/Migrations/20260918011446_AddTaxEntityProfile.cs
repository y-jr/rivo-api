using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxEntityProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_entity_profile",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    company_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    business_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    tax_registration_number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    address_detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    software_validation_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_entity_profile", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_entity_profile",
                schema: "fiscal");
        }
    }
}
