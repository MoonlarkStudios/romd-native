using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Packaging;

namespace Moonlark.Native.Engineering.Cli;

/// <summary>Local candidate construction and isolated consumption; no release or publication.</summary>
internal static class PackagingCommand
{
    internal static int Candidate(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--rids", "--output"], required: ["--rids", "--output"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        string[] rids = parsed.Value.Option("--rids")!.Split(',');
        if (rids.Any(rid => !NativeRids.IsSupported(rid)) || rids.Distinct(StringComparer.Ordinal).Count() != rids.Length)
            return context.Report(new Failure("RID list must contain unique supported exact RID names"), "");
        string[] manifests = rids.Select(rid => Path.Combine(context.Root, "artifacts", "native", "libchdr", rid, "build-manifest.json")).ToArray();
        string output = Path.GetFullPath(parsed.Value.Option("--output")!, context.Root);
        var result = PackageCandidate.Run(context.Root, new CandidateOptions(output, manifests), context.Environment, context.Out);
        return context.Report(result.Succeeded ? null : result.Failure, "local unqualified candidate packages recorded; no publication or release qualification");
    }

    internal static int Consumer(IReadOnlyList<string> arguments, CommandContext context)
    {
        Result<ParsedArguments> parsed = Arguments.Parse(arguments, ["--candidate", "--output"], required: ["--candidate", "--output"]);
        if (!parsed.Succeeded) return context.Report(parsed.Failure, "");
        string receipt = Path.GetFullPath(parsed.Value.Option("--candidate")!, context.Root);
        string output = Path.GetFullPath(parsed.Value.Option("--output")!, context.Root);
        string fixtures = Path.Combine(context.Root, "artifacts", "fixtures", "libchdr-final");
        var result = CandidateConsumer.Run(context.Root, new ConsumerOptions(receipt, output, fixtures), context.Environment, context.Out);
        return context.Report(result.Succeeded ? null : result.Failure, "clean local package consumer evidence recorded; no release qualification");
    }
}
