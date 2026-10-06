"""Generate internal pinned-header bindings, with a checked LibraryImport pass.

This never installs or downloads a tool. ClangSharp generates every C signature;
the deterministic pass only selects the .NET source-generated import mechanism.
Unknown attributes/signatures and missing or extra exported functions fail closed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng"))
from build_libchdr import build_environment, read_authorities, validate_artifacts_directory, verify_source

EXPECTED_INPUT_SHA256 = "605c09cc3c954b6905ea10f14311bf3436ffb57112e84aba45219308291d8181"
IMPORT = re.compile(r'^(\s*)\[DllImport\("moonlark_chdr", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true\)\]$', re.MULTILINE)
METHOD = re.compile(r'\b(?:internal|public) static extern\s+([\w* ]+)\s+(\w+)\(([^;]*)\);')
RESPONSE_OPTIONS = frozenset((
    "--file", "--traverse", "--include-directory", "--language", "-std", "--namespace",
    "--library-path", "--method-class-name", "--with-access-specifier", "--with-callconv",
    "--remap", "--remap-type", "--config", "--generate", "--output",
))
SINGLETON_OPTIONS = {
    "--output": "artifacts/generation/libchdr/Libchdr.g.cs",
    "--namespace": "Moonlark.Libchdr.Interop",
    "--library-path": "moonlark_chdr",
    "--method-class-name": "NativeMethods",
    "--language": "c",
    "-std": "c11",
    "--with-access-specifier": "*=Internal",
    "--with-callconv": "*=Cdecl",
}


def platform_arguments() -> list[str]:
    """Use the existing platform SDK; libclang does not discover Apple's SDK."""
    if platform.system() != "Darwin":
        return []
    sdk = subprocess.run(["xcrun", "--show-sdk-path"], check=True, capture_output=True,
                         text=True, env=build_environment(dict(os.environ))).stdout.strip()
    if not Path(sdk).is_absolute() or not (Path(sdk) / "usr/include/stdio.h").is_file():
        raise ValueError("Existing macOS SDK must provide an absolute standard-header root")
    return ["--additional=-isysroot" + sdk]


def verify_response(root: Path) -> None:
    """Constrain file/include inputs; reject compiler-argument escape hatches."""
    tokens = (root / "generation/libchdr/generate.rsp").read_text().splitlines()
    if len(tokens) % 2 or any(option not in RESPONSE_OPTIONS for option in tokens[::2]):
        raise ValueError("Unreviewed generator response options")
    pairs = list(zip(tokens[::2], tokens[1::2], strict=True))
    if any(not value or value.startswith(("@", "-", "#")) or any(character.isspace() for character in value)
           for _, value in pairs):
        raise ValueError("Unreviewed generator response value or argument escape")
    for option, expected in SINGLETON_OPTIONS.items():
        if [value for name, value in pairs if name == option] != [expected]:
            raise ValueError(f"Generation {option} must be exactly {expected}")
    # Fixed-width remaps keep host ABI spellings out (LP64 Linux emits nuint for uint64_t).
    for option, expected in {
        "--remap": ["_chd_error=chd_error", "_chd_file=chd_file", "_chd_header=chd_header", "FILE=void",
                    "int32_t=int", "uint32_t=uint", "int64_t=long", "uint64_t=ulong"],
        "--remap-type": ["chd_core_file_callbacks=core_file_callbacks",
                         "chd_core_file_callbacks_and_argp=core_file_callbacks_and_argp",
                         "chd_core_file=core_file"],
    }.items():
        if [value for name, value in pairs if name == option] != expected:
            raise ValueError(f"Unreviewed generation {option} mapping")
    if [value for option, value in pairs if option == "--config"] != ["codegen=latest", "file=single"]:
        raise ValueError("Unreviewed generation config")
    if [value for option, value in pairs if option == "--generate"] != [
        "file-scoped-namespaces", "helper-types", "funcs-with-body=false", "using-statics-for-enums=false",
    ]:
        raise ValueError("Unreviewed generation feature")
    includes = [value for option, value in pairs if option == "--include-directory"]
    if includes != ["native/libchdr/upstream/include"]:
        raise ValueError("Generation include directories must be exclusively the verified upstream tree")
    files = [value for option, value in pairs if option == "--file"]
    traversed = [value for option, value in pairs if option == "--traverse"]
    if files != ["generation/libchdr/input.h"] or traversed != [
        "native/libchdr/upstream/include/libchdr/chd.h",
        "native/libchdr/upstream/include/libchdr/coretypes.h",
        "native/libchdr/moonlark_chdr_build_info.h",
    ]:
        raise ValueError("Unreviewed generation file or traversal input")
    entry = root / "generation/libchdr/input.h"
    if entry.is_symlink() or hashlib.sha256(entry.read_bytes()).hexdigest() != EXPECTED_INPUT_SHA256:
        raise ValueError("Generation entry point must include the verified header and explicit shim path")
    # The shim's const-char-pointer ABI needs no transitive include directory.
    # It is included by explicit path, avoiding an unchecked -I wrapper root.
    shim = root / "native/libchdr/moonlark_chdr_build_info.h"
    if shim.is_symlink() or not shim.is_file() or re.search(r'^\s*#\s*include\b', shim.read_text(), re.MULTILINE):
        raise ValueError("Build-info ABI header must be a direct, self-contained input")


def source_exports(header: str) -> set[str]:
    return set(re.findall(r'^CHD_EXPORT\s+[^;\n]+?\b(chd_\w+)\(', header, re.MULTILINE)) | {"moonlark_chdr_build_info"}


def library_imports(text: str, expected: set[str]) -> str:
    """Preserve generated types and C signatures; reject any unreviewed form."""
    methods = list(METHOD.finditer(text))
    names = [match.group(2) for match in methods]
    if len(names) != len(set(names)) or set(names) != expected:
        raise ValueError(f"Generated functions disagree with pinned exports: {sorted(set(names) ^ expected)}")
    if len(IMPORT.findall(text)) != len(methods) or text.count("[DllImport(") != len(methods):
        raise ValueError("Unreviewed generated import attributes")
    if "internal static unsafe partial class NativeMethods" not in text:
        raise ValueError("Expected the internal unsafe partial method container")
    validate_type_visibility(text)
    text = IMPORT.sub(lambda match: match.group(1) + '[LibraryImport("moonlark_chdr")]\n' +
                      match.group(1) + '[UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]', text)
    text = METHOD.sub(lambda match: f"internal static partial {match.group(1)} {match.group(2)}({match.group(3)});", text)
    if "[DllImport(" in text or re.search(r'\bextern\b', text):
        raise ValueError("A raw runtime-marshalled import survived generation")
    return text.replace("\r\n", "\n")


def validate_type_visibility(text: str) -> None:
    """Nested helpers inherit their internal container's effective visibility."""
    if text.count("namespace Moonlark.Libchdr.Interop;") != 1:
        raise ValueError("Expected exactly one file-scoped raw namespace")
    depth = 0
    for line in text.splitlines():
        if line.lstrip().startswith("//"):
            continue
        if depth == 0 and re.match(r'\s*public\s+(?:(?:static|unsafe|partial|sealed)\s+)*(?:class|struct|enum)\b', line):
            raise ValueError("Raw generated types must remain internal")
        # Generated NativeTypeName strings can contain C syntax; braces inside
        # normal escaped strings do not participate in C# container nesting.
        syntax = re.sub(r'"(?:\\.|[^"\\])*"', '""', line)
        depth += syntax.count("{") - syntax.count("}")
        if depth < 0:
            raise ValueError("Unbalanced generated containers")
    if depth:
        raise ValueError("Unbalanced generated containers")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet", help="Existing SDK executable; never installed by this script")
    parser.add_argument("--check", action="store_true", help="Compare regenerated output without editing tracked bindings")
    args = parser.parse_args()
    pin, version = read_authorities(ROOT)
    tool = json.loads((ROOT / ".config/dotnet-tools.json").read_text())["tools"]["clangsharppinvokegenerator"]
    if tool["version"] != "21.1.8.4":
        raise ValueError("Generator pin changed without a reviewed regeneration")
    source = ROOT / "native/libchdr/upstream"
    verify_source(source, pin)
    verify_response(ROOT)
    output = validate_artifacts_directory(ROOT / "artifacts/generation/libchdr", ROOT)
    output.mkdir(parents=True, exist_ok=True)
    generated = output / "Libchdr.g.cs"
    # A successful invocation must create fresh output, never reuse a stale file.
    generated.unlink(missing_ok=True)
    subprocess.run([args.dotnet, "tool", "run", "ClangSharpPInvokeGenerator", "--",
                    "@generation/libchdr/generate.rsp", *platform_arguments()], cwd=ROOT,
                   env=build_environment(dict(os.environ)), check=True)
    verify_source(source, pin)
    verify_response(ROOT)
    expected = source_exports((source / "include/libchdr/chd.h").read_text())
    rewritten = library_imports(generated.read_text(), expected)
    destination = ROOT / "src/Moonlark.Libchdr/Interop/Libchdr.g.cs"
    contract_path = ROOT / "src/Moonlark.Libchdr/Internal/NativeBuildContract.g.cs"
    contract = build_contract(pin, version, expected)
    if args.check:
        if not destination.exists() or destination.read_text() != rewritten:
            raise ValueError("Generated binding drift; regenerate with the pinned tool")
        if not contract_path.exists() or contract_path.read_text() != contract:
            raise ValueError("Generated build-contract drift; regenerate with the pinned tool")
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(rewritten)
        contract_path.parent.mkdir(parents=True, exist_ok=True)
        contract_path.write_text(contract)
    print(f"PASS: {len(expected)} internal imports from the exact pinned headers")
    return 0


def build_contract(pin: dict, version: str, exports: set[str]) -> str:
    constants = {"FamilyVersion": version, "UpstreamVersion": pin["upstreamVersion"],
                 "UpstreamCommit": pin["commit"], "ChdHeaderSha256": pin["headers"]["include/libchdr/chd.h"],
                 "CoreTypesSha256": pin["headers"]["include/libchdr/coretypes.h"]}
    lines = ["// <auto-generated />", "namespace Moonlark.Libchdr.Internal;", "",
             "internal static class NativeBuildContract", "{"]
    lines += [f"    internal const string {name} = {json.dumps(value)};" for name, value in constants.items()]
    lines += ["    private static readonly string[] ExportNames =", "    [",
              *[f"        {json.dumps(name)}," for name in sorted(exports)], "    ];"]
    lines += ["    internal static System.ReadOnlySpan<string> Exports => ExportNames;"]
    return "\n".join([*lines, "}", ""])


if __name__ == "__main__":
    raise SystemExit(main())
