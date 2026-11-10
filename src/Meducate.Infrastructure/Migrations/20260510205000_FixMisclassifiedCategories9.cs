using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H1N1 Influenza: ICD-10 J09 = Influenza due to certain identified influenza viruses = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'H1N1 Influenza';""");

            // Avian Influenza: ICD-10 J09 = Influenza due to certain identified influenza viruses = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Avian Influenza';""");

            // Tick Bite: ICD-10 T63.4 = Toxic effect of venom of arthropods = Injury & Poisoning (Chapter XIX), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Tick Bite';""");

            // Tonsillitis: ICD-10 J03 = Acute tonsillitis = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Tonsillitis';""");

            // Tooth Decay: ICD-10 K02 = Dental caries = Digestive System (Chapter XI), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Tooth Decay';""");

            // Tongue Disorders: ICD-10 K14 = Diseases of tongue = Digestive System (Chapter XI), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Tongue Disorders';""");

            // Skin Infection: ICD-10 L08 = Other local infections of skin = Skin & Subcutaneous Tissue (Chapter XII), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Skin Infection';""");

            // Low Bone Density: ICD-10 M85 = Other disorders of bone density = Musculoskeletal & Connective Tissue (Chapter XIII), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Low Bone Density';""");

            // Acute Radiation Syndrome: ICD-10 T66 = Radiation sickness = Injury & Poisoning (Chapter XIX), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Acute Radiation Syndrome';""");

            // Behcet's Syndrome: ICD-10 M35.2 = Behcet's disease = Musculoskeletal & Connective Tissue (Chapter XIII), not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Behcet''s Syndrome';""");

            // Sjogren's Syndrome: ICD-10 M35.0 = Sicca syndrome = Musculoskeletal & Connective Tissue (Chapter XIII), not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Sjogren''s Syndrome';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'H1N1 Influenza';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Avian Influenza';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Tick Bite';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Tonsillitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Tooth Decay';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Tongue Disorders';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Skin Infection';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Low Bone Density';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Acute Radiation Syndrome';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Behcet''s Syndrome';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Sjogren''s Syndrome';""");
        }
    }
}
