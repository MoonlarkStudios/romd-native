using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Fixtures;
using Moonlark.Native.Engineering.Native;

namespace Moonlark.Native.Engineering.Packaging;

internal sealed record ConsumerOptions(string CandidateReceiptPath, string OutputDirectory, string FixtureDirectory);

/// <summary>Installs only local candidate packages into a new outside-repository consumer and preserves positive/negative execution evidence.</summary>
internal static class CandidateConsumer
{
    private const string ProjectName = "CandidateConsumer";

    internal static Result<JsonObject> Run(string root, ConsumerOptions options, IReadOnlyDictionary<string, string> environment, TextWriter? log)
    {
        try
        {
            Result<string> output = PackagePaths.NewExternalDirectory(root, options.OutputDirectory);
            if (!output.Succeeded) return output.Failure;
            if (PackagePaths.RegularFile(options.CandidateReceiptPath, root) is { } inputFailure) return inputFailure;
            CandidateReceipt receipt = JsonSerializer.Deserialize<CandidateReceipt>(JsonFields.ReadObject(options.CandidateReceiptPath, "Candidate").Value.ToJsonString(), PackageCandidate.JsonOptions)
                ?? throw new InvalidDataException("Missing candidate receipt");
            string receiptHash = Digest.Sha256File(options.CandidateReceiptPath);
            LibchdrAuthority authority = Authorities.ReadLibchdr(root).Value;
            VerifyCandidate(root, receipt, options.CandidateReceiptPath, authority);
            Result<JsonObject> fixtures = FixtureVerification.Verify(root, options.FixtureDirectory);
            if (!fixtures.Succeeded) return fixtures.Failure;
            string fixture = Path.Combine(options.FixtureDirectory, "dvd-lzma.chd");
            string fixtureHash = Digest.Sha256File(fixture);
            string rid = NativeRids.Host().Value;
            NativeCandidate native = receipt.Native.Single(item => item.Rid == rid);
            Result<IReadOnlyDictionary<string, string>> clean = BuildEnvironment.Create(environment);
            if (!clean.Succeeded) return clean.Failure;
            Directory.CreateDirectory(output.Value);
            var selected = new Dictionary<string, string>(clean.Value, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                ["NUGET_PACKAGES"] = Path.Combine(output.Value, "packages"),
                ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(output.Value, "http-cache"),
                ["DOTNET_CLI_HOME"] = Path.Combine(output.Value, "cli-home"),
            };
            var session = new CandidateSession(root, output.Value, selected, log);
            _ = session.Source("source-before", receipt.SourceCommit);
            string feed = Path.Combine(receipt.OutputDirectory, "feed");
            string consumer = Path.Combine(output.Value, "consumer");
            PackageProjects.Isolate(consumer, root, feed);
            string project = WriteProject(consumer, receipt.Version, rid);
            string source = WriteProgram(consumer, root);
            string sourceHash = Digest.Sha256File(source);
            string projectHash = Digest.Sha256File(project);
            Restore(session, "positive", consumer, project, selected["NUGET_PACKAGES"], locked: false);
            Restore(session, "positive-locked", consumer, project, selected["NUGET_PACKAGES"], locked: true);
            Dictionary<string, string> hashes = receipt.Packages.ToDictionary(item => item.Plan.Id,
                item => Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(item.Path))), StringComparer.Ordinal);
            string assets = Path.Combine(consumer, "obj", "project.assets.json");
            if (ConsumerEvidence.VerifyAssets(JsonFields.ReadObject(assets, "Consumer assets").Value, receipt.Version, hashes) is { } assetsFailure) return assetsFailure;
            if (ConsumerEvidence.VerifyLock(JsonFields.ReadObject(Path.Combine(consumer, "packages.lock.json"), "Consumer lock").Value,
                receipt.Version, rid, hashes) is { } lockFailure) return lockFailure;
            string build = Path.Combine(output.Value, "build");
            session.Run("consumer-build", consumer, "dotnet", "build", project, "-c", "Release", "--no-restore", "--output", build, "-warnaserror");
            JsonObject built = RunPositive(session, "framework-run", build, fixture, receipt.Version, authority.Pin.Commit, native.BuildId);
            VerifyNativeLayout(build, native);
            string publish = Path.Combine(output.Value, "publish");
            session.Run("consumer-publish", consumer, "dotnet", "publish", project, "-c", "Release", "--no-restore", "--runtime", rid,
                "--self-contained", "false", "-p:UseAppHost=false", "--output", publish, "-warnaserror");
            JsonObject published = RunPositive(session, "rid-publish-run", publish, fixture, receipt.Version, authority.Pin.Commit, native.BuildId);
            VerifyNativeLayout(publish, native);
            var negatives = new JsonArray();
            negatives.Add(RestoreNegative(root, output.Value, receipt, rid, selected, log, wrongVersion: false));
            negatives.Add(RestoreNegative(root, output.Value, receipt, rid, selected, log, wrongVersion: true));
            negatives.Add(LoaderNegative(session, output.Value, build, native, fixture, incompatible: false));
            negatives.Add(LoaderNegative(session, output.Value, build, native, fixture, incompatible: true));
            _ = session.Source("source-after", receipt.SourceCommit);
            VerifyCandidate(root, receipt, options.CandidateReceiptPath, authority);
            if (Digest.Sha256File(options.CandidateReceiptPath) != receiptHash || Digest.Sha256File(fixture) != fixtureHash
                || Digest.Sha256File(source) != sourceHash || Digest.Sha256File(project) != projectHash)
                throw new InvalidDataException("Consumer inputs changed during execution");
            JsonObject result = new()
            {
                ["schemaVersion"] = 1, ["qualification"] = "local-unqualified", ["rid"] = rid, ["sourceCommit"] = receipt.SourceCommit,
                ["candidateSha256"] = receiptHash, ["fixtureSha256"] = fixtureHash, ["assetsSha256"] = Digest.Sha256File(assets),
                ["lockSha256"] = Digest.Sha256File(Path.Combine(consumer, "packages.lock.json")), ["programSha256"] = sourceHash,
                ["projectSha256"] = projectHash, ["frameworkResult"] = built, ["publishResult"] = published,
                ["negativeResults"] = negatives, ["steps"] = session.Steps,
            };
            using (FileStream stream = new(Path.Combine(output.Value, "consumer.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, result, PackageCandidate.JsonOptions);
            return result;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return new Failure("Candidate consumer failed: " + error.Message);
        }
    }

    internal static Failure? VerifyInventory(CandidateReceipt receipt)
    {
        if (receipt.SourceCommit is null || !Authorities.Sha1().IsMatch(receipt.SourceCommit)
            || receipt.Native is null || receipt.DeclaredRids is null || receipt.Packages is null
            || receipt.Native.Count == 0 || receipt.Native.Any(item => item is null || !NativeRids.IsSupported(item.Rid))
            || receipt.DeclaredRids.Any(rid => !NativeRids.IsSupported(rid))
            || receipt.Packages.Count != 2 || receipt.Packages.Any(item => item?.Plan?.Files is null
                || item.Plan.Version != receipt.Version || item.Plan.Files.Any(file => file is null)))
            return new Failure("Candidate receipt has missing or invalid source/package/RID identity");
        HashSet<string> rids = receipt.Native.Select(item => item.Rid).ToHashSet(StringComparer.Ordinal);
        if (rids.Count != receipt.Native.Count || rids.Count != receipt.DeclaredRids.Count
            || !rids.SetEquals(receipt.DeclaredRids) || receipt.CompleteRidInventory != rids.SetEquals(NativeRids.Supported)
            || receipt.Packages.Count(item => item.Plan.Id == "Moonlark.Libchdr" && !item.Plan.Native) != 1
            || receipt.Packages.Count(item => item.Plan.Id == "Moonlark.Libchdr.Native" && item.Plan.Native) != 1)
            return new Failure("Candidate receipt has duplicate or mismatched package/RID coverage");
        IReadOnlyList<PackageFile> files = receipt.Packages.Single(item => item.Plan.Native).Plan.Files;
        if (files.Select(file => file.PackagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count
            || !Matches("LICENSE.txt", receipt.LicenseSha256)
            || !Matches("licenses/source-inventory.json", receipt.LicenseInventorySha256))
            return new Failure("Candidate native package license inventory does not match its receipt");
        HashSet<string> runtimePaths = new(StringComparer.Ordinal);
        foreach (NativeCandidate native in receipt.Native)
        {
            string prefix = "runtimes/" + native.Rid + "/native/";
            string binary = prefix + NativeRids.LibraryFileName(native.Rid);
            string manifest = prefix + "moonlark_chdr." + native.Rid + ".build-manifest.json";
            if (!Matches(binary, native.BinarySha256, native.BinaryPath) || !Matches(manifest, native.ManifestSha256, native.ManifestPath))
                return new Failure("Candidate native package bytes/source do not match the declared RID input");
            runtimePaths.Add(binary);
            runtimePaths.Add(manifest);
        }
        if (!runtimePaths.SetEquals(files.Where(file => file.PackagePath.StartsWith("runtimes/", StringComparison.Ordinal)).Select(file => file.PackagePath)))
            return new Failure("Candidate native package runtime inventory does not match its declared RIDs");
        return null;

        bool Matches(string path, string hash, string? source = null) => !string.IsNullOrEmpty(hash)
            && files.Count(file => file.PackagePath == path && file.Sha256 == hash && (source is null || file.SourcePath == source)) == 1;
    }

    private static void VerifyCandidate(string root, CandidateReceipt receipt, string path, LibchdrAuthority authority)
    {
        if (VerifyInventory(receipt) is { } inventory) throw new InvalidDataException(inventory.Message);
        if (receipt.SchemaVersion != 1 || receipt.Qualification != "local-unqualified" || receipt.Version != authority.ManagedVersion
            || Path.GetFullPath(path) != Path.Combine(receipt.OutputDirectory, "candidate.json")
            || !ArtifactsPath.ValidateDirectory(receipt.OutputDirectory, root).Succeeded)
            throw new InvalidDataException("Candidate receipt identity/inventory mismatch");
        if (PackageLicenses.Verify(root) is { } licenses) throw new InvalidDataException(licenses.Message);
        if (receipt.LicenseSha256 != Digest.Sha256File(Path.Combine(root, PackageLicenses.DirectoryPath, "LICENSE.txt"))
            || receipt.LicenseInventorySha256 != Digest.Sha256File(Path.Combine(root, PackageLicenses.DirectoryPath, "source-inventory.json")))
            throw new InvalidDataException("Candidate license inventory changed");
        foreach (NativeCandidate native in receipt.Native)
            if (PackageCandidate.ReadNative(root, native.ManifestPath) != native) throw new InvalidDataException("Candidate native input changed");
        foreach (CandidatePackage package in receipt.Packages)
        {
            string expectedPath = Path.Combine(receipt.OutputDirectory, "feed", package.Plan.Id + "." + receipt.Version + ".nupkg");
            if (package.Path != expectedPath || PackagePaths.RegularFile(package.Path, root) is { } || Digest.Sha256File(package.Path) != package.Sha256)
                throw new InvalidDataException("Candidate package bytes/path changed");
            if (PackageArchive.Verify(root, package.Path, package.Plan) is { } invalid) throw new InvalidDataException(invalid.Message);
        }
    }

    private static void Restore(CandidateSession session, string name, string directory, string project, string packages, bool locked)
    {
        string[] lockArgument = locked ? ["--locked-mode"] : [];
        session.Run(name + "-restore", directory, ["dotnet", "restore", project, "--configfile", Path.Combine(directory, "NuGet.Config"),
            "--packages", packages, "-p:RestoreFallbackFolders=", "-p:RestoreAdditionalProjectSources=", .. lockArgument]);
    }

    internal static string WriteProject(string directory, string version, string rid)
    {
        string path = Path.Combine(directory, ProjectName + ".csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"),
                new XElement("RuntimeIdentifier", rid), new XElement("UseAppHost", "false"), new XElement("SelfContained", "false"),
                new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"), new XElement("TreatWarningsAsErrors", "true"),
                new XElement("RestorePackagesWithLockFile", "true"), new XElement("ManagePackageVersionsCentrally", "false")),
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Moonlark.Libchdr"), new XAttribute("Version", "[" + version + "]"))))).Save(path);
        return path;
    }

    private static string WriteProgram(string directory, string root)
    {
        string readme = File.ReadAllText(Path.Combine(root, "src/Moonlark.Libchdr/README.md"));
        int first = readme.IndexOf("```csharp\n", StringComparison.Ordinal);
        int last = first < 0 ? -1 : readme.IndexOf("```", first + 10, StringComparison.Ordinal);
        if (first < 0 || last < 0) throw new InvalidDataException("Documented quick-start program is missing");
        string sample = readme[(first + 10)..last];
        if (sample.Contains("LibchdrLibrary.Load", StringComparison.Ordinal) || !sample.StartsWith("using Moonlark.Libchdr;", StringComparison.Ordinal))
            throw new InvalidDataException("Quick-start must exercise default native loading");
        string body = sample["using Moonlark.Libchdr;".Length..];
        string source = "using Moonlark.Libchdr;\nusing System.Text.Json;\ntry {\n" + body
            + "\nConsole.WriteLine(JsonSerializer.Serialize(new { IntegrityVerified = verified, BuildInfo = LibchdrLibrary.BuildInfo }));\nreturn verified ? 0 : 2;\n}\n"
            + "catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + \": \" + error.Message); return 1; }\n";
        string path = Path.Combine(directory, "Program.cs"); File.WriteAllText(path, source); return path;
    }

    private static JsonObject RunPositive(CandidateSession session, string name, string application, string fixture, string version, string upstream, string buildId)
    {
        string stdout = session.Run(name, application, "dotnet", Path.Combine(application, ProjectName + ".dll"), fixture);
        JsonObject result = JsonNode.Parse(stdout)!.AsObject();
        if (ConsumerEvidence.VerifyResult(result, version, upstream, buildId) is { } invalid) throw new InvalidDataException(invalid.Message);
        return result;
    }

    private static void VerifyNativeLayout(string directory, NativeCandidate native)
    {
        string[] binaries = CurrentNativePaths(directory, native.Rid);
        string[] manifests = Directory.GetFiles(directory, "moonlark_chdr." + native.Rid + ".build-manifest.json", SearchOption.AllDirectories);
        if (binaries.Length != 1 || manifests.Length != 1 || Digest.Sha256File(binaries[0]) != native.BinarySha256 || Digest.Sha256File(manifests[0]) != native.ManifestSha256)
            throw new InvalidDataException("Consumer native binary/uniquely named manifest did not propagate exactly");
    }

    private static string[] CurrentNativePaths(string directory, string rid) =>
        new[] { Path.Combine(directory, NativeRids.LibraryFileName(rid)), Path.Combine(directory, "runtimes", rid, "native", NativeRids.LibraryFileName(rid)) }
            .Where(File.Exists).ToArray();

    private static JsonObject RestoreNegative(string root, string output, CandidateReceipt receipt, string rid,
        IReadOnlyDictionary<string, string> environment, TextWriter? log, bool wrongVersion)
    {
        string name = wrongVersion ? "wrong-family" : "missing-native-package";
        string directory = Path.Combine(output, name);
        string feed = Directory.CreateDirectory(Path.Combine(directory, "feed")).FullName;
        CandidatePackage managed = receipt.Packages.Single(item => !item.Plan.Native);
        File.Copy(managed.Path, Path.Combine(feed, Path.GetFileName(managed.Path)));
        if (wrongVersion)
        {
            CandidatePackage native = receipt.Packages.Single(item => item.Plan.Native);
            string wrong = Path.Combine(feed, "Moonlark.Libchdr.Native.9.0.0.nupkg");
            using ZipArchive original = ZipFile.OpenRead(native.Path);
            using ZipArchive changed = ZipFile.Open(wrong, ZipArchiveMode.Create);
            foreach (ZipArchiveEntry entry in original.Entries)
            {
                using Stream target = changed.CreateEntry(entry.FullName).Open();
                if (entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                {
                    using Stream source = entry.Open(); XDocument document = XDocument.Load(source);
                    document.Descendants().Single(item => item.Name.LocalName == "version").Value = "9.0.0"; document.Save(target);
                }
                else { using Stream source = entry.Open(); source.CopyTo(target); }
            }
        }
        string projectDirectory = Path.Combine(directory, "consumer");
        PackageProjects.Isolate(projectDirectory, root, feed);
        string project = WriteProject(projectDirectory, receipt.Version, rid);
        var selected = new Dictionary<string, string>(environment, StringComparer.Ordinal)
        { ["NUGET_PACKAGES"] = Path.Combine(directory, "packages"), ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(directory, "http-cache"), ["DOTNET_CLI_HOME"] = Path.Combine(directory, "cli-home") };
        var session = new CandidateSession(root, directory, selected, log);
        ProcessOutput result = session.Execute("restore", projectDirectory, "dotnet", "restore", project,
            "--configfile", Path.Combine(projectDirectory, "NuGet.Config"), "--packages", selected["NUGET_PACKAGES"], "-p:RestoreFallbackFolders=", "-p:RestoreAdditionalProjectSources=");
        string code = wrongVersion ? "NU1102" : "NU1101";
        if (result.TimedOut || result.ExitCode == 0 || !result.Combined.Contains(code, StringComparison.Ordinal) || !result.Combined.Contains("Moonlark.Libchdr.Native", StringComparison.Ordinal))
            throw new InvalidDataException("Negative restore did not fail for its intended missing exact native dependency: " + name);
        return new JsonObject { ["name"] = name, ["expectedError"] = code, ["steps"] = session.Steps };
    }

    private static JsonObject LoaderNegative(CandidateSession session, string output, string original, NativeCandidate native, string fixture, bool incompatible)
    {
        string name = incompatible ? "incompatible-first-native" : "missing-native-asset";
        string application = Path.Combine(output, name);
        foreach (string path in Directory.GetFiles(original, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(application, Path.GetRelativePath(original, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
        }
        string filename = NativeRids.LibraryFileName(native.Rid);
        string current = CurrentNativePaths(application, native.Rid).Single();
        string? badHash = null;
        if (incompatible)
        {
            string fallback = Path.GetDirectoryName(current) == application ? Path.Combine(application, "runtimes", native.Rid, "native", filename) : Path.Combine(application, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!); File.Copy(current, fallback);
            File.WriteAllText(current, "Deliberately incompatible synthetic native artifact; never executable.\n");
            badHash = Digest.Sha256File(current);
        }
        else File.Delete(current);
        ProcessOutput result = session.Execute(name, application, "dotnet", Path.Combine(application, ProjectName + ".dll"), fixture);
        if (result.TimedOut || result.ExitCode != 1
            || !(result.Combined.Contains("DllNotFoundException", StringComparison.Ordinal) || (incompatible && result.Combined.Contains("BadImageFormatException", StringComparison.Ordinal)))
            || (incompatible && result.Combined.Contains("Packaged libchdr asset", StringComparison.Ordinal)))
            throw new InvalidDataException("Negative loader process did not reject its intended native artifact: " + name);
        return new JsonObject { ["name"] = name, ["exitCode"] = result.ExitCode, ["incompatibleSha256"] = badHash,
            ["validFallbackSha256"] = incompatible ? native.BinarySha256 : null };
    }
}
