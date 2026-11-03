using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meducate.Infrastructure.Persistence;

internal class MeducateDbContext(DbContextOptions<MeducateDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(MeducateDbContext).Assembly);

        SeedTopics(builder);
    }

    private static void SeedTopics(ModelBuilder builder)
    {
        var seedDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);

        builder.Entity<HealthTopic>().HasData(
            new HealthTopic
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000001"),
                Name = "Asthma",
                Summary = "A chronic respiratory condition characterised by inflammation and narrowing of the airways, leading to recurrent episodes of wheezing, breathlessness, chest tightness, and coughing.",
                Observations = new List<string> { "Wheezing", "Shortness of breath", "Chest tightness", "Coughing (especially at night or early morning)", "Difficulty breathing during exercise" },
                Factors = new List<string> { "Genetic predisposition", "Allergens (pollen, dust mites, pet dander)", "Respiratory infections in childhood", "Air pollution", "Occupational irritants" },
                Actions = new List<string> { "Short-acting bronchodilators (salbutamol)", "Inhaled corticosteroids", "Long-acting beta-agonists (LABAs)", "Leukotriene receptor antagonists", "Allergen avoidance strategies" },
                Citations = new List<string> { "NICE Guideline NG80 – Asthma: diagnosis, monitoring and chronic asthma management", "British Thoracic Society / SIGN – British guideline on the management of asthma" },
                Category = "Respiratory System",
                TopicType = "Disease",
                Tags = new List<string> { "chronic", "airways", "inflammation", "common" },
                LastUpdated = seedDate,
                Version = 1
            },
            new HealthTopic
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000002"),
                Name = "Type 2 Diabetes",
                Summary = "A metabolic disorder characterised by insulin resistance and relative insulin deficiency, resulting in chronic hyperglycaemia. It is strongly associated with obesity and physical inactivity.",
                Observations = new List<string> { "Increased thirst (polydipsia)", "Frequent urination (polyuria)", "Unexplained weight loss", "Fatigue", "Blurred vision", "Slow-healing wounds" },
                Factors = new List<string> { "Obesity and excess body fat", "Physical inactivity", "Genetic predisposition", "Age (risk increases over 40)", "Ethnicity (higher risk in South Asian, African-Caribbean populations)" },
                Actions = new List<string> { "Lifestyle modifications (diet and exercise)", "Metformin", "SGLT2 inhibitors (e.g. dapagliflozin)", "GLP-1 receptor agonists (e.g. semaglutide)", "Insulin therapy (if required)" },
                Citations = new List<string> { "NICE Guideline NG28 – Type 2 diabetes in adults: management", "WHO – Diabetes fact sheet" },
                Category = "Endocrine, Nutritional & Metabolic",
                TopicType = "Disease",
                Tags = new List<string> { "chronic", "metabolic", "common", "lifestyle" },
                LastUpdated = seedDate,
                Version = 1
            },
            new HealthTopic
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000003"),
                Name = "Hypertension",
                Summary = "Persistently elevated arterial blood pressure, typically defined as systolic \u2265140 mmHg and/or diastolic \u226590 mmHg. A major risk factor for cardiovascular disease, stroke, and chronic kidney disease.",
                Observations = new List<string> { "Usually asymptomatic (silent condition)", "Headaches (in severe cases)", "Visual disturbances", "Chest pain (hypertensive emergency)", "Nosebleeds (rare)" },
                Factors = new List<string> { "Excess dietary sodium", "Obesity", "Physical inactivity", "Excessive alcohol consumption", "Genetic factors", "Chronic stress" },
                Actions = new List<string> { "Lifestyle changes (reduced salt, exercise, weight loss)", "ACE inhibitors (e.g. ramipril)", "Calcium channel blockers (e.g. amlodipine)", "Thiazide-like diuretics (e.g. indapamide)", "Angiotensin II receptor blockers (e.g. losartan)" },
                Citations = new List<string> { "NICE Guideline NG136 – Hypertension in adults: diagnosis and management", "ESC/ESH 2018 Guidelines for the management of arterial hypertension" },
                Category = "Circulatory System",
                TopicType = "Disease",
                Tags = new List<string> { "chronic", "silent", "cardiovascular", "common" },
                LastUpdated = seedDate,
                Version = 1
            },
            new HealthTopic
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000004"),
                Name = "Migraine",
                Summary = "A primary headache disorder characterised by recurrent episodes of moderate to severe unilateral throbbing headache, often accompanied by nausea, vomiting, and sensitivity to light and sound.",
                Observations = new List<string> { "Severe throbbing headache (often unilateral)", "Nausea and vomiting", "Photophobia (sensitivity to light)", "Phonophobia (sensitivity to sound)", "Aura (visual disturbances, tingling) in some patients" },
                Factors = new List<string> { "Genetic predisposition", "Hormonal changes (e.g. menstruation)", "Stress and anxiety", "Certain foods (aged cheese, alcohol, caffeine)", "Sleep disturbances" },
                Actions = new List<string> { "Acute: triptans (e.g. sumatriptan)", "Acute: NSAIDs (e.g. ibuprofen, aspirin)", "Preventive: beta-blockers (e.g. propranolol)", "Preventive: topiramate", "CGRP monoclonal antibodies (e.g. erenumab)" },
                Citations = new List<string> { "NICE Guideline CG150 – Headaches in over 12s: diagnosis and management", "The Migraine Trust – Clinical resources" },
                Category = "Nervous System",
                TopicType = "Disease",
                Tags = new List<string> { "episodic", "headache", "neurological", "common" },
                LastUpdated = seedDate,
                Version = 1
            },
            new HealthTopic
            {
                Id = new Guid("a1000000-0000-0000-0000-000000000005"),
                Name = "Depression",
                Summary = "A common mental health disorder characterised by persistent low mood, loss of interest or pleasure in activities, and a range of emotional and physical symptoms lasting at least two weeks.",
                Observations = new List<string> { "Persistent low mood", "Loss of interest or pleasure (anhedonia)", "Fatigue and low energy", "Sleep disturbances (insomnia or hypersomnia)", "Changes in appetite or weight", "Difficulty concentrating", "Feelings of worthlessness or guilt" },
                Factors = new List<string> { "Biological factors (neurotransmitter imbalances)", "Genetic predisposition", "Traumatic or stressful life events", "Chronic illness", "Social isolation" },
                Actions = new List<string> { "Cognitive behavioural therapy (CBT)", "Selective serotonin reuptake inhibitors (SSRIs, e.g. sertraline)", "Serotonin-noradrenaline reuptake inhibitors (SNRIs)", "Physical exercise programmes", "Mindfulness-based cognitive therapy" },
                Citations = new List<string> { "NICE Guideline NG222 – Depression in adults: treatment and management", "WHO – Depression fact sheet" },
                Category = "Mental & Behavioral",
                TopicType = "Disease",
                Tags = new List<string> { "chronic", "mental health", "common", "mood disorder" },
                LastUpdated = seedDate,
                Version = 1
            }
        );
    }

    public DbSet<ApiClient> ApiClients { get; set; } = default!;
    public DbSet<ApiUsageLog> ApiUsageLogs { get; set; } = default!;

    public DbSet<HealthTopic> HealthTopics { get; set; } = default!;
    public DbSet<Organisation> Organisations { get; set; } = default!;
    public DbSet<SeenTopic> SeenTopics { get; set; } = default!;
    public DbSet<User> Users { get; set; } = default!;
    public DbSet<WaitlistEntry> WaitlistEntries { get; set; } = default!;
}
