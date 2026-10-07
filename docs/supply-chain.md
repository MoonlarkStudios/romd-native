# Native supply-chain policy

Pins live in eng/pins; the libchdr commit also appears in its family props.
libchdr is a submodule at that reviewed commit. MAME is a hash-verified
source tarball. No binary is generated or installed during MSBuild. Bindings are
generated from the pinned headers by the pinned ClangSharp tool, and CI rejects
binding drift. Local and CI evidence builds remain unqualified until the native gate.

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

Source and release skeletons retain contents:read and full-SHA action pins.
The approved Linux workflow builds native evidence and issues provenance only
after both architectures pass and all eight subjects validate against the
triggering commit. Only its isolated signing job has id-token:write and
attestations:write; it executes no repository code. Native manifests remain
local-unqualified with null attestation fields; the separate signed bundle
records provenance without granting release qualification. See
[Linux qualification](linux-qualification.md) for the evidence boundary.
Package publication, release assets, tags and automated binary PRs remain
unimplemented and require separate approval before a release can be accepted.
