"""Reports when an upstream publisher has released a newer version of a pinned rule pack.

Detection is automated; the bump is not. A rule-pack update can change the verdict on invoices
that previously passed, so it needs a human to read the upstream changelog and the corpus diff.
This script tells you an update exists and what changed; `tools/fetch-rules.py` performs the bump.

    python tools/check-rule-updates.py            # print a report, exit 1 if anything is behind
    python tools/check-rule-updates.py --issue     # also open or update a tracking GitHub issue
"""

import json
import os
import subprocess
import sys

MANIFEST = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "src", "Verifacta", "RulePacks.json")

ISSUE_TITLE = "Rule packs behind upstream"
ISSUE_LABEL = "rule-packs"


def gh(*args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    return result.stdout.strip() if result.returncode == 0 else None


def latest_release(repo):
    raw = gh("api", f"repos/{repo}/releases/latest", "--jq", "{tag: .tag_name, date: .published_at, url: .html_url}")
    return json.loads(raw) if raw else None


def main():
    with open(MANIFEST, encoding="utf-8") as handle:
        packs = json.load(handle)["packs"]

    # Several packs can share one upstream repository; report each pin.
    behind, current, unknown = [], [], []

    for pack in packs:
        latest = latest_release(pack["repo"])
        if latest is None:
            unknown.append(pack)
            continue
        if latest["tag"] != pack["tag"]:
            behind.append((pack, latest))
        else:
            current.append(pack)

    for pack in current:
        print(f"  current   {pack['id']:<14} {pack['tag']}")
    for pack in unknown:
        print(f"  unknown   {pack['id']:<14} {pack['repo']} has no published release")
    for pack, latest in behind:
        print(f"  BEHIND    {pack['id']:<14} pinned {pack['tag']}  ->  {latest['tag']} ({latest['date'][:10]})")

    if not behind:
        print(f"\nAll {len(packs)} packs are on the latest upstream release.")
        return 0

    body = [
        "The following rule packs are behind their upstream release.",
        "",
        "| Pack | Pinned | Latest | Published | Release |",
        "|---|---|---|---|---|",
    ]
    for pack, latest in behind:
        body.append(
            f"| `{pack['id']}` | `{pack['tag']}` | `{latest['tag']}` | {latest['date'][:10]} | {latest['url']} |")
    body += [
        "",
        "To bump: edit the version and asset names in `tools/fetch-rules.py`, run it, commit the",
        "regenerated `src/Verifacta/RulePacks.json`, and review the corpus reports for changed",
        "verdicts before releasing. A rule update can invalidate invoices that previously passed.",
    ]
    report = "\n".join(body)

    print()
    print(report)

    if "--issue" in sys.argv:
        existing = gh("issue", "list", "--state", "open", "--search", f'"{ISSUE_TITLE}" in:title',
                      "--json", "number", "--jq", ".[0].number")
        if existing:
            gh("issue", "comment", existing, "--body", report)
            print(f"\nCommented on issue #{existing}")
        else:
            gh("issue", "create", "--title", ISSUE_TITLE, "--body", report, "--label", ISSUE_LABEL)
            print(f"\nOpened issue: {ISSUE_TITLE}")

    return 1


if __name__ == "__main__":
    sys.exit(main())
