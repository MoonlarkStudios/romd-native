using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>Collects local evidence without granting release qualification.</summary>
internal static class QualificationCommand
{
    internal static int Linux(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--rid"], required: ["--rid"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        Result<JsonObject> result = LinuxEvidence.Run(context.Root, parsed.Value.Option("--rid")!, context.Environment, context.Out);
        return context.Report(result.Succeeded ? null : result.Failure, "local Linux evidence recorded; no release qualification");
    }
}
