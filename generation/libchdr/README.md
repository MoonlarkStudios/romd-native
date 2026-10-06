# libchdr binding generation boundary

`dotnet run --project eng/Moonlark.Native.Engineering -c Release -- generate`
drives the ClangSharpPInvokeGenerator version pinned in `.config/dotnet-tools.json`
(run `dotnet tool restore` once) over `input.h`, the pinned `chd.h` and
`coretypes.h` and the build-info shim. The configuration is typed C# in
`eng/Moonlark.Native.Engineering/Generation/GenerationConfiguration.cs`; there is
no response file. It remaps the fixed-width integer types so that the output does
not depend on the generating host's ABI spellings.

The raw ClangSharp output is committed unchanged as
`src/Moonlark.Libchdr/Interop/Libchdr.g.cs`: internal blittable `DllImport`
declarations, with runtime marshalling disabled for the assembly.
`NativeBuildContract.g.cs` is generated from the pin and the header's export list.
The generator verifies the source identity before and after the tool runs, writes
only under `artifacts/generation/libchdr` first (including the exact argument list
in `generation-command.json`), and rejects output whose functions differ from the
header. Reflection tests check the compiled interop boundary.

`generate --check` compares regenerated output with the committed files and fails
on drift; CI runs it. `upstream update --commit <sha>` moves the pin to a reviewed
commit already fetched into the submodule and regenerates the pin, props, export
allowlist, bindings and contract together, restoring all of them if any step
fails. Generation has been verified on macOS ARM64 only.
