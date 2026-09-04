using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Messaging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketsDeSuporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_conversation_open_per_customer",
                schema: "messaging",
                table: "conversation");

            // "Message", nunca vazio: toda a conversa que já existir nesta
            // coluna nasceu antes de `Kind` existir, e era sempre uma
            // mensagem directa (ADR-045) — tickets (ADR-046) só passam a
            // poder existir a partir desta migração. Um valor vazio
            // quebraria a leitura dessas linhas (a conversão string→enum
            // não reconhece "") e deixaria a invariante "uma aberta por
            // cliente" sem cobrir nenhuma delas.
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "messaging",
                table: "conversation",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Message");

            migrationBuilder.AddColumn<string>(
                name: "subject",
                schema: "messaging",
                table: "conversation",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversation_open_message_per_customer",
                schema: "messaging",
                table: "conversation",
                column: "customer_id",
                unique: true,
                filter: "[status] = 'Open' AND [kind] = 'Message'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_conversation_open_message_per_customer",
                schema: "messaging",
                table: "conversation");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "messaging",
                table: "conversation");

            migrationBuilder.DropColumn(
                name: "subject",
                schema: "messaging",
                table: "conversation");

            migrationBuilder.CreateIndex(
                name: "ux_conversation_open_per_customer",
                schema: "messaging",
                table: "conversation",
                column: "customer_id",
                unique: true,
                filter: "[status] = 'Open'");
        }
    }
}
