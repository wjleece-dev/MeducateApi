# Accuracy audit

This is a small, first-party check of how faithful the served lists are to their source text. It is **not** a clinical review and does not show the content is medically correct. It shows whether items in the lists (symptoms, causes, treatments and their equivalents) are stated by the MedlinePlus source the topic was built from.

## Method

- **Sampling frame:** 837 topics that have a source text over 500 characters and at least 6 list items.
- **Sample:** a seeded random draw (seed 20261120) of 30 topics, then 4 random list items from each: **120 items**.
- **Grading:** each item was read against the source text of its topic and graded:
  - **S**: the source states it (same or equivalent wording).
  - **P**: partly supported. The idea is in the source but a qualifier, scope or cause is lost, or the wording is vague.
  - **U**: unsupported. The source does not state it.
  - **C**: contradicted. The source says the opposite.
- **Who graded:** the project's AI assistant, reading the evidence. No clinician graded this sample. Grading is against the source text only, so an item the source gets wrong would still score S.
- **Data:** every graded item is in [accuracy-audit-sample.csv](accuracy-audit-sample.csv).

## Results

| Grade | Items | Share | 95% interval |
|---|---|---|---|
| Supported (S) | 115 | 95.8% | 90.6% to 98.2% |
| Partly supported (P) | 4 | 3.3% | 1.3% to 8.3% |
| Unsupported (U) | 1 | 0.8% | 0.1% to 4.6% |
| Contradicted (C) | 0 | 0.0% | 0.0% to 3.1% |

Intervals are Wilson 95% intervals. With 120 items from 30 topics they are wide, and items inside a topic are not independent, so treat them as rough.

Four of the 30 topics (13%) had at least one sampled item that was not fully supported.

## What the audit found

- **Unsupported item (1):** Gallbladder Cancer listed "Gallbladder disease history" as a risk factor. The same topic also listed "Chronic inflammation", "Age" and "Polyp size", none of which the source states as risk factors. Three of them passed the old word-overlap check because their words appear somewhere in the source; "Chronic inflammation" was simply not flagged by the earlier model check, which is not deterministic.
- **Partly supported (4):** two lost a qualifier ("rare cases"), one is a loose paraphrase, one is vague.
- **Misfiled (2, graded S):** a condition and a definition sitting in a symptom list.
- **No contradictions** were found in this sample. That does not mean none exist: a contradicting item shares most of its words with the source and is hard to find by sampling.

## What changed because of it

- Any item the first model check doubts, whose words appear in the source, now gets a second, stricter check (negation, direction, frequency, severity, numbers, and whether the source states it at all). Both must agree before the item is removed.
- An unsupported item that cannot be removed (it is the last in its list, or the checker rejected almost everything) now stops the topic being published, or replaces the stored version.
- The nightly integrity check now sends an email for content warnings (low overlap with the source, copied wording), not only for broken data.

## Known limits

- A checker that is itself a language model can be wrong in both directions.
- Source text can be thin; an item that is true but not in the source is removed, by design.
- Clinical correctness, completeness and severity have not been assessed. Do not use this API for diagnosis or treatment decisions.
- Per-statement evidence (source passage, URL, retrieval date) is not exposed yet. It is on the roadmap.

If you find a wrong item, open an issue with the topic name and the item.
