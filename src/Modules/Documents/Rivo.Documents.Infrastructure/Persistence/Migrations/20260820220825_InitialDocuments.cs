using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rivo.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documents");

            migrationBuilder.CreateTable(
                name: "document",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    size_in_bytes = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    content_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    storage_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_category",
                schema: "documents",
                table: "document",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_document_content_hash",
                schema: "documents",
                table: "document",
                column: "content_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document",
                schema: "documents");
        }
    }
}
