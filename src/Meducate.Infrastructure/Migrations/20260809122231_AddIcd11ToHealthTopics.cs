using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIcd11ToHealthTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Icd11Code",
                table: "HealthTopics",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icd11Title",
                table: "HealthTopics",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                columns: new[] { "Icd11Code", "Icd11Title" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                columns: new[] { "Icd11Code", "Icd11Title" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                columns: new[] { "Icd11Code", "Icd11Title" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                columns: new[] { "Icd11Code", "Icd11Title" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "HealthTopics",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                columns: new[] { "Icd11Code", "Icd11Title" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icd11Code",
                table: "HealthTopics");

            migrationBuilder.DropColumn(
                name: "Icd11Title",
                table: "HealthTopics");
        }
    }
}
