using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Prostate Diseases: ICD-10 N40-N42 = Diseases of prostate = Genitourinary System (Chapter XIV), not Neoplasms
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Prostate Diseases';""");

            // Throat Disorders: ICD-10 J39 = Other diseases of upper respiratory tract = Respiratory System (Chapter X), not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Throat Disorders';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Neoplasms' WHERE "Name" = 'Prostate Diseases';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Throat Disorders';""");
        }
    }
}
