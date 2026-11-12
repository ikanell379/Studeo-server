using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studeo.Api.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionWordCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WordCount",
                table: "Sections",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WordCount",
                table: "Sections");
        }
    }
}
