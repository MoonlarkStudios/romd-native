using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;

namespace Moonlark.Native.Engineering.Packaging;

internal sealed record CandidateOptions(string OutputDirectory, IReadOnlyList<string> NativeManifestPaths);
internal sealed record NativeCandidate(string Rid, string ManifestPath, string BinaryPath, string ManifestSha256, string BinarySha256, string BuildId);
internal sealed record CandidatePackage(string Path, string Sha256, PackagePlan Plan);
internal sealed record CandidateReceipt(int SchemaVersion, string Qualification, string SourceCommit, string Version, string OutputDirectory,
    IReadOnlyList<string> DeclaredRids, bool CompleteRidInventory, IReadOnlyList<NativeCandidate> Native, IReadOnlyList<CandidatePackage> Packages,
    string LicenseSha256, string LicenseInventorySha256, JsonArray Steps);

/// <summary>Builds local, unqualified package candidates without changing or bypassing ordinary product Pack gates.</summary>
internal static class PackageCandidate
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static Result<JsonObject> Run(string root, CandidateOptions options, IReadOnlyDictionary<string, string> environment, TextWriter? log)
    {
        try
        {
            Result<string> output = PackagePaths.NewArtifactsDirectory(root, options.OutputDirectory);
            if (!output.Succeeded) return output.Failure;
            if (options.NativeManifestPaths.Count == 0) return new Failure("At least one native manifest is required");
            Result<IReadOnlyDictionary<string, string>> clean = BuildEnvironment.Create(environment);
            if (!clean.Succeeded) return clean.Failure;
            Directory.CreateDirectory(output.Value);
            var session = new CandidateSession(root, output.Value, clean.Value, log);
            string commit = session.Source("source-before");
            LibchdrAuthority authority = Authorities.ReadLibchdr(root).Value;
            using JsonDocument global = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
            string sdk = session.Run("sdk", root, "dotnet", "--version");
            if (sdk != global.RootElement.GetProperty("sdk").GetProperty("version").GetString()) throw new InvalidDataException("Candidate SDK differs from global.json");
            Result<long> source = SourceIdentity.Verify(Path.Combine(root, "native/libchdr/upstream"), authority.Pin, clean.Value, log);
            if (!source.Succeeded) return source.Failure;
            if (PackageLicenses.Verify(root) is { } licenseFailure) return licenseFailure;
            NativeCandidate[] native = options.NativeManifestPaths.Select(path => ReadNative(root, path)).OrderBy(item => item.Rid, StringComparer.Ordinal).ToArray();
            if (native.Select(item => item.Rid).Distinct(StringComparer.Ordinal).Count() != native.Length) throw new InvalidDataException("Duplicate candidate RID");
            string managed = Path.Combine(output.Value, "managed-build");
            session.Run("managed-rebuild", root, "dotnet", "build", Path.Combine(root, "src/Moonlark.Libchdr/Moonlark.Libchdr.csproj"),
                "-c", "Release", "--no-restore", "--no-dependencies", "--output", managed, "-t:Rebuild", "-warnaserror");
            string dll = Path.Combine(managed, "Moonlark.Libchdr.dll");
            string xml = Path.Combine(managed, "Moonlark.Libchdr.xml");
            if (PackagePaths.RegularFile(dll, root) is { } dllFailure) return dllFailure;
            if (PackagePaths.RegularFile(xml, root) is { } xmlFailure) return xmlFailure;
            if (AssemblyName.GetAssemblyName(dll).Name != "Moonlark.Libchdr" || XDocument.Load(xml).Root?.Element("assembly")?.Element("name")?.Value != "Moonlark.Libchdr")
                throw new InvalidDataException("Fresh managed output identity mismatch");
            _ = session.Source("source-after-build", commit);
            string licenses = Path.Combine(root, PackageLicenses.DirectoryPath);
            PackageFile FileInput(string path, string packagePath)
            {
                if (PackagePaths.RegularFile(path, root) is { } failure) throw new InvalidDataException(failure.Message);
                return new(path, packagePath, new FileInfo(path).Length, Digest.Sha256File(path));
            }
            var nativeFiles = new List<PackageFile>
            {
                FileInput(Path.Combine(root, "src/Moonlark.Libchdr.Native/README.md"), "README.md"),
                FileInput(Path.Combine(licenses, "LICENSE.txt"), "LICENSE.txt"),
                FileInput(Path.Combine(licenses, "source-inventory.json"), "licenses/source-inventory.json"),
                FileInput(Path.Combine(root, "THIRD_PARTY_NOTICES.md"), "THIRD_PARTY_NOTICES.md"),
            };
            foreach (NativeCandidate item in native)
            {
                string prefix = "runtimes/" + item.Rid + "/native/";
                nativeFiles.Add(FileInput(item.BinaryPath, prefix + NativeRids.LibraryFileName(item.Rid)));
                nativeFiles.Add(FileInput(item.ManifestPath, prefix + "moonlark_chdr." + item.Rid + ".build-manifest.json"));
            }
            PackagePlan[] plans =
            [
                new("Moonlark.Libchdr.Native", authority.ManagedVersion, true, nativeFiles),
                new("Moonlark.Libchdr", authority.ManagedVersion, false,
                [FileInput(dll, "lib/net10.0/Moonlark.Libchdr.dll"), FileInput(xml, "lib/net10.0/Moonlark.Libchdr.xml"),
                    FileInput(Path.Combine(root, "src/Moonlark.Libchdr/README.md"), "README.md"), FileInput(Path.Combine(root, "LICENSE"), "LICENSE.txt")]),
            ];
            string feed = Directory.CreateDirectory(Path.Combine(output.Value, "feed")).FullName;
            string projects = Path.Combine(output.Value, "projects");
            PackageProjects.Isolate(projects, root, feed);
            var packages = new List<CandidatePackage>();
            foreach (PackagePlan plan in plans)
            {
                string project = PackageProjects.Write(Path.Combine(projects, plan.Id), plan);
                string packageCache = Path.Combine(output.Value, "pack-cache", plan.Id);
                session.Run(plan.Id + "-restore", projects, "dotnet", "restore", project, "--configfile", Path.Combine(projects, "NuGet.Config"), "--packages", packageCache);
                session.Run(plan.Id + "-locked-restore", projects, "dotnet", "restore", project, "--locked-mode", "--configfile", Path.Combine(projects, "NuGet.Config"), "--packages", packageCache);
                session.Run(plan.Id + "-pack", projects, "dotnet", "pack", project, "-c", "Release", "--no-restore", "--no-build", "--output", feed, "-warnaserror");
                string path = Path.Combine(feed, plan.Id + "." + plan.Version + ".nupkg");
                if (PackageArchive.Verify(root, path, plan) is { } invalid) return invalid;
                packages.Add(new(path, Digest.Sha256File(path), plan));
            }
            _ = session.Source("source-after", commit);
            if (PackageLicenses.Verify(root) is { } finalLicense) return finalLicense;
            foreach (NativeCandidate item in native)
                if (ReadNative(root, item.ManifestPath) != item) throw new InvalidDataException("Native candidate input changed during packing");
            foreach (CandidatePackage package in packages)
                if (PackageArchive.Verify(root, package.Path, package.Plan) is { } invalid) return invalid;
            var receipt = new CandidateReceipt(1, "local-unqualified", commit, authority.ManagedVersion, output.Value,
                native.Select(item => item.Rid).ToArray(), native.Length == NativeRids.Supported.Length, native, packages,
                Digest.Sha256File(Path.Combine(licenses, "LICENSE.txt")), Digest.Sha256File(Path.Combine(licenses, "source-inventory.json")), session.Steps);
            JsonObject document = JsonSerializer.SerializeToNode(receipt, JsonOptions)!.AsObject();
            using (FileStream stream = new(Path.Combine(output.Value, "candidate.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, receipt, JsonOptions);
            return document;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return new Failure("Candidate packaging failed: " + error.Message);
        }
    }

    internal static NativeCandidate ReadNative(string root, string path)
    {
        path = Path.GetFullPath(path);
        if (!ArtifactsPath.IsWithin(path, Path.Combine(root, "artifacts")) || PackagePaths.RegularFile(path, root) is { }) throw new InvalidDataException("Native manifest must be a regular artifacts input");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string rid = (string?)manifest["rid"] ?? "";
        if (!NativeRids.IsSupported(rid)) throw new InvalidDataException("Unsupported candidate RID");
        string binary = Path.Combine(Path.GetDirectoryName(path)!, "native", NativeRids.LibraryFileName(rid));
        if (PackagePaths.RegularFile(binary, root) is { } regular) throw new InvalidDataException(regular.Message);
        if (NativeVerify.VerifyManifest(manifest, binary, root) is { } failure) throw new InvalidDataException(failure.Message);
        return new(rid, path, binary, Digest.Sha256File(path), Digest.Sha256File(binary), (string)manifest["buildInfo"]!["buildId"]!);
    }
}
