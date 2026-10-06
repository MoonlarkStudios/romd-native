# Synthetic libchdr fixtures

All data is deterministic and contains no game content. Generate into ignored
artifacts with the exact pinned, locally built chdman:

```sh
dotnet run --project eng/Moonlark.Native.Engineering -c Release -- fixtures generate \
  --chdman artifacts/tools/chdman/osx-arm64/native/chdman \
  --manifest artifacts/tools/chdman/osx-arm64/build-manifest.json \
  --log /absolute/path/fixtures.log
dotnet test tests/Moonlark.Native.Engineering.Tests -c Release --filter "FullyQualifiedName~Fixtures"
```

The 1 MiB DVD source is a logical 2,048-byte sector stream with repeated
pattern and zero hunks. The six DVD cases exercise LZMA, zlib, Huffman, FLAC,
zstd and uncompressed v5. The CD source has 16 MODE1/2352 data frames and 16
stereo audio frames; four audio frames form a stored pregap. Its deliberately
synthetic data-sector EDC/ECC fields are preserved, rather than presented as a
standards-compliant mastered disc. Audio in the source BIN is little-endian.
CHD logical audio is stored with the native CD byte order, while `extractcd`
recovers the exact source BIN. CD cases exercise cdlz, cdzl, cdfl and cdzs.
A separate 16-frame CDRDAO `RW_RAW` fixture preserves deterministic nonzero
96-byte subcode for each raw frame.

The generator validates the tool pin and binary digest, requires both raw and
overall success messages from `chdman verify`, independently recomputes SHA-1
from the extracted logical bytes and checksummed metadata, and compares the
DVD/BIN extractions byte for byte with the original sources. A zero tool exit
status alone does not establish verification. Uncompressed v5 has no stored
hashes and chdman declines verification; its manifest explicitly records this
alongside independently computed hashes and exact source recovery.
Three negative header-hash copies exercise raw mismatch, overall mismatch and
missing raw SHA-1. The pinned chdman returns exit zero for all three, while the
fixture verification parser rejects their diagnostics.

`fixtures-manifest.json` records sources, recipes, CHD and logical hashes,
headers, metadata and recovery results. Source identities for v1 are:

| Source | Bytes | SHA-256 |
| --- | ---: | --- |
| DVD logical source | 1048576 | `28fdd300e672af165872ba82ed0bb5e054949dd75cdccc57f017ce13ddf7b73b` |
| CD BIN | 75264 | `94fcd21e5ed2483fdad9bd02227ee158d807e9b8b1ca3b5c9016512ea051288a` |
| CD CUE | 133 | `d7560171224893384d0aff08e74fdcacfb25a18d14821fe2ec69b5edb9146b03` |

Generated binaries and source data remain local ignored artifacts. These small
synthetic cases establish no real-disc, DVD-9 memory, emulator compatibility or
platform qualification.
