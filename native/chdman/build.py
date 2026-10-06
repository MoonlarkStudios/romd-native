"""Build only the pinned MAME chdman, into ignored local artifacts."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import shlex
import shutil
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng"))
from build_libchdr import build_environment, sha256, validate_artifacts_directory


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def read_pin(root: Path = ROOT) -> dict:
    pin = json.loads((root / "eng/pins/mame.json").read_text())
    require(pin["repository"] == "https://github.com/mamedev/mame", "Unexpected MAME repository")
    require(re.fullmatch(r"[0-9a-f]{64}", pin["sourceSha256"]) is not None, "Invalid archive digest")
    require(re.fullmatch(r"[0-9a-f]{40}", pin["commit"]) is not None, "Invalid source commit")
    require(pin["sourceBytes"] > 0 and pin["rebuildRevision"] > 0, "Invalid source size/revision")
    require(pin["tag"] == "mame" + pin["version"].replace(".", ""), "Tag/version mismatch")
    return pin


def validate_archive(archive: Path, pin: dict) -> None:
    require(archive.is_file() and not archive.is_symlink(), "Archive must be a regular file")
    require(archive.stat().st_size == pin["sourceBytes"], "Source archive size mismatch")
    require(sha256(archive) == pin["sourceSha256"], "Source archive digest mismatch")


def safe_members(members: list[tarfile.TarInfo], prefix: str) -> None:
    for member in members:
        path = PurePosixPath(member.name)
        require(not path.is_absolute() and ".." not in path.parts
                and path.parts[0] == prefix, "Unsafe archive member: " + member.name)
        require(member.isfile() or member.isdir() or member.issym(),
                "Unsupported archive member: " + member.name)
        if member.issym():
            target = PurePosixPath(member.linkname)
            require(not target.is_absolute() and ".." not in target.parts,
                    "Unsafe archive link: " + member.name)


def extract_source(archive: Path, destination: Path, pin: dict) -> tuple[Path, int]:
    validate_archive(archive, pin)
    require(not destination.exists(), "Extraction destination must be absent")
    prefix = "mame-" + pin["tag"]
    with tarfile.open(archive, "r:gz") as source:
        members = source.getmembers()
        safe_members(members, prefix)
        source.extractall(destination, members=members, filter="data")
        epoch = int(members[0].mtime)
    return destination / prefix, epoch


def environment(epoch: int) -> dict[str, str]:
    result = build_environment(dict(os.environ), epoch)
    require(not any(key in result for key in ("MAKEFLAGS", "MFLAGS", "GENIE_FLAGS", "MAKEFILES",
                                             "MAKEOVERRIDES", "GNUMAKEFLAGS")),
            "Unrecorded make/GENie environment override")
    # Upstream make variables can also be inherited by name. Supply only
    # process/runtime essentials so unrelated shell variables cannot set them.
    result = {key: value for key, value in result.items()
              if key in ("PATH", "HOME", "TMPDIR", "TMP", "TEMP", "USER", "LOGNAME", "SHELL",
                         "SOURCE_DATE_EPOCH")}
    result["LC_ALL"] = "C"
    result["TZ"] = "UTC"
    return result


def run(command: list[str], cwd: Path, env: dict[str, str], log) -> str:
    log.write("$ " + shlex.join(command) + "\n")
    log.flush()
    result = subprocess.run(command, cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    log.write(result.stdout + f"\nexit={result.returncode}\n")
    log.flush()
    require(result.returncode == 0, "Command failed: " + shlex.join(command))
    return result.stdout


def generate_arguments(source: Path, clang_version: str) -> list[str]:
    prefix = "-ffile-prefix-map=" + str(source) + "=/_/mame"
    return [str(source / "3rdparty/genie/bin/darwin/genie"), "--with-tools",
            "--target=mame", "--subtarget=mame", "--osd=mac", "--targetos=macosx",
            "--PLATFORM=arm64", "--build-dir=build", "--gcc=osx-clang",
            "--gcc_version=" + clang_version, "--OPTIMIZE=3", "--NOASM=1",
            "--SYMBOLS=0", "--STRIP_SYMBOLS=1", "--SEPARATE_BIN=1",
            "--ARCHOPTS=" + prefix + " -mmacosx-version-min=14.0",
            "--LDOPTS=-mmacosx-version-min=14.0 -Wl,-dead_strip,-dead_strip_dylibs", "gmake"]


def build(archive: Path, output: Path, jobs: int, log_path: Path) -> dict:
    require(platform.system() == "Darwin" and platform.machine() == "arm64",
            "This local recipe currently qualifies only its macOS ARM64 execution")
    require(1 <= jobs <= 32, "Jobs must be between 1 and 32")
    output = validate_artifacts_directory(output, ROOT)
    pin = read_pin()
    validate_archive(archive, pin)
    output.mkdir(parents=True, exist_ok=True)
    manifest = output / "build-manifest.json"
    manifest.unlink(missing_ok=True)
    for name in ("source", "native"):
        child = output / name
        require(not child.is_symlink(), "Output child cannot be a symlink: " + name)
        if child.exists():
            shutil.rmtree(child)
    source, epoch = extract_source(archive, output / "source", pin)
    env = environment(epoch)
    with log_path.open("w") as log:
        compiler = run(["clang", "--version"], source, env, log)
        sdk = run(["xcrun", "--show-sdk-version"], source, env, log).strip()
        make_version = run(["make", "--version"], source, env, log).splitlines()[0]
        match = re.search(r"clang version (\d+\.\d+\.\d+)", compiler)
        require(match is not None, "Cannot identify clang version")
        run(["make", "-C", "3rdparty/genie/build/gmake.darwin", "-f", "genie.make",
             f"-j{jobs}"], source, env, log)
        run(["make", "build/generated/version.cpp", "OSD=mac", "EMULATOR=0", "TOOLS=1",
             "PLATFORM=arm64", "OVERRIDE_CC=clang", "OVERRIDE_CXX=clang++",
             "NEW_GIT_VERSION=" + pin["commit"]], source, env, log)
        arguments = generate_arguments(source, match.group(1))
        run(arguments, source, env, log)
        # mac.lua adds emulator frameworks globally. A recorded, explicit tool
        # link uses the same bundled archives, retaining only chdman's imports.
        link_arguments = ["make", "-C", "build/projects/mac/mame/gmake-osx-clang", "config=release64",
                          f"-j{jobs}", "LIBS=$(LDDEPS) -lpthread",
                          "ALL_LDFLAGS=-m64 -arch arm64 -mmacosx-version-min=14.0 "
                          "-Wl,-dead_strip,-dead_strip_dylibs,-fatal_warnings,-reproducible", "chdman"]
        run(link_arguments, source, env, log)
        binary = source / "build/osx_clang/bin/x64/Release/chdman"
        require(binary.is_file(), "chdman output was not produced")
        native = output / "native"
        native.mkdir()
        tool = native / "chdman"
        shutil.copy2(binary, tool)
        tool.chmod(0o755)
        dependencies = run(["otool", "-L", str(tool)], source, env, log)
        dependency_paths = [line.strip().split(" ")[0] for line in dependencies.splitlines()[1:]]
        allowed = {"/usr/lib/libSystem.B.dylib", "/usr/lib/libc++.1.dylib"}
        require(set(dependency_paths) <= allowed, "Unexpected dynamic dependency: " + dependencies)
        architecture = run(["lipo", "-archs", str(tool)], source, env, log).strip()
        require(architecture == "arm64", "Unexpected binary architecture")
        version = run([str(tool), "listtemplates"], source, env, log).splitlines()[0]
        require(version.startswith("chdman - MAME Compressed Hunks of Data (CHD) manager "
                                   + pin["version"] + " "), "chdman version mismatch")
    receipt = {"schemaVersion": 1, "qualification": "local-unqualified", "attestation": None,
               "rid": "osx-arm64", "pin": pin, "compiler": compiler.strip(),
               "macosSdk": sdk, "makeVersion": make_version,
               "recipeSha256": sha256(Path(__file__)),
               "sourceDateEpoch": epoch, "arguments": arguments[1:],
               "linkArguments": link_arguments,
               "binary": {"path": "native/chdman", "sha256": sha256(tool),
                          "bytes": tool.stat().st_size, "dependencies": dependency_paths},
               "version": version}
    manifest.write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/tools/chdman/osx-arm64")
    parser.add_argument("--jobs", type=int, default=4)
    parser.add_argument("--log", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(build(args.archive, args.output, args.jobs, args.log), indent=2))


if __name__ == "__main__":
    main()
