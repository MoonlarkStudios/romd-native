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
ClangSharp regeneration/drift checks, zero-allocation tests, full-trimming
publish with zero warnings, package/API validation, independent integrity
checks, clean-sample package installation, native-relative benchmarks and
malformed-corpus/fuzz checks. Record actual commands and failed attempts in a
dated docs report. A narrow pass never establishes a full release pass.

Normal .NET runtime deployment is acceptable for the initial release. NativeAOT
is optional bounded best-effort work, prioritizing Linux/Docker. Record failed
or unrun AOT qualification as unqualified; it cannot block otherwise qualified
implementation or release. IsAotCompatible alone does not establish executed
NativeAOT support. All other qualification requirements remain unchanged.

Update both public API baselines and XML documentation when changing a public
member. A breaking public behavior/API change needs a major version and a
written note. The first release has no previous package baseline; subsequent
releases must set PackageValidationBaselineVersion to the previous published
release. Do not suppress diagnostics or alter thresholds to pass.

CI and release configuration are primary-owned. Foundation CI checks managed
engineering tests and binding drift. The separate Linux workflow produces
synthetic fixtures on macOS ARM64, then runs native builds and the complete
libchdr/engineering suites on standard Linux x64 and ARM64 runners. Windows
qualification runs only on manual dispatch of that workflow; it is not in
per-change CI. See [Windows qualification](docs/windows-qualification.md).
See [Linux qualification](docs/linux-qualification.md) for commands, evidence
boundaries and builder prerequisites.

All workflows retain contents:read and SHA-pinned actions. Only the approved
Linux attestation job has job-scoped id-token:write and attestations:write,
after both Linux jobs and read-only subject validation succeed on trusted
push/manual runs. That job never checks out or executes repository code.
The source workflows use the closed ASCII/LF grammar in `repo check`; the
Linux workflow is pinned by its complete file digest so runner/RID pairs,
dependencies, commands, artifact transfer and provenance permissions are
reviewed together. Changes require updating its digest and mutation tests.
No workflow can publish packages, release assets, tags or binary PRs. Further
publishing permissions require separate explicit approval and least privilege.
