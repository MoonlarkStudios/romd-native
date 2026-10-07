# Samples

`Moonlark.Libchdr.QuickStart` opens a CHD, reads its first hunk and verifies its checksums. Its namespace import
and its marked quick-start lines are, verbatim, the quick start in `src/Moonlark.Libchdr/README.md`; a test keeps
the two identical. The sample builds with the solution, so the documented code always compiles.

Until the native package ships there is no packaged native asset, so pass the path of a verified native build,
such as the one under `artifacts/native/libchdr/<rid>/native/`. Without it the sample fails with
`DllNotFoundException`.

```sh
dotnet run --project samples/Moonlark.Libchdr.QuickStart -c Release -- <file.chd> <native-library-path>
```

Before a release, publish the sample with NativeAOT and with full trimming. These are manual pre-release checks:
CI only restores, builds, tests and runs the repository checks, so it never publishes. The sample roots the whole
library assembly, so the publishes analyze every public API rather than only the calls the sample makes. Both
publishes must report zero warnings, and each published program must read and verify a CHD.

```sh
dotnet publish samples/Moonlark.Libchdr.QuickStart -c Release -r <rid> -p:PublishAot=true
dotnet publish samples/Moonlark.Libchdr.QuickStart -c Release -r <rid> -p:PublishTrimmed=true -p:TrimMode=full --self-contained
```

A RID-specific publish adds RID-specific entries to the lock files, so run it in a disposable checkout. Clean-project
package install tests follow once the packages are qualified; until then the sample references the library project.
