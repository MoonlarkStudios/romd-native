# Windows qualification

`win-x64` remains supported. Windows execution is manual: dispatch
`.github/workflows/native-libchdr.yml` at the reviewed source commit. Its
Windows job is skipped on every push and pull request. A release must have
accepted Windows evidence for its actual source and native asset before
including that asset; a Linux pass cannot substitute for it.

The job uses the standard `windows-2022` hosted runner, the pinned .NET SDK
and SHA-pinned checkout, setup and artifact actions. It downloads synthetic
fixtures produced in the same run. The runner image rolls; `runner.txt`
records its reported image version and architecture. This is an observation,
not a pinned image or a Windows qualification result by itself.

Within one `cmd` step, `vswhere` selects a complete Visual Studio 2022
installation and `VsDevCmd.bat` selects x64 host and target tools. The step
restores and builds the solution, restores the pinned generator, checks
binding drift, then invokes:

```text
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build --no-restore -- qualification windows
```

The command accepts no options and requires a native Windows x64 process,
clean source, verified synthetic fixtures and already built managed test
assemblies. It builds and verifies two native outputs, compares actual bytes
and build recipes, runs the libchdr suite twice and the engineering suite,
and checks the repository. It records managed assembly hashes before and
after execution. Failed runs invalidate the current success receipt and
retain their logs.

The native recipe records selected Visual Studio, MSVC and Windows SDK
versions, actual tool hashes and normalized header/library search paths.
Absolute tool locations remain diagnostic manifest data. The C ABI probe
measures layout and enum signedness under the actual compiler; managed tests
also exercise native enum arguments and returns. Generated bindings must
come from the pinned generator and headers.

Both evidence and generated binding artifacts upload even on failure and
expire after seven days. Retain reviewed artifacts with the dated gate
report. Windows receives no signing permissions; Linux provenance jobs
remain separate. Native manifests and evidence remain `local-unqualified`.
This command does not establish NativeAOT, full trimming, benchmarks, fuzzing,
clean package installation or real-disc compatibility. Those checks and the
whole-gate review remain required before product qualification.
