using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "app_role",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    security_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_session", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_role_claim",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_role_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_role_claim_app_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user_claim",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_user_claim_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user_login",
                schema: "identity",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_app_user_login_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user_role",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_role", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_app_user_role_app_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_app_user_role_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "app_user_token",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_app_user_token_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "app_role",
                column: "normalized_name",
                unique: true,
                filter: "[normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_app_role_claim_role_id",
                schema: "identity",
                table: "app_role_claim",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "app_user",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "app_user",
                column: "normalized_user_name",
                unique: true,
                filter: "[normalized_user_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_claim_user_id",
                schema: "identity",
                table: "app_user_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_login_user_id",
                schema: "identity",
                table: "app_user_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_user_role_role_id",
                schema: "identity",
                table: "app_user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_user_id_expires_at",
                schema: "identity",
                table: "user_session",
                columns: new[] { "user_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_role_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user_login",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user_role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_session",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "identity");
        }
    }
}
