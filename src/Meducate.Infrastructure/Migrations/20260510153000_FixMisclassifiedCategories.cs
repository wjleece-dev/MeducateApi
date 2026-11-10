using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hernia: ICD-10 K40-K46 = Digestive System, not Musculoskeletal
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Digestive System'
                WHERE "Name" = 'Hernia';
                """);

            // Sleep Deprivation / Sleep Disorders: ICD-10 G47 = Nervous System (primary), not Mental & Behavioral
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Nervous System'
                WHERE "Name" IN ('Sleep Deprivation', 'Sleep Disorders');
                """);

            // Spleen Disease: ICD-10 D73 = Blood & Immune System, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System'
                WHERE "Name" = 'Spleen Disease';
                """);

            // Tay-Sachs Disease: ICD-10 E75.0 = Endocrine, Nutritional & Metabolic (lysosomal storage), not Blood & Immune System
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic'
                WHERE "Name" = 'Tay-Sachs Disease';
                """);

            // Tear Dysfunction: ICD-10 H04 = Eye & Ear (lacrimal system), not Symptoms & Signs
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Eye & Ear'
                WHERE "Name" = 'Tear Dysfunction';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue'
                WHERE "Name" = 'Hernia';
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral'
                WHERE "Name" IN ('Sleep Deprivation', 'Sleep Disorders');
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases'
                WHERE "Name" = 'Spleen Disease';
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System'
                WHERE "Name" = 'Tay-Sachs Disease';
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs'
                WHERE "Name" = 'Tear Dysfunction';
                """);
        }
    }
}
