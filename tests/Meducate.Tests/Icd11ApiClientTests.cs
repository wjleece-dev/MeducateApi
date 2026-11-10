using Meducate.Infrastructure.Icd11;

namespace Meducate.Tests;

public class Icd11ApiClientTests
{
    [Fact]
    public void ParseSearchResponse_AcceptsAnEntityWhoseTitleIsTheTopic()
    {
        var json = """
        {
          "destinationEntities": [
            { "title": "<em class='found'>Unspecified</em> asthma, uncomplicated", "theCode": "CA23.32" },
            { "title": "<em class='found'>Asthma</em>", "theCode": "CA23" }
          ]
        }
        """;

        var match = Icd11ApiClient.ParseSearchResponse(json, "Asthma");

        Assert.NotNull(match);
        Assert.Equal("CA23", match!.Code);
        Assert.Equal("Asthma", match.Title);
    }

    [Fact]
    public void ParseSearchResponse_RejectsARelevantButDifferentCondition()
    {
        // WHO ranks "Secondary anxiety syndrome" above generalised anxiety disorder for "Anxiety Disorder".
        var json = """
        {
          "destinationEntities": [
            { "title": "Secondary anxiety syndrome", "theCode": "6E63", "matchingPVs": [ { "label": "Organic anxiety disorder" } ] },
            { "title": "Generalised anxiety disorder", "theCode": "6B00", "matchingPVs": [ { "label": "GAD - [generalised anxiety disorder]" } ] }
          ]
        }
        """;

        Assert.Null(Icd11ApiClient.ParseSearchResponse(json, "Anxiety Disorder"));
    }

    [Fact]
    public void ParseSearchResponse_AcceptsAListedSynonymAndIgnoresSpellingAndPossessives()
    {
        var json = """
        {
          "destinationEntities": [
            { "title": "Hemorrhoids, unspecified", "theCode": "DB60.Z", "matchingPVs": [ { "label": "Haemorrhoids" } ] },
            { "title": "Alzheimer disease", "theCode": "8A20" }
          ]
        }
        """;

        Assert.Equal("DB60.Z", Icd11ApiClient.ParseSearchResponse(json, "Hemorrhoids")!.Code);
        Assert.Equal("8A20", Icd11ApiClient.ParseSearchResponse(json, "Alzheimer's Disease")!.Code);
    }

    [Fact]
    public void ParseSearchResponse_AcceptsTheTopicNamePlusOnlyResidualWording()
    {
        var json = """
        {
          "destinationEntities": [
            { "title": "Hypothyroidism, unspecified", "theCode": "5A00.Z" }
          ]
        }
        """;

        Assert.Equal("5A00.Z", Icd11ApiClient.ParseSearchResponse(json, "Hypothyroidism")!.Code);
    }

    [Theory]
    [InlineData("Gas", "Granuloma annulare", "EE80.0")]
    [InlineData("G6PD Deficiency", "Glycogen storage disease", "5C51.3")]
    public void ParseSearchResponse_RejectsUnrelatedTopRankedResults(string topic, string title, string code)
    {
        var json = $$"""{ "destinationEntities": [ { "title": "{{title}}", "theCode": "{{code}}" } ] }""";

        Assert.Null(Icd11ApiClient.ParseSearchResponse(json, topic));
    }

    [Fact]
    public void ParseSearchResponse_SkipsChaptersBlocksAndClusterCodes()
    {
        var json = """
        {
          "destinationEntities": [
            { "title": "Cholera" },
            { "title": "Cholera", "theCode": "1A00&XN4TT" },
            { "title": "Cholera", "theCode": "1A00" }
          ]
        }
        """;

        Assert.Equal("1A00", Icd11ApiClient.ParseSearchResponse(json, "Cholera")!.Code);
    }

    [Fact]
    public void ParseSearchResponse_ReturnsNull_WhenNoEntitiesHaveACode()
    {
        var json = """{ "destinationEntities": [ { "title": "Certain infectious or parasitic diseases" } ] }""";

        Assert.Null(Icd11ApiClient.ParseSearchResponse(json, "Cholera"));
    }

    [Fact]
    public void ParseSearchResponse_ReturnsNull_WhenDestinationEntitiesIsEmpty()
    {
        Assert.Null(Icd11ApiClient.ParseSearchResponse("""{ "destinationEntities": [] }""", "Cholera"));
    }

    [Fact]
    public void ParseSearchResponse_ReturnsNull_WhenDestinationEntitiesIsMissing()
    {
        Assert.Null(Icd11ApiClient.ParseSearchResponse("""{ "words": [] }""", "Cholera"));
    }
}
