using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Endocarditis: ICD-10 I33/I38 = Circulatory System, not Infectious & Parasitic
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Circulatory System'
                WHERE "Name" = 'Endocarditis';
                """);

            // Fibromyalgia: ICD-10 M79.7 = Musculoskeletal & Connective Tissue, not Mental & Behavioral
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue'
                WHERE "Name" = 'Fibromyalgia';
                """);

            // Dwarfism: achondroplasia (Q77.4) is the dominant form — Perinatal & Congenital, not Symptoms & Signs
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital'
                WHERE "Name" = 'Dwarfism';
                """);

            // Eye Infection: ICD-10 H10-H16 = Eye & Ear (anatomical primary), not Infectious & Parasitic
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Eye & Ear'
                WHERE "Name" = 'Eye Infection';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases'
                WHERE "Name" = 'Endocarditis';
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral'
                WHERE "Name" = 'Fibromyalgia';
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs'
                WHERE "Name" = 'Dwarfism';
                """);

            migrationBuilder.Sql("""
                UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases'
                WHERE "Name" = 'Eye Infection';
                """);
        }
    }
}
