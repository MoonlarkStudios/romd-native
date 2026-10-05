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
python3 -B eng/check.py
python3 -B -m unittest discover -s eng -p 'test_*.py'
```

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
