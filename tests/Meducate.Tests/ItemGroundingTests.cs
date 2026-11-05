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
    public void Coverage_IsTheShareOfContentWordsFoundInTheSource(string item, double expected)
    {
        var source = ItemGrounding.Words(Source).ToHashSet();
        Assert.Equal(expected, ItemGrounding.Coverage(item, source), 2);
    }
}
