using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meducate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMisclassifiedCategories7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Diabetic Foot: ICD-10 E11.621 = Type 2 diabetes with foot ulcer = Endocrine, Nutritional & Metabolic, not Digestive System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Endocrine, Nutritional & Metabolic' WHERE "Name" = 'Diabetic Foot';""");

            // Dysmenorrhea: ICD-10 N94.4 = Primary dysmenorrhoea = Genitourinary System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Dysmenorrhea';""");

            // Heat Illness: ICD-10 T67 = Effects of heat and light = Injury & Poisoning, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Heat Illness';""");

            // Birth Weight: ICD-10 P07 = Disorders related to short gestation and low birth weight = Perinatal & Congenital, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital' WHERE "Name" = 'Birth Weight';""");

            // Hyperhidrosis: ICD-10 L74.5 = Focal hyperhidrosis = Skin & Subcutaneous Tissue, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Hyperhidrosis';""");

            // Cellulitis: ICD-10 L03 = Cellulitis and acute lymphangitis = Skin & Subcutaneous Tissue, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Skin & Subcutaneous Tissue' WHERE "Name" = 'Cellulitis';""");

            // Mosquito Bites: ICD-10 S00/T14 = superficial insect bite = Injury & Poisoning, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Mosquito Bites';""");

            // Meningitis: ICD-10 G00-G03 = Bacterial/viral meningitis = Nervous System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Meningitis';""");

            // Pelvic Inflammatory Disease: ICD-10 N70-N77 = Inflammatory disease of female pelvic organs = Genitourinary System, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Pelvic Inflammatory Disease';""");

            // Vaginitis: ICD-10 N76.0 = Acute vaginitis = Genitourinary System, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Vaginitis';""");

            // Voice Disorders: ICD-10 J38.3 = Other diseases of vocal cords = Respiratory System, not Nervous System
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Respiratory System' WHERE "Name" = 'Voice Disorders';""");

            // Von Hippel-Lindau Disease: ICD-10 Q85.8 = Other phakomatoses (hereditary congenital condition) = Perinatal & Congenital, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Perinatal & Congenital' WHERE "Name" = 'Von Hippel-Lindau Disease';""");

            // Menstruation: ICD-10 N92 = Excessive/frequent/irregular menstruation = Genitourinary System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Menstruation';""");

            // Canker Sore: ICD-10 K12.0 = Recurrent oral aphthae = Digestive System, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Canker Sore';""");

            // Urinary Tract Infection: ICD-10 N39.0 = UTI, site not specified = Genitourinary System, not Infectious & Parasitic Diseases
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Genitourinary System' WHERE "Name" = 'Urinary Tract Infection';""");

            // Mild Cognitive Impairment: ICD-10 G31.84 = Mild cognitive impairment = Nervous System, not Mental & Behavioral
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Mild Cognitive Impairment';""");

            // Motion Sickness: ICD-10 T75.3 = Motion sickness (effect of external cause) = Injury & Poisoning, not Symptoms & Signs
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Injury & Poisoning' WHERE "Name" = 'Motion Sickness';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Digestive System' WHERE "Name" = 'Diabetic Foot';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Dysmenorrhea';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Heat Illness';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Birth Weight';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Hyperhidrosis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Cellulitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Mosquito Bites';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Meningitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Pelvic Inflammatory Disease';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Vaginitis';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Nervous System' WHERE "Name" = 'Voice Disorders';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Von Hippel-Lindau Disease';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Menstruation';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Canker Sore';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Infectious & Parasitic Diseases' WHERE "Name" = 'Urinary Tract Infection';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Mental & Behavioral' WHERE "Name" = 'Mild Cognitive Impairment';""");
            migrationBuilder.Sql("""UPDATE "HealthTopics" SET "Category" = 'Symptoms & Signs' WHERE "Name" = 'Motion Sickness';""");
        }
    }
}
