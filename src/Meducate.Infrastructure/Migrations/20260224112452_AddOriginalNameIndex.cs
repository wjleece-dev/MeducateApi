using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HealthTopics_OriginalName",
                table: "HealthTopics",
                column: "OriginalName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HealthTopics_OriginalName",
                table: "HealthTopics");
        }
    }
}
