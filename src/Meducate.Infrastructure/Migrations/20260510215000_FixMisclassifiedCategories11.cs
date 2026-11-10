using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lupus: ICD-10 M32 = Systemic lupus erythematosus = Musculoskeletal & Connective Tissue (Chapter XIII), not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Lupus';""");

            // Sexual Dysfunction: ICD-10 N52/F52 — classified consistently with Erectile Dysfunction (Genitourinary) and Female Sexual Dysfunction (Genitourinary), not Endocrine, Nutritional & Metabolic
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Sexual Dysfunction';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Lupus';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Sexual Dysfunction';""");
        }
    }
}
