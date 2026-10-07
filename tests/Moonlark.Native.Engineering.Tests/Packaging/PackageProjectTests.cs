using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Packaging;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Packaging;

/// <summary>NuGet needs an actual LICENSE.txt filename for the extensionless repository license.</summary>
public sealed class PackageProjectTests
{
    /// <summary>Staging preserves original source identity while supplying the filename consumed by SDK pack.</summary>
    [Fact]
    public void ExtensionlessLicenseStagesExactBytes()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "LICENSE");
        File.WriteAllText(source, "exact license\r\nbytes\n");
        var file = new PackageFile(source, "LICENSE.txt", new FileInfo(source).Length, Digest.Sha256File(source));
        var plan = new PackagePlan("Moonlark.Libchdr", "1.0.0-preview.1", false, [file]);
        string directory = Path.Combine(temp.Path, "projects", plan.Id);
        string project = PackageProjects.Write(directory, plan);
        string staged = (string)XDocument.Load(project).Descendants("None").Single().Attribute("Include")!;
        Assert.Equal(Path.Combine(directory, "staging", "LICENSE.txt"), staged);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(staged));
        Assert.Equal(source, plan.Files.Single().SourcePath);
        Assert.Equal(file.Sha256, Digest.Sha256File(staged));
    }

    /// <summary>Existing staging evidence and redirected paths are never overwritten.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingStagingFails(bool linked)
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "LICENSE");
        File.WriteAllText(source, "license");
        var file = new PackageFile(source, "LICENSE.txt", new FileInfo(source).Length, Digest.Sha256File(source));
        var plan = new PackagePlan("Moonlark.Libchdr", "1.0.0-preview.1", false, [file]);
        string directory = Directory.CreateDirectory(Path.Combine(temp.Path, "project")).FullName;
        string staging = Path.Combine(directory, "staging");
        if (linked) Directory.CreateSymbolicLink(staging, temp.Path);
        else Directory.CreateDirectory(staging);
        Assert.Throws<InvalidDataException>(() => PackageProjects.Write(directory, plan));
        Assert.Equal("license", File.ReadAllText(source));
    }

    /// <summary>A changed input cannot be silently blessed by creating a new staged copy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedLicenseFailsBeforePacking(bool wrongLength)
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "LICENSE");
        File.WriteAllText(source, "license");
        var file = new PackageFile(source, "LICENSE.txt", wrongLength ? 99 : new FileInfo(source).Length,
            wrongLength ? Digest.Sha256File(source) : new string('0', 64));
        var plan = new PackagePlan("Moonlark.Libchdr", "1.0.0-preview.1", false, [file]);
        Assert.Throws<InvalidDataException>(() => PackageProjects.Write(Path.Combine(temp.Path, "project"), plan));
    }
}
