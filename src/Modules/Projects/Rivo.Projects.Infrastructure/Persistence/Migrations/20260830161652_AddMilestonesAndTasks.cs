using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Projects.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestonesAndTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "milestone",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reached_on = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milestone", x => x.id);
                    table.ForeignKey(
                        name: "fk_milestone_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_task",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    assigned_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_task_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_milestone_project_id",
                schema: "projects",
                table: "milestone",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_task_assigned_employee_id",
                schema: "projects",
                table: "project_task",
                column: "assigned_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_task_project_id",
                schema: "projects",
                table: "project_task",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "milestone",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "project_task",
                schema: "projects");
        }
    }
}
