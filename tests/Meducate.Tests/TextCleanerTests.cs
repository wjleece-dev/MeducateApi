using Meducate.Application.Helpers;
using Meducate.Domain.Entities;

namespace Meducate.Tests;

public class TextCleanerTests
{
    [Fact]
    public void Clean_DecodesEntitiesAndCollapsesWhitespace()
    {
        Assert.Equal("a child's underwear & more", TextCleaner.Clean("a child&#39;s  underwear &amp; more "));
        Assert.Equal("it's", TextCleaner.Clean("it&amp;#39;s"));
    }

    [Fact]
    public void CleanItem_DropsLoneTrailingFullStopOnly()
    {
        Assert.Equal("Embolization to reduce blood flow", TextCleaner.CleanItem("Embolization to reduce blood flow."));
        Assert.Equal("Take one. Then rest.", TextCleaner.CleanItem("Take one. Then rest."));
    }

    [Fact]
    public void CleanSummary_DropsCopiedFooterAndMetaSentences()
    {
        var cleaned = TextCleaner.CleanSummary(
            "Intestinal cancer is rare. Symptoms include pain. NIH: National Cancer Institute");
        Assert.Equal("Intestinal cancer is rare. Symptoms include pain.", cleaned);

        var meta = TextCleaner.CleanSummary(
            "Selenium is a mineral. Specific deficiency symptoms are not detailed in the source. It is found in foods.");
        Assert.Equal("Selenium is a mineral. It is found in foods.", meta);
    }

    [Fact]
    public void CleanSummary_KeepsOrdinaryUsesOfTheWordSource()
    {
        const string text = "Treatment removes the source of the infection. Endoscopy finds the source of bleeding.";
        Assert.Equal(text, TextCleaner.CleanSummary(text));
        Assert.False(TextCleaner.HasMetaSentence(text));
    }

    [Fact]
    public void CleanTags_DedupesLowercasesAndCapsAtEight()
    {
        var tags = TextCleaner.CleanTags(["Asthma", "asthma", "A", "B", "C", "D", "E", "F", "G", "H", "I"]);
        Assert.Equal(8, tags.Count);
        Assert.Equal("asthma", tags[0]);
    }

    [Fact]
    public void WithoutAftercare_DropsRecoveryStepsFromProceduresAndTestsOnly()
    {
        List<string> items = ["Chest pain from blocked arteries", "Take prescribed medications to prevent blood clots", "Follow-up appointment with cardiologist"];

        Assert.Equal(["Chest pain from blocked arteries"], TextCleaner.WithoutAftercare("Procedure", items));
        Assert.Equal(["Chest pain from blocked arteries"], TextCleaner.WithoutAftercare("Diagnostic Test", items));
        Assert.Equal(items, TextCleaner.WithoutAftercare("Disease", items));
    }

    [Theory]
    [InlineData("HIV: PrEP and PEP", "HIV: PrEP And PEP")]
    [InlineData("high blood pressure", "High Blood Pressure")]
    [InlineData("COPD", "COPD")]
    public void ToTitleCase_KeepsMixedCaseAcronyms(string input, string expected) =>
        Assert.Equal(expected, TopicHelpers.ToTitleCase(input));

    [Fact]
    public void SourceOrganisation_ReadsProviderTag()
    {
        Assert.Equal("National Heart, Lung, and Blood Institute",
            TextCleaner.SourceOrganisation("[MedlinePlus]\n[Source: National Heart, Lung, and Blood Institute]\n\nText"));
        Assert.Equal("MedlinePlus, U.S. National Library of Medicine", TextCleaner.SourceOrganisation("[MedlinePlus] Some text"));
        Assert.Equal("PubMed, U.S. National Library of Medicine", TextCleaner.SourceOrganisation("[PubMed] abstract text"));
        Assert.Null(TextCleaner.SourceOrganisation("no tag here"));
    }

    [Fact]
    public void SanitizeTopic_FixesStoredDefectsAndIsIdempotent()
    {
        var topic = new HealthTopic
        {
            Name = "Constipation",
            Summary = "Constipation is common. NIH: National Cancer Institute",
            Observations = ["A child&#39;s underwear", "a child's underwear", "Pain."],
            Factors = [],
            Actions = [],
            Tags = ["a", "b", "c", "d", "e", "f", "g", "h", "i"],
            Citations = [],
            RawSource = "[Source: National Cancer Institute]",
        };

        Assert.True(TextCleaner.SanitizeTopic(topic));
        Assert.Equal("Constipation is common.", topic.Summary);
        Assert.Equal(["A child's underwear", "Pain"], topic.Observations);
        Assert.Equal(8, topic.Tags!.Count);
        Assert.Equal(["National Cancer Institute"], topic.Citations);

        Assert.False(TextCleaner.SanitizeTopic(topic));
    }

    [Theory]
    [InlineData("Follow-up appointment with cardiologist", true)]
    [InlineData("Take prescribed medicines to prevent blood clots", true)]
    [InlineData("Avoid swimming for 2 weeks after surgery", true)]
    [InlineData("Pain medicines for recovery after surgery", true)]
    [InlineData("Chest pain from blocked coronary arteries", false)]
    [InlineData("Nausea and vomiting after surgery", false)]
    [InlineData("Colorectal cancer", false)]
    public void IsAftercareItem_SeparatesAftercareFromConditionsTreated(string item, bool expected) =>
        Assert.Equal(expected, TopicHelpers.IsAftercareItem(item));

    [Fact]
    public void CheckTopicQuality_FlagsAftercareOnlyForProceduresAndTests()
    {
        HealthTopic Topic(string type) => new()
        {
            Name = "Hysterectomy",
            TopicType = type,
            Summary = new string('x', 150),
            Observations = ["Bleeding"],
            Factors = ["Fibroids"],
            Actions = ["Pain medicines for recovery after surgery"],
        };

        Assert.Contains("aftercare", TopicHelpers.CheckTopicQuality(Topic("Procedure")));
        Assert.Contains("aftercare", TopicHelpers.CheckTopicQuality(Topic("Diagnostic Test")));
        Assert.Null(TopicHelpers.CheckTopicQuality(Topic("Disease")));
    }
}
