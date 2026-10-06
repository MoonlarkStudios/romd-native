"""Compile and execute a real pinned-header layout probe on the native host."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shlex
import subprocess

from build_libchdr import (ROOT, build_environment, native_rid, read_authorities,
                          require, sha256, validate_artifacts_directory, verify_source)


def run(command: list[str], env: dict[str, str], log) -> str:
    log.write("$ " + shlex.join(command) + "\n")
    log.flush()
    result = subprocess.run(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, check=False, timeout=120)
    log.write(result.stdout + f"\nexit={result.returncode}\n")
    log.flush()
    require(result.returncode == 0, "Command failed: " + shlex.join(command))
    return result.stdout


def probe(compiler: str, log_path: Path) -> dict:
    rid = native_rid()
    output = validate_artifacts_directory(ROOT / "artifacts/native/libchdr" / rid, ROOT)
    output.mkdir(parents=True, exist_ok=True)
    destination = output / "layout-probe.json"
    destination.unlink(missing_ok=True)
    binary = output / ("layout-probe.exe" if rid == "win-x64" else "layout-probe")
    require(not binary.is_symlink(), "Probe binary must not be a symlink")
    binary.unlink(missing_ok=True)
    pin, _ = read_authorities()
    source = ROOT / "native/libchdr/upstream"
    program = ROOT / "tests/native/libchdr_layout.c"
    program_digest = sha256(program)
    with log_path.open("w") as log:
        epoch = verify_source(source, pin, log)
        env = build_environment(dict(os.environ), epoch)
        version = run([compiler, "--version"], env, log).strip()
        require("clang" in version.lower(), "The layout probe currently requires existing clang")
        target = run([compiler, "-dumpmachine"], env, log).strip()
        command = [compiler, "-std=c11", "-Wall", "-Wextra", "-Werror", "-pedantic",
                   "-I", str(source / "include"), str(program), "-o", str(binary)]
        run(command, env, log)
        measured = json.loads(run([str(binary)], env, log))
        verify_source(source, pin, log)
    require(program_digest == sha256(program), "Probe source changed during compilation/execution")
    require(measured["schemaVersion"] == 1, "Unexpected probe schema")
    macros = measured["platformMacros"]
    require(macros == {"apple": rid == "osx-arm64", "linux": rid.startswith("linux"),
                       "windows": rid == "win-x64"}, "Probe target differs from the native host")
    require(measured["architectureMacros"] == {"arm64": rid.endswith("arm64"), "x64": rid.endswith("x64")},
            "Probe architecture differs from the native host")
    receipt = {"schemaVersion": 1, "rid": rid, "upstreamCommit": pin["commit"],
               "headers": pin["headers"], "compiler": version, "compilerTarget": target,
               "compileCommand": command, "programSha256": program_digest,
               "binarySha256": sha256(binary), "measurements": measured}
    destination.write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler", default="clang")
    parser.add_argument("--log", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(probe(args.compiler, args.log), indent=2))


if __name__ == "__main__":
    main()
