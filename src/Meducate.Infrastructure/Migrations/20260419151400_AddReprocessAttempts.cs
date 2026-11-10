using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReprocessAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReprocessAttempts",
                table: "HealthTopics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "ReprocessAttempts",
                value: 0);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "ReprocessAttempts",
                value: 0);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "ReprocessAttempts",
                value: 0);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "ReprocessAttempts",
                value: 0);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "ReprocessAttempts",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReprocessAttempts",
                table: "HealthTopics");
        }
    }
}
