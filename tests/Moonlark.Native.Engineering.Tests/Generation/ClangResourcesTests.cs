using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Generation;
using Moonlark.Native.Engineering.Qualification;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Generation;

/// <summary>Exported builtin headers are complete regular inputs, never a fallback to unrelated host headers.</summary>
public sealed class ClangResourcesTests
{
    /// <summary>A path containing spaces remains one typed option value.</summary>
    [Fact]
    public void ResourceArgumentPreservesPath() =>
        Assert.Equal<string>(["--resource-directory", "/owned/artifacts/clang resource"], ClangResources.Arguments("/owned/artifacts/clang resource"));

    /// <summary>The full tree, including headers included by stddef, is measured.</summary>
    [Fact]
    public void EveryHeaderIsFingerprinted()
    {
        using var root = new TemporaryDirectory();
        string directory = Create(root.Path);
        File.WriteAllText(Path.Combine(directory, "include", "__stddef_size_t.h"), "nested builtin");
        Directory.CreateDirectory(Path.Combine(directory, "include", "nested"));
        File.WriteAllText(Path.Combine(directory, "include", "nested", "builtin.h"), "nested tree");
        var result = ClangResources.Verify(root.Path, directory);
        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Value.Count);
        Assert.Equal(Digest.Sha256("nested tree"u8), result.Value["include/nested/builtin.h"]!.GetValue<string>());
        Assert.Equal(Digest.Sha256("nested builtin"u8), result.Value["include/__stddef_size_t.h"]!.GetValue<string>());
    }

    /// <summary>Missing or empty required builtin headers are rejected before running ClangSharp.</summary>
    [Theory]
    [InlineData("stddef.h", false)]
    [InlineData("stdarg.h", false)]
    [InlineData("stdint.h", false)]
    [InlineData("stddef.h", true)]
    [InlineData("stdarg.h", true)]
    [InlineData("stdint.h", true)]
    public void RequiredHeadersCannotBeMissingOrEmpty(string name, bool empty)
    {
        using var root = new TemporaryDirectory();
        string directory = Create(root.Path);
        string file = Path.Combine(directory, "include", name);
        if (empty) File.WriteAllText(file, ""); else File.Delete(file);
        Assert.False(ClangResources.Verify(root.Path, directory).Succeeded);
    }

    /// <summary>Symlinks cannot redirect a resource root, child directory or header.</summary>
    [Theory]
    [InlineData("root")]
    [InlineData("child")]
    [InlineData("header")]
    public void RedirectedResourcesAreRejected(string kind)
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        string directory = Create(root.Path);
        if (kind == "root")
        {
            string link = Path.Combine(root.Path, "artifacts", "redirected");
            Directory.CreateSymbolicLink(link, directory);
            directory = link;
        }
        else if (kind == "child") Directory.CreateSymbolicLink(Path.Combine(directory, "include", "redirected"), outside.Path);
        else
        {
            string file = Path.Combine(directory, "include", "stddef.h");
            File.Move(file, Path.Combine(outside.Path, "target"));
            File.CreateSymbolicLink(file, Path.Combine(outside.Path, "target"));
        }
        Assert.False(ClangResources.Verify(root.Path, directory).Succeeded);
    }

    /// <summary>An arbitrary host directory cannot become an unrecorded resource input.</summary>
    [Fact]
    public void OutsideArtifactsIsRejected()
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        Assert.False(ClangResources.Verify(root.Path, outside.Path).Succeeded);
    }

    /// <summary>Header export and host generation failures stop the orchestrator; success records exact inputs.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("copy-failure")]
    [InlineData("generator-failure")]
    [InlineData("drift")]
    [InlineData("resource-mutation")]
    [InlineData("wrong-version")]
    [InlineData("relative-resource")]
    public void ExportAndGenerationMustBothSucceed(string scenario)
    {
        using var fixture = new GenerationFixture();
        fixture.CopyTrackedOutputs();
        string directory = Path.Combine(fixture.Root, "artifacts", "qualification", "linux-arm64");
        Directory.CreateDirectory(directory);
        string exported = Path.Combine(directory, "clang-resource");
        Result<string> Container(string[] arguments)
        {
            if (arguments.Contains("--version")) return scenario == "wrong-version" ? "clang version 18.0.0 (host)" : "clang version 21.1.8 (pinned)";
            if (arguments.Contains("-print-resource-dir")) return scenario == "relative-resource" ? "relative" : "/pinned/clang/21";
            Assert.Contains("cp", arguments);
            Assert.Equal<string>(["--platform", "linux/arm64", "--user", "1000:1000", "--mount",
                "type=bind,source=" + ArtifactsPath.Resolve(exported) + ",target=/resources", "sha256:pinned", "cp", "-R", "--", "/pinned/clang/21/include", "/resources/include"], arguments);
            if (scenario == "copy-failure") return new Failure("copy failed");
            Directory.CreateDirectory(Path.Combine(exported, "include"));
            foreach (string name in (string[])["stddef.h", "stdarg.h", "stdint.h"])
                File.WriteAllText(Path.Combine(exported, "include", name), "synthetic builtin " + name);
            return "";
        }
        var result = LinuxBuilder.Generate(fixture.Root, "linux-arm64", directory, "sha256:pinned", "linux/arm64", "1000:1000",
            TestRepository.CleanEnvironment, TextWriter.Null, Container, fixture.Tool(command =>
            {
                int option = command.ToList().IndexOf("--resource-directory");
                Assert.True(option >= 0);
                Assert.Equal(ArtifactsPath.Resolve(exported), command[option + 1]);
                if (scenario == "resource-mutation") File.AppendAllText(Path.Combine(exported, "include", "stddef.h"), "changed during generation");
                return scenario == "generator-failure" ? new Failure("generator failed")
                    : fixture.WriteOutput(command, GenerationFixture.CommittedBindings + (scenario == "drift" ? "\n// drift\n" : ""));
            }));
        Assert.Equal(scenario == "success", result.Succeeded);
        Assert.Equal(scenario is "copy-failure" or "wrong-version" or "relative-resource" ? 0 : 1, fixture.Invocations.Count);
        if (result.Succeeded) Assert.Equal(3, result.Value["headerSha256"]!.AsObject().Count);
        Assert.Equal(GenerationFixture.CommittedBindings, File.ReadAllText(fixture.Bindings));
    }

    /// <summary>Only a single-line, NUL-free Linux absolute resource path can reach export or generation.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("C:/pinned/clang/21")]
    [InlineData("C:\\pinned\\clang\\21")]
    [InlineData("\\\\server\\share\\clang")]
    [InlineData("/pinned/clang/21\0")]
    [InlineData("/pinned/clang/21\n/other")]
    [InlineData("/pinned/clang/21\r/other")]
    [InlineData("/pinned/clang/21\r\n/other")]
    public void InvalidLinuxResourcePathsAreRejectedBeforeExport(string resource)
    {
        using var fixture = new GenerationFixture();
        string directory = Path.Combine(fixture.Root, "artifacts", "qualification", "linux-arm64");
        Directory.CreateDirectory(directory);
        int exports = 0;
        Result<string> Container(string[] arguments)
        {
            if (arguments.Contains("--version")) return "clang version 21.1.8 (pinned)";
            if (arguments.Contains("-print-resource-dir")) return resource;
            exports++;
            return new Failure("Unexpected resource export");
        }
        var result = LinuxBuilder.Generate(fixture.Root, "linux-arm64", directory, "sha256:pinned", "linux/arm64", "1000:1000",
            TestRepository.CleanEnvironment, TextWriter.Null, Container, fixture.Tool(_ => new Failure("Unexpected generation")));
        Assert.False(result.Succeeded);
        Assert.Equal(0, exports);
        Assert.Contains("Compiler resource path must be absolute", result.Failure.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Invocations);
        Assert.False(Directory.Exists(Path.Combine(directory, "clang-resource")));
    }

    private static string Create(string root)
    {
        string directory = Path.Combine(root, "artifacts", "clang resource");
        Directory.CreateDirectory(Path.Combine(directory, "include"));
        foreach (string name in (string[])["stddef.h", "stdarg.h", "stdint.h"])
            File.WriteAllText(Path.Combine(directory, "include", name), "synthetic builtin " + name);
        return directory;
    }
}
