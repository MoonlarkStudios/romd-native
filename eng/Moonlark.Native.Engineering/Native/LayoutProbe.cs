using System.Text;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Native;

/// <summary>Inputs for measuring the pinned ABI with the probe CMake built from the same compiler and flags.</summary>
internal sealed record ProbeRun(string Root, string Rid, string Output, string BuildDirectory, LibchdrPin Pin, CompilerIdentity Compiler,
    string ProgramSha256, IReadOnlyDictionary<string, string> Environment);

/// <summary>Runs the layout probe target and produces the receipt InteropLayoutTests reads.</summary>
internal static class LayoutProbe
{
    internal const string ProgramPath = "tests/native/libchdr_layout.c";
    internal const string ReceiptName = "layout-probe.json";
    internal const string Target = "moonlark_layout_probe";

    internal static string BinaryName(string rid) => rid == "win-x64" ? "layout-probe.exe" : "layout-probe";

    /// <summary>All supported compiler families provide the required C11 probe features.</summary>
    internal static bool IsBuiltBy(CompilerIdentity compiler) => compiler.Id is "MSVC" or "GNU" or "Clang" or "AppleClang";

    /// <summary>Removes the old receipt and probe binary, refusing a redirected probe binary.</summary>
    internal static Failure? Invalidate(string output, string rid)
    {
        string binary = Path.Combine(output, BinaryName(rid));
        if (ArtifactsPath.IsLink(binary)) return new Failure("Probe binary must not be a symlink");
        NativeOutput.DeleteFile(Path.Combine(output, ReceiptName));
        NativeOutput.DeleteFile(binary);
        return null;
    }

    internal static Result<JsonObject> Measure(ProbeRun probe, TextWriter? log)
    {
        string binary = Path.Combine(probe.Output, BinaryName(probe.Rid));
        if (Check.That(File.Exists(binary) && !ArtifactsPath.IsLink(binary), "Layout probe binary was not built") is { } missing) return missing;
        Result<string> version = CompilerVersion(probe.Compiler, probe.Environment, log);
        if (!version.Succeeded) return version.Failure;
        Result<string> target = CompilerTarget(probe.Compiler, probe.Rid, probe.Environment, log);
        if (!target.Succeeded) return target.Failure;
        string program = Path.Combine([probe.Root, .. ProgramPath.Split('/')]);
        Result<string> command = CompileCommand(probe.BuildDirectory, program);
        if (!command.Succeeded) return command.Failure;
        ProcessOutput run = ProcessRunner.Execute([binary], probe.Environment, log, timeout: TimeSpan.FromSeconds(120));
        if (Check.That(run.ExitCode == 0 && !run.TimedOut, "Command failed: " + binary) is { } failed) return failed;
        Result<JsonObject> measured = JsonFields.ParseObject(Encoding.UTF8.GetBytes(run.Stdout), "Layout probe output");
        if (!measured.Succeeded) return measured.Failure;
        if (ValidateMeasurements(measured.Value, probe.Rid) is { } invalid) return invalid;
        if (Check.That(Digest.Sha256File(program) == probe.ProgramSha256, "Probe source changed during compilation/execution") is { } changed) return changed;
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["rid"] = probe.Rid,
            ["upstreamCommit"] = probe.Pin.Commit,
            ["headers"] = probe.Pin.Document["headers"]?.DeepClone(),
            ["compiler"] = version.Value,
            ["compilerTarget"] = target.Value,
            ["compilerId"] = probe.Compiler.Id,
            ["compilerVersion"] = probe.Compiler.Version,
            ["compilerIdentitySource"] = "CMake toolchains reply",
            ["compilerTargetSource"] = probe.Compiler.Id == "MSVC" ? "validated probe platform/architecture macros" : "compiler -dumpmachine",
            ["compileCommand"] = command.Value,
            ["programSha256"] = probe.ProgramSha256,
            ["binarySha256"] = Digest.Sha256File(binary),
            ["measurements"] = measured.Value,
        };
    }

    /// <summary>The probe's own platform and architecture macros must describe this native host.</summary>
    internal static Failure? ValidateMeasurements(JsonObject measured, string rid)
    {
        Result<long> schema = JsonFields.RequireInteger(measured, "schemaVersion");
        if (Check.That(schema.Succeeded && schema.Value == 1, "Unexpected probe schema") is { } unexpected) return unexpected;
        var platform = new JsonObject { ["apple"] = rid == "osx-arm64", ["linux"] = rid.StartsWith("linux-", StringComparison.Ordinal), ["windows"] = rid == "win-x64" };
        if (Check.That(JsonFields.SameCanonical(measured["platformMacros"], platform), "Probe target differs from the native host") is { } host) return host;
        var architecture = new JsonObject { ["arm64"] = rid.EndsWith("arm64", StringComparison.Ordinal), ["x64"] = rid.EndsWith("x64", StringComparison.Ordinal) };
        if (Check.That(JsonFields.SameCanonical(measured["architectureMacros"], architecture), "Probe architecture differs from the native host") is { } arch) return arch;
        return Check.That(measured["primitives"] is JsonObject primitives && primitives["chd_error"] is JsonObject error
            && error["isSigned"] is JsonValue signedness && signedness.TryGetValue<bool>(out _), "Probe lacks measured chd_error signedness");
    }

    /// <summary>MSVC identity is already measured by CMake; cl does not support --version.</summary>
    internal static Result<string> CompilerVersion(CompilerIdentity compiler, IReadOnlyDictionary<string, string> environment, TextWriter? log) =>
        compiler.Id == "MSVC" ? $"MSVC {compiler.Version} (CMake toolchains reply)" : ProcessRunner.Run([compiler.Path, "--version"], environment, log);

    /// <summary>MSVC has no -dumpmachine: report the RID checked against the executed probe's macros.</summary>
    internal static Result<string> CompilerTarget(CompilerIdentity compiler, string validatedRid, IReadOnlyDictionary<string, string> environment, TextWriter? log) =>
        compiler.Id == "MSVC" ? validatedRid : ProcessRunner.Run([compiler.Path, "-dumpmachine"], environment, log);

    /// <summary>The exact compile command the build system used for the probe, from its exported compilation database.</summary>
    internal static Result<string> CompileCommand(string buildDirectory, string program)
    {
        string database = Path.Combine(buildDirectory, "compile_commands.json");
        if (!File.Exists(database)) return new Failure("Layout probe compile command was not exported");
        JsonNode? entries = JsonNode.Parse(File.ReadAllBytes(database));
        string expected = Path.GetFullPath(program);
        string[] commands = entries is JsonArray array
            ? [.. array.OfType<JsonObject>()
                .Where(entry => JsonFields.RequireString(entry, "file") is { Succeeded: true } file && Path.GetFullPath(file.Value) == expected)
                .Select(entry => JsonFields.RequireString(entry, "command"))
                .Where(command => command.Succeeded).Select(command => command.Value)]
            : [];
        return commands is [{ } only] ? only : new Failure("Layout probe compile command was not exported exactly once");
    }
}
