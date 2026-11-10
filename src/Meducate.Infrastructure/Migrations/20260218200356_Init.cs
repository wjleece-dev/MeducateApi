using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Observations = table.Column<string>(type: "jsonb", nullable: true),
                    Factors = table.Column<string>(type: "jsonb", nullable: true),
                    Actions = table.Column<string>(type: "jsonb", nullable: true),
                    Citations = table.Column<string>(type: "jsonb", nullable: true),
                    RawSource = table.Column<string>(type: "text", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TopicType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LastSourceRefresh = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NeedsLlmReprocessing = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthTopics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    IsEmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerificationToken = table.Column<string>(type: "text", nullable: true),
                    VerificationTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Organisations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DailyLimit = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    HashedSecret = table.Column<string>(type: "text", nullable: false),
                    Salt = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiClients_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    OrganisationName = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiUsageLogs_ApiClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "ApiClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "HealthTopics",
                columns: new[] { "Id", "Actions", "Category", "Citations", "Factors", "LastSourceRefresh", "LastUpdated", "Name", "NeedsLlmReprocessing", "Observations", "RawSource", "Summary", "Tags", "TopicType", "Version" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000001"), "[\"Short-acting bronchodilators (salbutamol)\",\"Inhaled corticosteroids\",\"Long-acting beta-agonists (LABAs)\",\"Leukotriene receptor antagonists\",\"Allergen avoidance strategies\"]", "Respiratory", "[\"NICE Guideline NG80 \\u2013 Asthma: diagnosis, monitoring and chronic asthma management\",\"British Thoracic Society / SIGN \\u2013 British guideline on the management of asthma\"]", "[\"Genetic predisposition\",\"Allergens (pollen, dust mites, pet dander)\",\"Respiratory infections in childhood\",\"Air pollution\",\"Occupational irritants\"]", null, new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Asthma", false, "[\"Wheezing\",\"Shortness of breath\",\"Chest tightness\",\"Coughing (especially at night or early morning)\",\"Difficulty breathing during exercise\"]", null, "A chronic respiratory condition characterised by inflammation and narrowing of the airways, leading to recurrent episodes of wheezing, breathlessness, chest tightness, and coughing.", "[\"chronic\",\"airways\",\"inflammation\",\"common\"]", "Disease", 1 },
                    { new Guid("a1000000-0000-0000-0000-000000000002"), "[\"Lifestyle modifications (diet and exercise)\",\"Metformin\",\"SGLT2 inhibitors (e.g. dapagliflozin)\",\"GLP-1 receptor agonists (e.g. semaglutide)\",\"Insulin therapy (if required)\"]", "Endocrine", "[\"NICE Guideline NG28 \\u2013 Type 2 diabetes in adults: management\",\"WHO \\u2013 Diabetes fact sheet\"]", "[\"Obesity and excess body fat\",\"Physical inactivity\",\"Genetic predisposition\",\"Age (risk increases over 40)\",\"Ethnicity (higher risk in South Asian, African-Caribbean populations)\"]", null, new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Type 2 Diabetes", false, "[\"Increased thirst (polydipsia)\",\"Frequent urination (polyuria)\",\"Unexplained weight loss\",\"Fatigue\",\"Blurred vision\",\"Slow-healing wounds\"]", null, "A metabolic disorder characterised by insulin resistance and relative insulin deficiency, resulting in chronic hyperglycaemia. It is strongly associated with obesity and physical inactivity.", "[\"chronic\",\"metabolic\",\"common\",\"lifestyle\"]", "Disease", 1 },
                    { new Guid("a1000000-0000-0000-0000-000000000003"), "[\"Lifestyle changes (reduced salt, exercise, weight loss)\",\"ACE inhibitors (e.g. ramipril)\",\"Calcium channel blockers (e.g. amlodipine)\",\"Thiazide-like diuretics (e.g. indapamide)\",\"Angiotensin II receptor blockers (e.g. losartan)\"]", "Cardiovascular", "[\"NICE Guideline NG136 \\u2013 Hypertension in adults: diagnosis and management\",\"ESC/ESH 2018 Guidelines for the management of arterial hypertension\"]", "[\"Excess dietary sodium\",\"Obesity\",\"Physical inactivity\",\"Excessive alcohol consumption\",\"Genetic factors\",\"Chronic stress\"]", null, new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Hypertension", false, "[\"Usually asymptomatic (silent condition)\",\"Headaches (in severe cases)\",\"Visual disturbances\",\"Chest pain (hypertensive emergency)\",\"Nosebleeds (rare)\"]", null, "Persistently elevated arterial blood pressure, typically defined as systolic ≥140 mmHg and/or diastolic ≥90 mmHg. A major risk factor for cardiovascular disease, stroke, and chronic kidney disease.", "[\"chronic\",\"silent\",\"cardiovascular\",\"common\"]", "Disease", 1 },
                    { new Guid("a1000000-0000-0000-0000-000000000004"), "[\"Acute: triptans (e.g. sumatriptan)\",\"Acute: NSAIDs (e.g. ibuprofen, aspirin)\",\"Preventive: beta-blockers (e.g. propranolol)\",\"Preventive: topiramate\",\"CGRP monoclonal antibodies (e.g. erenumab)\"]", "Neurological", "[\"NICE Guideline CG150 \\u2013 Headaches in over 12s: diagnosis and management\",\"The Migraine Trust \\u2013 Clinical resources\"]", "[\"Genetic predisposition\",\"Hormonal changes (e.g. menstruation)\",\"Stress and anxiety\",\"Certain foods (aged cheese, alcohol, caffeine)\",\"Sleep disturbances\"]", null, new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Migraine", false, "[\"Severe throbbing headache (often unilateral)\",\"Nausea and vomiting\",\"Photophobia (sensitivity to light)\",\"Phonophobia (sensitivity to sound)\",\"Aura (visual disturbances, tingling) in some patients\"]", null, "A primary headache disorder characterised by recurrent episodes of moderate to severe unilateral throbbing headache, often accompanied by nausea, vomiting, and sensitivity to light and sound.", "[\"episodic\",\"headache\",\"neurological\",\"common\"]", "Disease", 1 },
                    { new Guid("a1000000-0000-0000-0000-000000000005"), "[\"Cognitive behavioural therapy (CBT)\",\"Selective serotonin reuptake inhibitors (SSRIs, e.g. sertraline)\",\"Serotonin-noradrenaline reuptake inhibitors (SNRIs)\",\"Physical exercise programmes\",\"Mindfulness-based cognitive therapy\"]", "Mental Health", "[\"NICE Guideline NG222 \\u2013 Depression in adults: treatment and management\",\"WHO \\u2013 Depression fact sheet\"]", "[\"Biological factors (neurotransmitter imbalances)\",\"Genetic predisposition\",\"Traumatic or stressful life events\",\"Chronic illness\",\"Social isolation\"]", null, new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Depression", false, "[\"Persistent low mood\",\"Loss of interest or pleasure (anhedonia)\",\"Fatigue and low energy\",\"Sleep disturbances (insomnia or hypersomnia)\",\"Changes in appetite or weight\",\"Difficulty concentrating\",\"Feelings of worthlessness or guilt\"]", null, "A common mental health disorder characterised by persistent low mood, loss of interest or pleasure in activities, and a range of emotional and physical symptoms lasting at least two weeks.", "[\"chronic\",\"mental health\",\"common\",\"mood disorder\"]", "Disease", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_KeyId",
                table: "ApiClients",
                column: "KeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_OrganisationId",
                table: "ApiClients",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiUsageLogs_ClientId_Timestamp",
                table: "ApiUsageLogs",
                columns: new[] { "ClientId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthTopics_Name",
                table: "HealthTopics",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_HealthTopics_TopicType",
                table: "HealthTopics",
                column: "TopicType");

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_UserId",
                table: "Organisations",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_VerificationToken",
                table: "Users",
                column: "VerificationToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiUsageLogs");

            migrationBuilder.DropTable(
                name: "HealthTopics");

            migrationBuilder.DropTable(
                name: "ApiClients");

            migrationBuilder.DropTable(
                name: "Organisations");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
