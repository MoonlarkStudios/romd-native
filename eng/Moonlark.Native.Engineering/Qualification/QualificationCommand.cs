using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Chdman;
using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Fixtures;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>Read-only qualification jobs; no release, attestation or remote repository writes.</summary>
internal static class QualificationCommand
{
    internal static int Linux(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--rid"], required: ["--rid"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        Result<JsonObject> result = LinuxEvidence.Run(context.Root, parsed.Value.Option("--rid")!, context.Environment, context.Out);
        return context.Report(result.Succeeded ? null : result.Failure, "local Linux evidence recorded; no release qualification");
    }

    internal static int Subjects(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--source-commit"], required: ["--source-commit"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        Result<string> directory = ArtifactsPath.ValidateDirectory(Path.Combine(context.Root, "artifacts", "signing"), context.Root);
        if (!directory.Succeeded) return context.Report(directory.Failure, "");
        string output = Path.Combine(directory.Value, "subjects.sha256");
        if (ArtifactsPath.ValidateLogFile(output) is { } invalid) return context.Report(invalid, "");
        File.Delete(output);
        Result<string> subjects = AttestationSubjects.Create(context.Root, parsed.Value.Option("--source-commit")!);
        if (!subjects.Succeeded) return context.Report(subjects.Failure, "");
        File.WriteAllText(output, subjects.Value);
        return context.Report(null, "eight verified subject checksums written; no attestation created");
    }

    internal static int Container(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--rid"], required: ["--rid"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        string rid = parsed.Value.Option("--rid")!;
        if (rid is not ("linux-x64" or "linux-arm64")) return context.Report(new Failure("Unsupported Linux RID"), "");
        Result<string> directory = ArtifactsPath.ValidateDirectory(Path.Combine(context.Root, "artifacts", "qualification", rid), context.Root);
        if (!directory.Succeeded) return context.Report(directory.Failure, "");
        Directory.CreateDirectory(directory.Value);
        string path = Path.Combine(directory.Value, "container.log");
        if (ArtifactsPath.ValidateLogFile(path) is { } invalid) return context.Report(invalid, "");
        context.Out.WriteLine("Container log: " + path);
        using var log = new StreamWriter(path, append: true);
        Failure? failure = LinuxBuilder.Run(context.Root, rid, context.Environment, log);
        return context.Report(failure, "Linux container evidence completed; no attestation or publication");
    }

    internal static int Fixtures(IReadOnlyList<string> arguments, CommandContext context)
    {
        if (arguments.Count != 0) return context.Report(new Failure("qualification fixtures accepts no arguments"), "");
        Result<MamePin> pin = MamePin.Read(context.Root);
        if (!pin.Succeeded) return context.Report(pin.Failure, "");
        Result<IReadOnlyDictionary<string, string>> guarded = BuildEnvironment.Create(context.Environment);
        if (!guarded.Succeeded) return context.Report(guarded.Failure, "");
        IReadOnlyDictionary<string, string> environment = LinuxEvidence.TestEnvironment(guarded.Value);
        Result<string> output = ArtifactsPath.ValidateDirectory(Path.Combine(context.Root, "artifacts", "qualification", "fixtures"), context.Root);
        if (!output.Succeeded) return context.Report(output.Failure, "");
        string directory = output.Value;
        foreach (string leaf in (string[])["prepare.log", "mame.tar.gz", "chdman-build.log", "generate.log"])
            if (ArtifactsPath.ValidateLogFile(Path.Combine(directory, leaf)) is { } invalid) return context.Report(invalid, "");
        Directory.CreateDirectory(directory);
        string archive = Path.Combine(directory, "mame.tar.gz");
        string url = MamePin.Repository + "/archive/refs/tags/" + pin.Value.Tag + ".tar.gz";
        using var log = new StreamWriter(Path.Combine(directory, "prepare.log"), append: true);
        Result<string> download = ProcessRunner.Run(["curl", "--fail", "--location", "--proto", "=https", "--proto-redir", "=https",
            "--retry", "3", "--max-time", "600", "--output", archive, url], environment, log, context.Root, TimeSpan.FromMinutes(12));
        if (!download.Succeeded) return context.Report(download.Failure, "");
        var buildRequest = new ChdmanBuildRequest(archive, Path.Combine(context.Root, ChdmanCommand.DefaultOutput), 4,
            Path.Combine(directory, "chdman-build.log"));
        Result<JsonObject> tool = ChdmanBuild.Run(context.Root, environment, buildRequest);
        if (!tool.Succeeded) return context.Report(tool.Failure, "");
        string fixtures = Path.Combine(context.Root, "artifacts", "fixtures", "libchdr-final");
        var generateRequest = new FixtureRequest(Path.Combine(buildRequest.Output, "native", "chdman"),
            Path.Combine(buildRequest.Output, ChdmanBuild.ManifestName), fixtures, Path.Combine(directory, "generate.log"));
        Result<JsonObject> generated = FixtureGenerator.Run(context.Root, environment, generateRequest);
        if (!generated.Succeeded) return context.Report(generated.Failure, "");
        Result<JsonObject> verified = FixtureVerification.Verify(context.Root, fixtures);
        return context.Report(verified.Succeeded ? null : verified.Failure, "synthetic fixtures generated and verified with the pinned chdman");
    }
}
