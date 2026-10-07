using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Repository;

internal static class RepositoryCommand
{
    /// <summary><c>repo check [--tag TAG]</c>: validates a prospective tag but never creates one.</summary>
    internal static int Check(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--tag"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        return context.Report(RepositoryCheck.Run(context.Root, parsed.Value.Option("--tag")),
            "family versions, upstream identities, safety/RID contract and scoped CI contracts");
    }
}
