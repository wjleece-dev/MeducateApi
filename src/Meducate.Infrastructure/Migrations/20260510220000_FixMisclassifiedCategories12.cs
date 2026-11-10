using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Influenza: ICD-10 J09-J11 = Influenza = Respiratory System (Chapter X), not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Influenza';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Influenza';""");
        }
    }
}
