using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Repository;

/// <summary>Long performance experiments require explicit manual selection and retain the executed child builds.</summary>
public sealed class BenchmarkWorkflowTests
{
    /// <summary>A normal dispatch leaves the long timing experiment disabled.</summary>
    [Fact]
    public void BenchmarksRequireExplicitOptIn()
    {
        string workflow = Workflow();
        Assert.Contains("  workflow_dispatch:\n    inputs:\n      benchmarks:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("        type: boolean\n        default: false\n", workflow, StringComparison.Ordinal);
    }

    /// <summary>Each supported hosted architecture runs the fixed production policy and retains its actual inputs and children.</summary>
    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("win-x64")]
    public void ManualBenchmarkRunsAfterNativeEvidenceAndRetainsOutputs(string rid)
    {
        string job = string.Join('\n', Workflow().Split('\n').SkipWhile(line => line != "  " + rid + ":").Skip(1)
            .TakeWhile(line => !line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal)));
        const string benchmark = "      - name: Run full benchmark matrix\n        if: github.event_name == 'workflow_dispatch' && inputs.benchmarks\n";
        Assert.Contains(benchmark, job, StringComparison.Ordinal);
        Assert.True(job.IndexOf("qualification ", StringComparison.Ordinal) < job.IndexOf(benchmark, StringComparison.Ordinal));
        Assert.Contains("dotnet run --project tests/Moonlark.Libchdr.Benchmarks -c Release --no-build -- run artifacts/benchmarks/manual", job, StringComparison.Ordinal);
        Assert.Contains("    timeout-minutes: 180\n", job, StringComparison.Ordinal);
        Assert.Contains("        if: always() && github.event_name == 'workflow_dispatch' && inputs.benchmarks\n", job, StringComparison.Ordinal);
        Assert.Contains("          name: libchdr-" + rid + "-benchmarks\n", job, StringComparison.Ordinal);
        string prefix = rid == "win-x64" ? "" : "artifacts/qualification/" + rid + "/checkout/";
        Assert.Contains(prefix + "artifacts/benchmarks", job, StringComparison.Ordinal);
        Assert.Contains(prefix + "tests/Moonlark.Libchdr.Benchmarks/bin/Release/net10.0", job, StringComparison.Ordinal);
        if (rid == "win-x64")
        {
            Assert.Contains("set \"BENCHMARK_EXIT=%ERRORLEVEL%\"", job, StringComparison.Ordinal);
            Assert.Contains("exit /b %BENCHMARK_EXIT%", job, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("        working-directory: artifacts/qualification/" + rid + "/checkout\n", job, StringComparison.Ordinal);
            Assert.Contains("        shell: bash\n", job, StringComparison.Ordinal);
            Assert.Contains("set -o pipefail", job, StringComparison.Ordinal);
        }
    }

    private static string Workflow() => File.ReadAllText(Path.Combine(TestRepository.Root, ".github", "workflows", "native-libchdr.yml"));
}
