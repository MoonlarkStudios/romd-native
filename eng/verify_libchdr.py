"""Independently inspect a local libchdr manifest and its native-host binary."""

from __future__ import annotations

import argparse
import ctypes
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

import build_libchdr as build

DEPENDENCIES = {
    "osx-arm64": {"/usr/lib/libSystem.B.dylib"},
    "linux-x64": {"libc.so.6", "libm.so.6", "libpthread.so.0"},
    "linux-arm64": {"libc.so.6", "libm.so.6", "libpthread.so.0"},
    "win-x64": {"kernel32.dll"},
}


def validate_symbols(actual: list[str], expected: list[str]) -> list[str]:
    build.require(len(actual) == len(set(actual)), "Duplicate native exports")
    missing = sorted(set(expected) - set(actual))
    extra = sorted(set(actual) - set(expected))
    build.require(not missing and not extra, f"Export mismatch; missing={missing}; unexpected={extra}")
    return sorted(actual)


def validate_dependencies(actual: list[str], rid: str) -> list[str]:
    normalized = [item.lower() for item in actual] if rid == "win-x64" else actual
    build.require(len(normalized) == len(set(normalized)), "Duplicate dependencies")
    extra = set(normalized) - DEPENDENCIES[rid]
    build.require(not extra, f"Unexpected dynamic dependencies: {sorted(extra)}")
    build.require(bool(normalized), "Expected a system runtime dependency")
    return sorted(normalized)


def parse_nm(text: str, *, mac: bool) -> list[str]:
    symbols = []
    for line in text.splitlines():
        match = re.fullmatch(r"[0-9a-fA-F]+\s+([A-Za-z])\s+(\S+)", line.strip())
        build.require(match is not None, f"Unrecognized nm output: {line!r}")
        if match:
            kind, symbol = match.groups()
            build.require(kind.upper() not in {"U", "W", "V"}, f"Unexpected unresolved/weak export: {symbol}")
            if mac:
                build.require(symbol.startswith("_"), "Unexpected Mach-O symbol spelling")
                symbol = symbol[1:]
            symbols.append(symbol)
    return symbols


def parse_windows_exports(text: str) -> list[str]:
    return re.findall(r"^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)\s*$", text, re.MULTILINE)


def macos_minimum(commands: str) -> str:
    build.require("LC_RPATH" not in commands, "Native binary contains an RPATH")
    minimum = re.findall(r"^\s+minos (\d+\.\d+(?:\.\d+)?)$", commands, re.MULTILINE)
    build.require(minimum == [build.FLAGS["macosDeploymentTarget"]], f"Unexpected macOS floor: {minimum}")
    return minimum[0]


def linux_glibc_maximum(versions: str) -> str:
    identities = set(re.findall(r"\bGLIBC_([A-Za-z0-9_.]+)\b", versions))
    build.require(bool(identities) and all(re.fullmatch(r"\d+(?:\.\d+)+", item) for item in identities),
                  "Missing, private or unsupported GLIBC version requirements")
    maximum = max(identities, key=lambda value: tuple(map(int, value.split("."))))
    build.require(tuple(map(int, maximum.split("."))) <= (2, 31), f"GLIBC floor exceeds 2.31: {maximum}")
    return maximum


def read_build_info(binary: Path) -> dict:
    # Only call the export after digest, filename, architecture and export checks.
    # Read up to the ABI limit without touching bytes beyond the terminating NUL.
    library = ctypes.CDLL(str(binary.resolve()))
    function = library.moonlark_chdr_build_info
    function.argtypes = []
    function.restype = ctypes.c_void_p
    address = function()
    build.require(bool(address), "Native build-info returned NULL")
    data = ctypes.cast(address, ctypes.POINTER(ctypes.c_ubyte))
    payload = bytearray()
    for index in range(16384):
        if data[index] == 0:
            build.require(function() == address, "Native build-info pointer is not immutable")
            return json.loads(payload.decode("utf-8"))
        payload.append(data[index])
    raise ValueError("Native build-info exceeds 16 KiB including terminator")


def inspect_binary(binary: Path, rid: str, exports: list[str], info: dict, log=None) -> dict:
    build.require(rid == build.native_rid(), "Binary inspection requires its native host; no cross-platform qualification")
    build.require(binary.is_file() and not binary.is_symlink(), "Native binary must be a regular file")
    build.require(binary.name == build.FILENAMES[rid], "Unexpected native binary filename")
    platform_info = {}
    if rid == "osx-arm64":
        architectures = build.run(["lipo", "-archs", str(binary)], log=log).split()
        build.require(architectures == ["arm64"], f"Unexpected Mach-O architecture: {architectures}")
        symbols = parse_nm(build.run(["nm", "-gU", str(binary)], log=log), mac=True)
        linked = build.run(["otool", "-L", str(binary)], log=log).splitlines()[1:]
        names = [line.strip().split(" (", 1)[0] for line in linked]
        identity = "@loader_path/" + binary.name
        build.require(names.count(identity) == 1, "Unexpected Mach-O install name")
        dependencies = [name for name in names if name != identity]
        commands = build.run(["otool", "-l", str(binary)], log=log)
        platform_info = {"architecture": "arm64", "minimumOs": macos_minimum(commands)}
    elif rid.startswith("linux"):
        header = build.run(["readelf", "-h", str(binary)], log=log)
        architecture = "AArch64" if rid == "linux-arm64" else "Advanced Micro Devices X86-64"
        build.require(re.search(r"Machine:\s+" + re.escape(architecture) + r"\s*$", header, re.MULTILINE)
                      is not None, "Unexpected ELF architecture")
        build.require(re.search(r"Type:\s+DYN\b", header) is not None, "Expected an ELF shared library")
        symbols = parse_nm(build.run(["nm", "-D", "--defined-only", str(binary)], log=log), mac=False)
        dynamic = build.run(["readelf", "-d", str(binary)], log=log)
        build.require(not re.search(r"\((?:RPATH|RUNPATH)\)", dynamic), "Native binary contains an RPATH/RUNPATH")
        dependencies = re.findall(r"\(NEEDED\).*\[([^\]]+)\]", dynamic)
        versions = build.run(["readelf", "--version-info", str(binary)], log=log)
        maximum = linux_glibc_maximum(versions)
        platform_info = {"architecture": architecture, "maximumRequiredGlibc": maximum,
                         "glibcBaseline": build.FLAGS["linuxMaximumGlibc"]}
    else:
        header = build.run(["dumpbin", "/HEADERS", str(binary)], log=log)
        build.require(re.search(r"\b8664 machine \(x64\)", header) is not None, "Expected x64 PE architecture")
        symbols = parse_windows_exports(build.run(["dumpbin", "/EXPORTS", str(binary)], log=log))
        dependencies = re.findall(r"^\s+([A-Za-z0-9_.-]+\.dll)\s*$",
                                  build.run(["dumpbin", "/DEPENDENTS", str(binary)], log=log), re.MULTILINE | re.IGNORECASE)
        platform_info = {"architecture": "x64", "runtime": "static-msvc"}
    result = {"symbols": validate_symbols(symbols, exports),
              "dependencies": validate_dependencies(dependencies, rid), "platform": platform_info}
    actual = read_build_info(binary)
    build.require(actual == info, "Native build-info differs from the pinned build recipe")
    if log:
        log.write("Native build-info: " + build.canonical(actual).decode("ascii") + "\n")
        log.flush()
    return result


def verify_manifest(manifest: dict, binary: Path, root: Path = build.ROOT) -> None:
    pin, version = build.read_authorities(root)
    rid = manifest["rid"]
    build.require(rid in build.RIDS, "Unsupported manifest RID")
    build.require(manifest["schemaVersion"] == 1 and manifest["product"] == "moonlark_chdr",
                  "Unsupported manifest schema/product")
    build.require(manifest["file"] == build.FILENAMES[rid] == binary.name, "Manifest filename mismatch")
    build.require(binary.is_file() and not binary.is_symlink(), "Native binary must be a regular file")
    build.require(type(manifest["size"]) is int and manifest["size"] > 0
                  and binary.stat().st_size == manifest["size"], "Native binary size mismatch")
    build.require(re.fullmatch(r"[0-9a-f]{64}", manifest["sha256"]) is not None
                  and build.sha256(binary) == manifest["sha256"], "Native binary digest mismatch")
    recipe = manifest["recipe"]
    build.require(recipe["schemaVersion"] == 1 and recipe["source"] == pin, "Recipe source/pin mismatch")
    build.require(recipe["rid"] == rid and recipe["managedVersion"] == recipe["nativeVersion"] == version,
                  "Recipe RID/family version mismatch")
    build.require(type(recipe["sourceDateEpoch"]) is int and recipe["sourceDateEpoch"] > 0,
                  "Invalid SOURCE_DATE_EPOCH")
    build.require(recipe["flags"] == manifest["flags"] == build.FLAGS, "Build flags drift")
    build.require(recipe["toolchain"] == manifest["toolchain"] and bool(recipe["toolchain"]), "Toolchain mismatch")
    wrappers = {name: build.sha256(root / name) for name in build.RECIPE_FILES}
    build.require(recipe["wrapperSha256"] == wrappers, "Wrapper recipe digest mismatch")
    build.require(manifest["buildInfo"] == build.make_build_info(recipe), "Build-info/recipe mismatch")
    build.require(manifest["managedVersion"] == manifest["nativeVersion"] == version
                  and manifest["upstreamCommit"] == pin["commit"]
                  and manifest["upstreamVersion"] == pin["upstreamVersion"], "Manifest identity mismatch")
    validate_symbols(manifest["symbols"], build.expected_exports(root))
    validate_dependencies(manifest["dependencies"], rid)
    build.require(manifest["qualification"] == "local-unqualified" and manifest["attestation"] is None,
                  "Local manifest cannot claim release qualification or attestation")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--source", type=Path, default=build.ROOT / "native/libchdr/upstream")
    parser.add_argument("--log", type=Path)
    args = parser.parse_args()
    log_path = args.log or args.manifest.parent / "verify.log"
    try:
        log_path.parent.mkdir(parents=True, exist_ok=True)
        with log_path.open("a", encoding="utf-8") as log:
            try:
                manifest = json.loads(args.manifest.read_text())
                # Refuse traversal before using manifest content to construct any path.
                build.require(manifest["rid"] in build.RIDS and manifest["file"] == build.FILENAMES[manifest["rid"]],
                              "Unsupported manifest RID/filename")
                binary = args.manifest.parent / "native" / manifest["file"]
                verify_manifest(manifest, binary)
                pin, _ = build.read_authorities()
                epoch = build.verify_source(args.source.resolve(), pin, log)
                build.require(epoch == manifest["recipe"]["sourceDateEpoch"], "Source timestamp mismatch")
                exports = build.expected_exports(source=args.source)
                inspection = inspect_binary(binary, manifest["rid"], exports, manifest["buildInfo"], log)
                for name in ("symbols", "dependencies", "platform"):
                    build.require(inspection[name] == manifest[name], f"Actual binary {name} differs from manifest")
            except Exception as error:
                log.write(f"FAIL: {error}\n")
                raise
        print(f"PASS: digest, recipe, exports, dependencies, architecture and native build-info: {binary}")
        print("Qualification: local-unqualified; no release attestation")
    except (ValueError, KeyError, TypeError, OSError, UnicodeError, ET.ParseError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
