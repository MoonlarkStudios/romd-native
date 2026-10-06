"""Fixture source and verification checks; no tools or game data required."""

import hashlib
from pathlib import Path
import struct
import tempfile
import unittest

import generate


class FixtureContractTests(unittest.TestCase):
    def test_v1_source_identities_are_stable(self):
        sources = ((generate.dvd_bytes(), "28fdd300e672af165872ba82ed0bb5e054949dd75cdccc57f017ce13ddf7b73b"),
                   (generate.cd_bytes(), "94fcd21e5ed2483fdad9bd02227ee158d807e9b8b1ca3b5c9016512ea051288a"),
                   (generate.cd_cue().encode(), "d7560171224893384d0aff08e74fdcacfb25a18d14821fe2ec69b5edb9146b03"),
                   (generate.cd_subcode_bytes(), "66ef5fd87220361ff4aff84dc5c3e4b48dd9ec9955c03662bbf850a6b2fdc192"))
        for source, expected in sources:
            self.assertEqual(hashlib.sha256(source).hexdigest(), expected)

    def test_dvd_source_is_exact_sector_length_with_duplicate_and_zero_hunks(self):
        source = generate.dvd_bytes()
        self.assertEqual(len(source), 512 * 2048)
        self.assertEqual(source[:16384], bytes(16384))
        self.assertEqual(source[16384:32768], source[32768:49152])
        self.assertNotEqual(source[16384:32768], bytes(16384))

    def test_cd_source_has_raw_mode_one_and_distinct_little_endian_audio(self):
        source = generate.cd_bytes()
        self.assertEqual(len(source), 32 * 2352)
        self.assertEqual(source[:16], b"\0" + b"\xff" * 10 + b"\0\0\2\0\1")
        self.assertEqual(struct.unpack_from("<hh", source, 16 * 2352), (-32768, -32768))
        self.assertEqual(struct.unpack_from("<hh", source, 16 * 2352 + 4), (-32737, -32725))
        self.assertIn("INDEX 00 00:00:16", generate.cd_cue())
        self.assertIn("INDEX 01 00:00:20", generate.cd_cue())

    def test_subcode_source_preserves_raw_sector_and_nonzero_subcode_per_frame(self):
        source = generate.cd_subcode_bytes()
        data = generate.cd_bytes()
        self.assertEqual(len(source), 16 * 2448)
        for frame in range(16):
            self.assertEqual(source[frame * 2448:frame * 2448 + 2352],
                             data[frame * 2352:(frame + 1) * 2352])
            self.assertEqual(source[frame * 2448 + 2352:(frame + 1) * 2448],
                             bytes((frame * 11 + index * 7) & 255 for index in range(96)))
        self.assertIn("RW_RAW", generate.cd_subcode_toc())

    def test_verify_exit_success_or_raw_success_does_not_establish_overall_integrity(self):
        raw = "Raw SHA1 verification successful!\n"
        overall = "Overall SHA1 verification successful!\n"
        self.assertTrue(generate.verify_succeeded(raw + overall))
        for response in ("", raw, raw + "Error: Overall SHA1 in header", "No verification to be done",
                         raw + overall + "\nError: later operation failed"):
            self.assertFalse(generate.verify_succeeded(response))

    def test_overall_hash_sorts_checksumming_metadata_and_excludes_unchecked_metadata(self):
        raw_sha = "ab" * 20
        metadata = [{"tag": "ZZZZ", "sha1": "22" * 20, "flags": 1},
                    {"tag": "AAAA", "sha1": "11" * 20, "flags": 1},
                    {"tag": "CCCC", "sha1": "33" * 20, "flags": 0}]
        expected = hashlib.sha1(bytes.fromhex(raw_sha) + b"AAAA" + b"\x11" * 20
                                + b"ZZZZ" + b"\x22" * 20, usedforsecurity=False).hexdigest()
        self.assertEqual(generate.overall_sha1(raw_sha, metadata), expected)

    def test_direct_parser_rejects_metadata_cycles_before_repeated_visit(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "cycle.chd"
            data = bytearray(141)
            data[:8] = b"MComprHD"
            struct.pack_into(">II", data, 8, 124, 5)
            struct.pack_into(">Q", data, 48, 124)
            struct.pack_into(">IIQ", data, 124, int.from_bytes(b"TEST", "big"), 0x01000001, 124)
            data[140] = 1
            path.write_bytes(data)
            with self.assertRaisesRegex(ValueError, "Cyclic"):
                generate.parse_v5(path)


if __name__ == "__main__":
    unittest.main()
