using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>The existing Linux-only command and builder environment contract.</summary>
internal static class LinuxEvidence
{
    internal static Result<JsonObject> Run(string root, string rid, IReadOnlyDictionary<string, string> environment, TextWriter? log) =>
        rid is "linux-x64" or "linux-arm64" ? NativeEvidence.Run(root, rid, environment, log)
            : new Failure("Linux evidence requires linux-x64 or linux-arm64");

    internal static IReadOnlyDictionary<string, string> TestEnvironment(IReadOnlyDictionary<string, string> environment) =>
        NativeEvidence.TestEnvironment(environment).Value;
}
