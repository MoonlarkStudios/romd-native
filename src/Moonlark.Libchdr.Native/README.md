# Moonlark.Libchdr.Native

Pinned native libchdr assets for the matching `Moonlark.Libchdr` package family.
This package contains no managed API. Reference `Moonlark.Libchdr`; its exact
native dependency supplies the library for linux-x64, linux-arm64, osx-arm64
and win-x64. Windows qualification runs manually or before release.

## Quick start

With the matching managed package installed, this complete top-level program
opens the CHD path supplied as its argument:

```csharp
using Moonlark.Libchdr;

using ChdFile chd = ChdFile.Open(args[0]);
byte[] hunk = new byte[chd.Header.HunkBytes];
chd.ReadHunk(0, hunk);
bool verified = ChdIntegrity.Verify(chd).IsVerified;
```

Default loading checks the native build identity, ABI and required exports.
The package includes a separate build manifest for each supplied RID and full
component license notices. Local test candidates can contain a declared subset
of RIDs; their receipts explicitly remain unqualified and are not releases.

libchdr parses inputs in native code. Inspect untrusted files in a separate,
resource-limited process before opening them in an application. Successful
integrity verification does not establish emulator compatibility.

The combined native artifact retains the libchdr and codec grants in
`LICENSE.txt`. `licenses/source-inventory.json` records the exact source spans,
hashes and selected license alternatives. The repository's MIT license does
not replace those component notices.
