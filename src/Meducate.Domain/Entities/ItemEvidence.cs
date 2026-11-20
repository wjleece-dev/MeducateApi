namespace Meducate.Domain.Entities;

// A link to a page the topic was built from, e.g. its MedlinePlus topic page or an encyclopedia article.
internal sealed record SourceLink(string Title, string Url);

// Evidence for one list item. Field is "observations", "factors" or "actions".
// Match is "verbatim" when the model's quote was found word for word in the source; "matched" when it was not but the
// source sentence whose wording overlaps the item most was found (Quote is that sentence, real source text, not the
// model's words); or "judged" when the model said the source backs the item but no passage could be located (Quote is
// then null). Section names the part of the source the passage came from; Url is that part's page when known.
internal sealed record ItemEvidence(string Field, string Item, string? Quote, string Match, string? Section = null, string? Url = null);
