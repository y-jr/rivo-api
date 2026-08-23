using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBenefitsRecruitmentAndLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benefit",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    monthly_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benefit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_opening",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    vacancies = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    requirements = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_opening", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_process",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_working_day = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lifecycle_process", x => x.id);
                    table.ForeignKey(
                        name: "fk_lifecycle_process_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "benefit_enrolment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    benefit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    cancelled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_benefit_enrolment", x => x.id);
                    table.ForeignKey(
                        name: "fk_benefit_enrolment_benefit_benefit_id",
                        column: x => x.benefit_id,
                        principalSchema: "hr",
                        principalTable: "benefit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_benefit_enrolment_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "candidate",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    job_opening_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    applied_on = table.Column<DateOnly>(type: "date", nullable: false),
                    stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    hired_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_job_opening_job_opening_id",
                        column: x => x.job_opening_id,
                        principalSchema: "hr",
                        principalTable: "job_opening",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_task",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    process_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    order = table.Column<int>(type: "int", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    is_completed = table.Column<bool>(type: "bit", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lifecycle_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_lifecycle_task_lifecycle_process_process_id",
                        column: x => x.process_id,
                        principalSchema: "hr",
                        principalTable: "lifecycle_process",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_benefit_name",
                schema: "hr",
                table: "benefit",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_benefit_enrolment_benefit_id",
                schema: "hr",
                table: "benefit_enrolment",
                column: "benefit_id");

            migrationBuilder.CreateIndex(
                name: "ix_benefit_enrolment_employee_id_benefit_id",
                schema: "hr",
                table: "benefit_enrolment",
                columns: new[] { "employee_id", "benefit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_candidate_hired_employee_id",
                schema: "hr",
                table: "candidate",
                column: "hired_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_job_opening_id_stage",
                schema: "hr",
                table: "candidate",
                columns: new[] { "job_opening_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_job_opening_department_id",
                schema: "hr",
                table: "job_opening",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_opening_status",
                schema: "hr",
                table: "job_opening",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_process_employee_id",
                schema: "hr",
                table: "lifecycle_process",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_process_kind_status",
                schema: "hr",
                table: "lifecycle_process",
                columns: new[] { "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_task_process_id_order",
                schema: "hr",
                table: "lifecycle_task",
                columns: new[] { "process_id", "order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benefit_enrolment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "candidate",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "lifecycle_task",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "benefit",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "job_opening",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "lifecycle_process",
                schema: "hr");
        }
    }
}
