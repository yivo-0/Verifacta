"""Builds the package and proves a stranger could use it.

Packing is not the same as being usable. This packs Klarfakt, then builds and runs
samples/Quickstart against the resulting .nupkg from a clean directory with an empty package
cache — so a missing dependency, a broken target framework or a file left out of the package shows
up here rather than in someone else's build.

    python tools/verify-package.py
    python tools/verify-package.py --version 1.0.0-preview.1
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SAMPLE = os.path.join(ROOT, "samples", "Quickstart")


def run(command, cwd, env=None, check=True):
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True,
                            env={**os.environ, **(env or {})})
    if check and result.returncode != 0:
        print(result.stdout[-4000:])
        print(result.stderr[-2000:])
        sys.exit(f"failed ({result.returncode}): {' '.join(command)}")

    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="1.0.0-preview.1")
    arguments = parser.parse_args()

    artifacts = os.path.join(ROOT, "artifacts")
    shutil.rmtree(artifacts, ignore_errors=True)

    print(f"packing {arguments.version}")
    run(["dotnet", "pack", "--configuration", "Release",
         f"-p:Version={arguments.version}", "--output", artifacts], cwd=ROOT)

    packages = sorted(name for name in os.listdir(artifacts) if name.endswith(".nupkg"))
    print("  " + "\n  ".join(packages))

    workspace = tempfile.mkdtemp(prefix="klarfakt-package-")
    sample = os.path.join(workspace, "Quickstart")
    shutil.copytree(SAMPLE, sample)

    # A package cache of its own, so a copy of the library already on this machine cannot satisfy
    # the restore and hide a package that does not actually carry what it claims.
    packages_directory = os.path.join(workspace, "packages")

    with open(os.path.join(sample, "NuGet.config"), "w", encoding="utf-8") as handle:
        handle.write(
            '<?xml version="1.0" encoding="utf-8"?>\n'
            "<configuration>\n"
            "  <packageSources>\n"
            "    <clear />\n"
            f'    <add key="local" value="{artifacts}" />\n'
            '    <add key="nuget" value="https://api.nuget.org/v3/index.json" />\n'
            "  </packageSources>\n"
            "</configuration>\n")

    print(f"building the sample in {sample}")
    run(["dotnet", "build", "--configuration", "Release",
         f"-p:KlarfaktVersion={arguments.version}",
         f"--packages", packages_directory], cwd=sample)

    print("restoring the rule packs and validating")
    rules = os.path.join(workspace, "rules")
    result = run(["dotnet", "run", "--configuration", "Release", "--no-build",
                  f"-p:KlarfaktVersion={arguments.version}"],
                 cwd=sample, env={"KLARFAKT_RULES": rules}, check=False)

    print("\n" + result.stdout.strip() + "\n")

    expected = ["Ubl Invoice", "XRechnung", "Verdict:   valid"]
    missing = [text for text in expected if text not in result.stdout]

    if result.returncode != 0 or missing:
        print(result.stderr[-2000:])
        sys.exit(f"the sample did not run as expected (exit {result.returncode}, missing {missing})")

    shutil.rmtree(workspace, ignore_errors=True)
    print(f"the package works from a clean directory: {', '.join(packages)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
