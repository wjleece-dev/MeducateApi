using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chronic Pain: ICD-10 G89 = Nervous System (chronic pain syndrome), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Chronic Pain';""");

            // Color Blindness: ICD-10 H53.5 = Color vision deficiencies = Eye & Ear, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Eye & Ear' WHERE "Name" = 'Color Blindness';""");

            // Conjunctivitis: ICD-10 H10 = Eye & Ear, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Eye & Ear' WHERE "Name" = 'Conjunctivitis';""");

            // Gangrene: ICD-10 I96 = Circulatory System (tissue death from vascular loss), not Injury & Poisoning
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Circulatory System' WHERE "Name" = 'Gangrene';""");

            // GERD In Infants: ICD-10 K21 = Digestive System regardless of patient age, not Perinatal & Congenital
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Gastroesophageal Reflux Disease (GERD) In Infants';""");

            // Gastrointestinal Bleeding: ICD-10 K92 = Digestive System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Gastrointestinal Bleeding';""");

            // Gaucher Disease: ICD-10 E75.2 = lipid storage disorder = Endocrine, Nutritional & Metabolic, not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Gaucher Disease';""");

            // Giant Cell Arteritis: ICD-10 M31.5 = Musculoskeletal & Connective Tissue (vasculitis group), not Nervous System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Giant Cell Arteritis';""");

            // Granulomatosis With Polyangiitis: ICD-10 M31.3 = Musculoskeletal & Connective Tissue, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Granulomatosis With Polyangiitis';""");

            // Hay Fever: ICD-10 J30.1 = Respiratory System (allergic rhinitis), not Mental & Behavioral
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Hay Fever';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Chronic Pain';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Color Blindness';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Conjunctivitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Gangrene';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital' WHERE "Name" = 'Gastroesophageal Reflux Disease (GERD) In Infants';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Gastrointestinal Bleeding';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Gaucher Disease';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Giant Cell Arteritis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Granulomatosis With Polyangiitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral' WHERE "Name" = 'Hay Fever';""");
        }
    }
}
