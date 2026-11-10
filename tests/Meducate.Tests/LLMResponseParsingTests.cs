using Meducate.Infrastructure.DataProviders;
using Meducate.Infrastructure.LLM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Meducate.Tests;

public class LLMResponseParsingTests
{
    // --- SemanticKernelLLMProcessor.CleanupJson ---

    [Fact]
    public void CleanupJson_StripsCodeFences()
    {
        var result = SemanticKernelLLMProcessor.CleanupJson("```json\n{\"a\":1}\n```");

        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void CleanupJson_TrimsTextOutsideBraces()
    {
        var result = SemanticKernelLLMProcessor.CleanupJson("Here is the JSON: {\"a\":1} Hope that helps!");

        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void CleanupJson_BalancesMissingClosingBrace()
    {
        // Truncated mid-object, no trailing '}' at all — the balancing branch only
        // fires when the string doesn't already end with '}'.
        var result = SemanticKernelLLMProcessor.CleanupJson("{\"a\":\"b\"");

        Assert.Equal("{\"a\":\"b\"}", result);
    }

    [Fact]
    public void CleanupJson_ReturnsEmptyObject_ForUnparseableInput()
    {
        var result = SemanticKernelLLMProcessor.CleanupJson("not json at all");

        Assert.Equal("{}", result);
    }

    [Fact]
    public void CleanupJson_ReturnsEmptyObject_ForBlankInput()
    {
        var result = SemanticKernelLLMProcessor.CleanupJson("   ");

        Assert.Equal("{}", result);
    }

    // --- SemanticKernelLLMProcessor.IsValidTypeCategoryPair ---

    [Fact]
    public void IsValidTypeCategoryPair_ReturnsTrue_ForCorrectMandatoryMapping()
    {
        Assert.True(SemanticKernelLLMProcessor.IsValidTypeCategoryPair("Drug", "Drugs & Medications"));
    }

    [Fact]
    public void IsValidTypeCategoryPair_ReturnsFalse_ForWrongCategory_OnMandatoryType()
    {
        Assert.False(SemanticKernelLLMProcessor.IsValidTypeCategoryPair("Drug", "Nervous System"));
    }

    [Fact]
    public void IsValidTypeCategoryPair_ReturnsTrue_ForNonMandatoryType_RegardlessOfCategory()
    {
        Assert.True(SemanticKernelLLMProcessor.IsValidTypeCategoryPair("Disease", "Nervous System"));
    }

    // --- SemanticKernelLLMProcessor.TruncateToSentence ---

    [Fact]
    public void TruncateToSentence_ReturnsNull_ForBlankInput()
    {
        Assert.Null(SemanticKernelLLMProcessor.TruncateToSentence("   ", 50));
    }

    [Fact]
    public void TruncateToSentence_ReturnsTrimmedText_WhenUnderMaxLength()
    {
        Assert.Equal("Short text.", SemanticKernelLLMProcessor.TruncateToSentence(" Short text. ", 50));
    }

    [Fact]
    public void TruncateToSentence_CutsAtLastSentenceEnd_WithinMaxLength()
    {
        var text = "First sentence. Second sentence. Third sentence that runs long.";
        var result = SemanticKernelLLMProcessor.TruncateToSentence(text, 35);

        Assert.Equal("First sentence. Second sentence.", result);
    }

    [Fact]
    public void TruncateToSentence_FallsBackToLastSpace_WhenNoSentenceEnd()
    {
        var text = "word1 word2 word3 word4 word5";
        var result = SemanticKernelLLMProcessor.TruncateToSentence(text, 13);

        Assert.Equal("word1 word2", result);
    }

    // --- SemanticKernelLLMProcessor.ToSentenceCase / GetTypeInstructions ---

    [Fact]
    public void ToSentenceCase_CapitalizesFirstLetter()
    {
        Assert.Equal("Diabetes is common.", SemanticKernelLLMProcessor.ToSentenceCase("diabetes is common."));
    }

    [Fact]
    public void GetTypeInstructions_ReturnsDifferentText_ForKnownVsUnknownType()
    {
        var drugInstructions = SemanticKernelLLMProcessor.GetTypeInstructions("Drug");
        var defaultInstructions = SemanticKernelLLMProcessor.GetTypeInstructions("SomethingUnknown");

        Assert.Contains("DRUG/MEDICATION", drugInstructions);
        Assert.DoesNotContain("DRUG/MEDICATION", defaultInstructions);
    }

    // --- MedlinePlusDataProvider.ParseTopicsXml ---

    private const string SampleMedlinePlusXml = """
        <health-topics>
          <health-topic language="English" title="Asthma" url="https://medlineplus.gov/asthma.html">
            <full-summary><![CDATA[Asthma is a <b>chronic</b> disease that affects the airways and causes difficulty breathing with wheezing.]]></full-summary>
            <also-called>Bronchial Asthma</also-called>
            <group>Lung and Breathing Disorders</group>
            <primary-institute>National Heart, Lung, and Blood Institute</primary-institute>
            <site url="https://medlineplus.gov/ency/article/000141.htm" title="Asthma"/>
            <site url="https://medlineplus.gov/spanish/ency/article/000141.htm" title="Asma"/>
          </health-topic>
          <health-topic language="Spanish" title="Asma" url="https://medlineplus.gov/spanish/asma.html">
            <full-summary>Resumen en espanol que no deberia aparecer en los resultados.</full-summary>
          </health-topic>
          <health-topic language="English" title="Too Short" url="https://medlineplus.gov/short.html">
            <full-summary>Too short.</full-summary>
          </health-topic>
        </health-topics>
        """;

    [Fact]
    public void ParseTopicsXml_ExtractsExpectedFields_ForValidEnglishTopic()
    {
        var topics = MedlinePlusDataProvider.ParseTopicsXml(SampleMedlinePlusXml);

        var asthma = Assert.Single(topics);
        Assert.Equal("Asthma", asthma.Title);
        Assert.Contains("Bronchial Asthma", asthma.AlsoCalled);
        Assert.Contains("Lung and Breathing Disorders", asthma.Groups);
        Assert.Equal("National Heart, Lung, and Blood Institute", asthma.PrimaryInstitute);
    }

    [Fact]
    public void ParseTopicsXml_StripsHtmlTags_FromSummary()
    {
        var topics = MedlinePlusDataProvider.ParseTopicsXml(SampleMedlinePlusXml);

        var asthma = Assert.Single(topics);
        Assert.DoesNotContain("<b>", asthma.Summary);
        Assert.Contains("chronic", asthma.Summary);
    }

    [Fact]
    public void ParseTopicsXml_SkipsNonEnglishTopics()
    {
        var topics = MedlinePlusDataProvider.ParseTopicsXml(SampleMedlinePlusXml);

        Assert.DoesNotContain(topics, t => t.Title == "Asma");
    }

    [Fact]
    public void ParseTopicsXml_SkipsTopicsWithTooShortSummary()
    {
        var topics = MedlinePlusDataProvider.ParseTopicsXml(SampleMedlinePlusXml);

        Assert.DoesNotContain(topics, t => t.Title == "Too Short");
    }

    [Fact]
    public void ParseTopicsXml_FiltersOutSpanishEncyclopediaUrls()
    {
        var topics = MedlinePlusDataProvider.ParseTopicsXml(SampleMedlinePlusXml);

        var asthma = Assert.Single(topics);
        Assert.DoesNotContain(asthma.EncyclopediaUrls, u => u.Contains("/spanish/"));
        Assert.Contains(asthma.EncyclopediaUrls, u => u.Contains("/ency/article/000141.htm"));
    }

    [Fact]
    public void ParseTopicsXml_IgnoresEncyclopediaArticlesAboutSomethingElse()
    {
        const string xml = """
            <health-topics>
              <health-topic title="Menstruation" language="English" url="https://medlineplus.gov/menstruation.html">
                <full-summary>Menstruation is the monthly shedding of the uterine lining; this summary is long enough to be kept.</full-summary>
                <site title="Amenorrhea" url="https://medlineplus.gov/ency/article/001219.htm" />
                <site title="Menstruation" url="https://medlineplus.gov/ency/article/002000.htm" />
              </health-topic>
            </health-topics>
            """;

        var menstruation = Assert.Single(MedlinePlusDataProvider.ParseTopicsXml(xml));

        Assert.Equal(["https://medlineplus.gov/ency/article/002000.htm"], menstruation.EncyclopediaUrls);
        Assert.Equal("Menstruation", menstruation.EncyclopediaTitles!["https://medlineplus.gov/ency/article/002000.htm"]);
    }

    // --- PubMedDataProvider.ParseSearchResponse / ParseAbstractsXml / AppendApiKey ---

    [Fact]
    public void ParseSearchResponse_ExtractsIdList()
    {
        var ids = PubMedDataProvider.ParseSearchResponse("""{"esearchresult":{"idlist":["123","456"]}}""");

        Assert.Equal(["123", "456"], ids);
    }

    [Fact]
    public void ParseSearchResponse_ReturnsEmpty_WhenErrorPresent()
    {
        var ids = PubMedDataProvider.ParseSearchResponse("""{"esearchresult":{"ERROR":"bad request"}}""");

        Assert.Empty(ids);
    }

    [Fact]
    public void ParseSearchResponse_ReturnsEmpty_ForUnexpectedStructure()
    {
        var ids = PubMedDataProvider.ParseSearchResponse("""{"foo":"bar"}""");

        Assert.Empty(ids);
    }

    [Fact]
    public void ParseAbstractsXml_ExtractsNonEmptyAbstractSections()
    {
        const string xml = """
            <PubmedArticleSet>
              <PubmedArticle>
                <Abstract>
                  <AbstractText>First part.</AbstractText>
                  <AbstractText></AbstractText>
                  <AbstractText>Second part.</AbstractText>
                </Abstract>
              </PubmedArticle>
            </PubmedArticleSet>
            """;

        var abstracts = PubMedDataProvider.ParseAbstractsXml(xml);

        Assert.Equal(["First part.", "Second part."], abstracts);
    }

    [Fact]
    public void AppendApiKey_AppendsKey_WhenConfigured()
    {
        var provider = new PubMedDataProvider(new HttpClient(), new FakeConfiguration("test-key-123"), NullLogger<PubMedDataProvider>.Instance);

        var result = provider.AppendApiKey("esearch.fcgi?db=pubmed");

        Assert.Equal("esearch.fcgi?db=pubmed&api_key=test-key-123", result);
    }

    [Fact]
    public void AppendApiKey_LeavesUrlUnchanged_WhenNotConfigured()
    {
        var provider = new PubMedDataProvider(new HttpClient(), new FakeConfiguration(null), NullLogger<PubMedDataProvider>.Instance);

        var result = provider.AppendApiKey("esearch.fcgi?db=pubmed");

        Assert.Equal("esearch.fcgi?db=pubmed", result);
    }

    private sealed class FakeConfiguration(string? pubMedApiKey) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "PubMed:ApiKey" ? pubMedApiKey : null;
            set => throw new NotImplementedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => throw new NotImplementedException();
        public IChangeToken GetReloadToken() => throw new NotImplementedException();
        public IConfigurationSection GetSection(string key) => throw new NotImplementedException();
    }
}
