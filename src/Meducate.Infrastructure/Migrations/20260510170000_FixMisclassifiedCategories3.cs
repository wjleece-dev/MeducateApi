using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sciatica: ICD-10 M54.3-M54.4 = Musculoskeletal & Connective Tissue, not Symptoms & Signs
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue'
                WHERE "Name" = 'Sciatica';
                """);

            // Shock: ICD-10 R57 = Symptoms & Signs (circulatory failure as symptom), not Injury & Poisoning
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs'
                WHERE "Name" = 'Shock';
                """);

            // Insomnia: ICD-10 G47.0 = Nervous System (same pattern as Sleep Deprivation/Sleep Disorders), not Mental & Behavioral
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Nervous System'
                WHERE "Name" = 'Insomnia';
                """);

            // Itching: ICD-10 L29 = Pruritus = Skin & Subcutaneous Tissue, not Symptoms & Signs
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue'
                WHERE "Name" = 'Itching';
                """);

            // Stuttering: ICD-10 F98.5 = Adult onset fluency disorder = Mental & Behavioral, not Symptoms & Signs
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral'
                WHERE "Name" = 'Stuttering';
                """);

            // Hemochromatosis: ICD-10 E83.1 = Disorder of iron metabolism = Endocrine, Nutritional & Metabolic, not Blood & Immune System
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic'
                WHERE "Name" = 'Hemochromatosis';
                """);

            // Adhesions: ICD-10 K66.0 = Peritoneal adhesions = Digestive System, not Musculoskeletal & Connective Tissue
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Digestive System'
                WHERE "Name" = 'Adhesions';
                """);

            // Alpha-1 Antitrypsin Deficiency: ICD-10 E88.01 = Endocrine, Nutritional & Metabolic, not Blood & Immune System
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic'
                WHERE "Name" = 'Alpha-1 Antitrypsin Deficiency';
                """);

            // Amyloidosis: ICD-10 E85 = Amyloidosis = Endocrine, Nutritional & Metabolic, not Neoplasms
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic'
                WHERE "Name" = 'Amyloidosis';
                """);

            // Animal Bite: primarily a wound/injury (ICD-10 S/W codes), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning'
                WHERE "Name" = 'Animal Bite';
                """);

            // Ataxia Telangiectasia: ICD-10 G11.3 = Hereditary cerebellar ataxia = Nervous System, not Perinatal & Congenital
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Nervous System'
                WHERE "Name" = 'Ataxia Telangiectasia';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Sciatica';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Shock';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral' WHERE "Name" = 'Insomnia';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Itching';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Stuttering';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Hemochromatosis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Adhesions';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Alpha-1 Antitrypsin Deficiency';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Neoplasms' WHERE "Name" = 'Amyloidosis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Animal Bite';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital' WHERE "Name" = 'Ataxia Telangiectasia';""");
        }
    }
}
