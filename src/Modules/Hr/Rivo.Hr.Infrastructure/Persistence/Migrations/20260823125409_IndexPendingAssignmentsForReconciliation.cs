using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexPendingAssignmentsForReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_position_assignment_status_approval_request_id",
                schema: "hr",
                table: "position_assignment",
                columns: new[] { "status", "approval_request_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_position_assignment_status_approval_request_id",
                schema: "hr",
                table: "position_assignment");
        }
    }
}
