using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    delivery_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    delivery_attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_delivery_error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_next_attempt_at",
                schema: "notifications",
                table: "notification",
                column: "next_attempt_at",
                filter: "[delivery_status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipient_user_id_created_at",
                schema: "notifications",
                table: "notification",
                columns: new[] { "recipient_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification",
                schema: "notifications");
        }
    }
}
