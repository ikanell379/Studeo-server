using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studeo.Api.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddStudySetName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "StudySets",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            // Existing material is named after its section, like new material without a chosen name.
            migrationBuilder.Sql(
                """
                UPDATE "StudySets" AS s
                SET "Name" = LEFT(sec."Title", 120)
                FROM "Sections" AS sec
                WHERE s."SectionId" = sec."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "StudySets");
        }
    }
}
