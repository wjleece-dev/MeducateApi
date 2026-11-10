using Meducate.Application.Helpers;
using Meducate.Domain.Entities;

namespace Meducate.Tests;

public class TopicRegressionTests
{
    private const string Source = "Do not start CPR unless the seizure has clearly stopped and the person is not breathing or has no pulse. " +
        "Symptoms include fever, chills, pain, swelling, redness and a rash on the skin around the area that is affected.";

    private static HealthTopic Stored() => new()
    {
        Name = "X",
        TopicType = "Disease",
        Summary = "A sufficiently detailed summary describing the condition in clinical terms for the purposes of testing.",
        Observations = ["Fever", "Chills", "Pain", "Swelling"],
        Factors = ["Age", "Diabetes"],
        Actions = ["Rest", "Antibiotics"],
    };

    private static HealthTopic Fresh(Action<HealthTopic>? change = null)
    {
        var t = Stored();
        change?.Invoke(t);
        return t;
    }

    [Fact]
    public void AnEquivalentExtraction_IsNotARegression() =>
        Assert.False(TopicHelpers.IsRegression(Stored(), Fresh(), Source));

    [Fact]
    public void ListsThatGrowOrShrinkSlightly_AreNotARegression()
    {
        Assert.False(TopicHelpers.IsRegression(Stored(), Fresh(t => t.Observations = ["Fever", "Chills", "Pain"]), Source));
        Assert.False(TopicHelpers.IsRegression(Stored(), Fresh(t => t.Observations = [.. t.Observations!, "Rash", "Redness"]), Source));
        Assert.False(TopicHelpers.IsRegression(Stored(), Fresh(t => t.Factors = ["Age"]), Source)); // 2 -> 1 is below the 4-item threshold
    }

    [Fact]
    public void ACutOfHalfOrMoreOnALongList_IsARegression() =>
        Assert.True(TopicHelpers.IsRegression(Stored(), Fresh(t => t.Observations = ["Fever", "Chills"]), Source));

    [Theory]
    [InlineData(10, 6, true)]   // lost 4 (40%): real content gone
    [InlineData(16, 9, true)]   // Cirrhosis: lost hepatitis B/C, jaundice and ascites
    [InlineData(10, 8, false)]  // normal variation
    [InlineData(18, 14, false)] // lost 4 but only 22%
    public void ALongListThatLosesAThirdOrMore_IsARegression(int before, int after, bool regression)
    {
        var stored = Stored();
        stored.Observations = Enumerable.Range(1, before).Select(i => $"Symptom {i}").ToList();
        var fresh = Fresh(t => t.Observations = stored.Observations.Take(after).ToList());

        Assert.Equal(regression, TopicHelpers.IsRegression(stored, fresh, Source));
    }

    [Fact]
    public void SeveralModestCuts_AddUpToARegression()
    {
        var stored = Stored();
        stored.Observations = Enumerable.Range(1, 18).Select(i => $"Symptom {i}").ToList();
        stored.Factors = Enumerable.Range(1, 8).Select(i => $"Factor {i}").ToList();
        stored.Actions = Enumerable.Range(1, 14).Select(i => $"Action {i}").ToList();
        // 18->13, 8->6, 14->11: no list loses a third, but 10 of 40 items are gone
        var fresh = Fresh(t =>
        {
            t.Observations = stored.Observations.Take(13).ToList();
            t.Factors = stored.Factors.Take(6).ToList();
            t.Actions = stored.Actions.Take(11).ToList();
        });

        Assert.True(TopicHelpers.IsRegression(stored, fresh, Source));
    }

    [Fact]
    public void AnEmptiedList_IsARegression() =>
        Assert.True(TopicHelpers.IsRegression(Stored(), Fresh(t => t.Factors = []), Source));

    [Fact]
    public void ManyItemsSquashedIntoOneBullet_IsARegression() =>
        Assert.True(TopicHelpers.IsRegression(Stored(),
            Fresh(t => t.Observations = ["Fever", "Symptoms: chills, pain, swelling, redness, rash, itching, fatigue"]), Source));

    [Fact]
    public void WordingCopiedFromTheSource_IsARegression_WhenTheStoredTopicDidNotCopy()
    {
        var copied = Fresh(t => t.Actions = ["Do not start CPR unless the seizure has clearly stopped and the person is not breathing or has no pulse", "Rest"]);

        Assert.True(TopicHelpers.IsRegression(Stored(), copied, Source));
    }

    [Fact]
    public void CopiedWording_IsNotARegression_WhenTheStoredTopicAlreadyCopies()
    {
        var stored = Stored();
        stored.Actions = ["Do not start CPR unless the seizure has clearly stopped and the person is not breathing or has no pulse", "Rest"];

        Assert.False(TopicHelpers.IsRegression(stored, Fresh(t => t.Actions = stored.Actions), Source));
    }
}
