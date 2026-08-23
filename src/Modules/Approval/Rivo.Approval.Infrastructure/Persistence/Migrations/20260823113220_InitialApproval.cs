using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Approval.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "approval");

            migrationBuilder.CreateTable(
                name: "policy",
                schema: "approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    process_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    minimum_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    maximum_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    requires_budget_check = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request",
                schema: "approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    process_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    applied_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    current_step = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "policy_step",
                schema: "approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order = table.Column<int>(type: "int", nullable: false),
                    approver_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    sla_hours = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_policy_step_policy_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "approval",
                        principalTable: "policy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment",
                schema: "approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sla_hours = table.Column<int>(type: "int", nullable: true),
                    has_decided = table.Column<bool>(type: "bit", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_request_approval_request_id",
                        column: x => x.approval_request_id,
                        principalSchema: "approval",
                        principalTable: "request",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_assignment_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "approval",
                        principalTable: "request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decision",
                schema: "approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decision", x => x.id);
                    table.ForeignKey(
                        name: "fk_decision_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "approval",
                        principalTable: "request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_approval_request_id",
                schema: "approval",
                table: "assignment",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_approver_employee_id_has_decided",
                schema: "approval",
                table: "assignment",
                columns: new[] { "approver_employee_id", "has_decided" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_request_id_step",
                schema: "approval",
                table: "assignment",
                columns: new[] { "request_id", "step" });

            migrationBuilder.CreateIndex(
                name: "ix_decision_decided_by_employee_id",
                schema: "approval",
                table: "decision",
                column: "decided_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_decision_request_id_step",
                schema: "approval",
                table: "decision",
                columns: new[] { "request_id", "step" });

            migrationBuilder.CreateIndex(
                name: "ix_policy_process_type_is_active",
                schema: "approval",
                table: "policy",
                columns: new[] { "process_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_policy_step_approver_position_id",
                schema: "approval",
                table: "policy_step",
                column: "approver_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_policy_step_policy_id_order",
                schema: "approval",
                table: "policy_step",
                columns: new[] { "policy_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_request_process_type_status",
                schema: "approval",
                table: "request",
                columns: new[] { "process_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_request_source_module_source_reference",
                schema: "approval",
                table: "request",
                columns: new[] { "source_module", "source_reference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment",
                schema: "approval");

            migrationBuilder.DropTable(
                name: "decision",
                schema: "approval");

            migrationBuilder.DropTable(
                name: "policy_step",
                schema: "approval");

            migrationBuilder.DropTable(
                name: "request",
                schema: "approval");

            migrationBuilder.DropTable(
                name: "policy",
                schema: "approval");
        }
    }
}
