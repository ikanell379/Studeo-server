using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Studeo.Api.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddWholeDocumentSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWholeDocument",
                table: "Sections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing documents get «Ολόκληρο το κείμενο» too, like new uploads (DocumentService.WithWholeDocument):
            // a single section over every page becomes it; otherwise one is added first (Order 0).
            migrationBuilder.Sql(
                """
                UPDATE "Sections" AS s
                SET "IsWholeDocument" = TRUE, "Title" = 'Ολόκληρο το κείμενο'
                FROM "Documents" AS d
                WHERE s."DocumentId" = d."Id"
                  AND s."StartPage" = 1 AND s."EndPage" = d."PageCount"
                  AND (SELECT COUNT(*) FROM "Sections" AS o WHERE o."DocumentId" = d."Id") = 1;

                INSERT INTO "Sections" ("Id", "DocumentId", "Order", "Title", "StartPage", "EndPage", "WordCount", "IsWholeDocument")
                SELECT gen_random_uuid(), d."Id", 0, 'Ολόκληρο το κείμενο', 1, d."PageCount",
                       COALESCE((SELECT SUM(COALESCE(array_length(regexp_split_to_array(btrim(p."Text"), '\s+'), 1), 0))
                                 FROM "DocumentPages" AS p WHERE p."DocumentId" = d."Id"), 0),
                       TRUE
                FROM "Documents" AS d
                WHERE NOT EXISTS (SELECT 1 FROM "Sections" AS w WHERE w."DocumentId" = d."Id" AND w."IsWholeDocument");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "Sections" WHERE "IsWholeDocument" AND "Order" = 0;""");

            migrationBuilder.DropColumn(
                name: "IsWholeDocument",
                table: "Sections");
        }
    }
}
