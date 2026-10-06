using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Native;

internal static class NativeCommand
{
    internal static int Build(IReadOnlyList<string> arguments, CommandContext context) =>
        context.Report(new Failure("Not implemented yet"), "");

    internal static int Verify(IReadOnlyList<string> arguments, CommandContext context) =>
        context.Report(new Failure("Not implemented yet"), "");
}
