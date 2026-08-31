using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_resource_allocation",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    resource_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_resource_allocation", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_resource_allocation_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_resource_allocation_kind_resource_id",
                schema: "projects",
                table: "project_resource_allocation",
                columns: new[] { "kind", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_resource_allocation_project_id",
                schema: "projects",
                table: "project_resource_allocation",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_resource_allocation",
                schema: "projects");
        }
    }
}
