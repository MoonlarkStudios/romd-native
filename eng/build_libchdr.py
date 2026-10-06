"""Build a pinned libchdr locally; never install, package, download or publish it."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

from check import property_value, validate_semver

ROOT = Path(__file__).resolve().parent.parent
RIDS = ("linux-x64", "linux-arm64", "osx-arm64", "win-x64")
HEADERS = ("include/libchdr/chd.h", "include/libchdr/coretypes.h")
FEATURES = ("raw-sectors", "subcode", "block-crc")
FILENAMES = {
    "linux-x64": "libmoonlark_chdr.so", "linux-arm64": "libmoonlark_chdr.so",
    "osx-arm64": "libmoonlark_chdr.dylib", "win-x64": "moonlark_chdr.dll",
}
FLAGS = {
    "BUILD_SHARED_LIBS": "OFF", "INSTALL_STATIC_LIBS": "OFF",
    "WITH_SYSTEM_ZLIB": "OFF", "WITH_SYSTEM_ZSTD": "OFF",
    "CHDR_WANT_RAW_DATA_SECTOR": "ON", "CHDR_WANT_SUBCODE": "ON",
    "CHDR_VERIFY_BLOCK_CRC": "ON", "CHDR_LOWRAM_TARGET": "OFF",
    "CHDR_CD_SCRATCH_BUFFER": "ON", "CHDR_FLAC_BACKEND": "drflac",
    "CHDR_WANT_TESTS": "OFF", "BUILD_LTO": "OFF", "WITH_LZMA_ASM": "OFF",
    "configuration": "Release", "codecs": "static,pic,hidden",
    "prefixMap": "/_/moonlark", "macosDeploymentTarget": "14.0",
    "linuxMaximumGlibc": "2.31", "msvcRuntime": "MultiThreaded",
}
RECIPE_FILES = (
    "eng/build_libchdr.py", "eng/verify_libchdr.py",
    "native/libchdr/CMakeLists.txt", "native/libchdr/moonlark_chdr_build_info.c",
    "native/libchdr/moonlark_chdr_build_info.h", "native/libchdr/exports.txt",
    "native/libchdr/exports.map", "native/libchdr/exports.osx", "native/libchdr/exports.def",
)
IMPLICIT_TOOL_INPUTS = frozenset((
    "CC", "CXX", "AS", "AR", "LD", "NM", "RANLIB", "RC",
    "CFLAGS", "CPPFLAGS", "CXXFLAGS", "LDFLAGS", "CL", "_CL_", "LINK", "_LINK_",
    "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "OBJC_INCLUDE_PATH",
    "LIBRARY_PATH", "COMPILER_PATH", "GCC_EXEC_PREFIX", "SDKROOT",
    "MACOSX_DEPLOYMENT_TARGET", "LD_PRELOAD", "LD_LIBRARY_PATH",
    "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH", "DYLD_FRAMEWORK_PATH",
))


def build_environment(environment: dict[str, str], epoch: int | None = None) -> dict[str, str]:
    """Reject implicit compiler/git inputs instead of claiming unrecorded flags."""
    overrides = sorted(name for name in environment if name.upper() in IMPLICIT_TOOL_INPUTS
                       or name.upper().startswith(("CCC_", "CMAKE_"))
                       or (name.upper().startswith("GIT_") and name.upper() != "GIT_PAGER"))
    require(not overrides, "Unrecorded build environment overrides: " + ", ".join(overrides))
    result = dict(environment)
    # Noninteractive commands never need the agent/terminal's display pager.
    result.pop("GIT_PAGER", None)
    if epoch is not None:
        result["SOURCE_DATE_EPOCH"] = str(epoch)
    return result


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")


def build_id(recipe: dict) -> str:
    return hashlib.sha256(canonical(recipe)).hexdigest()


def run(command: list[str], *, env: dict | None = None, log=None) -> str:
    if log:
        log.write(f"$ {shlex.join(command)}\n")
        log.flush()
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT,
                            env=build_environment(dict(os.environ)) if env is None else env, check=False)
    if log:
        log.write(result.stdout)
        log.write(f"exit={result.returncode}\n")
        log.flush()
    if result.returncode:
        raise ValueError(f"Command failed ({result.returncode}): {shlex.join(command)}\n{result.stdout}")
    return result.stdout.strip()


def read_authorities(root: Path = ROOT) -> tuple[dict, str]:
    pin = json.loads((root / "eng/pins/libchdr.json").read_text())
    tree = ET.parse(root / "eng/versions/libchdr.props").getroot()
    version = property_value(tree, "LibchdrManagedVersion")
    validate_semver(version)
    require(property_value(tree, "LibchdrNativeVersionMode") == "Family", "Native mode must be Family")
    require(re.fullmatch(r"[0-9a-f]{40}", pin["commit"]) is not None, "Invalid pinned commit")
    require(pin["commit"] == property_value(tree, "LibchdrUpstreamCommit"), "Props/pin commit mismatch")
    require(pin["upstreamVersion"] == property_value(tree, "LibchdrUpstreamVersion"),
            "Props/pin upstream version mismatch")
    require(pin["repository"] == "https://github.com/rtissera/libchdr", "Unexpected upstream repository")
    require(sorted(pin["supportedRids"]) == sorted(RIDS), "Supported RID contract changed")
    require(sorted(pin["features"]) == sorted(FEATURES), "Required features changed")
    require(set(pin["headers"]) == set(HEADERS), "Header authority must contain exactly the two ABI headers")
    for name, digest in pin["headers"].items():
        require(re.fullmatch(r"[0-9a-f]{64}", digest) is not None, f"Invalid header digest: {name}")
    return pin, version


def verify_source(source: Path, pin: dict, log=None) -> int:
    build_environment(dict(os.environ))
    require(source.is_dir() and (source / ".git").exists(), "Source must be an existing git checkout")
    require(run(["git", "-C", str(source), "rev-parse", "HEAD"], log=log) == pin["commit"],
            "Upstream checkout does not match pinned commit")
    require(not run(["git", "-C", str(source), "status", "--porcelain", "--ignored",
                     "--untracked-files=all"], log=log), "Upstream checkout is dirty (including ignored files)")
    verify_tracked_bytes(source, log)
    require(run(["git", "-C", str(source), "describe", "--always", "--tags", "--long"], log=log)
            == pin["upstreamVersion"], "Upstream describe identity does not match pin")
    for name, digest in pin["headers"].items():
        require(sha256(source / name) == digest, f"Upstream header digest mismatch: {name}")
    epoch = run(["git", "-C", str(source), "show", "-s", "--format=%ct", "HEAD"], log=log)
    require(epoch.isdecimal(), "Invalid pinned source timestamp")
    return int(epoch)


def verify_tracked_bytes(source: Path, log=None) -> None:
    """Compare bytes with committed blobs, independently of index shortcuts."""
    entries = run(["git", "--no-replace-objects", "-C", str(source), "ls-tree",
                   "--full-tree", "-r", "-z", "HEAD"], log=log)
    for entry in entries.rstrip("\0").split("\0"):
        identity, name = entry.split("\t", 1)
        mode, kind, expected = identity.split(" ")
        path = source / name
        require(not Path(name).is_absolute() and ".." not in Path(name).parts,
                "Unsafe path in committed source tree")
        require(kind == "blob" and mode in ("100644", "100755"),
                f"Unsupported tracked source mode: {name}")
        require(path.is_file() and not path.is_symlink(), f"Missing or substituted tracked source: {name}")
        data = path.read_bytes()
        # Git blob identity uses SHA-1; binary supply-chain digests use SHA-256.
        actual = hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data,
                              usedforsecurity=False).hexdigest()
        require(actual == expected, f"Tracked source bytes disagree with pinned tree: {name}")


def native_rid(system: str | None = None, machine: str | None = None) -> str:
    system = system or platform.system()
    machine = (machine or platform.machine()).lower()
    known = {("Darwin", "arm64"): "osx-arm64", ("Linux", "x86_64"): "linux-x64",
             ("Linux", "aarch64"): "linux-arm64", ("Linux", "arm64"): "linux-arm64",
             ("Windows", "amd64"): "win-x64", ("Windows", "x86_64"): "win-x64"}
    require((system, machine) in known, f"Unsupported native host: {system}/{machine}; supported: {', '.join(RIDS)}")
    return known[(system, machine)]


def validate_artifacts_directory(output: Path, root: Path = ROOT) -> Path:
    """Refuse redirected output components before creating or deleting files."""
    artifacts = Path(os.path.abspath(root)) / "artifacts"
    output = Path(os.path.abspath(output))
    require(output.is_relative_to(artifacts),
            "Local outputs must remain under the ignored artifacts directory")
    current = artifacts
    for component in (None, *output.relative_to(artifacts).parts):
        if component is not None:
            current /= component
        require(not current.is_symlink(), f"artifacts output must not contain a symlink: {current}")
        require(not current.exists() or current.is_dir(), f"artifacts output must be a directory: {current}")
    resolved = output.resolve()
    require(resolved.is_relative_to(root.resolve() / "artifacts"),
            "Local outputs must remain under the ignored artifacts directory")
    return resolved


def validate_output(output: Path, rid: str, root: Path = ROOT) -> Path:
    require(rid in RIDS, f"Unsupported RID: {rid}")
    return validate_artifacts_directory(output, root)


def prepare_output(output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    for name in ("build", "native", "generated"):
        path = output / name
        require(not path.is_symlink(), f"Output directory must not be a symlink: {path}")
        require(not path.exists() or path.is_dir(), f"Output must be a directory: {path}")
    # Never reuse a cache containing unrecorded compiler/linker/toolchain flags.
    for name in ("build", "generated"):
        if (output / name).exists():
            shutil.rmtree(output / name)
    (output / "generated").mkdir()
    (output / "native").mkdir(exist_ok=True)


def expected_exports(root: Path = ROOT, source: Path | None = None) -> list[str]:
    directory = root / "native/libchdr"
    exports = (directory / "exports.txt").read_text().splitlines()
    require(len(exports) == 19 and len(set(exports)) == 19, "Exactly 19 unique exports required")
    require(all(re.fullmatch(r"(?:chd_[a-z_]+|moonlark_chdr_build_info)", item) for item in exports),
            "Unexpected export spelling")
    if source:
        public = re.findall(r"^CHD_EXPORT\s+[^;\n]*?\b(chd_[a-z_]+)\s*\(",
                            (source / HEADERS[0]).read_text(), re.MULTILINE)
        require(set(exports) == set(public) | {"moonlark_chdr_build_info"} and len(public) == 18,
                "Export allowlist disagrees with pinned public header")
    mac = (directory / "exports.osx").read_text().splitlines()
    windows = (directory / "exports.def").read_text().splitlines()[2:]
    linux = re.findall(r"^\s+((?:chd_[a-z_]+|moonlark_chdr_build_info));$",
                       (directory / "exports.map").read_text(), re.MULTILINE)
    require(mac == ["_" + item for item in exports], "macOS export list drift")
    require([item.strip() for item in windows] == exports, "Windows export list drift")
    require(linux == exports, "Linux export list drift")
    return sorted(exports)


def toolchain(rid: str, compiler: str, log=None) -> dict:
    # cl's /Bv deliberately emits its compiler identity while compiling no file.
    if rid == "win-x64":
        result = subprocess.run([compiler], text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, check=False)
        require("Microsoft" in result.stdout and "Compiler" in result.stdout, "win-x64 requires an existing MSVC compiler")
        compiler_version = result.stdout.strip()
    else:
        compiler_version = run([compiler, "--version"], log=log)
        require("clang" in compiler_version.lower() or "gcc" in compiler_version.lower()
                or "free software foundation" in compiler_version.lower(), "Unsupported C compiler")
    result = {"compiler": compiler_version, "cmake": run(["cmake", "--version"], log=log),
              "ninja": run(["ninja", "--version"], log=log), "hostSystem": platform.system(),
              "hostMachine": platform.machine()}
    if rid == "osx-arm64":
        result["sdkVersion"] = run(["xcrun", "--show-sdk-version"], log=log)
    elif rid.startswith("linux"):
        result["hostGlibc"] = run(["getconf", "GNU_LIBC_VERSION"], log=log)
    return result


def make_recipe(pin: dict, version: str, rid: str, tools: dict, epoch: int, root: Path = ROOT) -> dict:
    return {"schemaVersion": 1, "source": pin, "managedVersion": version,
            "nativeVersion": version, "rid": rid, "flags": FLAGS, "toolchain": tools,
            "sourceDateEpoch": epoch,
            "wrapperSha256": {name: sha256(root / name) for name in RECIPE_FILES}}


def make_build_info(recipe: dict) -> dict:
    pin = recipe["source"]
    return {"schemaVersion": 1, "abiVersion": 1, "managedVersion": recipe["managedVersion"],
            "nativeVersion": recipe["nativeVersion"], "upstreamVersion": pin["upstreamVersion"],
            "upstreamCommit": pin["commit"], "headers": pin["headers"],
            "features": pin["features"], "buildId": build_id(recipe)}


def write_build_info(path: Path, info: dict) -> None:
    payload = canonical(info)
    require(len(payload) + 1 <= 16384, "Build-info exceeds 16 KiB including terminator")
    require(not path.is_symlink(), "Generated build-info must not be a symlink")
    # JSON escaping also produces the C string literal for this ASCII payload.
    path.write_text("#define MOONLARK_CHDR_BUILD_INFO_JSON " + json.dumps(payload.decode("ascii")) + "\n")


def build(args: argparse.Namespace, log) -> Path:
    from verify_libchdr import inspect_binary, verify_manifest

    output = validate_output(args.output or ROOT / "artifacts/native/libchdr" / args.rid, args.rid, ROOT)
    # Invalidate the old receipt even when source/tool validation fails early.
    manifest_path = output / "build-manifest.json"
    manifest_path.unlink(missing_ok=True)
    build_environment(dict(os.environ))
    require(args.rid == native_rid(), "Cross-compilation is not qualified; select this native host's RID")
    pin, version = read_authorities()
    source = args.source.resolve()
    epoch = verify_source(source, pin, log)
    exports = expected_exports(source=source)
    prepare_output(output)
    tools = toolchain(args.rid, args.compiler, log)
    recipe = make_recipe(pin, version, args.rid, tools, epoch)
    info = make_build_info(recipe)
    generated = output / "generated"
    info_header = generated / "moonlark_chdr_build_info_json.h"
    write_build_info(info_header, info)
    env = build_environment(dict(os.environ), epoch)
    command = ["cmake", "-S", str(ROOT / "native/libchdr"), "-B", str(output / "build"),
               "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_C_COMPILER=" + args.compiler,
               "-DMOONLARK_RID=" + args.rid, "-DMOONLARK_UPSTREAM=" + str(source),
               "-DMOONLARK_SOURCE_ROOT=" + str(ROOT),
               "-DMOONLARK_NATIVE_OUTPUT=" + str(output / "native"),
               "-DMOONLARK_BUILD_INFO_HEADER=" + str(info_header)]
    if args.rid == "osx-arm64":
        command += ["-DCMAKE_OSX_ARCHITECTURES=arm64", "-DCMAKE_OSX_DEPLOYMENT_TARGET=14.0"]
    run(command, env=env, log=log)
    run(["cmake", "--build", str(output / "build"), "--target", "moonlark_chdr"], env=env, log=log)
    binary = output / "native" / FILENAMES[args.rid]
    inspection = inspect_binary(binary, args.rid, exports, info, log)
    verify_source(source, pin, log)
    require(recipe == make_recipe(pin, version, args.rid, tools, epoch), "Build recipe changed during compilation")
    manifest = {"schemaVersion": 1, "product": "moonlark_chdr", "rid": args.rid,
                "file": binary.name, "size": binary.stat().st_size, "sha256": sha256(binary),
                "upstreamVersion": pin["upstreamVersion"], "upstreamCommit": pin["commit"],
                "managedVersion": version, "nativeVersion": version, "buildInfo": info,
                "recipe": recipe, "flags": FLAGS, "toolchain": tools,
                "symbols": inspection["symbols"], "dependencies": inspection["dependencies"],
                "platform": inspection["platform"], "qualification": "local-unqualified",
                "attestation": None}
    verify_manifest(manifest, binary)
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    return manifest_path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=RIDS)
    parser.add_argument("--source", type=Path, default=ROOT / "native/libchdr/upstream")
    parser.add_argument("--output", type=Path, help="Ignored artifacts subdirectory only")
    parser.add_argument("--compiler", default="cl" if os.name == "nt" else "clang")
    parser.add_argument("--log", type=Path, help="Preserve commands/output, including failed attempts")
    args = parser.parse_args()
    log_path = args.log or ROOT / "artifacts/native/libchdr" / args.rid / "build.log"
    try:
        if args.log is None:
            validate_artifacts_directory(log_path.parent, ROOT)
            require(not log_path.is_symlink(), "Default artifacts log must not be a symlink")
            require(not log_path.exists() or log_path.is_file(), "Default artifacts log must be a regular file")
        log_path.parent.mkdir(parents=True, exist_ok=True)
        with log_path.open("a", encoding="utf-8") as log:
            try:
                result = build(args, log)
            except Exception as error:
                log.write(f"FAIL: {error}\n")
                raise
        print(f"PASS: local {args.rid} build and inspection; unqualified manifest: {result}")
        print(f"Log: {log_path}")
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError) as error:
        print(f"FAIL: {error}\nLog: {log_path}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
