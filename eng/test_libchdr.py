"""Fail-closed native source, export and local manifest regression checks."""

import copy
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from argparse import Namespace
from unittest.mock import patch

import build_libchdr as build
import verify_libchdr as verify


class NativeBuildChecks(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        for name in (*build.RECIPE_FILES, "eng/pins/libchdr.json", "eng/versions/libchdr.props"):
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(build.ROOT / name, target)
        self.pin, self.version = build.read_authorities(self.root)

    def manifest(self):
        binary = self.root / "native/libmoonlark_chdr.so"
        binary.write_bytes(b"synthetic binary; never loaded")
        tools = {"compiler": "synthetic compiler", "cmake": "synthetic cmake", "ninja": "synthetic ninja"}
        recipe = build.make_recipe(self.pin, self.version, "linux-x64", tools, 12345, self.root)
        return binary, {
            "schemaVersion": 1, "product": "moonlark_chdr", "rid": "linux-x64",
            "file": binary.name, "size": binary.stat().st_size, "sha256": build.sha256(binary),
            "upstreamVersion": self.pin["upstreamVersion"], "upstreamCommit": self.pin["commit"],
            "managedVersion": self.version, "nativeVersion": self.version,
            "recipe": recipe, "buildInfo": build.make_build_info(recipe), "flags": build.FLAGS.copy(),
            "toolchain": tools, "symbols": build.expected_exports(self.root),
            "dependencies": ["libc.so.6"], "platform": {"architecture": "synthetic"},
            "qualification": "local-unqualified", "attestation": None,
        }

    def source(self):
        source = self.root / "source"
        source.mkdir()
        for name in build.HEADERS:
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("synthetic " + name)
        (source / ".gitignore").write_text("ignored\n")
        (source / "implementation.c").write_text("int checked_source = 1;\n")
        commands = (["git", "init", "-q", str(source)], ["git", "-C", str(source), "add", "."],
                    ["git", "-C", str(source), "-c", "user.name=Synthetic Test", "-c",
                     "user.email=synthetic@example.invalid", "commit", "-qm", "synthetic test source"])
        for command in commands:
            subprocess.run(command, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        pin = copy.deepcopy(self.pin)
        pin["commit"] = build.run(["git", "-C", str(source), "rev-parse", "HEAD"])
        pin["upstreamVersion"] = build.run(["git", "-C", str(source), "describe", "--always", "--tags", "--long"])
        pin["headers"] = {name: build.sha256(source / name) for name in build.HEADERS}
        return source, pin

    def test_clean_exact_source_and_header_hashes_pass(self):
        source, pin = self.source()
        self.assertGreater(build.verify_source(source, pin), 0)

    def test_source_wrong_commit_fails_before_compilation(self):
        source, pin = self.source()
        pin["commit"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "pinned commit"):
            build.verify_source(source, pin)

    def test_modified_tracked_source_fails(self):
        source, pin = self.source()
        (source / build.HEADERS[0]).write_text("modified")
        with self.assertRaisesRegex(ValueError, "dirty"):
            build.verify_source(source, pin)

    def test_index_flags_cannot_hide_modified_implementation(self):
        for flag in ("--assume-unchanged", "--skip-worktree"):
            with self.subTest(flag=flag):
                source, pin = self.source()
                build.run(["git", "-C", str(source), "update-index", flag, "implementation.c"])
                (source / "implementation.c").write_text("int checked_source = 0;\n")
                self.assertEqual(build.run(["git", "-C", str(source), "status", "--porcelain"]), "")
                with self.assertRaisesRegex(ValueError, "Tracked source bytes"):
                    build.verify_source(source, pin)
                shutil.rmtree(source)

    def test_implicit_compiler_and_git_overrides_are_rejected(self):
        overrides = (*build.IMPLICIT_TOOL_INPUTS, "CCC_OVERRIDE_OPTIONS", "CMAKE_TOOLCHAIN_FILE",
                     "GIT_INDEX_FILE", "GIT_WORK_TREE", "GIT_CONFIG_COUNT")
        for name in overrides:
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "environment overrides"):
                build.build_environment({"PATH": "existing tools", name: "unrecorded"}, 12345)
        self.assertEqual(build.build_environment({"PATH": "existing tools"}, 12345),
                         {"PATH": "existing tools", "SOURCE_DATE_EPOCH": "12345"})
        self.assertEqual(build.build_environment({"PATH": "existing tools", "GIT_PAGER": "agent pager"}),
                         {"PATH": "existing tools"})
        with self.assertRaisesRegex(ValueError, "environment overrides"):
            build.build_environment({"cl": "/DVERIFY_BLOCK_CRC=0"})

    def test_early_failed_build_invalidates_old_manifest(self):
        source, pin = self.source()
        (source / "implementation.c").write_text("unreviewed implementation")
        output = self.root / "artifacts/selected-output"
        output.mkdir(parents=True)
        manifest = output / "build-manifest.json"
        manifest.write_text("old successful manifest")
        args = Namespace(rid="osx-arm64", source=source, output=output, compiler="clang")
        with patch.object(build, "ROOT", self.root), patch.object(build, "native_rid", return_value=args.rid), \
                patch.object(build, "read_authorities", return_value=(pin, self.version)), \
                patch.object(build, "prepare_output") as prepare:
            with self.assertRaisesRegex(ValueError, "dirty"):
                build.build(args, None)
            prepare.assert_not_called()
        self.assertFalse(manifest.exists())

    def test_untracked_and_ignored_inputs_both_fail(self):
        for name in ("extra.c", "ignored"):
            with self.subTest(name=name):
                source, pin = self.source()
                (source / name).write_text("unreviewed")
                with self.assertRaisesRegex(ValueError, "dirty"):
                    build.verify_source(source, pin)
                shutil.rmtree(source)

    def test_clean_source_with_wrong_header_digest_fails(self):
        source, pin = self.source()
        pin["headers"][build.HEADERS[0]] = "0" * 64
        with self.assertRaisesRegex(ValueError, "header digest"):
            build.verify_source(source, pin)

    def test_pin_props_and_closed_header_list_are_required(self):
        path = self.root / "eng/pins/libchdr.json"
        for change, message in (({"commit": "0" * 40}, "commit mismatch"),
                                ({"headers": {"../../escape": "0" * 64}}, "two ABI headers"),
                                ({"features": ["raw-sectors"]}, "features")):
            with self.subTest(change=change):
                pin = copy.deepcopy(self.pin)
                pin.update(change)
                path.write_text(json.dumps(pin))
                with self.assertRaisesRegex(ValueError, message):
                    build.read_authorities(self.root)

    def test_supported_native_hosts_are_explicit(self):
        for system, machine, rid in (("Darwin", "arm64", "osx-arm64"),
                                    ("Linux", "x86_64", "linux-x64"),
                                    ("Linux", "aarch64", "linux-arm64"),
                                    ("Windows", "AMD64", "win-x64")):
            self.assertEqual(build.native_rid(system, machine), rid)
        with self.assertRaisesRegex(ValueError, "Unsupported native host"):
            build.native_rid("Darwin", "x86_64")

    def test_output_cannot_escape_artifacts_through_parent_or_symlink(self):
        with self.assertRaisesRegex(ValueError, "artifacts"):
            build.validate_output(self.root / "src", "osx-arm64", self.root)
        artifacts = self.root / "artifacts"
        artifacts.mkdir()
        (artifacts / "escape").symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "artifacts"):
            build.validate_output(artifacts / "escape/out", "osx-arm64", self.root)
        with self.assertRaisesRegex(ValueError, "Unsupported RID"):
            build.validate_output(artifacts, "linux-musl-x64", self.root)

    def test_artifacts_root_cannot_redirect_build_outputs_into_source(self):
        source = self.root / "src"
        source.mkdir()
        (self.root / "artifacts").symlink_to(source, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "artifacts"):
            build.validate_output(self.root / "artifacts/libchdr", "osx-arm64", self.root)

    def test_default_build_log_cannot_create_directories_in_source(self):
        source = self.root / "src"
        source.mkdir()
        (self.root / "artifacts").symlink_to(source, target_is_directory=True)
        with patch.object(build, "ROOT", self.root), \
                patch.object(sys, "argv", ["build_libchdr.py", "--rid", "osx-arm64"]), \
                patch.object(build, "build", return_value=Path("unused-manifest")) as compile_native:
            self.assertEqual(build.main(), 1)
            compile_native.assert_not_called()
        self.assertFalse((source / "native").exists())

    def test_default_build_log_cannot_append_through_symlink(self):
        source = self.root / "src/existing-source.cs"
        source.parent.mkdir()
        source.write_text("source sentinel\n")
        log_path = self.root / "artifacts/native/libchdr/osx-arm64/build.log"
        log_path.parent.mkdir(parents=True)
        log_path.symlink_to(source)
        with patch.object(build, "ROOT", self.root), \
                patch.object(sys, "argv", ["build_libchdr.py", "--rid", "osx-arm64"]), \
                patch.object(build, "build", side_effect=ValueError("mock tool failure")) as compile_native:
            self.assertEqual(build.main(), 1)
            compile_native.assert_not_called()
        self.assertEqual(source.read_text(), "source sentinel\n")

    def test_export_lists_must_agree(self):
        expected = build.expected_exports(self.root)
        self.assertEqual(len(expected), 19)
        path = self.root / "native/libchdr/exports.osx"
        path.write_text(path.read_text().replace("_chd_read\n", "_zstd_decompress\n"))
        with self.assertRaisesRegex(ValueError, "macOS export list drift"):
            build.expected_exports(self.root)

    def test_build_cache_is_recreated_and_directory_symlinks_are_refused(self):
        output = self.root / "artifacts/build-test"
        (output / "build").mkdir(parents=True)
        (output / "build/CMakeCache.txt").write_text("unreviewed flags")
        build.prepare_output(output)
        self.assertFalse((output / "build").exists())
        (output / "build").symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "symlink"):
            build.prepare_output(output)

    def test_generated_include_cache_cannot_retain_unrecorded_headers(self):
        output = self.root / "artifacts/build-test"
        (output / "generated").mkdir(parents=True)
        (output / "generated/stdint.h").write_text("unrecorded transitive include")
        build.prepare_output(output)
        self.assertEqual(list((output / "generated").iterdir()), [])

    def test_export_verifier_rejects_missing_extra_and_duplicate_symbols(self):
        expected = build.expected_exports(self.root)
        for symbols in (expected[:-1], expected + ["ZSTD_decompress"], expected + [expected[0]]):
            with self.subTest(symbols=symbols), self.assertRaises(ValueError):
                verify.validate_symbols(symbols, expected)
        self.assertEqual(verify.validate_symbols(expected, expected), expected)

    def test_nm_parser_rejects_undefined_and_unrecognized_output(self):
        self.assertEqual(verify.parse_nm("0000000000010000 T _chd_read", mac=True), ["chd_read"])
        for line in ("0000000000000000 U _chd_read", "garbled output"):
            with self.subTest(line=line), self.assertRaises(ValueError):
                verify.parse_nm(line, mac=True)

    def test_dynamic_codec_and_relative_dependencies_fail(self):
        for dependency in ("libzstd.so.1", "libz.so.1", "./libc.so.6", "/tmp/libSystem.B.dylib"):
            with self.subTest(dependency=dependency), self.assertRaisesRegex(ValueError, "dependencies"):
                verify.validate_dependencies([dependency], "linux-x64")

    def test_macos_floor_uses_minos_without_other_command_versions(self):
        commands = "cmd LC_BUILD_VERSION\n  minos 14.0\n  version 27037.1\n  version 0.0\n"
        self.assertEqual(verify.macos_minimum(commands), "14.0")
        for invalid in (commands.replace("minos 14.0", "minos 15.0"), commands + "cmd LC_RPATH\n"):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                verify.macos_minimum(invalid)

    def test_linux_glibc_ceiling_rejects_new_private_and_named_versions(self):
        self.assertEqual(verify.linux_glibc_maximum("Name: GLIBC_2.17\nName: GLIBC_2.31\n"), "2.31")
        for versions in ("Name: GLIBC_2.32", "Name: GLIBC_PRIVATE", "Name: GLIBC_ABI_DT_RELR", ""):
            with self.subTest(versions=versions), self.assertRaises(ValueError):
                verify.linux_glibc_maximum(versions)

    def test_valid_local_manifest_passes(self):
        binary, manifest = self.manifest()
        verify.verify_manifest(manifest, binary, self.root)

    def test_manifest_tampered_digest_size_filename_and_symlink_fail(self):
        for name, value, message in (("sha256", "0" * 64, "digest"), ("size", 1, "size"),
                                    ("size", True, "size"), ("file", "../libmoonlark_chdr.so", "filename"),
                                    ("rid", "linux-musl-x64", "RID")):
            with self.subTest(name=name):
                binary, manifest = self.manifest()
                manifest[name] = value
                with self.assertRaisesRegex(ValueError, message):
                    verify.verify_manifest(manifest, binary, self.root)
        binary, manifest = self.manifest()
        real = binary.with_name("real.so")
        binary.rename(real)
        binary.symlink_to(real)
        with self.assertRaisesRegex(ValueError, "regular file"):
            verify.verify_manifest(manifest, binary, self.root)

    def test_manifest_cannot_claim_qualification_or_attestation(self):
        for name, value in (("qualification", "release-qualified"), ("attestation", "https://example.invalid/provenance")):
            binary, manifest = self.manifest()
            manifest[name] = value
            with self.assertRaisesRegex(ValueError, "qualification or attestation"):
                verify.verify_manifest(manifest, binary, self.root)

    def test_recipe_changed_flags_pin_or_wrapper_fails(self):
        for mutate, message in ((lambda item: item["recipe"]["source"].update(commit="0" * 40), "source/pin"),
                                (lambda item: item["recipe"]["flags"].update(CHDR_VERIFY_BLOCK_CRC="OFF"), "flags"),
                                (lambda item: item["recipe"]["wrapperSha256"].update({build.RECIPE_FILES[0]: "0" * 64}), "recipe digest")):
            with self.subTest(message=message):
                binary, manifest = self.manifest()
                manifest = copy.deepcopy(manifest)
                mutate(manifest)
                with self.assertRaisesRegex(ValueError, message):
                    verify.verify_manifest(manifest, binary, self.root)

    def test_binary_hash_and_recipe_build_id_are_distinct_identities(self):
        binary, manifest = self.manifest()
        build_identity = manifest["buildInfo"]["buildId"]
        binary.write_bytes(b"different synthetic binary")
        manifest.update(size=binary.stat().st_size, sha256=build.sha256(binary))
        verify.verify_manifest(manifest, binary, self.root)
        self.assertEqual(manifest["buildInfo"]["buildId"], build_identity)
        self.assertNotEqual(manifest["sha256"], build_identity)
        changed = copy.deepcopy(manifest["recipe"])
        changed["toolchain"]["compiler"] = "other compiler"
        self.assertNotEqual(build.build_id(changed), build_identity)

    def test_manifest_wrong_build_info_and_missing_fields_fail(self):
        binary, manifest = self.manifest()
        manifest["buildInfo"]["abiVersion"] = 2
        with self.assertRaisesRegex(ValueError, "Build-info/recipe"):
            verify.verify_manifest(manifest, binary, self.root)
        del manifest["sha256"]
        with self.assertRaises(KeyError):
            verify.verify_manifest(manifest, binary, self.root)

    def test_build_info_size_includes_terminator(self):
        path = self.root / "build_info.h"
        build.write_build_info(path, {"a": "x" * (16383 - 8)})
        with self.assertRaisesRegex(ValueError, "16 KiB"):
            build.write_build_info(path, {"a": "x" * (16384 - 8)})


if __name__ == "__main__":
    unittest.main()
