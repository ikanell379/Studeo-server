using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studeo.Api.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerId_UploadedAt",
                table: "Documents",
                columns: new[] { "OwnerId", "UploadedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_OwnerId_UploadedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Documents");
        }
    }
}
