using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixLegacyOriginalNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix topics where the LLM renamed too aggressively and the
            // backfill couldn't reverse-match the original MedlinePlus title.
            migrationBuilder.Sql(
                """
                UPDATE "HealthTopics" SET "OriginalName" = 'Breast Diseases'
                WHERE "Name" = 'Benign Breast Disease';

                UPDATE "HealthTopics" SET "OriginalName" = 'Spleen Diseases'
                WHERE "Name" = 'Splenomegaly';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "HealthTopics" SET "OriginalName" = NULL
                WHERE "Name" IN ('Benign Breast Disease', 'Splenomegaly');
                """);
        }
    }
}
