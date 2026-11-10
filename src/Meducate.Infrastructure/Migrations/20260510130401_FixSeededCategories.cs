using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixSeededCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "Category",
                value: "Respiratory System");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "Category",
                value: "Endocrine, Nutritional & Metabolic");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "Category",
                value: "Circulatory System");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "Category",
                value: "Nervous System");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "Category",
                value: "Mental & Behavioral");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "Category",
                value: "Respiratory");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "Category",
                value: "Endocrine");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "Category",
                value: "Cardiovascular");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "Category",
                value: "Neurological");

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "Category",
                value: "Mental Health");
        }
    }
}
