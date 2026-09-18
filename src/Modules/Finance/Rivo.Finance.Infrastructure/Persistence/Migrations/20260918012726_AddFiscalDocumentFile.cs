using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalDocumentFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_document_file",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stored_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    reflects_cancellation = table.Column<bool>(type: "bit", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_document_file", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_file_kind_source_document_id_reflects_cancellation",
                schema: "finance",
                table: "fiscal_document_file",
                columns: new[] { "kind", "source_document_id", "reflects_cancellation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_document_file",
                schema: "finance");
        }
    }
}
