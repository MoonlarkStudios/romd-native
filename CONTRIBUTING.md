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

CI and release configuration are primary-owned. Foundation CI checks managed
engineering tests and binding drift. The separate Linux workflow produces
synthetic fixtures on macOS ARM64, then runs native builds and the complete
libchdr/engineering suites on standard Linux x64 and ARM64 runners. Windows
qualification remains manual or release-gated; it is not in per-change CI.
See [Linux qualification](docs/linux-qualification.md) for commands, evidence
boundaries and builder prerequisites.

All workflows retain contents:read permissions and SHA-pinned actions. The
source workflows use the closed ASCII/LF grammar in `repo check`; the Linux
workflow is pinned by its complete file digest so its runner/RID pairs,
dependencies, commands and temporary artifact transfer are reviewed together.
Changing that pipeline requires updating its digest and mutation tests. These
workflows cannot publish packages, release assets, tags, attestations or binary
PRs. A publishing pipeline requires separate explicit approval and least privilege.
