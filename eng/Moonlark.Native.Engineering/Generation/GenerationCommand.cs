using System.Globalization;
using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Generation;

internal static class GenerationCommand
{
    /// <summary><c>generate [--check] [--dotnet PATH]</c>: regenerates the tracked bindings and contract, or with --check only compares them.</summary>
    internal static int Generate(IReadOnlyList<string> arguments, CommandContext context) =>
        Generate(arguments, context, BindingGenerator.PinnedTool, BindingGenerator.HostPlatformArguments);

    internal static int Generate(IReadOnlyList<string> arguments, CommandContext context, GeneratorTool tool, PlatformArguments platform)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--dotnet"], ["--check"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        bool check = parsed.Value.Flag("--check");
        Result<GenerationResult> result = BindingGenerator.Run(context.Root, parsed.Value.Option("--dotnet") ?? "dotnet", check,
            context.Environment, tool, platform);
        if (!result.Succeeded) return context.Report(result.Failure, "");
        return context.Report(null, result.Value.ExportCount.ToString(CultureInfo.InvariantCulture)
            + " internal imports from the exact pinned headers; tracked bindings and contract " + (check ? "match" : "written"));
    }
}
