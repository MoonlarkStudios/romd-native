# C# engineering migration evidence — 2026-10-05

All engineering tooling moved from Python to the C# project
`eng/Moonlark.Native.Engineering`, tested by `tests/Moonlark.Native.Engineering.Tests`.
No Python remains. This is a tooling migration on local branch
`refactor/csharp-engineering`. It is not G2 acceptance, native qualification or
publication. Nothing was pushed, tagged, packaged or published, and no tool was
installed: the pinned ClangSharpPInvokeGenerator 21.1.8.4 was already restored
locally.

## Owner decisions

- Model the tooling on Silk.NET:
  - binding generation from Silk.NET 3.0;
  - C# build orchestration from Silk.NET 2.x;
  - none of Silk.NET's trust model.
- Commit raw ClangSharp `DllImport` output, with runtime marshalling disabled. This
  replaces the `LibraryImport` rewrite recorded in
  `2026-10-05-libchdr-preparation.md`. Reflection tests on the compiled assembly
  now carry the structural checks.
- Port the workflow lint to xunit.
- Leave zig and the native CI matrix (A2/A5) as the next gate.
- Make bindings regenerate whenever the pin moves.

## What changed

| Python (lines incl. tests) | C# replacement |
| --- | --- |
| `eng/check.py` (296) | `repo check [--tag]`. The closed workflow grammar allowlists only dotnet commands, requires the drift check in CI and, after review, also closes earlier bypasses (below). |
| `eng/build_libchdr.py`, `verify_libchdr.py`, `run_native_layout.py`, `test_libchdr.py` (959) | `native build`, `native verify`. Build settings live only in `CMakeLists.txt` and `CMakePresets.json`. CMake generates the platform export lists from `exports.txt`. The recipe (schema 2) records the location-independent effective configuration from the CMake File API instead of a duplicated flag table. The layout probe is a CMake target. |
| `generation/libchdr/generate.py`, `test_generate.py`, `generate.rsp` (491) | `generate [--check]`: typed arguments to the pinned CLI tool, with fixed-width integer remaps, committing raw output. `upstream update --commit SHA` rewrites the pin, props, export allowlist, bindings and contract together, with rollback. |
| `native/chdman/build.py`, `test_build.py` (264) | `chdman build`, with stricter validate-all-before-extract tar handling through `System.Formats.Tar`. |
| `tests/fixtures/generate.py`, `test_generate.py` (346) | `fixtures generate`. |

Shared checks live in `Core/`: source identity, the environment guard, artifacts
containment, authorities and the header-derived export inventory. The committed
`exports.map`, `exports.osx` and `exports.def` were removed. The export allowlist
is checked as the exact header declaration sequence, with no fixed count.
`AssemblyContractTests` reads its expected upstream identity from the pin.
`InteropLayoutTests.CallbackSignaturesUseCdeclAndNativeWidths`, which already
failed at the G2 baseline, now reads calling conventions from modified field types.

## Validation (macOS ARM64, SDK 10.0.302, `GIT_EDITOR` unset)

| Check | Actual result |
| --- | --- |
| Baseline, before any change | `check.py` PASS; Python unittest 38 + 17 + 6 + 7 OK; build 0 warnings; tests 200/201 (pre-existing callback test failure) |
| `dotnet restore --locked-mode`, `build -warnaserror` | PASS, 0 warnings |
| `dotnet test` | PASS: Moonlark.Libchdr.Tests 204/204, Moonlark.Native.Engineering.Tests 319/319 (418/418 after the review fixes and coverage below) |
| `repo check`; with tags `libchdr-v1.0.0-preview.1`, `chdman-0.289-r1` | PASS |
| `repo check --tag libchdr-v0.3.0` | Expected FAIL |
| `generate --check`, real pinned tool | PASS, 19 imports. Raw output is byte-identical to the Python generator's raw output. The tracked binding diff is the import mechanism only. |
| `native build`, then `native verify` | PASS. Dylib SHA-256 `0fbca879a40ef27a3b8e8e78c2043cb05ca96a06cf5991433f7ffdf26d91f56a` before the review fixes, and `cc151e226efe94a1fb8a3d54ba731c9960fa42717c30d9c4ac6d18d0faa45627` after them. Recipe inputs include the engineering sources, so the build ID changed. |
| Same build in another output directory and in another checkout | Byte-identical |
| `upstream update --commit` (current pin) | PASS; no tracked file changed |
| `chdman build --jobs 4`, 25-character output path | Binary `e47a873059df60e1e706c1189e89968427ed8a36750d3f665c45a598a477cda1`, identical to the final Python-recipe builds |
| `fixtures generate` with that tool | All 46 data files identical to `libchdr-final`. The manifest differs only in the tool receipt's `arguments` and `recipeSha256`. |
| `dotnet pack` | Expected FAIL: both packages still refuse unqualified packing |

Negative probes failed closed with their intended messages:
- dirty or ignored source files;
- a wrong header digest;
- tampered `input.h`;
- symlinked artifacts outputs or children;
- a tampered manifest digest;
- false qualification or attestation claims;
- a flipped embedded build ID, both re-signed and unsigned;
- cross-compilation;
- a missing upstream commit;
- a failing generator, after which `upstream update` rolled back.

Raw logs are kept in ROMD's ignored `tmp/`, prefix `romd-native-cs-`.

## Independent review and fixes (2026-10-06)

A read-only reviewer compared every check with the Python baseline. Each finding
was fixed with a regression test, and each fix was mutation-checked: removing it
turns its tests red.

- **Blocker, a regression.** The workflow lint split lines on LF only. CR, NEL,
  LS and PS could hide `permissions: write-all`, unpinned actions or `run` steps,
  where Python had refused them. Workflows must now be LF-terminated printable
  ASCII before any check runs.
- **Major, already present in the Python baseline.** The lint did not bind
  `run` and `uses` values completely. Flow sequences, colon-only lines that YAML
  folds into a `run` value, and unchecked `on`, `runs-on` and checkout values all
  passed. Every key now has an exact value rule, flow sequences are limited to
  the two reviewed trigger lines, and continuation lines are refused.
- **Minor: the drift check could be gated to tags.** `if:` is now allowed only on
  the tag-check step.
- **Minor: tool resolution.** Bare tool names resolved from the application and
  current directory before PATH. They now resolve only from absolute entries of
  the explicit PATH.
- **Minor: default logs.** A FIFO or other non-regular default log passed the
  guard. Logs must now be regular files.
- **Minor: case-variant paths.** Artifacts containment compared case-insensitively
  on macOS; components are now compared ordinally.
- **Minor: the location guard.** It rejected only paths it knew about. Any rooted
  host path token in a recipe now fails.
- **Allocation-test flake.** The reviewer saw
  `ChdDataSourceTests.FileReadsAllocateNothingAfterWarmup` fail once in a
  full-solution run. It was not reproduced in 18 runs, 12 of them under full CPU
  contention. The test now requires one allocation-free steady-state window out
  of five; this hardening is unverified against the original failure.
- **Orchestration coverage.** Previously, deleting a check in
  `FixtureGenerator.Run`, `NativeVerify.Run` or `BinaryInspection.Inspect` failed
  no test.
  - 66 tests now drive these paths with fake tools on an explicit PATH and a
    fake chdman, covering every RID's inspector.
  - 38 of 40 mutations were killed. One was an equivalent mutant; the other was
    a harness quoting error that was fixed and re-run, which killed the mutation.
  - The only production change is an injectable inspection host whose default
    is the real host and loader.

Final state after the fixes:
- build: 0 warnings;
- tests: 204 + 418;
- `repo check`, `generate --check`, and `native build`/`verify` in two
  directories: byte-identical;
- `upstream update` at the current pin: no-op;
- fixtures: data-identical.

## Failed attempts retained

- Analyzer errors CA1859, CA1861 and CA1825, missing test XML docs (CS1591), and
  CS0037, all corrected without suppressions.
- xunit compared `ImmutableArray` values by reference; the tests now compare
  sequences.
- The agent shell exports `GIT_EDITOR`, which the environment guard correctly
  rejects. Commands ran with it unset.
- `nm` parsing treated a trailing newline as an empty line.
- `TarEntry.LinkName` throws for device and FIFO test entries.
- A tampered Mach-O build ID got the process SIGKILLed while build-info was being
  read in-process. `codesign --verify --strict` now runs before the load.
- A chdman rebuild from a longer worktree path produced a different LC_UUID. A
  same-length path reproduced the digest, so chdman digest comparisons need a
  fixed path length.

## Not verified

- Linux and Windows have not run any command. Their inspectors are tested only
  with canned tool output.
- Linux generation is expected, but unproven, to match macOS once the stdint
  remaps apply.
- Windows enum backing (`chd_error : uint` vs `int`) is still open, and MSVC
  builds produce no layout-probe receipt.
- The CI workflows, including the new macOS `generate --check` job (submodule with
  full history for `git describe`), have not run. On a runner without native
  artifacts, `dotnet test` fails by design, because native tests never skip.
- AOT/trimming, packaging and native qualification are unchanged and still
  outstanding.
