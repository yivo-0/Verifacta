"""Compares Verifacta's verdicts against the German reference validator, rule by rule.

Verifacta runs the publishers' own artefacts, so it should agree with the tool the publishers
ship. This is what turns that into a claim anyone can check: it runs KoSIT's validationtool over
a corpus, runs Verifacta over the same files, and diffs the rule ids each reports.

Java is needed to run the *reference*. It is not needed to use Verifacta, which is the point.

    python tools/reference-diff.py                 # writes corpus/reference-diff.md
    python tools/reference-diff.py --corpus corpus/kosit-xrechnung

Exits non-zero when the two disagree about a file in a way that is not recorded in EXPECTED below.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The reference implementation of the validation framework, pinned. The scenario configuration is
# NOT pinned here: it is read from the manifest, so the comparison always runs the same
# configuration release Verifacta validates against. Comparing against a different one would
# produce disagreements that say nothing about either tool.
VALIDATOR_REPO = "itplr-kosit/validator"
VALIDATOR_TAG = "v1.6.3"
VALIDATOR_JAR = "validator-1.6.3-standalone.jar"

CACHE = os.path.join(ROOT, ".tmp", "reference")

# Files the two are known to disagree about, and why. A disagreement not listed here fails the run.
# Keyed by file name, valued by a reason that has to be written by a person who checked it.
EXPECTED: dict[str, str] = {}

# A floor on how much has to be comparable for a run to mean anything, set from the corpora this
# defaults to. Without it, anything that stopped Verifacta reading files reported perfect agreement
# about nothing at all and exited zero. Raise it when the corpora grow.
MINIMUM_COMPARABLE = 90

MESSAGE = re.compile(
    r'<rep:message\b[^>]*?level="(?P<level>[^"]*)"[^>]*?code="(?P<code>[^"]*)"', re.DOTALL)
MESSAGE_REVERSED = re.compile(
    r'<rep:message\b[^>]*?code="(?P<code>[^"]*)"[^>]*?level="(?P<level>[^"]*)"', re.DOTALL)
REFERENCE = re.compile(r"<rep:documentReference>(.*?)</rep:documentReference>", re.DOTALL)


def download(url, destination):
    if os.path.exists(destination):
        return destination

    os.makedirs(os.path.dirname(destination), exist_ok=True)
    print(f"  downloading {url}")
    with urllib.request.urlopen(url) as response, open(destination, "wb") as handle:
        shutil.copyfileobj(response, handle)

    return destination


def configuration():
    """The scenario configuration, taken from the same release the xrechnung rule pack pins."""
    with open(os.path.join(ROOT, "src", "Verifacta", "RulePacks.json"), encoding="utf-8") as handle:
        packs = json.load(handle)["packs"]

    pack = next(p for p in packs if p["id"] == "xrechnung")
    asset = next(iter(pack["files"].values()))["origin"].split("!", 1)[0]
    directory = os.path.join(CACHE, f"config-{pack['version']}")

    if not os.path.isdir(directory):
        archive = download(
            f"https://github.com/{pack['repo']}/releases/download/{pack['tag']}/{asset}",
            os.path.join(CACHE, asset))
        with zipfile.ZipFile(archive) as zipped:
            zipped.extractall(directory)

    return directory, f"{pack['repo']}@{pack['tag']}"


def java():
    for candidate in (os.environ.get("JAVA_HOME"), None):
        binary = os.path.join(candidate, "bin", "java") if candidate else shutil.which("java")
        if binary and (shutil.which(binary) or os.path.exists(binary) or os.path.exists(binary + ".exe")):
            return binary

    sys.exit("No java on PATH and JAVA_HOME is not set. The reference validator needs Java 11+.")


def reference_verdicts(files, reports, config):
    """Runs KoSIT's validationtool and reads the rule ids it reported per file."""
    jar = download(
        f"https://github.com/{VALIDATOR_REPO}/releases/download/{VALIDATOR_TAG}/{VALIDATOR_JAR}",
        os.path.join(CACHE, VALIDATOR_JAR))

    # Cleared per run: reports are named after the input file, so leftovers from another corpus
    # would silently be read as this one's.
    shutil.rmtree(reports, ignore_errors=True)
    os.makedirs(reports, exist_ok=True)

    command = [java(), "-jar", jar,
               "-s", os.path.join(config, "scenarios.xml"),
               "-r", config,
               "-o", reports] + files

    # input="" rather than DEVNULL: the tool calls System.in.available() to detect piped input, and
    # on Windows that throws on a null device handle but is happy with an empty pipe.
    result = subprocess.run(command, input="", capture_output=True, text=True)

    # The exit code is the number of documents it rejected, which is a verdict and not an error.
    # Producing no reports at all is the failure worth stopping for.
    if not os.listdir(reports):
        print(result.stdout[-2000:])
        sys.exit(f"the reference validator wrote no reports (exit {result.returncode})")

    verdicts = {}
    for name in os.listdir(reports):
        if not name.endswith(".xml"):
            continue

        with open(os.path.join(reports, name), encoding="utf-8") as handle:
            report = handle.read()

        source = REFERENCE.search(report)
        if source is None:
            continue

        key = os.path.basename(source.group(1).replace("\\", "/"))

        # No scenario matched means the reference declined to judge the file at all, which is not
        # the same as judging it clean. Left out so it is skipped rather than read as agreement.
        if "rep:scenarioMatched" not in report:
            continue

        verdicts[key] = {
            normalise(match.group("code"))
            for pattern in (MESSAGE, MESSAGE_REVERSED)
            for match in pattern.finditer(report)
            if match.group("level") == "error"
        }

    return verdicts


def normalise(code):
    """The reference reports the schema parser's own code; Verifacta reports one id for all of them."""
    return "XSD" if code.startswith(("cvc-", "sch-")) else code


def verifacta_verdicts(corpora):
    command = os.environ.get("VERIFACTA_CLI")
    command = command.split() if command else [
        "dotnet", "run", "--project", os.path.join(ROOT, "src", "Verifacta.Cli"),
        "-c", "Release", "--no-build", "--"]

    result = subprocess.run(command + ["validate", *corpora, "-r", "--json"],
                            capture_output=True, text=True, cwd=ROOT)
    if not result.stdout.strip():
        print(result.stderr[-2000:])
        sys.exit("verifacta produced no output")

    return {
        os.path.basename(report["File"].replace("\\", "/")): (
            report["Status"],
            {finding["RuleId"] for finding in report["Findings"] if finding["Severity"] == "Error"},
        )
        for report in json.loads(result.stdout)
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", nargs="+", default=[
        os.path.join("corpus", "kosit-xrechnung"),
        os.path.join("corpus", "mustang"),
        os.path.join("corpus", "facturx"),
    ])
    parser.add_argument("--out", default=os.path.join("corpus", "reference-diff.md"))
    arguments = parser.parse_args()

    files = []
    for relative in arguments.corpus:
        corpus = os.path.join(ROOT, relative)
        if not os.path.isdir(corpus):
            sys.exit(f"{corpus} is missing. Run tools/fetch-corpus.py first.")

        files.extend(
            os.path.join(base, name)
            for base, _, names in os.walk(corpus)
            for name in names
            if name.lower().endswith(".xml"))

    files.sort()
    config, configuration_ref = configuration()
    print(f"comparing {len(files)} files against {VALIDATOR_REPO}@{VALIDATOR_TAG}")

    reference = reference_verdicts(files, os.path.join(CACHE, "reports"), config)
    ours = verifacta_verdicts(arguments.corpus)

    rows, disagreements, skipped = [], [], []

    for path in files:
        name = os.path.basename(path)
        theirs = reference.get(name)
        mine = ours.get(name)

        # Not every file is a fair comparison: ZUGFeRD 1.x is out of scope here and the reference
        # matches no scenario for some inputs. Those are recorded, not counted as disagreement.
        if theirs is None:
            skipped.append((name, "the reference matched no scenario"))
            continue

        # The reference judged this document. Verifacta failing to read it is a disagreement about
        # the document, not a reason to drop it from the count — anything that broke every read
        # used to report "0/0 agree" and exit 0.
        if mine is None or mine[0] == "error":
            disagreements.append((name, theirs, {"(Verifacta could not process this file)"}))
            rows.append((name, theirs, theirs, set()))
            continue

        missed, extra = theirs - mine[1], mine[1] - theirs
        if missed or extra:
            disagreements.append((name, missed, extra))

        rows.append((name, theirs, missed, extra))

    unexpected = [d for d in disagreements if d[0] not in EXPECTED]
    write_report(arguments.out, configuration_ref, rows, disagreements, skipped)

    print(f"\n{len(rows) - len(disagreements)}/{len(rows)} comparable files agree rule for rule "
          f"with the reference validator ({len(skipped)} not comparable)")

    # A run that compared nothing is not a run that agreed about everything.
    if len(rows) < MINIMUM_COMPARABLE:
        print(f"\nonly {len(rows)} files were comparable, expected at least {MINIMUM_COMPARABLE}. "
              "Nothing meaningful was checked.")
        return 1

    if unexpected:
        for name, missed, extra in unexpected:
            print(f"  {name}:"
                  + (f" reference reported {sorted(missed)}" if missed else "")
                  + (f" we reported {sorted(extra)}" if extra else ""))
        return 1

    return 0


def write_report(out, configuration_ref, rows, disagreements, skipped):
    path = os.path.join(ROOT, out)
    os.makedirs(os.path.dirname(path), exist_ok=True)

    with open(path, "w", encoding="utf-8") as handle:
        handle.write("# Agreement with the reference validator\n\n")
        handle.write(
            f"Verifacta against KoSIT's validationtool `{VALIDATOR_TAG}`, both using the scenario "
            f"configuration from `{configuration_ref}` — the same release the `xrechnung` rule pack "
            "pins, so the two run identical artefacts.\n\n")
        handle.write(f"**{len(rows) - len(disagreements)} of {len(rows)} comparable files agree "
                     f"rule for rule.**\n\n")

        if disagreements:
            handle.write("## Disagreements\n\n")
            handle.write("| File | Reference reported | Verifacta reported | Known |\n|---|---|---|---|\n")
            for name, missed, extra in disagreements:
                handle.write(f"| `{name}` | {', '.join(sorted(missed)) or '—'} | "
                             f"{', '.join(sorted(extra)) or '—'} | "
                             f"{EXPECTED.get(name, '**no**')} |\n")
            handle.write("\n")

        if skipped:
            handle.write(f"## Not comparable ({len(skipped)})\n\n")
            handle.write("| File | Reason |\n|---|---|\n")
            for name, reason in skipped:
                handle.write(f"| `{name}` | {reason} |\n")
            handle.write("\n")

        with_findings = [(name, theirs) for name, theirs, _, _ in rows if theirs]
        handle.write(f"## Files both reject, and why ({len(with_findings)})\n\n")
        handle.write("| File | Rules |\n|---|---|\n")
        for name, theirs in with_findings:
            handle.write(f"| `{name}` | {', '.join(sorted(theirs))} |\n")

    print(f"wrote {os.path.normpath(path)}")


if __name__ == "__main__":
    sys.exit(main())
