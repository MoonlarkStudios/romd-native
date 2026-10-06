"""Archive and execution-contract checks without installing or executing tools."""

import hashlib
import io
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import build


class BuildContractTests(unittest.TestCase):
    def test_pinned_archive_is_checked_before_extraction(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = Path(temporary) / "archive.tar.gz"
            archive.write_bytes(b"wrong")
            pin = {"sourceBytes": 5, "sourceSha256": "0" * 64}
            with self.assertRaisesRegex(ValueError, "digest mismatch"):
                build.validate_archive(archive, pin)

    def test_safe_archive_preserves_regular_files_and_internal_links(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = Path(temporary) / "archive.tar.gz"
            with tarfile.open(archive, "w:gz") as output:
                item = tarfile.TarInfo("mame-mame000/file")
                item.size = 5
                item.mtime = 12345
                output.addfile(item, io.BytesIO(b"bytes"))
                link = tarfile.TarInfo("mame-mame000/link")
                link.type = tarfile.SYMTYPE
                link.linkname = "file"
                output.addfile(link)
            pin = {"tag": "mame000", "sourceBytes": archive.stat().st_size,
                   "sourceSha256": hashlib.sha256(archive.read_bytes()).hexdigest()}
            source, epoch = build.extract_source(archive, Path(temporary) / "output", pin)
            self.assertEqual((source / "link").read_bytes(), b"bytes")
            self.assertEqual(epoch, 12345)

    def test_path_traversal_and_external_links_are_rejected(self):
        for name, link in (("mame-mame000/../escape", None), ("/mame-mame000/file", None),
                           ("other/file", None), ("mame-mame000/link", "../outside"),
                           ("mame-mame000/link", "/outside")):
            with self.subTest(name=name, link=link):
                item = tarfile.TarInfo(name)
                if link:
                    item.type = tarfile.SYMTYPE
                    item.linkname = link
                with self.assertRaisesRegex(ValueError, "Unsafe"):
                    build.safe_members([item], "mame-mame000")

    def test_special_files_and_hard_links_are_rejected(self):
        for kind in (tarfile.FIFOTYPE, tarfile.CHRTYPE, tarfile.LNKTYPE):
            with self.subTest(kind=kind):
                item = tarfile.TarInfo("mame-mame000/device")
                item.type = kind
                with self.assertRaisesRegex(ValueError, "Unsupported"):
                    build.safe_members([item], "mame-mame000")

    def test_generation_keeps_fatal_warnings_and_bundled_codecs(self):
        arguments = build.generate_arguments(Path("/source"), "21.0.0")
        self.assertNotIn("--with-emulator", arguments)
        self.assertFalse(any("NOWERROR" in value or "with-system" in value for value in arguments))
        self.assertIn("--STRIP_SYMBOLS=1", arguments)
        self.assertIn("--osd=mac", arguments)
        self.assertIn("--gcc_version=21.0.0", arguments)

    def test_make_environment_cannot_change_the_recorded_recipe(self):
        with patch.dict(build.os.environ, {"PATH": "/tools", "OPTIMIZE": "0", "NOWERROR": "1"}, clear=True):
            environment = build.environment(12345)
        self.assertEqual(environment["PATH"], "/tools")
        self.assertEqual(environment["SOURCE_DATE_EPOCH"], "12345")
        self.assertNotIn("OPTIMIZE", environment)
        self.assertNotIn("NOWERROR", environment)
        with patch.dict(build.os.environ, {"MAKEFILES": "/unsafe/file"}, clear=True):
            with self.assertRaisesRegex(ValueError, "override"):
                build.environment(12345)


if __name__ == "__main__":
    unittest.main()
