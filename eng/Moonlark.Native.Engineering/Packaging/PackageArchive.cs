using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;

namespace Moonlark.Native.Engineering.Packaging;

internal sealed record PackageFile(string SourcePath, string PackagePath, long Bytes, string Sha256);
internal sealed record PackagePlan(string Id, string Version, bool Native, IReadOnlyList<PackageFile> Files);

/// <summary>Checks actual package bytes and the closed NuGet contract before a local candidate receipt is issued.</summary>
internal static class PackageArchive
{
    private const string CorePrefix = "package/services/metadata/core-properties/";

    internal static Failure? Verify(string root, string path, PackagePlan expected)
    {
        try
        {
            LibchdrAuthority authority = Authorities.ReadLibchdr(root).Value;
            Require(expected.Id == (expected.Native ? "Moonlark.Libchdr.Native" : "Moonlark.Libchdr") && expected.Version == authority.ManagedVersion,
                "Package identity differs from the current family authority");
            using ZipArchive zip = ZipFile.OpenRead(path);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                Require(SafeEntry(entry.FullName) && entries.TryAdd(entry.FullName, entry), "Unsafe or duplicate package path");
                int mode = entry.ExternalAttributes >>> 16;
                Require((mode & 0xf000) is 0 or 0x8000, "Package entries must be regular files");
            }
            string[] core = entries.Keys.Where(name => name.StartsWith(CorePrefix, StringComparison.Ordinal) && name.EndsWith(".psmdcp", StringComparison.Ordinal)).ToArray();
            Require(core.Length == 1 && !core[0][CorePrefix.Length..].Contains('/', StringComparison.Ordinal), "Package must have one core-properties part");
            string nuspec = expected.Id + ".nuspec";
            string[] metadata = [nuspec, "_rels/.rels", "[Content_Types].xml", core[0]];
            string[] wanted = [.. expected.Files.Select(file => file.PackagePath), .. metadata];
            Require(wanted.Distinct(StringComparer.OrdinalIgnoreCase).Count() == wanted.Length
                && entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(wanted), "Unexpected or missing package entries");
            foreach (PackageFile file in expected.Files)
            {
                Require(PackagePaths.RegularFile(file.SourcePath, root) is null, "Package input is not a regular unredirected file");
                byte[] bytes = Read(entries[file.PackagePath], file.Bytes);
                Require(bytes.LongLength == file.Bytes && Digest.Sha256(bytes) == file.Sha256
                    && Digest.Sha256File(file.SourcePath) == file.Sha256, "Packaged bytes differ from the staged input: " + file.PackagePath);
            }
            VerifyPayload(root, expected, entries);
            VerifyNuspec(Xml(entries[nuspec]), expected);
            XDocument relationships = Xml(entries["_rels/.rels"]);
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            Require(relationships.Root?.Name == rel + "Relationships", "Invalid package relationship root");
            XElement[] links = relationships.Root!.Elements().ToArray();
            Require(links.Length == 2 && links.All(link => link.Name == rel + "Relationship" && (string?)link.Attribute("TargetMode") is null),
                "Unexpected or external package relationship");
            Require(links.Count(link => (string?)link.Attribute("Type") == "http://schemas.microsoft.com/packaging/2010/07/manifest"
                    && TrimTarget((string?)link.Attribute("Target")) == nuspec) == 1
                && links.Count(link => (string?)link.Attribute("Type") == "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"
                    && TrimTarget((string?)link.Attribute("Target")) == core[0]) == 1, "Package relationships do not bind its actual parts");
            Require(Xml(entries["[Content_Types].xml"]).Root?.Name == XName.Get("Types", "http://schemas.openxmlformats.org/package/2006/content-types"), "Invalid content-types part");
            Require(Xml(entries[core[0]]).Root?.Name == XName.Get("coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"), "Invalid core-properties part");
            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or XmlException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new Failure("Package verification failed: " + error.Message);
        }
    }

    private static void VerifyPayload(string root, PackagePlan expected, Dictionary<string, ZipArchiveEntry> entries)
    {
        HashSet<string> names = expected.Files.Select(file => file.PackagePath).ToHashSet(StringComparer.Ordinal);
        if (!expected.Native)
        {
            Require(names.SetEquals(["README.md", "LICENSE.txt", "lib/net10.0/Moonlark.Libchdr.dll", "lib/net10.0/Moonlark.Libchdr.xml"]), "Managed package payload changed");
            return;
        }
        HashSet<string> allowed = ["README.md", "LICENSE.txt", "THIRD_PARTY_NOTICES.md", "licenses/source-inventory.json"];
        string[] rids = names.Where(name => name.StartsWith("runtimes/", StringComparison.Ordinal)).Select(name => name.Split('/')[1]).Distinct(StringComparer.Ordinal).ToArray();
        Require(rids.Length > 0 && rids.All(NativeRids.IsSupported), "Native package must declare supported RIDs");
        foreach (string rid in rids)
        {
            string binaryName = "runtimes/" + rid + "/native/" + NativeRids.LibraryFileName(rid);
            string manifestName = "runtimes/" + rid + "/native/moonlark_chdr." + rid + ".build-manifest.json";
            Require(names.Contains(binaryName) && names.Contains(manifestName), "Native RID requires its binary and uniquely named manifest");
            allowed.Add(binaryName); allowed.Add(manifestName);
            JsonObject manifest = JsonNode.Parse(Read(entries[manifestName], 2_000_000))?.AsObject() ?? throw new InvalidDataException("Missing native manifest");
            Require((string?)manifest["rid"] == rid, "Packaged manifest RID differs from its path");
            PackageFile binary = expected.Files.Single(file => file.PackagePath == binaryName);
            if (NativeVerify.VerifyManifest(manifest, binary.SourcePath, root) is { } invalid) throw new InvalidDataException(invalid.Message);
        }
        Require(names.SetEquals(allowed), "Native package is not the declared asset-only inventory");
    }

    private static void VerifyNuspec(XDocument document, PackagePlan expected)
    {
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        Require(ns.NamespaceName is "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd"
            or "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd", "Unsupported nuspec namespace");
        Require(document.Root?.Name == ns + "package" && document.Root.Elements().Count() == 1, "Unexpected nuspec root");
        XElement metadata = document.Root!.Element(ns + "metadata") ?? throw new InvalidDataException("Missing nuspec metadata");
        string[] allowedMetadata = ["id", "version", "authors", "description", "license", "licenseUrl", "readme", "dependencies", "repository", "requireLicenseAcceptance"];
        Require(metadata.Elements().All(item => item.Name.Namespace == ns && allowedMetadata.Contains(item.Name.LocalName, StringComparer.Ordinal))
            && metadata.Elements().GroupBy(item => item.Name).All(group => group.Count() == 1), "Unknown or duplicate nuspec metadata");
        string One(string name)
        {
            XElement[] values = metadata.Elements(ns + name).ToArray();
            Require(values.Length == 1, "Missing or duplicate nuspec " + name);
            return values[0].Value;
        }
        Require(One("id") == expected.Id && One("version") == expected.Version && One("authors") == "Moonlark Studios", "Nuspec family identity mismatch");
        Require(One("readme") == "README.md" && One("license") == "LICENSE.txt"
            && (string?)metadata.Element(ns + "license")!.Attribute("type") == "file", "Nuspec README/license metadata mismatch");
        XElement[] dependencies = metadata.Elements(ns + "dependencies").ToArray();
        if (expected.Native)
        {
            Require(dependencies.Length == 0, "Native package must have no dependency groups");
            return;
        }
        Require(dependencies.Length == 1, "Managed package requires exactly one dependency group");
        XElement[] groups = dependencies[0].Elements().ToArray();
        Require(groups.Length == 1 && groups[0].Name == ns + "group"
            && (string?)groups[0].Attribute("targetFramework") is "net10.0" or ".NETCoreApp10.0", "Managed dependency target framework mismatch");
        XElement[] items = groups[0].Elements().ToArray();
        Require(items.Length == 1 && items[0].Name == ns + "dependency"
            && items[0].Attributes().All(attribute => attribute.Name.Namespace == XNamespace.None && attribute.Name.LocalName is "id" or "version" or "exclude")
            && (string?)items[0].Attribute("id") == "Moonlark.Libchdr.Native"
            && (string?)items[0].Attribute("version") == "[" + expected.Version + "]"
            && (string?)items[0].Attribute("include") is null && (string?)items[0].Attribute("exclude") is null or "Build,Analyzers",
            "Managed package must have only the exact native family dependency with runtime assets available");
    }

    private static XDocument Xml(ZipArchiveEntry entry)
    {
        using var input = new MemoryStream(Read(entry, 2_000_000));
        using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static byte[] Read(ZipArchiveEntry entry, long maximum)
    {
        Require(entry.Length <= maximum, "Package entry exceeds its expected size");
        using Stream input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static string? TrimTarget(string? path) => path?.TrimStart('/');
    private static bool SafeEntry(string path) => path.Length > 0 && !path.Contains('\\', StringComparison.Ordinal) && !path.Contains(':', StringComparison.Ordinal)
        && path.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
