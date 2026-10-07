# Linux qualification evidence

The Libchdr Linux evidence workflow runs on pull requests, pushes to main or
refactor/csharp-engineering, and manual dispatch. It uses standard
ubuntu-24.04 and ubuntu-24.04-arm runners. Windows has no per-change job.
This workflow produces evidence for review; it cannot accept a release gate.

## Inputs and execution

`global.json` disables SDK roll-forward. The exact SDK also selects the
ILLink tool package used by the lock files; selecting a newer patch implicitly
breaks locked restore even when the target framework is unchanged.

The macos-15 fixture job builds the pinned MAME chdman from its size/hash-checked
source archive, generates synthetic data, verifies it, and transfers it through
a SHA-pinned same-run artifact action. No game data or locally built native
library is checked in. Consumers verify the complete fixture inventory,
compressor slots, source bytes, file hashes, header/hash relationships and
recorded recovery results. This checks transfer integrity and current pin
agreement; it does not authenticate a manifest independently of its producing
workflow or re-execute its recorded Mac tool.

Each Linux job runs the pinned binding generator on Ubuntu, outside the
baseline builder. The container command exports the complete builtin header
tree from the pinned Clang 21.1.8 image, records its hashes, and passes that
resource directory explicitly to the generator before native qualification. ClangSharp's Linux executable requires glibc 2.34; the
AlmaLinux 8.10 builder intentionally has glibc 2.28. Do not raise the native
library's glibc ceiling to accommodate the generator.

The C# container command downloads checksum-pinned .NET 10.0.302 and CMake
3.31.12 archives and builds the digest-pinned AlmaLinux image with the exact
RPM lists under `eng/qualification/linux`. It uses an isolated clean Git clone,
a matching host/process architecture, and the host's numeric user/group for
writable bind mounts. Compiler outputs never overwrite the host checkout's
managed build. The container performs a locked restore and warnings-as-errors
build before invoking the evidence command.

The evidence command validates source identity and fixture integrity, builds
and verifies libchdr in two independent output directories, requires identical
actual native bytes and recipes, runs the entire libchdr suite twice plus the
engineering suite, and checks repository policy. ABI, export/dependency and
GLIBC <=2.31 checks are part of native verification. Successful test exits must
also have nonempty complete passing TRX results. The evidence command requires
its caller to have built the managed code from the recorded clean checkout.

## Commands

Run from the repository root with the pinned SDK and owner-approved tools and
runners. A verified fixture bundle must already exist for container execution:

```sh
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- fixtures verify --directory artifacts/fixtures/libchdr-final
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- qualification container --rid linux-arm64
```

Use linux-x64 on an actual x64 Linux host. The `qualification fixtures` command
runs only on the Mac tool's supported host and downloads/builds the pinned tool.
The container command deliberately requires a fresh context and clone beneath
`artifacts/qualification/<rid>`; preserve an earlier failed attempt before
starting another. Do not delete evidence to disguise a failure.

## Evidence and limits

The container log and builder receipt record resolved RPMs, image identity,
archive and recipe digests. The nested evidence receipt records actual native
outputs, source and fixture identities, ABI/build/verify logs and test results.
Failed container/evidence runs retain logs and cannot retain a current success receipt. Workflow
artifacts are kept for seven days; copy reviewed receipts into the dated gate
report before they expire.

Both native and evidence manifests remain `local-unqualified` with no
attestation. An emulated local run is diagnostic only. Hosted runner labels and
run metadata must establish actual architecture independently of the guest's
reported architecture. A successful pipeline does not establish benchmarks,
fuzzing, package installation, Windows/macOS qualification or real-disc behavior.
The package gate remains closed until all required evidence and approvals exist.
