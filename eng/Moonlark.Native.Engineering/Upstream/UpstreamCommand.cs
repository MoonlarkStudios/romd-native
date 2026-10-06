using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Upstream;

internal static class UpstreamCommand
{
    internal static int Update(IReadOnlyList<string> arguments, CommandContext context) =>
        context.Report(new Failure("Not implemented yet"), "");
}
