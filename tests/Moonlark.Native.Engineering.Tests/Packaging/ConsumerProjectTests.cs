using System.Xml.Linq;
using Moonlark.Native.Engineering.Packaging;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Packaging;

/// <summary>The generated framework-dependent consumer has one actual target RID, with normal SDK checks.</summary>
public sealed class ConsumerProjectTests
{
    /// <summary>A singular RID avoids requesting unrelated self-contained runtime packs during folder-only restore.</summary>
    [Fact]
    public void FrameworkDependentProjectHasOneTargetRid()
    {
        using var temp = new TemporaryDirectory();
        XDocument project = XDocument.Load(CandidateConsumer.WriteProject(temp.Path, "1.0.0-preview.1", "osx-arm64"));
        XElement properties = project.Root!.Element("PropertyGroup")!;
        Assert.Equal("osx-arm64", (string?)properties.Element("RuntimeIdentifier"));
        Assert.Null(properties.Element("RuntimeIdentifiers"));
        Assert.Equal("false", (string?)properties.Element("SelfContained"));
        Assert.Equal("false", (string?)properties.Element("UseAppHost"));
        Assert.Null(properties.Element("EnableRuntimePackDownload"));
        Assert.Null(properties.Element("NuGetAudit"));
        XElement reference = project.Descendants("PackageReference").Single();
        Assert.Equal("Moonlark.Libchdr", (string?)reference.Attribute("Include"));
        Assert.Equal("[1.0.0-preview.1]", (string?)reference.Attribute("Version"));
    }
}
