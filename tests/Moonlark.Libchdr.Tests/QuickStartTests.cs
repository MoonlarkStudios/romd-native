using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Qualifies that the package README's quick start is exactly the code the compiled sample runs.</summary>
/// <remarks>It compares text only, so it runs without native assets.</remarks>
public sealed class QuickStartTests
{
    /// <summary>The README's C# block is the sample's only namespace import, a blank line and its marked quick-start
    /// statements. The sample disables implicit usings, so building it proves the block compiles as pasted.</summary>
    [Fact]
    public void ReadmeQuickStartIsTheCompiledSampleCode()
    {
        string root = TestRepository.Root;
        string sampleDirectory = Path.Combine(root, "samples", "Moonlark.Libchdr.QuickStart");
        string[] readme = File.ReadAllLines(Path.Combine(root, "src", "Moonlark.Libchdr", "README.md"));
        string[] sample = File.ReadAllLines(Path.Combine(sampleDirectory, "Program.cs"));
        string[] documented = Between(readme, "```csharp", "```");
        // The engineering standard asks for a five-line quick start: one import and four statements.
        Assert.Equal(6, documented.Length);
        Assert.Equal(["using Moonlark.Libchdr;"], Directives(sample));
        Assert.Equal("using Moonlark.Libchdr;", documented[0]);
        Assert.Equal(sample[0], documented[0]);
        Assert.Equal("", documented[1]);
        Assert.Equal(documented[2..], Between(sample, "// quick-start:begin", "// quick-start:end"));
        Assert.Contains("<ImplicitUsings>disable</ImplicitUsings>",
            File.ReadAllText(Path.Combine(sampleDirectory, "Moonlark.Libchdr.QuickStart.csproj")));
    }

    /// <summary>The using directives, which must precede the first top-level statement.</summary>
    private static string[] Directives(string[] lines)
    {
        int end = 0;
        while (end < lines.Length && (lines[end].Length == 0 || lines[end].StartsWith("using ", StringComparison.Ordinal) ||
            lines[end].StartsWith("//", StringComparison.Ordinal))) end++;
        return Array.FindAll(lines[..end], line => line.StartsWith("using ", StringComparison.Ordinal));
    }

    private static string[] Between(string[] lines, string begin, string end)
    {
        int start = Array.IndexOf(lines, begin);
        Assert.True(start >= 0, $"missing '{begin}'");
        int stop = Array.IndexOf(lines, end, start + 1);
        Assert.True(stop > start, $"missing '{end}' after '{begin}'");
        return lines[(start + 1)..stop];
    }
}
