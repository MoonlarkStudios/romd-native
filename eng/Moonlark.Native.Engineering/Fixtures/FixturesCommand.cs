using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Fixtures;

internal static class FixturesCommand
{
    internal static int Generate(IReadOnlyList<string> arguments, CommandContext context) =>
        context.Report(new Failure("Not implemented yet"), "");
}
