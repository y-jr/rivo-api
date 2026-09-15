using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccaoDaNotificacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "action_label",
                schema: "notifications",
                table: "notification",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "action_url",
                schema: "notifications",
                table: "notification",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "action_label",
                schema: "notifications",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "action_url",
                schema: "notifications",
                table: "notification");
        }
    }
}
