using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Chdman;

internal static class ChdmanCommand
{
    internal static int Build(IReadOnlyList<string> arguments, CommandContext context) =>
        context.Report(new Failure("Not implemented yet"), "");
}
