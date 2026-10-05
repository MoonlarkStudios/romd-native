"""Offline checks for family identity and the non-publishing CI skeleton."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
SHA1 = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
SEMVER = re.compile(
    r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
)
ACTIONS = {
    "actions/checkout": "3d3c42e5aac5ba805825da76410c181273ba90b1",
    "actions/setup-dotnet": "a98b56852c35b8e3190ac28c8c2271da59106c68",
    "actions/setup-python": "5fda3b95a4ea91299a34e894583c3862153e4b97",
}
WORKFLOW_KEYS = {
    "name", "on", "pull_request", "push", "branches", "tags", "permissions",
    "contents", "jobs", "foundation", "source", "preflight", "runs-on",
    "timeout-minutes", "env", "CI", "steps", "uses", "with",
    "persist-credentials", "global-json-file", "python-version", "run", "if",
    "workflow_dispatch", "inputs", "tag", "description", "required", "type",
    "EXPECTED_TAG",
}
SOURCE_COMMANDS = {
    "python3 -B eng/check.py",
    'python3 -B eng/check.py --tag "$EXPECTED_TAG"',
    "python3 -B -m unittest discover -s eng -p 'test_*.py'",
    "dotnet restore Moonlark.Native.slnx --locked-mode",
    "dotnet build Moonlark.Native.slnx -c Release --no-restore -warnaserror",
    "dotnet test Moonlark.Native.slnx -c Release --no-build --no-restore",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def validate_semver(version: str) -> None:
    match = SEMVER.fullmatch(version)
    require(match is not None, f"Invalid managed SemVer: {version!r}")
    if match and match.group(4):
        require(all(not item.isdigit() or item == "0" or not item.startswith("0")
                    for item in match.group(4).split(".")),
                "Numeric prerelease identifiers must not have leading zeroes")


def property_value(tree: ET.Element, name: str) -> str:
    matches = tree.findall(f"./PropertyGroup/{name}")
    require(len(matches) == 1 and bool(matches[0].text), f"One authority required for {name}")
    return matches[0].text or ""


def validate_tag(tag: str, version: str, mame: dict) -> None:
    expected = {f"libchdr-v{version}", f"chdman-{mame['version']}-r{mame['rebuildRevision']}"}
    require(tag in expected, f"Tag {tag!r} disagrees with version authorities")


def check(root: Path, tag: str | None = None) -> None:
    tree = ET.parse(root / "eng/versions/libchdr.props").getroot()
    version = property_value(tree, "LibchdrManagedVersion")
    validate_semver(version)
    require(property_value(tree, "LibchdrNativeVersionMode") == "Family",
            "Libchdr's native mode is permanently Family")
    commit = property_value(tree, "LibchdrUpstreamCommit")
    require(SHA1.fullmatch(commit) is not None, "libchdr commit must be a full SHA")
    pin = json.loads((root / "eng/pins/libchdr.json").read_text())
    require(pin["commit"] == commit, "libchdr props/pin commits disagree")
    require(pin["upstreamVersion"] == property_value(tree, "LibchdrUpstreamVersion"),
            "libchdr props/pin upstream identities disagree")
    require(set(pin["supportedRids"]) == {"linux-x64", "linux-arm64", "osx-arm64", "win-x64"},
            "libchdr RID qualification contract changed")
    require(set(pin["features"]) == {"raw-sectors", "subcode", "block-crc"},
            "Required native safety features changed")
    for name, digest in pin["headers"].items():
        require(SHA256.fullmatch(digest) is not None, f"Invalid header SHA256: {name}")
    major, minor, patch = version.split("-", 1)[0].split("+", 1)[0].split(".")
    require(property_value(tree, "AssemblyVersion") == f"{major}.0.0.0",
            "AssemblyVersion must remain major.0.0.0")
    require(property_value(tree, "FileVersion") == f"{major}.{minor}.{patch}.0",
            "FileVersion must carry the numeric family release")
    mame = json.loads((root / "eng/pins/mame.json").read_text())
    require(SHA1.fullmatch(mame["commit"]) is not None and SHA1.fullmatch(mame["tagObject"]) is not None,
            "MAME requires full tag/commit identities")
    require(SHA256.fullmatch(mame["sourceSha256"]) is not None,
            "MAME requires a source archive SHA256")
    require(type(mame["sourceBytes"]) is int and mame["sourceBytes"] > 0,
            "MAME requires a positive source archive byte length")
    require(type(mame["rebuildRevision"]) is int and mame["rebuildRevision"] > 0,
            "chdman rebuild revision must be positive")
    require(mame["tag"] == "mame" + mame["version"].replace(".", ""),
            "MAME tag/version disagree")
    require(mame["sourceUrl"] == f"https://github.com/mamedev/mame/archive/refs/tags/{mame['tag']}.tar.gz",
            "MAME source URL must match the pinned tag")
    require(set(mame["supportedRids"]) == {"linux-x64", "linux-arm64", "osx-arm64"},
            "chdman RID qualification contract changed")
    if tag is not None:
        validate_tag(tag, version, mame)
    workflow_paths = sorted(path for path in (root / ".github/workflows").iterdir()
                            if path.suffix in {".yml", ".yaml"})
    require({p.name for p in workflow_paths} == {
        "ci.yml", "native-libchdr.yml", "native-chdman.yml", "release.yml"},
        "Missing or unexpected foundation workflow")
    for path in workflow_paths:
        text = path.read_text()
        # This is a deliberately closed skeleton grammar, not a general YAML
        # parser. Reject other forms instead of silently ignoring their meaning.
        for number, line in enumerate(text.splitlines(), 1):
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            require("\t" not in line, f"{path.name}:{number}: tab indentation is unsupported")
            without_expression = re.sub(r"\$\{\{[^}\n]+\}\}", "", line)
            require("{" not in without_expression and "}" not in without_expression,
                    f"{path.name}:{number}: flow mappings are unsupported")
            entry = re.fullmatch(r"\s*(?:-\s+)?([A-Za-z_][A-Za-z0-9_-]*):(.*)", line)
            require(entry is not None, f"{path.name}:{number}: unsupported skeleton YAML form")
            if entry is None:
                continue
            key, value = entry.groups()
            value = value.split(" #", 1)[0].strip()
            require(key in WORKFLOW_KEYS, f"{path.name}:{number}: unreviewed skeleton key {key}")
            require(not value.startswith(("&", "*", "!", "|", ">")),
                    f"{path.name}:{number}: YAML references/tags/block scalars are unsupported")
            require("secrets." not in value, f"{path.name}:{number}: source skeleton cannot access secrets")
            require("${{" not in value or key == "EXPECTED_TAG",
                    f"{path.name}:{number}: expressions are allowed only in the approved tag environment")
            if key == "permissions":
                require(not value, f"{path.name}: permissions must use the explicit read-only mapping")
            if key == "contents":
                require(value == "read", f"{path.name}: contents permission must be read-only")
            if key == "run":
                require(value in SOURCE_COMMANDS, f"{path.name}:{number}: unreviewed source command")
            if key == "if":
                require(value == "github.ref_type == 'tag'", f"{path.name}: unreviewed tag routing")
            if key == "EXPECTED_TAG":
                require(value in {"${{ inputs.tag }}", "${{ github.ref_name }}"},
                        f"{path.name}: tag must come from the dispatch input or actual tag ref")
        active = {line for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#")}
        require({"permissions:", "  contents: read"} <= active,
                f"{path.name}: require active read-only permissions")
        require(not re.search(r"^\s*[\w-]+:\s*write\s*$", text, re.MULTILINE),
                f"{path.name}: publishing permissions require approval")
        require(not re.search(r"nuget\s+push|gh\s+release|git\s+push", text),
                f"{path.name}: publishing commands require approval")
        uses = re.findall(r"^\s*(?:-\s*)?uses:\s*(\S+)", text, re.MULTILINE)
        require(bool(uses), f"{path.name}: expected pinned checkout")
        for action in uses:
            name, _, sha = action.partition("@")
            require(ACTIONS.get(name) == sha, f"{path.name}: unreviewed action {action}")
    ci = (root / ".github/workflows/ci.yml").read_text()
    active_ci = {line for line in ci.splitlines() if line.strip() and not line.lstrip().startswith("#")}
    require({"  push:", "    tags: ['libchdr-*', 'chdman-*']",
             '      - run: python3 -B eng/check.py --tag "$EXPECTED_TAG"',
             "        if: github.ref_type == 'tag'",
             "          EXPECTED_TAG: ${{ github.ref_name }}"} <= active_ci,
            "CI must validate actual library/tool tag refs, including family-native tag rejection")
    print("PASS: family versions, upstream identities, safety/RID contract and source-only CI")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", help="Validate a prospective tag; this never creates it")
    args = parser.parse_args()
    try:
        check(ROOT, args.tag)
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
