# Local package candidates

Candidate packages exercise the package layout and default loader before release
qualification. They are always `local-unqualified`, even when all supported RIDs
are present. Ordinary product `dotnet pack` remains blocked. These commands do
not publish packages, grant signing permission, or accept a release gate.

Start from a clean source commit, the SDK in `global.json`, a normal locked
solution restore/build, verified synthetic fixtures in
`artifacts/fixtures/libchdr-final`, and verified native manifests in
`artifacts/native/libchdr/<rid>/build-manifest.json`:

```sh
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- \
  package candidate --rids osx-arm64 --output artifacts/packages/candidate-1
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- \
  package consumer --candidate artifacts/packages/candidate-1/candidate.json \
  --output /private/tmp/moonlark-consumer-1
```

Use a new output directory for every attempt. The candidate output must be under
the checkout's ignored `artifacts`; the consumer directory must be new, outside
the checkout, and have no symlink ancestors. On this Mac, use `/private/tmp`
instead of the `/tmp` symlink. The consumer requires a candidate for the current
host RID. A comma-separated `--rids` list may select any subset of `win-x64`,
`linux-x64`, `linux-arm64`, and `osx-arm64`. A partial inventory remains explicit
in the receipt and cannot establish all-platform package qualification.

The candidate command rebuilds the managed library with warnings as errors,
checks pinned source and native recipe identities, and verifies license inputs.
It uses two isolated temporary SDK pack projects. The managed package contains
the actual DLL, XML documentation and README, with an exact family dependency
on the native package. The native package contains RID assets and their uniquely
named manifests, README, complete license texts and source inventory; it has no
managed assembly or package dependency. Both actual ZIP inventories and nuspec
metadata are checked before `candidate.json` is written. Keep the complete
output, including pack projects, logs, packages and managed build, for review.

The consumer creates fresh package and HTTP caches, clears fallback sources,
and restores only from the candidate folder feed. It uses package references
and the documented quick start with default native loading. Framework execution
and a RID-specific framework-dependent publish must prove the exact native
build identity and full synthetic CHD integrity. Separate negative attempts
exercise a missing native package, the wrong family version, missing native
files and an incompatible first native artifact. Failed attempts and their
logs remain available. No trim, NativeAOT, performance or unexecuted-platform
claim follows from a local consumer pass; those need their own evidence.

Package/API validation runs with warnings as errors and no historical baseline
for the first release. Future releases still require the prior published package
baseline described in `CONTRIBUTING.md`. Do not relax diagnostics or substitute
packages after seeing a failed attempt.
