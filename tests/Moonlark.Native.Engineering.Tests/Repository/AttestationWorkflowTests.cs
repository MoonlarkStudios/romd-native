using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Repository;

/// <summary>Provenance writes are isolated from builds and restricted to complete trusted-run evidence.</summary>
public sealed class AttestationWorkflowTests
{
    private const string Trusted = "    if: github.repository == 'MoonlarkStudios/romd-native' && (github.event_name == 'push' || github.event_name == 'workflow_dispatch') && (github.ref == 'refs/heads/main' || github.ref == 'refs/heads/refactor/csharp-engineering')";

    /// <summary>Neither pull requests nor a partial Linux result can reach signing.</summary>
    [Fact]
    public void SigningRequiresBothLinuxJobsAndTrustedEvents()
    {
        string prepare = Job("prepare-linux-subjects");
        string sign = Job("attest-linux");
        Assert.Contains("    needs: [linux-x64, linux-arm64]\n", prepare, StringComparison.Ordinal);
        Assert.Contains("    needs: prepare-linux-subjects\n", sign, StringComparison.Ordinal);
        Assert.Contains(Trusted + "\n", prepare, StringComparison.Ordinal);
        Assert.Contains(Trusted + "\n", sign, StringComparison.Ordinal);
        Assert.DoesNotContain("always()", prepare + sign, StringComparison.Ordinal);
    }

    /// <summary>Only the signing job obtains write permissions and it never checks out or executes repository code.</summary>
    [Fact]
    public void WritesAreIsolatedFromRepositoryExecution()
    {
        string text = Workflow();
        string sign = Job("attest-linux");
        Assert.Equal(2, text.Split('\n').Count(line => line.EndsWith(": write", StringComparison.Ordinal)));
        Assert.Contains("    permissions:\n      contents: read\n      id-token: write\n      attestations: write\n", sign, StringComparison.Ordinal);
        Assert.DoesNotContain("      - run:", sign, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout", sign, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(": write", Job("prepare-linux-subjects"), StringComparison.Ordinal);
    }

    /// <summary>Read-only preflight checks both same-run artifacts against the triggering commit and exports one exact checksum file.</summary>
    [Fact]
    public void PreflightBindsCompleteSubjectsToSourceCommit()
    {
        string prepare = Job("prepare-linux-subjects");
        foreach (string rid in new[] { "linux-x64", "linux-arm64" })
            Assert.Contains("          name: libchdr-" + rid + "-evidence\n          path: artifacts/signing/" + rid + "\n", prepare, StringComparison.Ordinal);
        Assert.Contains("qualification subjects --source-commit \"$EXPECTED_COMMIT\"\n        env:\n          EXPECTED_COMMIT: ${{ github.sha }}\n", prepare, StringComparison.Ordinal);
        Assert.Contains("          name: libchdr-linux-subjects\n          path: artifacts/signing/subjects.sha256\n          if-no-files-found: error\n", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("run-id:", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("github-token:", prepare, StringComparison.Ordinal);
    }

    /// <summary>The pinned attestation action receives explicit checksums and cannot publish to a registry or storage record.</summary>
    [Fact]
    public void SigningUsesExactChecksumsAndRetainsBundle()
    {
        string sign = Job("attest-linux");
        Assert.Contains("          name: libchdr-linux-subjects\n          path: subjects\n", sign, StringComparison.Ordinal);
        Assert.Contains("uses: actions/attest@1e69f48acb82d1966a394da916b4c1698aa569d6", sign, StringComparison.Ordinal);
        Assert.Contains("          subject-checksums: subjects/subjects.sha256\n          push-to-registry: false\n          create-storage-record: false\n", sign, StringComparison.Ordinal);
        Assert.DoesNotContain("subject-path:", sign, StringComparison.Ordinal);
        Assert.Contains("          name: libchdr-linux-provenance\n          path: ${{ steps.provenance.outputs.bundle-path }}\n          if-no-files-found: error\n", sign, StringComparison.Ordinal);
    }

    private static string Workflow() => File.ReadAllText(Path.Combine(TestRepository.Root, ".github", "workflows", "native-libchdr.yml"));

    private static string Job(string name)
    {
        string marker = "  " + name + ":\n";
        string text = Workflow();
        Assert.Contains(marker, text, StringComparison.Ordinal);
        return string.Join('\n', text.Split(marker, StringSplitOptions.None)[1].Split('\n')
            .TakeWhile(line => line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal))) + "\n";
    }
}
