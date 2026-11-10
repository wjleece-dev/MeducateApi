using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceHasg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceHash",
                table: "HealthTopics",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "SourceHash",
                value: null);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "SourceHash",
                value: null);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "SourceHash",
                value: null);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "SourceHash",
                value: null);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "SourceHash",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "HealthTopics");
        }
    }
}
