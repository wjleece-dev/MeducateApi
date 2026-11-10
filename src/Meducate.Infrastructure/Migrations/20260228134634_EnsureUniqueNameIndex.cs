using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnsureUniqueNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealthTopics_Name",
                table: "HealthTopics");

            migrationBuilder.CreateIndex(
                name: "IX_HealthTopics_Name",
                table: "HealthTopics",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealthTopics_Name",
                table: "HealthTopics");

            migrationBuilder.CreateIndex(
                name: "IX_HealthTopics_Name",
                table: "HealthTopics",
                column: "Name");
        }
    }
}
