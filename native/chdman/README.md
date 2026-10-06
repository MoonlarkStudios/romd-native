# chdman build boundary

[../../eng/pins/mame.json](../../eng/pins/mame.json) records the exact MAME
0.289 source archive, size, digest, tag and commit. The distribution gate must
build only chdman, strip it and check dependencies on all three pinned native
RIDs. Every release needs corresponding source, an exact linked-license
inventory and build provenance. The preflight workflow publishes no binary.

The local preparation recipe currently supports macOS ARM64 only:

```sh
python3 -B native/chdman/build.py --archive /absolute/path/mame0289.tar.gz \
  --output artifacts/tools/chdman/osx-arm64 --jobs 4 --log /absolute/path/build.log
python3 -B -m unittest discover -s native/chdman -v
```

The script verifies the archive size and SHA-256 before safely extracting it.
It builds MAME's bundled GENie, generates the tool projects and builds the
chdman target with the bundled codec archives. It records the pinned commit in
the version banner. The final link explicitly omits emulator frameworks from
MAME's global macOS settings; dead stripping and an exact allowlist leave only
libSystem and libc++. No source file is patched. Link warnings are fatal.
Apple ld's `-reproducible` preserves stable UUIDs despite incidental object
properties; builds must compare complete bytes, including their signatures.

Apple's C++ runtime is libc++ on macOS. The required Linux recipes must use
`-static-libstdc++`; neither Linux RID has run under this local recipe. Upstream
MAME and its bundled GENie/FLAC emit compiler warnings on Apple clang 21. These
remain visible in the raw log, with upstream warning flags unchanged. This
local tool is explicitly unqualified, has no attestation and must never be
committed as a native binary.
