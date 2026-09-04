"""Maintainer tool: bumps the pinned rule packs and regenerates the committed manifest.

Users do not need this script. They run `verifacta rules restore`, or call
RulePackCatalog.Restore(), which downloads the same artefacts using the embedded manifest.

Downloads the precompiled Schematron XSLT for each rule pack Verifacta validates against.

Every pack is pinned to an upstream release tag. The artefacts are NOT committed: the CEN
rules are EUPL-1.2 and the KoSIT configurations are Apache-2.0, so they stay outside the
repository and are fetched on demand. `manifest.json` records the exact release, licence
and SHA-256 of every file so a pack is auditable and reproducible.

Bumping a pack = change the version here, re-run, review the manifest diff and the test results.
"""

import hashlib
import io
import json
import os
import subprocess
import sys
import zipfile

RULES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rules")

# The manifest is committed and embedded in the assembly: it is what RulePackCatalog.Restore()
# downloads from, so a clone with only the .NET SDK can fetch its own artefacts. The rules/
# directory holds the artefacts themselves and stays out of git.
MANIFEST = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "src", "Verifacta", "RulePacks.json")

KOSIT_CONFIG = "xrechnung-3.0.2-validator-configuration-2026-08-31.zip"


def _schema_members():
    """The XSDs for the two document types Verifacta reads, plus the modules they import."""
    groups = {
        "resources/ubl/2.1/xsd/maindoc/": (
            "UBL-Invoice-2.1.xsd", "UBL-CreditNote-2.1.xsd",
        ),
        "resources/ubl/2.1/xsd/common/": (
            "CCTS_CCT_SchemaModule-2.1.xsd", "UBL-CommonAggregateComponents-2.1.xsd",
            "UBL-CommonBasicComponents-2.1.xsd", "UBL-CommonExtensionComponents-2.1.xsd",
            "UBL-CommonSignatureComponents-2.1.xsd", "UBL-CoreComponentParameters-2.1.xsd",
            "UBL-ExtensionContentDataType-2.1.xsd", "UBL-QualifiedDataTypes-2.1.xsd",
            "UBL-SignatureAggregateComponents-2.1.xsd", "UBL-SignatureBasicComponents-2.1.xsd",
            "UBL-UnqualifiedDataTypes-2.1.xsd", "UBL-XAdESv132-2.1.xsd",
            "UBL-XAdESv141-2.1.xsd", "UBL-xmldsig-core-schema-2.1.xsd",
        ),
        "resources/cii/16b/xsd/": (
            "CrossIndustryInvoice_100pD16B.xsd",
            "CrossIndustryInvoice_QualifiedDataType_100pD16B.xsd",
            "CrossIndustryInvoice_ReusableAggregateBusinessInformationEntity_100pD16B.xsd",
            "CrossIndustryInvoice_UnqualifiedDataType_100pD16B.xsd",
        ),
    }
    return {
        prefix + name: prefix[len("resources/"):] + name
        for prefix, names in groups.items()
        for name in names
    }


PACKS = [
    {
        "id": "schemas",
        "version": "2026-08-31",
        "repo": "itplr-kosit/validator-configuration-xrechnung",
        "tag": "v2026-08-31",
        "licence": "OASIS UBL 2.1 and UN/CEFACT CII D16B, as redistributed by KoSIT (Apache-2.0)",
        "assets": {KOSIT_CONFIG: _schema_members()},
    },
    {
        "id": "cen-en16931",
        "version": "1.3.16",
        "repo": "ConnectingEurope/eInvoicing-EN16931",
        "tag": "validation-1.3.16",
        "licence": "EUPL-1.2",
        "assets": {
            "en16931-ubl-1.3.16.zip": {"xslt/EN16931-UBL-validation.xslt": "EN16931-UBL-validation.xslt"},
            "en16931-cii-1.3.16.zip": {"xslt/EN16931-CII-validation.xslt": "EN16931-CII-validation.xslt"},
        },
    },
    {
        "id": "peppol-bis",
        "version": "3.0.21",
        "repo": "itplr-kosit/validator-configuration-bis",
        "tag": "release-3.0.21",
        "licence": "Apache-2.0",
        "assets": {
            "validation-configuration-bis-3.0.21.zip": {
                "resources/peppol/billing-bis/3.0.21/CEN-EN16931-UBL.xslt": "CEN-EN16931-UBL.xslt",
                "resources/peppol/billing-bis/3.0.21/PEPPOL-EN16931-UBL.xslt": "PEPPOL-EN16931-UBL.xslt",
            },
        },
    },
    {
        # The official XRechnung visualisation: source -> intermediate "xr" XML -> HTML. The
        # xr-pdf stylesheets are not fetched; they emit XSL-FO, which needs an FO processor.
        "id": "visualization",
        "version": "3.0.2",
        "repo": "itplr-kosit/xrechnung-visualization",
        "tag": "v2026-08-31",
        "licence": "Apache-2.0",
        "assets": {
            "xrechnung-3.0.2-visualization-2026-08-31.zip": {
                "xsl/ubl-invoice-xr.xsl": "xsl/ubl-invoice-xr.xsl",
                "xsl/ubl-creditnote-xr.xsl": "xsl/ubl-creditnote-xr.xsl",
                "xsl/cii-xr.xsl": "xsl/cii-xr.xsl",
                "xsl/common-xr.xsl": "xsl/common-xr.xsl",
                "xsl/functions.xsl": "xsl/functions.xsl",
                "xsl/xr-content.xsl": "xsl/xr-content.xsl",
                "xsl/xrechnung-html.xsl": "xsl/xrechnung-html.xsl",
                # Inlined into the rendered page by xrechnung-html.xsl, and its label catalogues.
                "xsl/xrechnung-viewer.css": "xsl/xrechnung-viewer.css",
                "xsl/xrechnung-viewer.js": "xsl/xrechnung-viewer.js",
                "xsl/FileSaver-v2.0.5.js": "xsl/FileSaver-v2.0.5.js",
                "xsl/l10n/de.xml": "xsl/l10n/de.xml",
                "xsl/l10n/en.xml": "xsl/l10n/en.xml",
            },
        },
    },
    {
        "id": "xrechnung",
        "version": "3.0.2",
        "repo": "itplr-kosit/validator-configuration-xrechnung",
        "tag": "v2026-08-31",
        "licence": "Apache-2.0",
        "assets": {
            KOSIT_CONFIG: {
                "resources/xrechnung/3.0.2/xsl/XRechnung-UBL-validation.xsl": "XRechnung-UBL-validation.xsl",
                "resources/xrechnung/3.0.2/xsl/XRechnung-CII-validation.xsl": "XRechnung-CII-validation.xsl",
                # KoSIT pairs the CIUS layer with its own copy of the EN 16931 rules; using theirs
                # rather than the CEN release is what makes our verdicts match their validator.
                "EN16931-UBL-validation.xsl": "EN16931-UBL-validation.xsl",
                "EN16931-CII-validation.xsl": "EN16931-CII-validation.xsl",
            },
        },
    },
]


_downloads = {}


def download(repo, tag, asset):
    key = (repo, tag, asset)
    if key not in _downloads:
        _downloads[key] = subprocess.run(
            ["gh", "release", "download", tag, "-R", repo, "-p", asset, "-O", "-"],
            capture_output=True, check=True).stdout
    return _downloads[key]


def main():
    manifest = {"packs": []}

    for pack in PACKS:
        folder = os.path.join(RULES, pack["id"], pack["version"])
        os.makedirs(folder, exist_ok=True)
        files = {}

        for asset, members in pack["assets"].items():
            archive = zipfile.ZipFile(io.BytesIO(download(pack["repo"], pack["tag"], asset)))
            for member, name in members.items():
                content = archive.read(member)
                destination = os.path.join(folder, *name.split("/"))
                os.makedirs(os.path.dirname(destination), exist_ok=True)
                with open(destination, "wb") as handle:
                    handle.write(content)
                files[name] = {
                    "bytes": len(content),
                    "sha256": hashlib.sha256(content).hexdigest(),
                    "origin": f"{asset}!{member}",
                }
                print(f"{pack['id']:<14} {pack['version']:<12} {name:<64} {len(content):>9} bytes")

        manifest["packs"].append({
            "id": pack["id"],
            "version": pack["version"],
            "repo": pack["repo"],
            "tag": pack["tag"],
            "licence": pack["licence"],
            "files": files,
        })

    with open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")

    print(f"\n{sum(len(p['files']) for p in manifest['packs'])} artefacts in {os.path.normpath(RULES)}")
    print(f"manifest written to {os.path.normpath(MANIFEST)} - commit it")


if __name__ == "__main__":
    sys.exit(main())
