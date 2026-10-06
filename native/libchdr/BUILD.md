# Local native build

From the repository root, with the existing CMake (3.24 or later), Ninja and native C
compiler available:

```sh
dotnet build Moonlark.Native.slnx -c Release -warnaserror
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- native build --rid osx-arm64
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- native verify --manifest artifacts/native/libchdr/osx-arm64/build-manifest.json
dotnet test Moonlark.Native.slnx -c Release --no-build
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
An attempted rebuild invalidates its old manifest and layout-probe receipt before
source/tool checks. The generated-header cache is recreated. The build-info header
is included by explicit path, never by adding that unverified cache to compiler
search paths. `--log` appends commands and output, preserving failed attempts.

`CMakeLists.txt` and `CMakePresets.json` (one configure preset per supported RID)
are the only place build settings live. The shared library contains bundled
static, PIC codecs. Raw-sector, subcode, block CRC and CD scratch buffers are
enabled; LOWRAM, system zlib, system zstd, LTO, LZMA assembly and upstream tests
are disabled. The FLAC backend is embedded dr_flac. The public `chd_*` functions
declared with `CHD_EXPORT` in the pinned `chd.h`, followed by
`moonlark_chdr_build_info`, are the only exports. `exports.txt` is that exact
sequence and is checked against the header; CMake generates the Linux version
script, macOS exported-symbol list and Windows definition file from it at
configure time. Linkers reject unresolved symbols. Actual export inspection
rejects both missing symbols and codec exports.

The build-info declaration in `moonlark_chdr_build_info.h` returns library-owned,
immutable UTF-8 JSON with a maximum of 16384 bytes including NUL. Schema and ABI
versions are 1. The family version is used for both managed and native versions.
`buildId` is SHA-256 over canonical JSON of the recipe: the source pin, versions,
RID, the effective location-independent CMake configuration read from the CMake
File API (cache-v2 and toolchains-v1), compiler identity, tool versions, source
commit epoch and the SHA-256 of every recipe input (the CMake files, shim,
`exports.txt`, the layout probe source and the C# `Core` and `Native` sources).
The recipe never contains paths, so builds into different directories or
checkouts are byte-identical; path-bearing evidence is recorded only in the
manifest's `locations`. The build configures, computes the recipe, writes the
build-info header and only then compiles. `buildId` is deliberately independent
of the separately recorded binary SHA-256 and byte length. File, debug and macro
prefix maps and `SOURCE_DATE_EPOCH` remove local source paths and build-time
timestamps where the toolchain supports them.

The same build compiles and runs `tests/native/libchdr_layout.c` as a CMake
target with clang or GCC and writes `layout-probe.json` beside the manifest;
the managed interop tests compare it with the generated C# layouts. MSVC builds
do not yet produce a probe receipt.

Every local manifest (schema version 2) has `qualification: "local-unqualified"`
and `attestation: null`. Verification checks its recipe against current
authorities and input hashes, then inspects architecture, exports, dependencies
and the actual build-info export. On macOS the code signature is verified before
the build-info export is loaded in-process. These checks do not establish release
qualification, decode correctness, fuzz safety, package installation or managed
API acceptance. Editing any recipe input changes `buildId`; rebuild before verifying.

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
