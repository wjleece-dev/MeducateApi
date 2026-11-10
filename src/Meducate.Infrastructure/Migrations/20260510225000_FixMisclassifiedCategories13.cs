using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Abnormal Vaginal Bleeding: ICD-10 N93 = Other abnormal uterine and vaginal bleeding = Genitourinary System (Chapter XIV), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Abnormal Vaginal Bleeding';""");

            // Insect Bites And Stings: ICD-10 T63 = Toxic effects of contact with venomous animals/insects = Injury & Poisoning (Chapter XIX), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Insect Bites And Stings';""");

            // Malabsorption Syndrome: ICD-10 K90 = Intestinal malabsorption = Digestive System (Chapter XI), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Malabsorption Syndrome';""");

            // Abscess: ICD-10 L02 = Cutaneous abscess = Skin & Subcutaneous Tissue (Chapter XII); abscesses classified by site not cause in ICD-10, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Abscess';""");

            // Blood Clots: ICD-10 I82 = Other venous embolism and thrombosis = Circulatory System (Chapter IX), not Blood & Immune System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Circulatory System' WHERE "Name" = 'Blood Clots';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Abnormal Vaginal Bleeding';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Insect Bites And Stings';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Malabsorption Syndrome';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Abscess';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Blood & Immune System' WHERE "Name" = 'Blood Clots';""");
        }
    }
}
