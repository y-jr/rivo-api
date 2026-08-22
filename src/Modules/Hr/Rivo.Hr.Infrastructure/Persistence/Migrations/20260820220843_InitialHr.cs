using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hr");

            migrationBuilder.CreateTable(
                name: "department",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    manager_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    hired_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "position",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    hierarchy_level = table.Column<int>(type: "int", nullable: false),
                    grants_approval_authority = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_document",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_document_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_assignment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_assignment_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_assignment_position_position_id",
                        column: x => x.position_id,
                        principalSchema: "hr",
                        principalTable: "position",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_department_name",
                schema: "hr",
                table: "department",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_department_id",
                schema: "hr",
                table: "employee",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_user_id",
                schema: "hr",
                table: "employee",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_document_id",
                schema: "hr",
                table: "employee_document",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_employee_id",
                schema: "hr",
                table: "employee_document",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_grants_approval_authority",
                schema: "hr",
                table: "position",
                column: "grants_approval_authority");

            migrationBuilder.CreateIndex(
                name: "ix_position_name",
                schema: "hr",
                table: "position",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_position_assignment_employee_id_effective_from",
                schema: "hr",
                table: "position_assignment",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_position_assignment_position_id_effective_from",
                schema: "hr",
                table: "position_assignment",
                columns: new[] { "position_id", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "department",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_document",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "position_assignment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "position",
                schema: "hr");
        }
    }
}
