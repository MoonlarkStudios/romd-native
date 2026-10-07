using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Packaging;

/// <summary>Two temporary SDK pack projects, with no imports or settings inherited from the product projects.</summary>
internal static class PackageProjects
{
    internal static void Isolate(string directory, string root, string feed)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />\n");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />\n");
        File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"), "<Project />\n");
        File.Copy(Path.Combine(root, "global.json"), Path.Combine(directory, "global.json"));
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")),
            new XElement("packageSourceMapping", new XElement("clear"), new XElement("packageSource", new XAttribute("key", "candidate"), new XElement("package", new XAttribute("pattern", "Moonlark.Libchdr*"))))))
            .Save(Path.Combine(directory, "NuGet.Config"));
    }

    internal static string Write(string directory, PackagePlan plan)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, plan.Id + ".csproj");
        var properties = new XElement("PropertyGroup",
            Property("TargetFramework", "net10.0"), Property("PackageId", plan.Id), Property("Version", plan.Version),
            Property("Authors", "Moonlark Studios"), Property("Description", "Local unqualified package candidate for " + plan.Id + "; no release qualification."),
            Property("IncludeBuildOutput", "false"), Property("EnableDefaultItems", "false"), Property("IsPackable", "true"),
            Property("PackageReadmeFile", "README.md"), Property("PackageLicenseFile", "LICENSE.txt"),
            Property("ManagePackageVersionsCentrally", "false"), Property("RestorePackagesWithLockFile", "true"),
            Property("TreatWarningsAsErrors", "true"), Property("EnablePackageValidation", "true"),
            Property("EnableStrictModeForCompatibleTfms", "true"), Property("EnableStrictModeForCompatibleFrameworksInPackage", "true"),
            Property("SuppressDependenciesWhenPacking", plan.Native ? "true" : "false"));
        var items = new XElement("ItemGroup", plan.Files.Select(file => new XElement("None", new XAttribute("Include", PackSource(file)),
            new XAttribute("Pack", "true"), new XAttribute("PackagePath", file.PackagePath))));
        if (!plan.Native) items.Add(new XElement("PackageReference", new XAttribute("Include", "Moonlark.Libchdr.Native"), new XAttribute("Version", "[" + plan.Version + "]")));
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, items)).Save(path);
        return path;

        string PackSource(PackageFile file)
        {
            if (plan.Native || file.PackagePath != "LICENSE.txt") return file.SourcePath;
            string staging = Path.Combine(directory, "staging");
            if (Path.Exists(staging)) throw new InvalidDataException("Candidate license staging path already exists");
            Directory.CreateDirectory(staging);
            string staged = Path.Combine(staging, "LICENSE.txt");
            using (FileStream source = File.OpenRead(file.SourcePath))
            using (FileStream target = new(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)) source.CopyTo(target);
            if (new FileInfo(staged).Length != file.Bytes || Digest.Sha256File(staged) != file.Sha256)
                throw new InvalidDataException("Candidate staged license differs from its original source identity");
            return staged;
        }
    }

    private static XElement Property(string name, string value) => new(name, value);
}
