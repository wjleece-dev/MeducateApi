using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rickets: ICD-10 E55.0 = vitamin D deficiency rickets = Endocrine, Nutritional & Metabolic
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Rickets';""");

            // Sarcoidosis: ICD-10 D86 = Blood & Immune System (granulomatous disease), not Infectious & Parasitic
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Sarcoidosis';""");

            // ME/CFS: ICD-10 G93.3 = Postviral fatigue syndrome = Nervous System, not Health & Wellness
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Myalgic Encephalomyelitis/Chronic Fatigue Syndrome';""");

            // Paralysis: ICD-10 G83 = Nervous System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Paralysis';""");

            // Porphyria: ICD-10 E80 = disorders of porphyrin/bilirubin metabolism = Endocrine, Nutritional & Metabolic
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Porphyria';""");

            // Post-COVID: ICD-10 U09.9 = post-COVID sequel, classified alongside infectious disease origin
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Post-COVID Conditions (Long COVID)';""");

            // Prader-Willi Syndrome: ICD-10 Q87.1 = congenital malformation syndrome = Perinatal & Congenital
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital' WHERE "Name" = 'Prader-Willi Syndrome';""");

            // Kawasaki Disease: ICD-10 M30.3 = Musculoskeletal & Connective Tissue (polyarteritis group), not Infectious & Parasitic
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Kawasaki Disease';""");

            // Leukodystrophy: ICD-10 G37 = demyelinating diseases = Nervous System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Leukodystrophy';""");

            // Lewy Body Dementia: ICD-10 G31.83 = Nervous System, not Mental & Behavioral
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Lewy Body Dementia';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Rickets';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Sarcoidosis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Health & Wellness' WHERE "Name" = 'Myalgic Encephalomyelitis/Chronic Fatigue Syndrome';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Paralysis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Porphyria';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Post-COVID Conditions (Long COVID)';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Prader-Willi Syndrome';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Kawasaki Disease';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Leukodystrophy';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral' WHERE "Name" = 'Lewy Body Dementia';""");
        }
    }
}
