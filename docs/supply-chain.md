# Native supply-chain policy

Pins live in eng/pins; the libchdr commit also appears in its family props.
libchdr is a submodule at that reviewed commit. MAME is a hash-verified
source tarball. No binary is generated or installed during MSBuild. Bindings are
generated from the pinned headers by the pinned ClangSharp tool, and CI rejects
binding drift; native builds remain local and unqualified until the native gate.

Only CI produces repository binaries, through reviewed automated PRs that carry
manifests and attestations. No local binary is committed. Each binary manifest
records file, size, SHA-256, upstream identity, build flags/toolchain and
attestation reference. Every RID must be tested with its actual native binary,
symbol allowlist, dependency allowlist and documented Linux glibc floor.
Rebuilds compare digests where reproducibility permits. Source date epoch,
file-prefix maps and no timestamps are required. Releases include exact sources.

libchdr is shared, with statically bundled miniz/LZMA/zstd and embedded dr_flac.
Raw sectors, subcode and block CRC are enabled. Only public chd_* exports and
moonlark_chdr_build_info are visible. The Linux version script, macOS export
list and Windows def must include the shim while hiding bundled codec symbols.

The initial CI skeleton has only contents:read and full-SHA action pins. Native
and release dispatches run source checks; they produce no binaries, publish
nothing and issue no attestations. A complete approved native/release pipeline
must replace these source-only skeletons before a release can be accepted.
No credential, tag or release permissions are requested by the skeleton.
