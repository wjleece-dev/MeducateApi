using Meducate.Application.Helpers;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Tests;

public class ItemGroundingTests
{
    private const string Source = "Symptoms include fever and chills, pain and swelling in the area. " +
        "Treatment is draining the abscess and sometimes antibiotics. It is more common in people with diabetes.";

    private static HealthTopic Topic() => new()
    {
        Name = "Abscess",
        Observations = ["Fever and chills", "Pain and swelling", "Hair loss"],
        Factors = ["Diabetes"],
        Actions = ["Draining the abscess", "Antibiotics"],
    };

    private static ItemJudgement Yes(string field, string item, string quote) => new(field, item, true, quote);
    private static ItemJudgement No(string field, string item) => new(field, item, false, null);

    [Fact]
    public void Apply_DropsItemsTheJudgeRejects_AndKeepsTheRest()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source,
        [
            Yes("observations", "Fever and chills", "include fever and chills, pain and swelling"),
            Yes("observations", "Pain and swelling", "fever and chills, pain and swelling in the area"),
            No("observations", "Hair loss"),
            Yes("factors", "Diabetes", "more common in people with diabetes"),
        ]);

        Assert.Equal(["Fever and chills", "Pain and swelling"], topic.Observations);
        Assert.Equal(["Diabetes"], topic.Factors);
        Assert.Equal(["Draining the abscess", "Antibiotics"], topic.Actions); // not judged, so kept
        Assert.Equal("Hair loss", Assert.Single(outcome.Dropped).Item);
    }

    [Fact]
    public void Apply_DropsAnApprovalWhoseQuoteIsNotInTheSource()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source,
        [
            Yes("observations", "Fever and chills", "include fever and chills, pain and swelling"),
            Yes("observations", "Hair loss", "Hair loss is a common sign of abscesses"),
        ]);

        Assert.DoesNotContain("Hair loss", topic.Observations!);
        Assert.Contains("not found in source", Assert.Single(outcome.Dropped).Reason);
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
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source, [No("factors", "Diabetes"), No("observations", "Hair loss")]);

        Assert.Equal(["Diabetes"], topic.Factors);
        Assert.DoesNotContain("Hair loss", topic.Observations!);
        Assert.Single(outcome.Dropped);
    }

    [Fact]
    public void Apply_IgnoresAJudgeThatRejectsAlmostEverything()
    {
        var topic = Topic();
        var outcome = ItemGrounding.Apply(topic, Source,
        [
            No("observations", "Fever and chills"), No("observations", "Pain and swelling"), No("observations", "Hair loss"),
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
}
