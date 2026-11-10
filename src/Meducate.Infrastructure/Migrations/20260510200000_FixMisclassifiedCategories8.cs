using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Muscular Dystrophy: ICD-10 G71.0 = Duchenne/Becker/other muscular dystrophies = Nervous System (Chapter VI), not Musculoskeletal
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Muscular Dystrophy';""");

            // Alzheimer's Disease: ICD-10 G30 = Alzheimer's disease = Nervous System (Chapter VI), not Mental & Behavioral
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Alzheimer''s Disease';""");

            // Sore Throat: ICD-10 J02 = Acute pharyngitis = Respiratory System (Chapter X), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Sore Throat';""");

            // Spider Bite: ICD-10 T63.3 = Toxic effect of spider venom = Injury & Poisoning (Chapter XIX), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Spider Bite';""");

            // Premenstrual Syndrome: ICD-10 N94.3 = Premenstrual tension syndrome = Genitourinary System (Chapter XIV), not Mental & Behavioral
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Premenstrual Syndrome';""");

            // Scars: ICD-10 L90.5 = Scar conditions and fibrosis of skin = Skin & Subcutaneous Tissue (Chapter XII), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Scars';""");

            // Scleroderma: ICD-10 M34 = Systemic sclerosis = Musculoskeletal & Connective Tissue (Chapter XIII), not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Scleroderma';""");

            // Breast Disease: ICD-10 N60-N64 = Noninflammatory disorders of breast = Genitourinary System (Chapter XIV), not Neoplasms
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Breast Disease';""");

            // Taste And Smell Disorders: ICD-10 R43 = Disturbances of smell and taste = Symptoms & Signs (Chapter XVIII), not Eye & Ear
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Taste And Smell Disorders';""");

            // Impetigo: ICD-10 L01 = Impetigo = Skin & Subcutaneous Tissue (Chapter XII), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Impetigo';""");

            // Infectious Arthritis: ICD-10 M00-M01 = Infectious arthropathies = Musculoskeletal & Connective Tissue (Chapter XIII), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Infectious Arthritis';""");

            // Common Cold: ICD-10 J00 = Acute nasopharyngitis = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Common Cold';""");

            // Pneumonia: ICD-10 J12-J18 = Pneumonia = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Pneumonia';""");

            // Bone Infection: ICD-10 M86 = Osteomyelitis = Musculoskeletal & Connective Tissue (Chapter XIII), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Bone Infection';""");

            // Respiratory Syncytial Virus Infection: ICD-10 J21.0 = Acute bronchiolitis due to RSV = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Respiratory Syncytial Virus Infection';""");

            // Reye Syndrome: ICD-10 G93.7 = Reye's syndrome = Nervous System (Chapter VI), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Reye Syndrome';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Muscular Dystrophy';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral' WHERE "Name" = 'Alzheimer''s Disease';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Sore Throat';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Spider Bite';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral' WHERE "Name" = 'Premenstrual Syndrome';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Scars';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Scleroderma';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Neoplasms' WHERE "Name" = 'Breast Disease';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Eye & Ear' WHERE "Name" = 'Taste And Smell Disorders';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Impetigo';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Infectious Arthritis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Common Cold';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Pneumonia';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Bone Infection';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Respiratory Syncytial Virus Infection';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Reye Syndrome';""");
        }
    }
}
