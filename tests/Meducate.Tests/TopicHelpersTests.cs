using Meducate.Application.Helpers;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Tests;

public class TopicHelpersTests
{
    [Fact]
    public void CheckTopicQuality_ReturnsNull_ForValidTopic()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            Summary = new string('A', 100),
            Observations = ["Increased thirst"],
            Factors = ["Genetics"],
            Actions = ["Monitor blood sugar"]
        };

        Assert.Null(TopicHelpers.CheckTopicQuality(topic));
    }

    [Fact]
    public void CheckTopicQuality_ReturnsSummaryTooShort()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            Summary = "Short",
            Observations = ["Obs"]
        };

        var result = TopicHelpers.CheckTopicQuality(topic);

        Assert.NotNull(result);
        Assert.Contains("summary too short", result);
    }

    [Fact]
    public void CheckTopicQuality_ReturnsSummaryRestatesName()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            Summary = new string(' ', 5) + "Diabetes" + new string(' ', TopicHelpers.MinSummaryLength),
            Observations = ["Obs"]
        };

        var result = TopicHelpers.CheckTopicQuality(topic);

        Assert.NotNull(result);
        Assert.Contains("restates", result);
    }

    [Fact]
    public void CheckTopicQuality_ReturnsNoContent_WhenEmpty()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            Summary = new string('A', 100)
        };

        var result = TopicHelpers.CheckTopicQuality(topic);

        Assert.NotNull(result);
        Assert.Contains("no observations", result);
    }

    [Fact]
    public void CheckTopicQuality_ReturnsError_WhenFactorsMissing()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            Summary = new string('A', 100),
            Observations = ["Increased thirst"],
            Actions = ["Monitor blood sugar"]
        };

        var result = TopicHelpers.CheckTopicQuality(topic);

        Assert.NotNull(result);
        Assert.Contains("no factors", result);
    }

    [Fact]
    public void CheckTopicQuality_ReturnsError_WhenActionsMissing()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            Summary = new string('A', 100),
            Observations = ["Increased thirst"],
            Factors = ["Genetics"]
        };

        var result = TopicHelpers.CheckTopicQuality(topic);

        Assert.NotNull(result);
        Assert.Contains("no actions", result);
    }

    [Fact]
    public void CheckTopicQuality_ReturnsNull_ForDiagnosticTest_WithEmptyObservationsAndFactors()
    {
        // The extraction prompt tells the LLM these are "otherwise []" for this type --
        // an empty array here is a valid answer, not a sign of a bad extraction.
        var topic = new HealthTopic
        {
            Name = "Urinalysis",
            TopicType = "Diagnostic Test",
            Summary = new string('A', 100),
            Observations = [],
            Factors = [],
            Actions = ["Check for urinary tract infection"]
        };

        Assert.Null(TopicHelpers.CheckTopicQuality(topic));
    }

    [Fact]
    public void CheckTopicQuality_StillRejectsEmptyActions_ForDiagnosticTest()
    {
        // Actions ("what it treats or tests for") is NOT documented as "otherwise []"
        // for this type, so it should still be enforced.
        var topic = new HealthTopic
        {
            Name = "Urinalysis",
            TopicType = "Diagnostic Test",
            Summary = new string('A', 100),
            Observations = [],
            Factors = [],
            Actions = []
        };

        var result = TopicHelpers.CheckTopicQuality(topic);

        Assert.NotNull(result);
        Assert.Contains("no actions", result);
    }

    [Fact]
    public void CheckTopicQuality_ReturnsNull_ForNutrient_WithEmptyObservations()
    {
        // Short glossary-style nutrient definitions often just define the substance
        // without stating deficiency/excess symptoms -- empty here is expected too.
        var topic = new HealthTopic
        {
            Name = "Folate",
            TopicType = "Nutrient",
            Summary = new string('A', 100),
            Observations = [],
            Factors = ["Leafy greens", "Fortified grains"],
            Actions = ["Prevents neural tube defects in pregnancy"]
        };

        Assert.Null(TopicHelpers.CheckTopicQuality(topic));
    }

    [Fact]
    public void BuildMergedRawSource_MergesMultipleSources()
    {
        var sources = new[]
        {
            new RawTopicData("Topic", "Source one text", "MedlinePlus"),
            new RawTopicData("Topic", "Source two text", "PubMed")
        };

        var result = TopicHelpers.BuildMergedRawSource(sources);

        Assert.Contains("[MedlinePlus]", result);
        Assert.Contains("[PubMed]", result);
        Assert.Contains("Source one text", result);
        Assert.Contains("Source two text", result);
        Assert.Contains("---", result);
    }

    [Fact]
    public void BuildMergedRawSource_TruncatesLongSources()
    {
        var longText = new string('x', TopicHelpers.MaxCharsPerSource + 1000);
        var sources = new[]
        {
            new RawTopicData("Topic", longText, "Source")
        };

        var result = TopicHelpers.BuildMergedRawSource(sources);

        Assert.True(result.Length < longText.Length);
    }

    [Fact]
    public void BuildMergedRawSource_PrependsGroupNames()
    {
        var sources = new[]
        {
            new RawTopicData("Topic", "text", "MedlinePlus", Groups: ["Heart Diseases", "Cardiology"])
        };

        var result = TopicHelpers.BuildMergedRawSource(sources);

        Assert.StartsWith("[MedlinePlus Groups: Heart Diseases, Cardiology]", result);
    }

    [Fact]
    public void ToTitleCase_CapitalizesFirstLetter()
    {
        Assert.Equal("Hello World", TopicHelpers.ToTitleCase("hello world"));
    }

    [Fact]
    public void ToTitleCase_PreservesAllCapsAcronyms()
    {
        Assert.Equal("HIV AIDS", TopicHelpers.ToTitleCase("HIV AIDS"));
    }

    [Fact]
    public void ToTitleCase_HandlesSlashSeparatedWords()
    {
        Assert.Equal("Ocd/Anxiety", TopicHelpers.ToTitleCase("ocd/anxiety"));
    }
}
