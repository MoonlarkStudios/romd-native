using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;
using Moonlark.Native.Engineering.Packaging;
using Moonlark.Native.Engineering.Tests.Native;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Packaging;

/// <summary>Actual ZIP/nuspec mutations exercise candidate verification without packing, restoring or loading synthetic code.</summary>
public sealed class PackageArchiveTests
{
    /// <summary>Exact managed and native package shapes are accepted.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactPackagesPass(bool native)
    {
        using var fixture = new ArchiveFixture(native);
        Assert.Null(PackageArchive.Verify(fixture.Root.Path, fixture.Write(), fixture.Plan));
    }

    /// <summary>The pinned SDK emits the 2011 native nuspec schema while dependency-bearing packages use 2013.</summary>
    [Fact]
    public void PinnedSdkNativeNuspecPasses()
    {
        using var fixture = new ArchiveFixture(true);
        fixture.Replace(fixture.Plan.Id + ".nuspec", "2013/05", "2011/10");
        Assert.Null(PackageArchive.Verify(fixture.Root.Path, fixture.Write(), fixture.Plan));
    }

    /// <summary>Missing, additional, unsafe, mismatched and relabeled bytes or metadata cannot produce a candidate.</summary>
    [Theory]
    [InlineData("unknown-namespace")]
    [InlineData("foreign-metadata")]
    [InlineData("missing-readme")]
    [InlineData("missing-license")]
    [InlineData("missing-inventory")]
    [InlineData("native-managed-dll")]
    [InlineData("unknown-entry")]
    [InlineData("traversal")]
    [InlineData("backslash")]
    [InlineData("duplicate")]
    [InlineData("case-duplicate")]
    [InlineData("symlink")]
    [InlineData("wrong-rid-name")]
    [InlineData("wrong-binary-bytes")]
    [InlineData("missing-manifest")]
    [InlineData("rehashed-family")]
    [InlineData("rehashed-recipe")]
    [InlineData("rehashed-qualification")]
    [InlineData("wrong-id")]
    [InlineData("wrong-version")]
    [InlineData("wrong-license-metadata")]
    [InlineData("wrong-readme-metadata")]
    [InlineData("native-dependency")]
    [InlineData("missing-native-dependency")]
    [InlineData("loose-native-dependency")]
    [InlineData("extra-dependency")]
    [InlineData("wrong-framework")]
    [InlineData("runtime-excluded")]
    [InlineData("external-relationship")]
    [InlineData("extra-core-properties")]
    [InlineData("unknown-metadata")]
    [InlineData("duplicate-authors")]
    [InlineData("unknown-dependency-attribute")]
    public void CorruptPackagesFail(string change)
    {
        bool managed = change is "missing-native-dependency" or "loose-native-dependency" or "extra-dependency" or "wrong-framework" or "runtime-excluded" or "unknown-dependency-attribute";
        using var fixture = new ArchiveFixture(!managed);
        string nuspec = fixture.Plan.Id + ".nuspec";
        switch (change)
        {
            case "unknown-namespace": fixture.Replace(nuspec, "2013/05", "2026/10"); break;
            case "foreign-metadata": fixture.Replace(nuspec, "<metadata>", "<metadata xmlns=\"urn:other\">"); break;
            case "missing-readme": fixture.Entries.Remove("README.md"); break;
            case "missing-license": fixture.Entries.Remove("LICENSE.txt"); break;
            case "missing-inventory": fixture.Entries.Remove("licenses/source-inventory.json"); break;
            case "native-managed-dll": fixture.Put("lib/net10.0/unexpected.dll", "managed"); break;
            case "unknown-entry": fixture.Put("other.txt", "other"); break;
            case "traversal": fixture.Put("../outside.txt", "outside"); break;
            case "backslash": fixture.Put("runtimes\\linux-x64\\native\\other.so", "other"); break;
            case "duplicate": fixture.Duplicate = "README.md"; break;
            case "case-duplicate": fixture.Put("readme.md", "duplicate"); break;
            case "symlink": fixture.Link = "README.md"; break;
            case "wrong-rid-name": fixture.Put("runtimes/linux-musl-x64/native/libmoonlark_chdr.so", "native"); break;
            case "wrong-binary-bytes": fixture.Put(ArchiveFixture.BinaryEntry, "different"); break;
            case "missing-manifest": fixture.Entries.Remove(ArchiveFixture.ManifestEntry); break;
            case "rehashed-family": fixture.ChangeManifest(manifest => manifest["nativeVersion"] = "9.0.0"); break;
            case "rehashed-recipe": fixture.ChangeManifest(manifest => manifest["recipe"]!["inputSha256"]![BuildRecipe.NativeInputs[0]] = new string('0', 64)); break;
            case "rehashed-qualification": fixture.ChangeManifest(manifest => manifest["qualification"] = "release-qualified"); break;
            case "wrong-id": fixture.Replace(nuspec, fixture.Plan.Id, "Another.Product"); break;
            case "wrong-version": fixture.Replace(nuspec, fixture.Plan.Version, "9.0.0"); break;
            case "wrong-license-metadata": fixture.Replace(nuspec, "type=\"file\">LICENSE.txt", "type=\"expression\">MIT"); break;
            case "wrong-readme-metadata": fixture.Replace(nuspec, "<readme>README.md</readme>", "<readme>other.md</readme>"); break;
            case "native-dependency": fixture.Replace(nuspec, "</metadata>", "<dependencies><dependency id=\"Other\" version=\"1.0.0\" /></dependencies></metadata>"); break;
            case "missing-native-dependency": fixture.Replace(nuspec, fixture.Dependency, ""); break;
            case "loose-native-dependency": fixture.Replace(nuspec, "[" + fixture.Plan.Version + "]", fixture.Plan.Version); break;
            case "extra-dependency": fixture.Replace(nuspec, fixture.Dependency, fixture.Dependency + "<dependency id=\"Other\" version=\"1.0.0\" />"); break;
            case "wrong-framework": fixture.Replace(nuspec, "targetFramework=\"net10.0\"", "targetFramework=\"net9.0\""); break;
            case "runtime-excluded": fixture.Replace(nuspec, "exclude=\"Build,Analyzers\"", "exclude=\"Runtime,Build,Analyzers\""); break;
            case "external-relationship": fixture.Replace("_rels/.rels", "Target=\"" + nuspec + "\"", "Target=\"https://example.invalid/other.nuspec\" TargetMode=\"External\""); break;
            case "unknown-metadata": fixture.Replace(nuspec, "</metadata>", "<developmentDependency>true</developmentDependency></metadata>"); break;
            case "duplicate-authors": fixture.Replace(nuspec, "</metadata>", "<authors>Other</authors></metadata>"); break;
            case "unknown-dependency-attribute": fixture.Replace(nuspec, "exclude=", "unknown=\"other\" exclude="); break;
            case "extra-core-properties": fixture.Put("package/services/metadata/core-properties/extra.psmdcp", "<other/>"); break;
            default: throw new InvalidOperationException(change);
        }
        Assert.NotNull(PackageArchive.Verify(fixture.Root.Path, fixture.Write(), fixture.Plan));
    }

    private sealed class ArchiveFixture : IDisposable
    {
        internal NativeRoot Root { get; } = new();
        internal Dictionary<string, byte[]> Entries { get; } = new(StringComparer.Ordinal);
        internal PackagePlan Plan { get; private set; }
        internal const string BinaryEntry = "runtimes/linux-x64/native/libmoonlark_chdr.so";
        internal const string ManifestEntry = "runtimes/linux-x64/native/moonlark_chdr.linux-x64.build-manifest.json";
        internal string? Duplicate { get; set; }
        internal string? Link { get; set; }
        internal string Dependency => "<dependency id=\"Moonlark.Libchdr.Native\" version=\"[" + Plan.Version + "]\" exclude=\"Build,Analyzers\" />";

        internal ArchiveFixture(bool native)
        {
            string id = native ? "Moonlark.Libchdr.Native" : "Moonlark.Libchdr";
            var files = new List<PackageFile>();
            Plan = new(id, Root.Authority.ManagedVersion, native, files);
            Add("README.md", "readme"u8.ToArray());
            Add("LICENSE.txt", "license"u8.ToArray());
            if (native)
            {
                (string binary, JsonObject manifest) = Root.Manifest();
                Add(BinaryEntry, File.ReadAllBytes(binary), binary);
                Add(ManifestEntry, JsonFields.Serialize(manifest, sortKeys: true));
                Add("licenses/source-inventory.json", "{}"u8.ToArray());
                Add("THIRD_PARTY_NOTICES.md", "notices"u8.ToArray());
            }
            else
            {
                Add("lib/net10.0/Moonlark.Libchdr.dll", "synthetic assembly; never loaded"u8.ToArray());
                Add("lib/net10.0/Moonlark.Libchdr.xml", "<doc/>"u8.ToArray());
            }
            Put(id + ".nuspec", "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>" + id + "</id><version>" + Plan.Version
                + "</version><authors>Moonlark Studios</authors><description>Local candidate</description><readme>README.md</readme><license type=\"file\">LICENSE.txt</license>"
                + (native ? "" : "<dependencies><group targetFramework=\"net10.0\">" + Dependency + "</group></dependencies>") + "</metadata></package>");
            Put("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Type=\"http://schemas.microsoft.com/packaging/2010/07/manifest\" Target=\"" + id + ".nuspec\" Id=\"R1\" /><Relationship Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"package/services/metadata/core-properties/core.psmdcp\" Id=\"R2\" /></Relationships>");
            Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" /></Types>");
            Put("package/services/metadata/core-properties/core.psmdcp", "<coreProperties xmlns=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\"/>");

            void Add(string packagePath, byte[] bytes, string? source = null)
            {
                source ??= System.IO.Path.Combine(Root.Path, "inputs", packagePath);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(source)!);
                File.WriteAllBytes(source, bytes);
                Entries.Add(packagePath, bytes);
                files.Add(new(source, packagePath, bytes.Length, Digest.Sha256File(source)));
            }
        }

        internal void Put(string name, string text) => Entries[name] = Encoding.UTF8.GetBytes(text);
        internal void Replace(string name, string before, string after) => Put(name, Encoding.UTF8.GetString(Entries[name]).Replace(before, after, StringComparison.Ordinal));
        internal void ChangeManifest(Action<JsonObject> change)
        {
            JsonObject manifest = JsonNode.Parse(Entries[ManifestEntry])!.AsObject();
            change(manifest);
            Entries[ManifestEntry] = JsonFields.Serialize(manifest, sortKeys: true);
            PackageFile previous = Plan.Files.Single(file => file.PackagePath == ManifestEntry);
            File.WriteAllBytes(previous.SourcePath, Entries[ManifestEntry]);
            Plan = Plan with { Files = Plan.Files.Select(file => file == previous ? file with { Bytes = Entries[ManifestEntry].Length, Sha256 = Digest.Sha256File(previous.SourcePath) } : file).ToArray() };
        }
        internal string Write()
        {
            string path = System.IO.Path.Combine(Root.Path, "candidate.nupkg");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach ((string name, byte[] bytes) in Entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                if (name == Link) entry.ExternalAttributes = unchecked((int)0xa1ff0000);
                using Stream stream = entry.Open(); stream.Write(bytes);
            }
            if (Duplicate is not null) { using Stream stream = zip.CreateEntry(Duplicate).Open(); stream.Write(Entries[Duplicate]); }
            return path;
        }
        public void Dispose() => Root.Dispose();
    }
}
