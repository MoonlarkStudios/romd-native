"""Checks the deterministic import pass without substituting for regeneration."""

import importlib.util
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("generate", Path(__file__).with_name("generate.py"))
assert SPEC and SPEC.loader
GENERATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GENERATOR)


def generated(name="chd_read", attribute=None):
    attribute = attribute or '[DllImport("moonlark_chdr", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]'
    return f'''using System.Runtime.InteropServices;
namespace Moonlark.Libchdr.Interop;
internal static unsafe partial class NativeMethods
{{
    {attribute}
    internal static extern chd_error {name}(chd_file* file, uint index, void* buffer);
}}
'''


def pinned_inputs(root):
    actual_root = GENERATOR.ROOT
    source = root / "native/libchdr/upstream"
    source.parent.mkdir(parents=True)
    subprocess.run(["git", "clone", "--quiet", "--shared", "--no-checkout",
                    str(actual_root / "native/libchdr/upstream"), str(source)], check=True)
    pin = json.loads((actual_root / "eng/pins/libchdr.json").read_text())
    subprocess.run(["git", "-C", str(source), "checkout", "--quiet", "--detach", pin["commit"]], check=True)
    for name in ("eng/pins/libchdr.json", "eng/versions/libchdr.props", ".config/dotnet-tools.json",
                 "generation/libchdr/generate.rsp", "generation/libchdr/input.h",
                 "native/libchdr/moonlark_chdr_build_info.h"):
        destination = root / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(actual_root / name, destination)
    return source, pin


class ImportPassTests(unittest.TestCase):
    def test_check_cannot_unlink_tracked_bindings_through_output_symlink(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pinned_inputs(root)
            bindings = root / "src/Moonlark.Libchdr/Interop/Libchdr.g.cs"
            bindings.parent.mkdir(parents=True)
            bindings.write_text("tracked bindings sentinel\n")
            output = root / "artifacts/generation/libchdr"
            output.parent.mkdir(parents=True)
            output.symlink_to(bindings.parent, target_is_directory=True)
            actual_run = subprocess.run

            def intercept_tool(command, **kwargs):
                if command[0] == "dotnet":
                    raise RuntimeError("Output symlink reached generator")
                return actual_run(command, **kwargs)

            with patch.object(GENERATOR, "ROOT", root), patch.object(sys, "argv", ["generate.py", "--check"]), \
                    patch.object(GENERATOR.subprocess, "run", side_effect=intercept_tool):
                with self.assertRaisesRegex(ValueError, "artifacts"):
                    GENERATOR.main()
            self.assertEqual(bindings.read_text(), "tracked bindings sentinel\n")

    def test_response_file_value_cannot_reintroduce_unchecked_includes(self):
        self.assert_bad_response_is_rejected(
            "codegen=latest", "@artifacts/unchecked.rsp", "response value")

    def test_check_cannot_let_tool_write_directly_to_tracked_bindings(self):
        self.assert_bad_response_is_rejected(
            "artifacts/generation/libchdr/Libchdr.g.cs", "src/Moonlark.Libchdr/Interop/Libchdr.g.cs", "output")

    def test_added_changed_or_duplicate_abi_remaps_are_rejected(self):
        for original, replacement in [
            ("--remap\nFILE=void", "--remap\nFILE=void\n--remap-type\nuint32_t=ulong"),
            ("_chd_error=chd_error", "_chd_error=long"),
            ("chd_core_file=core_file", "chd_core_file=core_file\n--remap-type\nchd_core_file=core_file"),
        ]:
            with self.subTest(replacement=replacement):
                self.assert_bad_response_is_rejected(original, replacement, "mapping")

    def test_missing_fixed_width_integer_remap_is_rejected(self):
        # Without these, ClangSharp emits host ABI types, e.g. nuint for uint64_t on LP64 Linux.
        for remap in ("int32_t=int", "uint32_t=uint", "int64_t=long", "uint64_t=ulong"):
            with self.subTest(remap=remap):
                self.assert_bad_response_is_rejected(f"--remap\n{remap}\n", "", "mapping")

    def assert_bad_response_is_rejected(self, original, replacement, message):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pinned_inputs(root)
            nested = root / "artifacts/unchecked.rsp"
            nested.parent.mkdir(parents=True)
            nested.write_text("codegen=latest\n--include-directory\nnative/libchdr\n")
            response = root / "generation/libchdr/generate.rsp"
            response.write_text(response.read_text().replace(original, replacement))
            actual_run = subprocess.run

            def intercept_tool(command, **kwargs):
                if command[0] == "dotnet":
                    raise RuntimeError("Unchecked response reached generator")
                return actual_run(command, **kwargs)

            with patch.object(GENERATOR, "ROOT", root), patch.object(sys, "argv", ["generate.py", "--check"]), \
                    patch.object(GENERATOR.subprocess, "run", side_effect=intercept_tool) as tool:
                with self.assertRaisesRegex(ValueError, message):
                    GENERATOR.main()
                self.assertFalse(any(call.args[0][0] == "dotnet" for call in tool.call_args_list))
            self.assertFalse((root / "src/Moonlark.Libchdr/Interop/Libchdr.g.cs").exists())

    def test_source_change_during_tool_invocation_cannot_commit_bindings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, _ = pinned_inputs(root)
            actual_run = subprocess.run

            def intercept_tool(command, **kwargs):
                if command[0] != "dotnet":
                    return actual_run(command, **kwargs)
                (root / "artifacts/generation/libchdr/Libchdr.g.cs").write_text(generated())
                (source / "include/stdint.h").write_text("unreviewed ABI input introduced during generation")
                return subprocess.CompletedProcess(command, 0)

            with patch.object(GENERATOR, "ROOT", root), patch.object(sys, "argv", ["generate.py"]), \
                    patch.object(GENERATOR.subprocess, "run", side_effect=intercept_tool):
                with self.assertRaisesRegex(ValueError, "dirty"):
                    GENERATOR.main()
            self.assertFalse((root / "src/Moonlark.Libchdr/Interop/Libchdr.g.cs").exists())

    def test_successful_tool_without_fresh_output_cannot_reuse_old_bindings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pinned_inputs(root)
            stale = root / "artifacts/generation/libchdr/Libchdr.g.cs"
            stale.parent.mkdir(parents=True)
            stale.write_text(generated())
            actual_run = subprocess.run

            def intercept_tool(command, **kwargs):
                return subprocess.CompletedProcess(command, 0) if command[0] == "dotnet" else actual_run(command, **kwargs)

            with patch.object(GENERATOR, "ROOT", root), patch.object(sys, "argv", ["generate.py"]), \
                    patch.object(GENERATOR.subprocess, "run", side_effect=intercept_tool):
                with self.assertRaises(FileNotFoundError):
                    GENERATOR.main()
            self.assertFalse(stale.exists())
            self.assertFalse((root / "src/Moonlark.Libchdr/Interop/Libchdr.g.cs").exists())

    def test_unverified_wrapper_include_directory_is_rejected(self):
        actual_root = GENERATOR.ROOT
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in (".config/dotnet-tools.json", "generation/libchdr/generate.rsp"):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(actual_root / name, path)
            response = root / "generation/libchdr/generate.rsp"
            arguments = response.read_text()
            if "--include-directory\nnative/libchdr\n" not in arguments:
                response.write_text(arguments + "--include-directory\nnative/libchdr\n")
            wrapper = root / "native/libchdr"
            wrapper.mkdir(parents=True)
            (wrapper / "stdint.h").write_text("#include_next <stdint.h>\n#define uint32_t uint64_t\n")
            pin = json.loads((actual_root / "eng/pins/libchdr.json").read_text())
            with patch.object(GENERATOR, "ROOT", root), patch.object(sys, "argv", ["generate.py"]), \
                    patch.object(GENERATOR, "read_authorities", return_value=(pin, "1.0.0-preview.1")), \
                    patch.object(GENERATOR, "verify_source"), \
                    patch.object(GENERATOR.subprocess, "run", side_effect=RuntimeError("Unchecked include reached generator")) as tool:
                with self.assertRaisesRegex(ValueError, "include directories"):
                    GENERATOR.main()
                tool.assert_not_called()

    def test_ignored_transitive_include_is_rejected_before_invoking_generator(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, pin = pinned_inputs(root)
            with (source / ".git/info/exclude").open("a") as stream:
                stream.write("\ninclude/stdint.h\n")
            (source / "include/stdint.h").write_text("#include_next <stdint.h>\n#define uint32_t uint64_t\n")
            self.assertEqual(subprocess.check_output(["git", "-C", str(source), "status", "--porcelain"]), b"")
            for name, digest in pin["headers"].items():
                self.assertEqual(hashlib.sha256((source / name).read_bytes()).hexdigest(), digest)
            actual_run = subprocess.run

            def intercept_tool(command, **kwargs):
                if command[0] == "dotnet":
                    raise RuntimeError("Altered ABI input reached generator")
                return actual_run(command, **kwargs)

            with patch.object(GENERATOR, "ROOT", root), patch.object(sys, "argv", ["generate.py"]), \
                    patch.object(GENERATOR.subprocess, "run", side_effect=intercept_tool) as tool:
                with self.assertRaisesRegex(ValueError, "dirty"):
                    GENERATOR.main()
                self.assertFalse(any(call.args[0][0] == "dotnet" for call in tool.call_args_list))

    def test_signature_is_preserved_and_import_is_source_generated(self):
        result = GENERATOR.library_imports(generated(), {"chd_read"})
        self.assertIn("internal static partial chd_error chd_read(chd_file* file, uint index, void* buffer);", result)
        self.assertIn('[LibraryImport("moonlark_chdr")]', result)
        self.assertIn("CallConvCdecl", result)
        self.assertNotIn("DllImport", result)

    def test_actual_generator_public_members_and_nested_helpers_remain_internal(self):
        source = generated().replace("internal static extern", "public static extern")
        source += "internal partial struct chd_header\n{\n    public partial struct Buffer\n    {\n        public byte e0;\n    }\n}\n"
        result = GENERATOR.library_imports(source, {"chd_read"})
        self.assertIn("internal static partial chd_error chd_read", result)

    def test_indented_public_top_level_type_is_rejected(self):
        source = generated() + "    public partial struct LeakedHeader\n{\n}\n"
        with self.assertRaisesRegex(ValueError, "remain internal"):
            GENERATOR.library_imports(source, {"chd_read"})

    def test_unknown_attribute_fails(self):
        with self.assertRaises(ValueError):
            GENERATOR.library_imports(generated(attribute='[DllImport("chdr")]'), {"chd_read"})

    def test_missing_or_extra_function_fails(self):
        for expected in ({"chd_close"}, {"chd_read", "chd_close"}):
            with self.subTest(expected=expected), self.assertRaises(ValueError):
                GENERATOR.library_imports(generated(), expected)

    def test_public_raw_container_fails(self):
        with self.assertRaises(ValueError):
            GENERATOR.library_imports(generated().replace("internal static unsafe", "public static unsafe"), {"chd_read"})

    def test_duplicate_function_fails(self):
        with self.assertRaises(ValueError):
            GENERATOR.library_imports(generated() + generated(), {"chd_read"})

    def test_export_inventory_uses_declarations_only(self):
        text = '/* chd_error chd_create(void); */\nCHD_EXPORT chd_error chd_read(void);\n'
        self.assertEqual(GENERATOR.source_exports(text), {"chd_read", "moonlark_chdr_build_info"})


if __name__ == "__main__":
    unittest.main()
