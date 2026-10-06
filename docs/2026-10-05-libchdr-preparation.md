# libchdr preparation evidence — 2026-10-05

G2 is **not accepted**. This records local native build and managed value-type
preparation. The reader implementation, actual ClangSharp generation, packaging
and release qualification are still outstanding. No package, asset, workflow,
tag or commit was published. G1 remains accepted as a source foundation only.

## Implemented scope

The upstream checkout is pinned to
`607694ca0812edfc9cc2030c64634fc2393668de`. The build validates that identity,
both ABI header SHA-256 digests, all tracked source bytes, and a clean tree
including ignored inputs. Bundled codecs are static and hidden; the shared
library exports only the 18 public `chd_*` functions and the build-info shim.
The shim reports the family version, header identity, required feature flags
and recipe build identity. Local manifests explicitly remain unqualified and
have no release attestation.

Public preparation includes the immutable header, SHA-1 and metadata-tag value
types, codec and error enums, positional data-source contract, and read-ahead
options. Header tests preserve unknown codec values and slot ordering. SHA-1
and metadata-tag span operations enforce zero steady-state managed allocation.
These are value-operation results; no native read allocation claim is made.

The pinned local tool manifest and generation scripts are prepared. The scripts
constrain the exact input headers, response options, output location and internal
interop boundary. Their deterministic import pass preserves ClangSharp-produced
C signatures while selecting `LibraryImport`. Tests use synthetic generated
text and mocked tool invocation. **The actual pinned generator has not run.**

## Current validation

| Check | Actual result | Scope |
| --- | --- | --- |
| Existing SDK 10.0.302 `dotnet build Moonlark.Native.slnx -warnaserror` | PASS, zero warnings/errors | Current managed preparation |
| Same SDK `dotnet test Moonlark.Native.slnx --no-build` | PASS, 10 tests, zero skips | Value operations and assembly contracts |
| `python3 -B -m unittest discover -s eng -p 'test_*.py' -v` | PASS, 38 tests | Foundation and native build/manifest regression checks |
| `python3 -B -m unittest discover -s generation/libchdr -p 'test_*.py' -v` | PASS, 13 tests | Generation preparation; mocked generator only |
| `python3 -B eng/check.py` | PASS | Offline source conventions |
| Two clean `osx-arm64` builds and separate verifier runs | PASS | This macOS ARM64 host only |
| Byte comparison of the two dylibs | PASS, identical | Same source/toolchain on this host |

Both final dylibs are 241,480 bytes with SHA-256
`24f1e95310113cea57b97d5246f725a5c928e798089f010dc2069b936ae92f87`.
Their recipe build ID is
`9c00f404630951d04b095e0ddf7b67ec65e10cc95179c57ef3718fc99350485b`.
Actual inspection found the exact 19 exports, ARM64 architecture, macOS 14.0
minimum, and `/usr/lib/libSystem.B.dylib` as the sole dynamic dependency.
Feature flags are build evidence; decode correctness and container integrity
remain unqualified.

## Independent review and retained failures

Independent preparation review found and resolved implicit compiler/search
inputs, index shortcuts concealing source modification, stale build manifests,
unchecked transitive generation includes, stale generated include caches,
response-file argument escapes, and redirected output/log paths. Each reproduced
regression retains its failed-before-fix attempt. The final bounded independent
review passed 38 engineering and 13 generation-preparation tests and found no
remaining actionable issue in the corrected scope. Additional checks preserved
source and binding sentinels and confirmed rejected paths never invoked the
mocked tool.

Artifact directory components now reject symlinks and file substitutions before
resolution or mutation. The default native build log rejects symlink and
non-regular leaves before opening. Explicit evidence-log paths remain supported.
The safer directory guard is shared by native build and generation preparation.
Clearing generated native inputs and including the shim by an explicit path
prevents old unrecorded headers from influencing a later build.

Earlier native binaries and their passing receipts predate the final recipe and
are historical only. Earlier managed failures included a SHA-1 analyzer warning,
missing public API entries, duplicate analyzer inputs, and an unchecked parse
result. These were corrected without suppressions or warning-policy changes.
Restricted SDK attempts stalled or failed before validation; the unchanged
commands passed using the exact existing SDK with scoped execution permission.
An external source scout was interrupted by content screening; it provides no
native parser or fuzz result.

Raw local receipts are retained under ROMD's ignored `tmp/`, with the common
prefix `playstation-chd-g2-`. Current evidence uses `api-scope-build.log`,
`api-scope-tests.log`, `native-log-leaf-green.log`,
`generation-output-final.log`, `source-check-final.log`,
`native-output-safe-final-{build,rebuild,verify,rebuild-verify}.log`, their
`-result.log` files, and `native-output-safe-final-reproducibility.json`.
Failed path regressions include `native-output-red.log`,
`generation-output-red.log`, `native-log-output-red.log` and
`native-log-leaf-red.log`. `preparation-source-freeze.json` records the local
source snapshot. These receipts and native binaries are not committed assets.

## Outstanding work and approval

Owner boundary A2 blocks installing the pinned local
`ClangSharpPInvokeGenerator` 21.1.8.4 and building/installing pinned MAME
0.289-r1 `chdman` into ignored `artifacts/tools` for synthetic fixtures. The
proposal uses the existing SDK and compiler and requires no global or Homebrew
installation. No installation has happened.

After A2 approval, the next action is actual binding generation and drift
validation, followed by verified loading, SafeHandle ownership, callback error
containment, `ChdFile` reading, metadata/CD helpers and integrity verification.
Synthetic encode/decode fixtures then qualify those paths.

Linux x64/ARM64 and Windows x64 builds have not run. Native read allocation
checks, AOT/trimming publish, native-relative benchmarks, fuzz/malformed corpus,
package/API validation, packing and clean-sample installation have not run for
G2. The foundation's deliberate incomplete-package refusal remains in place;
its earlier pack failure is not a packaging pass. Additional native qualification
hardware/runners require A5. Real media needs A4; no real media was obtained or
used. ROMD integration, archival recovery and emulator compatibility are all
unqualified. No ROMD source or frontend files were changed by this native work.
