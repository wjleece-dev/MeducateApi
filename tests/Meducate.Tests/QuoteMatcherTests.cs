using Meducate.Application.Helpers;

namespace Meducate.Tests;

public class QuoteMatcherTests
{
    private static readonly string[] Source = QuoteMatcher.Tokens(
        "Abscess: A collection of pus. Symptoms may include: Fever and chills, pain and swelling in the area. " +
        "It is more common in people who have diabetes or a weak immune system.");

    [Theory]
    [InlineData("Symptoms may include: Fever and chills, pain and swelling in the area")]
    [InlineData("symptoms may include fever and chills pain and swelling")]   // case and punctuation
    [InlineData("Symptoms include fever and chills, pain and swelling")]       // one dropped word
    [InlineData("it is more common in people who have diabetes")]
    public void Supports_AcceptsQuotesThatReallyAppearInTheSource(string quote) =>
        Assert.True(QuoteMatcher.Supports(Source, quote));

    [Theory]
    [InlineData("Obesity is a major risk factor for abscesses")]               // invented
    [InlineData("Pus formation is the main sign")]                              // plausible but not there
    [InlineData("")]
    [InlineData(null)]
    public void Supports_RejectsQuotesThatAreInventedOrTooShort(string? quote) =>
        Assert.False(QuoteMatcher.Supports(Source, quote));

    [Theory]
    // The model glues a lead-in from elsewhere onto the item's own line: the item part is what must be in the source.
    [InlineData("Symptoms may include: Fever and chills, pain and swelling in the area.", "Fever and chills")]
    [InlineData("Risk factors: more common in people who have diabetes", "Diabetes")]
    [InlineData("Symptoms may include: pain", "Pain")]                         // short fragment, but exactly present
    public void Supports_AcceptsAStitchedLeadInWhenTheItemPartIsInTheSource(string quote, string item) =>
        Assert.True(QuoteMatcher.Supports(Source, quote, item));

    [Theory]
    [InlineData("Symptoms may include: Hair loss", "Hair loss")]               // lead-in is real, item part is not
    [InlineData("Symptoms may include", "Hair loss")]                          // lead-in alone proves nothing
    [InlineData("pain", "Obesity")]                                            // in the source but unrelated to the item
    public void Supports_RejectsALeadInThatIsNotAboutTheItem(string quote, string item) =>
        Assert.False(QuoteMatcher.Supports(Source, quote, item));

    [Fact]
    public void Supports_RejectsWordsThatAreScatteredAcrossTheSource()
    {
        // Every word exists somewhere, but not together.
        Assert.False(QuoteMatcher.Supports(Source, "weak diabetes pus chills swelling immune"));
    }
}
