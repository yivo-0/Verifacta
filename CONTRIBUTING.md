# Contributing

## Getting it running

```bash
git clone https://github.com/yivo-0/Verifacta && cd Verifacta
dotnet build
dotnet run --project src/Verifacta.Cli -- rules restore
dotnet test
```

`rules restore` downloads the validation artefacts from their publishers and
checks each against the SHA-256 in the embedded manifest. Nothing else in the
library touches the network, and the artefacts are not committed — their licences
are not ours to redistribute.

The full test suite needs the official corpora as well:

```bash
python tools/fetch-corpus.py     # needs Python 3 and the GitHub CLI
dotnet test
```

Without them most of the suite quietly becomes a no-op. CI sets
`VERIFACTA_REQUIRE_CORPUS=1`, which turns anything missing into a failure, and
each run writes `corpus/coverage-<framework>.md` recording what it actually had.

## What is likely to be accepted

Bugs, especially anything under [Security](SECURITY.md). A document that crashes,
hangs or produces a verdict the publishers' own tools disagree with is the most
valuable thing you can send — with the file, if you can share it.

The [open issues](https://github.com/yivo-0/Verifacta/issues) are the current
picture of known limitations. Several are marked `limitation` rather than `bug`
because they are boundaries rather than defects.

## What is likely to be declined

- **Reimplementing a rule by hand.** Verifacta runs the publishers' compiled
  artefacts. A rule expressed in C# would be a second opinion, and the whole
  value of this library is that there is only one.
- **Vendoring the artefacts into the repository.** They are EUPL-1.2 and
  Apache-2.0 and stay with their publishers.
- **New formats or countries** without a maintained Schematron behind them. See
  issue #13 for how that reasoning goes.

## House style

Match the file you are editing. Beyond that:

- Comments earn their place. A comment that restates the code is noise; one that
  explains why the obvious version was wrong is the reason the next person does
  not reintroduce the bug. There are none in tests, DTOs or configuration.
- Every fix comes with a test that fails without it. For input-handling bugs that
  usually means constructing the hostile file in the test itself, as
  `PdfTraversalTests` and `DocumentLimitsTests` do, rather than adding a binary.
- `dotnet build -warnaserror` must be clean, on net8.0 and net10.0 both.
- Prefer a small pull request that does one thing.

## Rule pack versions

Bumping a pack is deliberately manual. Change the version in `tools/fetch-rules.py`,
re-run it, and review two things: the manifest diff, and whether any corpus
verdict changed. `python tools/reference-diff.py` compares every verdict against
KoSIT's own validator and fails on a disagreement that is not written down.

A national CIUS release can change the verdict on invoices that previously
passed, so that review is the point of the exercise rather than a formality.

## Licensing of contributions

Contributions are accepted under the repository's licence, and by opening a pull
request you confirm you have the right to submit the work and agree that it may
be distributed under that licence, including under any future licence the project
adopts.

For anything substantial, expect a conversation about this before the code
review rather than after it.
