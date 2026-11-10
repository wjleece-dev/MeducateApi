using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deep Vein Thrombosis: ICD-10 I80-I82 = Circulatory System, not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Circulatory System' WHERE "Name" = 'Deep Vein Thrombosis';""");

            // Dehydration: ICD-10 E86 = Volume depletion = Endocrine, Nutritional & Metabolic, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Dehydration';""");

            // Tremor: ICD-10 G25 = Other extrapyramidal disorders = Nervous System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Tremor';""");

            // Tuberous Sclerosis: ICD-10 Q85.1 = Perinatal & Congenital (phakomatoses), not Neoplasms
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital' WHERE "Name" = 'Tuberous Sclerosis';""");

            // Vasculitis: ICD-10 M30-M31 = Musculoskeletal & Connective Tissue (vasculitis group), not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Musculoskeletal & Connective Tissue' WHERE "Name" = 'Vasculitis';""");

            // Warts: ICD-10 B07 = Viral warts = Infectious & Parasitic Diseases (HPV), not Skin & Subcutaneous Tissue
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Warts';""");

            // Wilson Disease: ICD-10 E83.0 = Disorder of copper metabolism = Endocrine, Nutritional & Metabolic, not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Wilson Disease';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Deep Vein Thrombosis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Dehydration';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Tremor';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Neoplasms' WHERE "Name" = 'Tuberous Sclerosis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Vasculitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Warts';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Wilson Disease';""");
        }
    }
}
