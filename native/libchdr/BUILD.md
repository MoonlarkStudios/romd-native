# Local native build

From the repository root, with the existing Python, CMake, Ninja and native C
compiler available:

```sh
python3 -B eng/build_libchdr.py --rid osx-arm64
python3 -B eng/verify_libchdr.py --manifest artifacts/native/libchdr/osx-arm64/build-manifest.json
python3 -B -m unittest discover -s eng -p 'test_libchdr.py'
```

The source checkout must already exist at the pin in `eng/pins/libchdr.json`.
No command downloads sources, installs dependencies, copies assets into a
package, or publishes anything. The builder validates the exact source commit,
git describe identity, both ABI header hashes and a clean source tree, including
ignored files. Every tracked file is compared with its committed blob, so
assume-unchanged and skip-worktree cannot conceal implementation changes.
It selects an explicit RID, refuses cross compilation and writes
only ignored local artifacts. Artifact directory components cannot be symlinks
or files; containment is checked before creating or deleting output. The default
build log also rejects symlinks and non-regular files before opening. An explicit
`--log` may point to an evidence file outside artifacts.
Rebuilding discards the generated CMake cache;
inherited compiler, linker and toolchain overrides are rejected, including
Clang's `CCC_OVERRIDE_OPTIONS`, MSVC's `CL`/`_CL_`, implicit search paths and
Git environment overrides. Use `--compiler` to select an existing compiler.
The shim also asserts the effective required feature macros at compile time.
An attempted rebuild invalidates its old manifest before source/tool checks.
The generated-header cache is recreated. The build-info header is included by
explicit path, never by adding that unverified cache to compiler search paths.
`--log` appends commands and output, preserving failed attempts.

The shared library contains bundled static, PIC codecs. Raw-sector, subcode,
block CRC and CD scratch buffers are enabled; LOWRAM, system zlib, system zstd,
LTO, LZMA assembly and upstream tests are disabled. The FLAC backend is embedded
dr_flac. Exactly 18 public `chd_*` functions and `moonlark_chdr_build_info` are
exported. Linux uses a version script, macOS an exported-symbol list, and Windows
a definition file. Linkers reject unresolved symbols. Actual export inspection
rejects both missing symbols and codec exports.

The build-info declaration in `moonlark_chdr_build_info.h` returns library-owned,
immutable UTF-8 JSON with a maximum of 16384 bytes including NUL. Schema and ABI
versions are 1. The family version is used for both managed and native versions.
`buildId` is SHA-256 over canonical JSON of the source pin, versions, RID, flags,
toolchain versions, source commit epoch and wrapper/script file hashes. It is
deliberately independent of the separately recorded binary SHA-256 and byte
length. File, debug and macro prefix maps and `SOURCE_DATE_EPOCH` remove local
source paths and build-time timestamps where the toolchain supports them.

Every local manifest has `qualification: "local-unqualified"` and
`attestation: null`. Verification checks its recipe against current authorities
and wrapper hashes, then inspects architecture, exports, dependencies and the
actual build-info export. These checks do not establish release qualification,
decode correctness, fuzz safety, package installation or managed API acceptance.

The supported recipes are Linux x64 and ARM64, macOS ARM64 and Windows x64.
macOS targets 14.0 and allows only `/usr/lib/libSystem.B.dylib`. Linux rejects
RPATH/RUNPATH, codec shared-library dependencies, GLIBC_PRIVATE and any required
GLIBC version above 2.31; its allowed dependencies are libc, libm and libpthread.
The GLIBC symbol ceiling is checked on the artifact, but a runner/toolchain built
against the documented baseline still needs separate qualification. Windows
requires an existing MSVC compiler and Ninja in a native x64 developer shell;
it uses the static MSVC runtime and allows only KERNEL32.dll. Windows and Linux
recipes remain unexecuted until their native runners build and inspect them.
musl, macOS x64 and cross compilation are unsupported.
