using Meducate.Application.Helpers;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Tests;

public class ItemGroundingTests
{
    private const string Source = "Symptoms include fever and chills, pain and swelling in the area. " +
        "Treatment is draining the abscess and sometimes antibiotics. It is more common in women with diabetes.";

    private static HealthTopic Topic() => new()
    {
        Name = "Abscess",
        Observations = ["Fever and chills", "Pain and swelling", "Hair loss"],
        Factors = ["Diabetes", "Female gender", "Genetic predisposition"],
        Actions = ["Draining the abscess", "Antibiotics"],
    };

    private static ItemJudgement No(string field, string item) => new(field, item, false, null);
    private static ItemJudgement Yes(string field, string item) => new(field, item, true, "quote");

    [Fact]
    public void Apply_DropsAnItemOnlyWhenTheJudgeRejectsItAndItsWordsAreAbsentFromTheSource()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source,
        [
            No("observations", "Hair loss"),                // judge says no, words absent: dropped
            No("factors", "Genetic predisposition"),        // judge says no, words absent: dropped
        ]);

        Assert.Equal(["Fever and chills", "Pain and swelling"], topic.Observations);
        Assert.Equal(["Diabetes", "Female gender"], topic.Factors);
        Assert.Equal(2, outcome.Dropped.Count);
    }

    [Fact]
    public void Apply_KeepsAnItemTheJudgeRejectsWhenTheSourceUsesItsWords()
    {
        // The judge was wrong about this one in real runs: the symptom is listed in the source.
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source, [No("observations", "Fever and chills")]);

        Assert.Empty(outcome.Dropped);
        Assert.Contains("Fever and chills", topic.Observations!);
    }

    [Fact]
    public void Apply_KeepsAnItemWhoseWordsAreAbsentWhenTheJudgeApprovesIt()
    {
        // "Female gender" is backed by "women"; no word overlap, but the judge understood it.
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source, [Yes("factors", "Female gender")]);

        Assert.Empty(outcome.Dropped);
        Assert.Contains("Female gender", topic.Factors!);
    }

    [Fact]
    public void Apply_KeepsEverythingWhenThereIsNoUsableReply()
    {
        var topic = Topic();
        Assert.Empty(ItemGrounding.Apply(topic, Source, null).Dropped);
        Assert.Empty(ItemGrounding.Apply(topic, Source, []).Dropped);
        Assert.Equal(3, topic.Observations!.Count);
    }

    [Fact]
    public void Apply_NeverEmptiesAField()
    {
        var topic = new HealthTopic { Name = "X", Observations = ["Hair loss"], Factors = ["Diabetes"], Actions = ["Antibiotics"] };
        var outcome = ItemGrounding.Apply(topic, Source, [No("observations", "Hair loss")]);

        Assert.Equal(["Hair loss"], topic.Observations);
        Assert.Empty(outcome.Dropped);
    }

    [Fact]
    public void Apply_IgnoresAJudgeThatRejectsAlmostEverything()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source,
        [
            No("observations", "Fever and chills"), No("observations", "Pain and swelling"), No("observations", "Hair loss"),
            No("factors", "Diabetes"), No("factors", "Genetic predisposition"),
            No("actions", "Draining the abscess"), No("actions", "Antibiotics"),
        ]);

        Assert.Empty(outcome.Dropped);
        Assert.NotNull(outcome.Rejected);
        Assert.Equal(3, topic.Observations!.Count);
    }

    [Fact]
    public void Evaluate_ReportsDropsWithoutChangingTheTopic()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Evaluate(topic, Source, [No("observations", "Hair loss")]);

        Assert.Single(outcome.Dropped);
        Assert.Contains("Hair loss", topic.Observations!);
    }

    [Theory]
    [InlineData("Fever and chills", 1.0)]
    [InlineData("Hair loss", 0.0)]
    [InlineData("Swelling or hair loss", 0.33)]   // swelling found, hair and loss are not
    [InlineData("and the", 1.0)]                  // nothing to check
    public void Coverage_IsTheShareOfContentWordsFoundInTheSource(string item, double expected) =>
        Assert.Equal(expected, ItemGrounding.Coverage(item, ItemGrounding.Words(Source).ToHashSet(), Source.ToLowerInvariant()), 2);

    [Theory]
    [InlineData("Obesity", "Obese men are at higher risk.")]            // real false drops seen in calibration
    [InlineData("Blockages", "Problems include infections, blockage, and cancer.")]
    public void Coverage_CountsRelatedWordFormsAsPresent(string item, string source) =>
        Assert.Equal(1.0, ItemGrounding.Coverage(item, ItemGrounding.Words(source).ToHashSet(), source.ToLowerInvariant()), 2);

    [Fact]
    public void Coverage_KeepsAnItemWhenHalfItsWordsAreFound()
    {
        // "Poor nutrition" vs "malnutrition": nutrition is found, poor is not, so 50% and the item is not dropped.
        const string source = "Causes include malnutrition (when severe).";
        Assert.True(ItemGrounding.Coverage("Poor nutrition", ItemGrounding.Words(source).ToHashSet(), source) >= 0.5);
    }

    [Fact]
    public void LongestVerbatimRun_CountsTheLongestSharedStretchOfWords()
    {
        const string source = "Do not start CPR unless the seizure has clearly stopped and the person is not breathing or has no pulse. Call 911.";
        Assert.Equal(0, ItemGrounding.LongestVerbatimRun("Begin CPR only after the seizure ends", source));
        Assert.Equal(20, ItemGrounding.LongestVerbatimRun("Do not start CPR unless the seizure has clearly stopped and the person is not breathing or has no pulse", source));
    }

    // Contradictions share almost every word with the source, so word overlap alone cannot catch them.
    [Theory]
    [InlineData("This drug increases blood pressure.", "Decreases blood pressure")]
    [InlineData("This drug should be used with food.", "Should not be used with food")]
    [InlineData("A rare adverse effect is rash.", "Common adverse effect is rash")]
    [InlineData("The usual dose is 5 mg a day.", "Usual dose is 50 mg a day")]
    public void AContradictedItem_IsQueuedForAStrictCheck_AndDroppedWhenThatCheckAgrees(string source, string item)
    {
        HealthTopic Build() => new() { Name = "X", Observations = [item, "Filler symptom one", "Filler symptom two"], Factors = ["F1", "F2"], Actions = ["A1", "A2"] };
        var judge = new List<ItemJudgement> { No("observations", item) };

        var first = ItemGrounding.Evaluate(Build(), source, judge);
        Assert.Empty(first.Dropped);
        Assert.Contains(first.RecheckItems, r => r.Item == item);

        var confirmed = ItemGrounding.Apply(Build(), source, judge, [No("observations", item)]);
        Assert.Contains(confirmed.Dropped, d => d.Item == item);

        var cleared = ItemGrounding.Apply(Build(), source, judge, [Yes("observations", item)]);
        Assert.Empty(cleared.Dropped);
    }

    [Fact]
    public void AnItemThatSharesWordsWithTheSource_IsOnlyDroppedWhenTheStrictCheckAgrees()
    {
        HealthTopic Build() => new() { Name = "X", Observations = ["Fever and chills", "Pain", "Swelling"], Factors = ["F1", "F2"], Actions = ["A1", "A2"] };
        var judge = new List<ItemJudgement> { No("observations", "Fever and chills") };

        var first = ItemGrounding.Evaluate(Build(), Source, judge);
        Assert.Empty(first.Dropped);
        Assert.Single(first.RecheckItems);

        Assert.Single(ItemGrounding.Apply(Build(), Source, judge, [No("observations", "Fever and chills")]).Dropped);
        Assert.Empty(ItemGrounding.Apply(Build(), Source, judge, [Yes("observations", "Fever and chills")]).Dropped);
    }

    [Fact]
    public void AGenericRiskFactorWhoseWordAppearsInTheSource_IsDroppedWhenBothChecksAgree()
    {
        // Real case from the accuracy audit: "Age" passes any word check, but the source never lists it as a risk.
        const string source = "Cancer of the gallbladder is rare. It is more common in women and Native Americans.";
        HealthTopic Build() => new() { Name = "Gallbladder Cancer", Observations = ["Jaundice", "Fever"], Factors = ["Gender (more common in women)", "Age"], Actions = ["Surgery", "Radiation"] };
        var judge = new List<ItemJudgement> { No("factors", "Age") };

        Assert.Single(ItemGrounding.Apply(Build(), source, judge, [No("factors", "Age")]).Dropped);
    }

    [Theory]
    [InlineData("Should not be used", true)]
    [InlineData("Rare complications", true)]
    [InlineData("Decreases blood pressure", true)]
    [InlineData("Dose of 5 mg", true)]
    [InlineData("Fever and chills", false)]
    [InlineData("Chest pain", false)]
    public void HasQualifier_SpotsNegationDirectionFrequencyAndNumbers(string item, bool expected) =>
        Assert.Equal(expected, ItemGrounding.HasQualifier(item));

    [Fact]
    public void AnUnsupportedItemThatCannotBeRemoved_IsReportedAsRetained_AndMarksTheTopicSuspect()
    {
        var topic = new HealthTopic { Name = "X", Observations = ["Hair loss"], Factors = ["Diabetes"], Actions = ["Antibiotics"] };

        var outcome = ItemGrounding.Apply(topic, Source, [No("observations", "Hair loss")]);

        Assert.Empty(outcome.Dropped);
        Assert.Single(outcome.RetainedItems);
        Assert.True(outcome.IsSuspect);
        Assert.Equal(["Hair loss"], topic.Observations);
    }

    [Fact]
    public void AJudgeThatRejectsAlmostEverything_MarksTheTopicSuspect_ButRemovesNothing()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source,
        [
            No("observations", "Fever and chills"), No("observations", "Pain and swelling"), No("observations", "Hair loss"),
            No("factors", "Diabetes"), No("factors", "Genetic predisposition"),
            No("actions", "Draining the abscess"), No("actions", "Antibiotics"),
        ]);

        Assert.Empty(outcome.Dropped);
        Assert.True(outcome.IsSuspect);
        Assert.NotEmpty(outcome.RetainedItems);
    }

    [Fact]
    public void ACleanTopic_IsNotSuspect()
    {
        var outcome = ItemGrounding.Apply(Topic(), Source, [No("observations", "Hair loss")]);

        Assert.Single(outcome.Dropped);
        Assert.False(outcome.IsSuspect);
    }

    private const string Encyclopedia =
        "[MedlinePlus Groups: Blood] --- [MedlinePlus] [Source: NHLBI] Anemia is a condition. --- " +
        "[Encyclopedia Article: Anemia] Symptoms include fatigue, pale skin and shortness of breath. Treatment depends on the cause.";

    private static HealthTopic AnemiaTopic() => new()
    {
        Name = "Anemia",
        Observations = ["Fatigue", "Pale skin", "Dizziness"],
        Factors = ["Iron deficiency"],
        Actions = ["Treatment depends on the cause"],
    };

    [Fact]
    public void BuildEvidence_RecordsAQuoteOnlyWhenItIsFoundWordForWordInTheSource()
    {
        SourceLink[] links = [new("MedlinePlus topic page", "https://medlineplus.gov/anemia.html"), new("Anemia", "https://medlineplus.gov/ency/article/000560.htm")];
        var judge = new List<ItemJudgement>
        {
            Yes("observations", "Fatigue") with { Quote = "Symptoms include fatigue, pale skin and shortness of breath" },
            Yes("observations", "Pale skin") with { Quote = "tired and pale looking skin" },   // loose wording: not in the source
            Yes("observations", "Dizziness") with { Quote = null },
        };

        var evidence = ItemGrounding.BuildEvidence(AnemiaTopic(), Encyclopedia, judge, null, links);

        var verbatim = Assert.Single(evidence, e => e.Item == "Fatigue");
        Assert.Equal("verbatim", verbatim.Match);
        Assert.Equal("Symptoms include fatigue, pale skin and shortness of breath", verbatim.Quote);
        Assert.Equal("Encyclopedia article: Anemia", verbatim.Section);
        Assert.Equal("https://medlineplus.gov/ency/article/000560.htm", verbatim.Url);

        // A quote that cannot be found is never shown as evidence.
        var loose = Assert.Single(evidence, e => e.Item == "Pale skin");
        Assert.Equal("judged", loose.Match);
        Assert.Null(loose.Quote);
        Assert.Equal("judged", Assert.Single(evidence, e => e.Item == "Dizziness").Match);
    }

    [Fact]
    public void BuildEvidence_SkipsItemsNobodyApproved_AndItemsBeingRemoved()
    {
        var judge = new List<ItemJudgement>
        {
            Yes("observations", "Fatigue") with { Quote = "Symptoms include fatigue" },
            No("observations", "Pale skin"),
            Yes("observations", "Dizziness") with { Quote = "Symptoms include fatigue" },
        };
        var dropped = new List<GroundingDrop> { new("observations", "Dizziness", "removed") };

        var evidence = ItemGrounding.BuildEvidence(AnemiaTopic(), Encyclopedia, judge, null, null, dropped);

        Assert.Equal(["Fatigue"], evidence.Select(e => e.Item).ToArray());
    }

    [Fact]
    public void BuildEvidence_UsesTheStrictChecksApprovalWhenTheFirstCheckDoubtedTheItem()
    {
        var judge = new List<ItemJudgement> { No("observations", "Fatigue") };
        var strict = new List<ItemJudgement> { Yes("observations", "Fatigue") with { Quote = "Symptoms include fatigue" } };

        var evidence = ItemGrounding.BuildEvidence(AnemiaTopic(), Encyclopedia, judge, strict);

        Assert.Equal("verbatim", Assert.Single(evidence).Match);
    }

    [Fact]
    public void MergeLinks_CombinesProvidersAndDropsDuplicates()
    {
        var a = new RawTopicData("T", "x", "MedlinePlus", Links: [new("MedlinePlus topic page", "https://a"), new("Anemia", "https://b")]);
        var b = new RawTopicData("T", "y", "PubMed", Links: [new("Anemia", "https://b")]);

        var merged = TopicHelpers.MergeLinks([a, b]);

        Assert.Equal(["https://a", "https://b"], merged!.Select(l => l.Url).ToArray());
        Assert.Null(TopicHelpers.MergeLinks([new RawTopicData("T", "x", "PubMed")]));
    }
}
