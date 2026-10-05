# Native foundation source review

Date: 2026-10-05 (America/Chicago). G1 is **accepted as the source foundation**.
Independent source and gate-boundary reviews are closed with no remaining
foundation findings. G2's usable API, native binaries, successful packaging and
native-platform qualification remain outstanding. No generator/tool is installed
and no package is released. Acceptance authorizes a local foundation commit;
push and publication remain separately gated.

## What is prepared

Moonlark.Libchdr naming and internal raw namespace; permanent family mode and
1.0.0-preview.1 authority; actual upstream commit/describe identity; .NET10 LTS
SDK pin; deterministic/analyzer/XML/public-API/package-validation settings;
SDK SourceLink and embedded symbols; central locked development dependencies;
asset-only packaging boundary; conservative exact-source/license inventory;
threat model; source-only least-privilege SHA-pinned workflow skeleton.

The scaffold blocks Pack explicitly, preventing empty or unqualified packages.
Those blocks can be replaced only with complete safe API, exact family native
dependency, real native assets and their qualification gates. The current
native packaging project emits no managed assembly into a package. Its build
output is a scaffold artifact, not a native binary or consumer package.

## Actual checks

All commands use the already installed SDK10.0.302, except the first failed
attempt. No tools/SDKs were installed. Logs are preserved in the orchestrator's
local status/evidence workspace; no game data or secrets were used.

| Check | Result and limit |
| --- | --- |
| First `dotnet build Moonlark.Native.slnx -c Release -warnaserror` | FAIL before restore: the system dotnet exposed SDK10.0.401, while the pin requires installed mise SDK10.0.302/latestPatch |
| Same build with exact existing mise SDK path | PASS: zero warnings/errors on macOS arm64; scaffold only |
| `dotnet test Moonlark.Native.slnx -c Release --no-build --no-restore` | PASS: three assembly boundary/metadata tests, zero skips |
| `dotnet pack Moonlark.Native.slnx -c Release --no-restore` | FAIL: both projects explicitly reject unqualified packages; no package generated. This is not packaging acceptance |
| `python3 -B eng/check.py` | PASS: versions/pins/RID/features/source-only workflow convention checks |
| `python3 -B -m unittest discover -s eng -p 'test_*.py'` | PASS: final nine regression tests; initial five/seven-test passes retained |
| Independent compiler probes | Expected FAIL: missing XML produces CS1591; documented but undeclared public type/ctor produces RS0016. Diagnostic enforcement verified |
| Official action tag refs | PASS: all three workflow SHA pins independently match official GitHub commits; initial restricted CLI/web reads failed before scoped read-only fallback |
| SourceLink before the first product commit | UNVERIFIED at review time: the unborn repository had no revision/source map. Recheck after the accepted local commit |
| Source-identical disposable Git snapshot | PASS: 40 source files matched current SHA256, warning-free locked CI build, SemVer plus snapshot commit, exact SDK source mapping and embedded portable-PDB mapping independently verified |
| First SourceLink snapshot assertion | FAIL: harness expected a github.com /raw/ URL; SDK correctly emitted raw.githubusercontent.com. Corrected assertion requires exact canonical host/repository/commit URL; original warning-free build log/tool traceback retained |
| Native RID/AOT/trimming/benchmarks/fuzz/clean install/package compatibility | NOT RUN; no native/usable package exists |

Final snapshot commit was `053ec053b14b9133d64d8b1ee89a80d1b77956a9` in a
disposable Git repository. It is not a product commit or a remotely reachable
source URL. The snapshot proves SDK mapping mechanics only. Product-commit
mapping must be checked again after an accepted product commit.

## Resolved independent findings

- Alternative .yaml workflows, flow permission mappings and flow action steps
  bypassed the first scanner. The source skeleton now rejects unsupported YAML
  grammar, inventories both extensions and permits only reviewed commands/actions.
- Actual tags did not trigger version checks. Nonpublishing CI now validates
  libchdr/chdman tags against the real GitHub ref via a controlled environment
  value, including rejection of family-native tags.
- Bracket/format secret expressions bypassed a dot-syntax blacklist. Expressions
  are now restricted to the two exact approved tag environment values.
- Commented routing lines satisfied raw substring checks. The guard now requires
  active canonical trigger/condition/environment/command lines.

Every reported bypass has a regression. Independent final review rejected all
six later bypass probes and passed the nine checks. It found no remaining
source-only findings. No diagnostic was suppressed, test skipped or
qualification threshold raised.

## Acceptance boundary

The first review disposition left G1 unaccepted because Pack failed and native
platforms were unqualified. A second independent audit corrected that boundary:
the owner's G1 scope is repository scaffold, conventions, analyzers, versioning
and a CI skeleton. The originally recorded validation requires foundation
checks and pack routing, expressly excluding native/platform qualification.
Successful native packaging, safe API, assets, AOT, benchmarks and installation
belong to G2. Importing those requirements into G1 prevented the ordered gates
from advancing to the implementation that supplies them.

G1 acceptance therefore covers the passing foundation checks and the verified
refusal to package incomplete work. The `dotnet pack` attempt remains **FAIL**;
it is never reported as packaging acceptance. No target requirement changed,
check was weakened or missing native evidence extrapolated. G2 remains wholly
unqualified.

## Remaining boundaries

ClangSharpPInvokeGenerator21.1.8.4 and pinned chdman source-build installation
need tool approval. CMake/clang/Ninja/SDK already exist. All four native libchdr
RIDs and three chdman RIDs need actual qualification and runner approval.
Source-only CI cannot produce/publish binaries, attestations, automated PRs or
releases; full native/release pipelines must be implemented and independently
reviewed before separate push/publication approval. No first-publication/root/
MIT/prefix reservation approval is inferred from these local conventions.
