using System.Text.Json;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Repository;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Repository;

/// <summary>Family identity and the closed, source-only workflow grammar.</summary>
public sealed class RepositoryCheckTests
{
    /// <summary>The current repository satisfies every check.</summary>
    [Fact]
    public void CurrentRepositoryPasses() => Assert.Null(RepositoryCheck.Run(TestRepository.Root, null));

    /// <summary>Tags cannot misstate the family version or tool revision.</summary>
    [Theory]
    [InlineData("libchdr-v0.3.0")]
    [InlineData("libchdr-native-v1.0.0-preview.1")]
    [InlineData("chdman-0.289-r2")]
    [InlineData("other")]
    public void TagsCannotMisstateTheFamilyOrToolRevision(string tag) =>
        Assert.NotNull(RepositoryCheck.ValidateTag(tag, "1.0.0-preview.1", Mame()));

    /// <summary>The exact family and tool tags pass.</summary>
    [Theory]
    [InlineData("libchdr-v1.0.0-preview.1")]
    [InlineData("chdman-0.289-r1")]
    public void ActualFamilyAndToolTagsPass(string tag) => Assert.Null(RepositoryCheck.ValidateTag(tag, "1.0.0-preview.1", Mame()));

    /// <summary>Pin drift and write permissions fail closed.</summary>
    [Fact]
    public void SourcePinAndPermissionDriftFailClosed()
    {
        using var directory = Copy();
        string pinPath = Path.Combine(directory.Path, Authorities.PinPath);
        string original = File.ReadAllText(pinPath);
        JsonObject pin = JsonNode.Parse(original)!.AsObject();
        pin["commit"] = new string('0', 40);
        File.WriteAllText(pinPath, pin.ToJsonString());
        Assert.Contains("commit mismatch", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
        File.WriteAllText(pinPath, original);
        Replace(directory, "release.yml", "contents: read", "contents: write");
        Assert.NotNull(RepositoryCheck.Run(directory.Path, null));
    }

    /// <summary>Actions must be pinned to reviewed full SHAs.</summary>
    [Fact]
    public void MutableActionTagIsRejected()
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", RepositoryCheck.Actions["actions/checkout"], "v7");
        Assert.Contains("unreviewed action", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Flow mappings, extra workflows and flow steps cannot bypass the grammar.</summary>
    [Theory]
    [InlineData("ci.yml", "  extra:\n    permissions: {contents: write}\n")]
    [InlineData("publish.yaml", "name: Publish\npermissions: write-all\njobs:\n  publish:\n    steps:\n      - run: gh release create x\n")]
    [InlineData("ci.yml", "      - {uses: unknown/action@main}\n")]
    public void UnsupportedYamlFormsCannotBypassReview(string name, string addition)
    {
        using var directory = Copy();
        string path = Path.Combine(directory.Path, ".github/workflows", name);
        File.WriteAllText(path, (File.Exists(path) ? File.ReadAllText(path) : "") + addition);
        Assert.NotNull(RepositoryCheck.Run(directory.Path, null));
    }

    /// <summary>CI must keep the actual tag trigger.</summary>
    [Fact]
    public void ActualTagTriggerCannotDisappear()
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", "    tags: ['libchdr-*', 'chdman-*']\n", "");
        Assert.Contains("actual library/tool tag", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Secrets cannot be reached through alternative expression syntax.</summary>
    [Theory]
    [InlineData("${{ secrets['PUBLISH_TOKEN'] }}")]
    [InlineData("${{ format('{0}', secrets.PUBLISH_TOKEN) }}")]
    public void SecretsCannotBeReferencedWithAlternativeExpressionSyntax(string expression)
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", "CI: true", "CI: " + expression);
        Assert.NotNull(RepositoryCheck.Run(directory.Path, null));
    }

    /// <summary>Commented routing lines are not active evidence.</summary>
    [Theory]
    [InlineData("    tags: ['libchdr-*', 'chdman-*']")]
    [InlineData("        if: github.ref_type == 'tag'")]
    [InlineData("          EXPECTED_TAG: ${{ github.ref_name }}")]
    [InlineData("      - run: python3 -B eng/check.py --tag \"$EXPECTED_TAG\"")]
    public void CommentedTagRoutingIsNotActiveEvidence(string line)
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", line, "#" + line);
        Assert.Contains("actual library/tool tag", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    private static TemporaryDirectory Copy()
    {
        var directory = new TemporaryDirectory();
        TestRepository.CopyTo(directory.Path, "eng/pins", "eng/versions", ".github/workflows");
        return directory;
    }

    private static void Replace(TemporaryDirectory directory, string workflow, string original, string replacement)
    {
        string path = Path.Combine(directory.Path, ".github/workflows", workflow);
        string text = File.ReadAllText(path);
        Assert.Contains(original, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(original, replacement, StringComparison.Ordinal));
    }

    private static JsonElement Mame() => JsonDocument.Parse("{\"version\": \"0.289\", \"rebuildRevision\": 1}").RootElement;
}
