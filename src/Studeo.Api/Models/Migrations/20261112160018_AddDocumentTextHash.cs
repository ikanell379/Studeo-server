using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studeo.Api.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTextHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextHash",
                table: "Documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerId_TextHash",
                table: "Documents",
                columns: new[] { "OwnerId", "TextHash" });

            // PDFs already stored get their text hash from their pages, so uploading one of them again is caught.
            migrationBuilder.Sql(Studeo.Api.Services.DocumentHashes.BackfillTextHashSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_OwnerId_TextHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TextHash",
                table: "Documents");
        }
    }
}
