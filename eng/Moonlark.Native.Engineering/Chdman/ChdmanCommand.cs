using System.Globalization;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Chdman;

internal static class ChdmanCommand
{
    internal const string DefaultOutput = "artifacts/tools/chdman/osx-arm64";
    internal const int DefaultJobs = 4;

    /// <summary><c>chdman build --archive PATH --log PATH [--output PATH] [--jobs N]</c>: builds only the pinned chdman and prints its receipt.</summary>
    internal static int Build(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--archive", "--log", "--output", "--jobs"], required: ["--archive", "--log"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        string? jobsText = parsed.Value.Option("--jobs");
        int jobs = DefaultJobs;
        if (jobsText is not null && !int.TryParse(jobsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out jobs))
            return context.Report(new Failure("Jobs must be an integer: " + jobsText), "");
        var request = new ChdmanBuildRequest(Path.GetFullPath(parsed.Value.Option("--archive")!),
            Path.GetFullPath(parsed.Value.Option("--output") ?? Path.Combine(context.Root, DefaultOutput)),
            jobs, Path.GetFullPath(parsed.Value.Option("--log")!));
        Result<JsonObject> receipt = ChdmanBuild.Run(context.Root, context.Environment, request);
        if (!receipt.Succeeded) return context.Report(receipt.Failure, "");
        context.Out.Write(ReceiptJson.Serialize(receipt.Value));
        return context.Report(null, "pinned chdman built with receipt " + Path.Combine(request.Output, ChdmanBuild.ManifestName));
    }
}
