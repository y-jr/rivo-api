using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Hr.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// `Employee.UserId` deixa de tolerar duplicados (ADR-042, Portal do
    /// Colaborador) — o índice já existia desde a Fase 0 de `hr`, mas
    /// ninguém confiava em "no máximo um colaborador por conta" até agora.
    /// Índice filtrado (<c>WHERE user_id IS NOT NULL</c>): vários
    /// colaboradores sem conta continuam a caber, só duas contas para o
    /// mesmo utilizador é que passa a ser recusado.
    /// </summary>
    public partial class UniqueEmployeeUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_user_id",
                schema: "hr",
                table: "employee");

            migrationBuilder.CreateIndex(
                name: "ix_employee_user_id",
                schema: "hr",
                table: "employee",
                column: "user_id",
                unique: true,
                filter: "[user_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_user_id",
                schema: "hr",
                table: "employee");

            migrationBuilder.CreateIndex(
                name: "ix_employee_user_id",
                schema: "hr",
                table: "employee",
                column: "user_id");
        }
    }
}
