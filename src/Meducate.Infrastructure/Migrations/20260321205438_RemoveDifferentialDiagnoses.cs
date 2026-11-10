using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDifferentialDiagnoses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'HealthTopics' AND column_name = 'DifferentialDiagnoses'
                    ) THEN
                        ALTER TABLE "HealthTopics" DROP COLUMN "DifferentialDiagnoses";
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260317143347_AddDifferentialDiagnoses';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DifferentialDiagnoses",
                table: "HealthTopics",
                type: "jsonb",
                nullable: true);
        }
    }
}
