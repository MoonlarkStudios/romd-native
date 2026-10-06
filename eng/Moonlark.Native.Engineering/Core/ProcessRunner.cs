using System.Diagnostics;
using System.Globalization;

namespace Moonlark.Native.Engineering.Core;

/// <summary>Captured process output. <see cref="Combined"/> mirrors a stderr-into-stdout merge.</summary>
internal sealed record ProcessOutput(int ExitCode, string Stdout, string Stderr, bool TimedOut)
{
    internal string Combined => Stdout + Stderr;
}

/// <summary>Runs tools with an explicit, complete environment and appends evidence to an optional log.</summary>
internal static class ProcessRunner
{
    internal static ProcessOutput Execute(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment,
        TextWriter? log = null, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        log?.Write("$ " + Display(command) + "\n");
        log?.Flush();
        var start = new ProcessStartInfo(command[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (string argument in command.Skip(1)) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach ((string name, string value) in environment) start.Environment[name] = value;
        using Process process = Process.Start(start) ?? throw new IOException("Could not start " + command[0]);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(timeout ?? Timeout.InfiniteTimeSpan);
        if (!exited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        var output = new ProcessOutput(exited ? process.ExitCode : -1, stdout.Result, stderr.Result, !exited);
        log?.Write(output.Combined + (exited ? "" : "\ntimeout") + "\nexit=" + output.ExitCode.ToString(CultureInfo.InvariantCulture) + "\n");
        log?.Flush();
        return output;
    }

    /// <summary>Runs a command that must succeed and returns its trimmed combined output.</summary>
    internal static Result<string> Run(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment,
        TextWriter? log = null, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        ProcessOutput output = Execute(command, environment, log, workingDirectory, timeout);
        if (output.TimedOut) return new Failure("Command timed out: " + Display(command));
        if (output.ExitCode != 0)
            return new Failure($"Command failed ({output.ExitCode.ToString(CultureInfo.InvariantCulture)}): {Display(command)}\n{output.Combined}");
        return output.Combined.Trim();
    }

    /// <summary>POSIX-shell quoting for evidence logs only; commands are never executed through a shell.</summary>
    internal static string Display(IReadOnlyList<string> command) => string.Join(' ', command.Select(Quote));

    private static string Quote(string argument) =>
        argument.Length > 0 && argument.All(character => char.IsAsciiLetterOrDigit(character) || "@%+=:,./-_".Contains(character, StringComparison.Ordinal))
            ? argument
            : "'" + argument.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
