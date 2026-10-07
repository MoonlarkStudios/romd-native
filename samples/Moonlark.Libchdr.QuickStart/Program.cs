using Moonlark.Libchdr;

// Reads a CHD's first hunk, then verifies its checksums. A second argument names a native libchdr build to load
// instead of the packaged asset; until the native package ships there is no packaged asset, so it is required.
if (args.Length is < 1 or > 2)
{
    System.Console.Error.WriteLine("usage: Moonlark.Libchdr.QuickStart <file.chd> [native-library-path]");
    return 2;
}
if (args.Length == 2) LibchdrLibrary.Load(System.IO.Path.GetFullPath(args[1]));

// quick-start:begin
using ChdFile chd = ChdFile.Open(args[0]);
byte[] hunk = new byte[chd.Header.HunkBytes];
chd.ReadHunk(0, hunk);
bool verified = ChdIntegrity.Verify(chd).IsVerified;
// quick-start:end

ChdHeader header = chd.Header;
System.Console.WriteLine($"CHD v{header.Version}: {header.LogicalBytes} bytes in {header.HunkCount} hunks of {header.HunkBytes}");
System.Console.WriteLine($"libchdr {LibchdrLibrary.BuildInfo.UpstreamVersion}; checksums verified: {verified}");
return verified ? 0 : 1;
