using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Chdman;
using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Fixtures;

internal static class FixturesCommand
{
    internal const string DefaultOutput = "artifacts/fixtures/libchdr";

    /// <summary><c>fixtures generate --chdman PATH --manifest PATH --log PATH [--output PATH]</c>: generates synthetic CHDs and prints their manifest.</summary>
    internal static int Generate(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--chdman", "--manifest", "--log", "--output"], required: ["--chdman", "--manifest", "--log"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        var request = new FixtureRequest(Path.GetFullPath(parsed.Value.Option("--chdman")!), Path.GetFullPath(parsed.Value.Option("--manifest")!),
            Path.GetFullPath(parsed.Value.Option("--output") ?? Path.Combine(context.Root, DefaultOutput)), Path.GetFullPath(parsed.Value.Option("--log")!));
        Result<JsonObject> manifest = FixtureGenerator.Run(context.Root, context.Environment, request);
        if (!manifest.Succeeded) return context.Report(manifest.Failure, "");
        context.Out.Write(ReceiptJson.Serialize(manifest.Value));
        return context.Report(null, "synthetic fixtures generated with manifest " + Path.Combine(request.Output, FixtureGenerator.ManifestName));
    }
}
