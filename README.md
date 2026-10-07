# Moonlark Native

Safe .NET wrappers and pinned native tools for emulation media. Each library
has its own wrapper family; consumers depend on that family alone. The first
family is `Moonlark.Libchdr`, over upstream libchdr. Pinned chdman is a tool
distribution, with no NuGet package. No product depends on ROMD.

Status: foundation in development. There is no usable package or published
safe API yet. Packing is explicitly blocked until native/API qualification.
The library targets the in-support .NET LTS, currently net10.0. Supported
native targets will be linux-x64, linux-arm64, osx-arm64 and win-x64. musl and
other RIDs are unsupported. A glibc baseline must be measured before release.

```sh
dotnet restore Moonlark.Native.slnx --locked-mode
dotnet build Moonlark.Native.slnx -c Release --no-restore -warnaserror
dotnet test Moonlark.Native.slnx -c Release --no-build --no-restore
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- repo check
dotnet tool restore
dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- generate --check
```

The full libchdr suite requires verified native outputs and generated synthetic
fixtures. The [Linux qualification workflow](docs/linux-qualification.md) supplies
both on fresh CI runners; a plain source checkout cannot pass that suite.

All engineering tooling is C# in `eng/Moonlark.Native.Engineering`, tested by
`tests/Moonlark.Native.Engineering.Tests`; there are no Python scripts.
`generate --check` fails when the committed bindings differ from the pinned
headers. To move the libchdr pin, fetch the reviewed commit into the submodule,
then run `upstream update --commit <sha>`: it rewrites the pin, props, export
allowlist and bindings together, or restores all of them on failure.

SourceLink is provided by the .NET SDK. Libraries use deterministic builds,
embedded symbols, XML documentation errors, public API baselines and NuGet
package validation. See [versioning](docs/versioning.md),
[supply-chain policy](docs/supply-chain.md), [contributing](CONTRIBUTING.md)
and [third-party notices](THIRD_PARTY_NOTICES.md).

## Threat model

libchdr parses inputs in native code. A safe managed API can own handles,
validate managed bounds and keep exceptions from crossing callbacks; it cannot
make the native parser memory-safe. Inspect untrusted files in a separate,
resource-limited process before host reads. Treat integrity verification and
emulator compatibility as separate results. No game data belongs here.
