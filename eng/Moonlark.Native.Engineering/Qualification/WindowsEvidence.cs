using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>Manual Windows qualification evidence; no signing or release qualification is granted.</summary>
internal static class WindowsEvidence
{
    internal static Result<JsonObject> Run(string root, IReadOnlyDictionary<string, string> environment, TextWriter? log) =>
        NativeEvidence.Run(root, "win-x64", environment, log);

    internal static Result<IReadOnlyDictionary<string, string>> TestEnvironment(IReadOnlyDictionary<string, string> environment) =>
        NativeEvidence.TestEnvironment(environment, windows: true);
}
