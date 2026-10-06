"""Generate deterministic synthetic CHDs and independent chdman/hash evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng"))
from build_libchdr import build_environment, sha256, validate_artifacts_directory

DVD_CODECS = ("lzma", "zlib", "huff", "flac", "zstd", "none")
CD_CODECS = ("cdlz", "cdzl", "cdfl", "cdzs")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def dvd_bytes() -> bytes:
    """512 sectors including repeated zero/pattern hunks; no filesystem/game content."""
    return b"".join(bytes(2048) if (sector // 8) % 4 == 0 else
                    bytes((sector % 8 * 17 + index * 13 + (index >> 4)) & 255
                          for index in range(2048)) for sector in range(512))


def cd_bytes() -> bytes:
    """16 synthetic MODE1/2352 sectors followed by 16 little-endian audio frames."""
    data = []
    for frame in range(16):
        sector = bytearray((frame * 17 + index * 13 + (index >> 4)) & 255
                           for index in range(2352))
        sector[:12] = b"\0" + b"\xff" * 10 + b"\0"
        sector[12:16] = bytes((0, 2, ((frame // 10) << 4) | frame % 10, 1))
        data.append(bytes(sector))
    for frame in range(16):
        data.append(b"".join(struct.pack("<hh", (frame * 701 + sample * 31) % 65536 - 32768,
                                        (frame * 1103 + sample * 43) % 65536 - 32768)
                             for sample in range(588)))
    return b"".join(data)


def cd_cue() -> str:
    return ('FILE "cd-source.bin" BINARY\n'
            '  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n'
            '  TRACK 02 AUDIO\n    INDEX 00 00:00:16\n    INDEX 01 00:00:20\n')


def cd_subcode_bytes() -> bytes:
    data = cd_bytes()
    return b"".join(data[frame * 2352:(frame + 1) * 2352]
                    + bytes((frame * 11 + index * 7) & 255 for index in range(96))
                    for frame in range(16))


def cd_subcode_toc() -> str:
    return 'CD_ROM\nTRACK MODE1_RAW RW_RAW\nDATAFILE "cd-subcode-source.bin" 00:00:00 00:00:16\n'


def file_info(path: Path, root: Path) -> dict:
    data = path.read_bytes()
    return {"path": str(path.relative_to(root)), "bytes": len(data),
            "sha1": hashlib.sha1(data, usedforsecurity=False).hexdigest(),
            "sha256": hashlib.sha256(data).hexdigest()}


def parse_v5(path: Path) -> dict:
    """Inspect our bounded fixture bytes directly, independent of either native reader."""
    data = path.read_bytes()
    require(len(data) >= 124 and data[:8] == b"MComprHD"
            and struct.unpack_from(">II", data, 8) == (124, 5), "Expected CHD v5 fixture")
    entries = []
    visited = set()
    offset = struct.unpack_from(">Q", data, 48)[0]
    while offset:
        require(offset not in visited and len(visited) < 128, "Cyclic/excessive metadata")
        visited.add(offset)
        require(offset + 16 <= len(data), "Metadata header outside fixture")
        tag, flags_length, next_offset = struct.unpack_from(">IIQ", data, offset)
        length, flags = flags_length & 0xffffff, flags_length >> 24
        require(offset + 16 + length <= len(data), "Metadata value outside fixture")
        value = data[offset + 16:offset + 16 + length]
        entries.append({"tag": tag.to_bytes(4, "big").decode("ascii"), "flags": flags,
                        "length": length, "sha1": hashlib.sha1(value, usedforsecurity=False).hexdigest(),
                        "valueHex": value.hex()})
        offset = next_offset
    return {"logicalBytes": struct.unpack_from(">Q", data, 32)[0],
            "hunkBytes": struct.unpack_from(">I", data, 56)[0],
            "unitBytes": struct.unpack_from(">I", data, 60)[0],
            "rawSha1": data[64:84].hex(), "overallSha1": data[84:104].hex(),
            "parentSha1": data[104:124].hex(), "metadata": entries}


def overall_sha1(raw_sha1: str, metadata: list[dict]) -> str:
    values = sorted(entry["tag"].encode("ascii") + bytes.fromhex(entry["sha1"])
                    for entry in metadata if entry["flags"] & 1)
    return hashlib.sha1(bytes.fromhex(raw_sha1) + b"".join(values), usedforsecurity=False).hexdigest()


def verify_succeeded(output: str) -> bool:
    return ("Raw SHA1 verification successful!" in output
            and "Overall SHA1 verification successful!" in output
            and "Error:" not in output and "No verification" not in output)


def run(command: list[str], cwd: Path, log) -> str:
    log.write("$ " + shlex.join(command) + "\n")
    log.flush()
    result = subprocess.run(command, cwd=cwd, env=build_environment(dict(os.environ)),
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    log.write(result.stdout + f"\nexit={result.returncode}\n")
    log.flush()
    require(result.returncode == 0, "Command failed: " + shlex.join(command))
    return result.stdout


def verify_tool(tool: Path, manifest: Path) -> dict:
    receipt = json.loads(manifest.read_text())
    pin = json.loads((ROOT / "eng/pins/mame.json").read_text())
    require(receipt["pin"] == pin and receipt["schemaVersion"] == 1, "Tool pin/manifest mismatch")
    require(tool.is_file() and not tool.is_symlink(), "Tool must be a regular file")
    require(tool.stat().st_size == receipt["binary"]["bytes"]
            and sha256(tool) == receipt["binary"]["sha256"], "Tool binary digest/size mismatch")
    return receipt


def make_fixture(tool: Path, output: Path, media: str, codec: str, log) -> dict:
    path = output / f"{media}-{codec}.chd"
    source = "dvd-source.iso" if media == "dvd" else "cd-source.cue"
    hunk = 16384 if media == "dvd" else 19584
    run([str(tool), "create" + media, "-i", source, "-o", path.name,
         "-c", codec, "-hs", str(hunk), "-np", "1"], output, log)
    header = parse_v5(path)
    verification = run([str(tool), "verify", "-i", path.name], output, log)
    if codec == "none":
        require("No verification to be done; CHD is uncompressed" in verification,
                "Unexpected uncompressed verification response")
        verified = False
    else:
        require(verify_succeeded(verification), "chdman did not verify both hashes")
        verified = True
    raw = output / f"{media}-{codec}.logical"
    run([str(tool), "extractraw", "-i", path.name, "-o", raw.name], output, log)
    raw_info = file_info(raw, output)
    require(raw_info["bytes"] == header["logicalBytes"], "Logical extraction length mismatch")
    computed_overall = overall_sha1(raw_info["sha1"], header["metadata"])
    if verified:
        require(raw_info["sha1"] == header["rawSha1"], "Independently computed raw SHA1 mismatch")
        require(computed_overall == header["overallSha1"], "Independently computed overall SHA1 mismatch")
    if media == "dvd":
        extracted = output / f"{media}-{codec}.extracted.iso"
        run([str(tool), "extractdvd", "-i", path.name, "-o", extracted.name], output, log)
        require(extracted.read_bytes() == dvd_bytes(), "DVD bytes were not exactly recovered")
    else:
        extracted = output / f"{media}-{codec}.extracted.bin"
        run([str(tool), "extractcd", "-i", path.name, "-o", f"{media}-{codec}.extracted.cue",
             "-ob", extracted.name], output, log)
        require(extracted.read_bytes() == cd_bytes(), "CD source bytes were not exactly recovered")
    return {"media": media, "codec": codec, "chd": file_info(path, output), "header": header,
            "logical": raw_info, "computedOverallSha1": computed_overall,
            "chdmanVerifiedBothHashes": verified, "exactSourceRecovery": True,
            "extracted": file_info(extracted, output)}


def make_subcode_fixture(tool: Path, output: Path, log) -> dict:
    source = cd_subcode_bytes()
    (output / "cd-subcode-source.bin").write_bytes(source)
    (output / "cd-subcode-source.toc").write_text(cd_subcode_toc())
    path = output / "cd-subcode.chd"
    run([str(tool), "createcd", "-i", "cd-subcode-source.toc", "-o", path.name,
         "-c", "cdlz", "-hs", "19584", "-np", "1"], output, log)
    header = parse_v5(path)
    verification = run([str(tool), "verify", "-i", path.name], output, log)
    require(verify_succeeded(verification), "chdman did not verify both subcode fixture hashes")
    logical = output / "cd-subcode.logical"
    run([str(tool), "extractraw", "-i", path.name, "-o", logical.name], output, log)
    require(logical.read_bytes() == source, "Stored frame/subcode bytes were not exactly recovered")
    raw_info = file_info(logical, output)
    computed = overall_sha1(raw_info["sha1"], header["metadata"])
    require(raw_info["sha1"] == header["rawSha1"] and computed == header["overallSha1"],
            "Independent subcode fixture hash mismatch")
    extracted = output / "cd-subcode.extracted.bin"
    run([str(tool), "extractcd", "-i", path.name, "-o", "cd-subcode.extracted.toc",
         "-ob", extracted.name], output, log)
    require(extracted.read_bytes() == source, "TOC frame/subcode bytes were not exactly recovered")
    return {"media": "cd", "codec": "cdlz", "chd": file_info(path, output), "header": header,
            "logical": raw_info, "computedOverallSha1": computed,
            "chdmanVerifiedBothHashes": True, "exactSourceRecovery": True,
            "extracted": file_info(extracted, output)}


def verify_negative_hashes(tool: Path, output: Path, log) -> list[dict]:
    original = (output / "dvd-lzma.chd").read_bytes()
    cases = (("raw-mismatch", 64, "Error: Raw SHA1 in header"),
             ("overall-mismatch", 84, "Error: Overall SHA1 in header"),
             ("raw-missing", 64, "No verification to be done; CHD has no checksum"))
    results = []
    for name, offset, diagnostic in cases:
        data = bytearray(original)
        if name == "raw-missing":
            data[64:84] = bytes(20)
        else:
            data[offset] ^= 1
        path = output / ("negative-" + name + ".chd")
        path.write_bytes(data)
        command = [str(tool), "verify", "-i", path.name]
        log.write("$ " + shlex.join(command) + "\n")
        result = subprocess.run(command, cwd=output, env=build_environment(dict(os.environ)),
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                check=False, timeout=120)
        log.write(result.stdout + f"\nexit={result.returncode}\n")
        log.flush()
        require(diagnostic in result.stdout and not verify_succeeded(result.stdout),
                "Negative verification was not rejected: " + name)
        results.append({"case": name, "chd": file_info(path, output), "exitCode": result.returncode,
                        "diagnostic": diagnostic, "acceptedByVerificationParser": False})
    return results


def generate(tool: Path, manifest: Path, output: Path, log_path: Path) -> dict:
    tool_receipt = verify_tool(tool, manifest)
    tool = tool.resolve()
    output = validate_artifacts_directory(output, ROOT)
    require(output.is_relative_to((ROOT / "artifacts/fixtures").resolve())
            and output != (ROOT / "artifacts/fixtures").resolve(), "Output must be a fixtures subdirectory")
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    (output / "dvd-source.iso").write_bytes(dvd_bytes())
    (output / "cd-source.bin").write_bytes(cd_bytes())
    (output / "cd-source.cue").write_text(cd_cue())
    with log_path.open("w") as log:
        fixtures = [make_fixture(tool, output, "dvd", codec, log) for codec in DVD_CODECS]
        fixtures += [make_fixture(tool, output, "cd", codec, log) for codec in CD_CODECS]
        fixtures.append(make_subcode_fixture(tool, output, log))
        negative_verification = verify_negative_hashes(tool, output, log)
    receipt = {"schemaVersion": 1, "synthetic": True, "generatorVersion": "libchdr-synthetic/v1",
               "tool": tool_receipt, "sources": [file_info(output / name, output) for name in
                                                   ("dvd-source.iso", "cd-source.bin", "cd-source.cue",
                                                    "cd-subcode-source.bin", "cd-subcode-source.toc")],
               "fixtures": fixtures, "negativeVerification": negative_verification}
    (output / "fixtures-manifest.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--chdman", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/fixtures/libchdr")
    parser.add_argument("--log", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(generate(args.chdman, args.manifest, args.output, args.log), indent=2))


if __name__ == "__main__":
    main()
