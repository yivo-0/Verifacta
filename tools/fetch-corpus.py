"""Downloads the official EN 16931 / XRechnung / Peppol / Factur-X test instances.

The corpus is NOT committed: the CEN artefacts are EUPL-1.2 and the rest carry their
own terms. Run this once, then `dotnet test`.
"""

import json
import os
import subprocess
import sys
import urllib.request

TARGET = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "corpus")

# A corpus entry may pin a git ref. The XRechnung testsuite is pinned to the release that matches
# the rule pack in tools/fetch-rules.py — mismatched versions produce phantom disagreements.
SOURCES = [
    ("kosit-xrechnung", "itplr-kosit/xrechnung-testsuite", ("src/test/business-cases/",), "v2026-08-31"),
    ("cen-en16931", "ConnectingEurope/eInvoicing-EN16931", ("cii/examples/", "ubl/examples/"), "validation-1.3.16"),
    ("cen-rule-tests", "ConnectingEurope/eInvoicing-EN16931", ("test/",), "validation-1.3.16"),
    # Every source is pinned. These three publish no suitable tag, so they are pinned to a commit:
    # tracking a branch would make the corpus -- and therefore the test count -- drift with upstream.
    ("peppol-bis", "OpenPEPPOL/peppol-bis-invoice-3", ("rules/examples/", "rules/national-examples/"),
     "261c458474e27d58a25be629cccac28883171c92"),
    ("facturx", "akretion/factur-x", ("tests/fixtures/xml/",),
     "9397c8573f362fe1a97912876afde06aaa92746f"),
    ("mustang", "ZUGFeRD/mustangproject", ("library/src/test/resources/",),
     "bb60510b13647f9fa8f9db6956b7b42d639fae90"),
    # Hybrid PDF/A-3 invoices, plus the edge cases the incumbent library hit: an encrypted PDF,
    # one with no attachment, a corrupt one, and a PDF/A-4 variant.
    ("pdf-mustang", "ZUGFeRD/mustangproject",
     ("library/src/test/resources/", "Mustang-CLI/src/test/resources/"),
     "bb60510b13647f9fa8f9db6956b7b42d639fae90", (".pdf",)),
    ("pdf-facturx", "akretion/factur-x", ("tests/fixtures/pdf/",),
     "9397c8573f362fe1a97912876afde06aaa92746f", (".pdf",)),
    ("pdf-edge-cases", "stephanstapel/ZUGFeRD-csharp", ("ZUGFeRD.PDF.Test/",),
     "18.0.0", (".pdf",)),
]

EXCLUDE = ("order-x", "order_x", "orderx", "/schema/", "/xsd/")


def tree(repo, ref):
    resolved = ref or subprocess.run(
        ["gh", "api", f"repos/{repo}", "--jq", ".default_branch"],
        capture_output=True, text=True, check=True).stdout.strip()
    # Only blobs: a directory named e.g. "ZUGFeRD.PDF" would otherwise match an extension filter.
    paths = subprocess.run(
        ["gh", "api", f"repos/{repo}/git/trees/{resolved}?recursive=1",
         "--jq", '.tree[] | select(.type == "blob") | .path'],
        capture_output=True, text=True, check=True).stdout.splitlines()
    return resolved, paths


def wanted(path, prefixes, extensions):
    lowered = path.lower()
    if not lowered.endswith(extensions):
        return False
    if any(token in lowered for token in EXCLUDE):
        return False
    return any(path.startswith(prefix) for prefix in prefixes)


def main():
    total = 0
    for name, repo, prefixes, ref, *rest in SOURCES:
        extensions = rest[0] if rest else (".xml",)
        branch, paths = tree(repo, ref)
        selected = [p for p in paths if wanted(p, prefixes, extensions)]
        folder = os.path.join(TARGET, name)
        os.makedirs(folder, exist_ok=True)
        written = 0
        for path in selected:
            trimmed = next((path[len(p):] for p in prefixes if path.startswith(p)), path)
            destination = os.path.join(folder, *trimmed.split("/"))
            os.makedirs(os.path.dirname(destination), exist_ok=True)
            if os.path.exists(destination):
                written += 1
                continue
            url = f"https://raw.githubusercontent.com/{repo}/{branch}/" + urllib.parse.quote(path)
            try:
                with urllib.request.urlopen(url, timeout=30) as response:
                    data = response.read()
            except Exception as error:
                print(f"  skip {path}: {error}")
                continue
            with open(destination, "wb") as handle:
                handle.write(data)
            written += 1
        print(f"{name:<16} {written:>4} files from {repo}@{branch}")
        total += written

    print(f"\n{total} files in {os.path.normpath(TARGET)}")


if __name__ == "__main__":
    sys.exit(main())
