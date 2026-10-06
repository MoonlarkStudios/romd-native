# Contributing

Use the SDK pinned in global.json. Do not introduce runtime dependencies beyond
the BCL and the exact family native package. Tooling and test dependencies are
centrally pinned and locked. Restore once to update locks deliberately; CI
uses `dotnet restore --locked-mode`. No build downloads native assets.

Run the foundation commands in [README](README.md). Also run
`dotnet pack Moonlark.Native.slnx -c Release --no-restore`; it currently rejects
unqualified packages by design. This rejection is not a packaging pass and the
foundation is not a complete wrapper release. Do not remove the gate until
the native binaries, safe API and required evidence are complete.

Before product qualification, run real native tests on all supported RIDs,
ClangSharp regeneration/drift checks, zero-allocation tests, AOT/trimming
publish with zero warnings, package/API validation, independent integrity
checks, clean-sample package installation, native-relative benchmarks and
malformed-corpus/fuzz checks. Record actual commands and failed attempts in a
dated docs report. A narrow pass never establishes a full release pass.

Update both public API baselines and XML documentation when changing a public
member. A breaking public behavior/API change needs a major version and a
written note. The first release has no previous package baseline; subsequent
releases must set PackageValidationBaselineVersion to the previous published
release. Do not suppress diagnostics or alter thresholds to pass.

CI and release configuration are primary-owned. The initial workflows validate
source conventions only and have contents:read permissions. They cannot publish
packages, release assets, tags, attestations or binary PRs. An approved release
pipeline must add those separately with explicit approval and least privilege.
