using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moonlark.Libchdr.Benchmarks;

internal sealed record CaseMeasurement(string Fixture, string Operation, string Method, double MeanNanoseconds,
    double Confidence95HalfWidthNanoseconds, double? AllocatedBytesPerOperation, int Launches);

internal enum GateOutcome { Passed, Failed, Inconclusive }
internal sealed record GateResult(GateOutcome Outcome, string[] Diagnostics);

internal static class BenchmarkGate
{
    internal static readonly string[] Fixtures = ["dvd-lzma", "dvd-zlib", "dvd-huff", "dvd-flac", "dvd-zstd", "dvd-none", "cd-cdlz"];
    internal static readonly string[] Operations = ["Hunk", "Hit", "Miss", "Cross"];
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static GateResult EvaluateJson(string json)
    {
        try { return Evaluate(JsonSerializer.Deserialize<CaseMeasurement[]>(json, JsonOptions)); }
        catch (JsonException error) { return new(GateOutcome.Failed, ["Invalid measurement JSON: " + error.Message]); }
    }

    internal static GateResult Evaluate(IReadOnlyList<CaseMeasurement>? measurements)
    {
        if (measurements is null) return new(GateOutcome.Failed, ["Missing measurement matrix"]);
        var rows = new Dictionary<(string Fixture, string Operation, string Method), CaseMeasurement>();
        var failures = new List<string>();
        var noisy = new List<string>();
        foreach (CaseMeasurement row in measurements)
        {
            if (row is null || !Fixtures.Contains(row.Fixture, StringComparer.Ordinal) || !Operations.Contains(row.Operation, StringComparer.Ordinal)
                || row.Method is not ("Raw" or "Safe"))
            {
                failures.Add("Unknown or missing case identity");
                continue;
            }
            string name = row.Fixture + "/" + row.Operation + "/" + row.Method;
            if (!rows.TryAdd((row.Fixture, row.Operation, row.Method), row)) failures.Add("Duplicate case: " + name);
            if (!double.IsFinite(row.MeanNanoseconds) || row.MeanNanoseconds <= 0
                || !double.IsFinite(row.Confidence95HalfWidthNanoseconds) || row.Confidence95HalfWidthNanoseconds < 0
                || row.AllocatedBytesPerOperation is not 0 || row.Launches != 3)
                failures.Add("Invalid metrics, allocation or launch count: " + name);
            else if (row.Confidence95HalfWidthNanoseconds / row.MeanNanoseconds > 0.05)
                noisy.Add("95% CI half-width exceeds 5%: " + name);
        }
        foreach (string fixture in Fixtures)
            foreach (string operation in Operations)
                foreach (string method in new[] { "Raw", "Safe" })
                    if (!rows.ContainsKey((fixture, operation, method))) failures.Add($"Missing case: {fixture}/{operation}/{method}");
        if (failures.Count != 0) return new(GateOutcome.Failed, failures.ToArray());
        if (noisy.Count != 0) return new(GateOutcome.Inconclusive, noisy.ToArray());
        foreach (string fixture in Fixtures)
            foreach (string operation in Operations)
            {
                double limit = 1.25 * rows[(fixture, operation, "Raw")].MeanNanoseconds + 250;
                if (!double.IsFinite(limit) || rows[(fixture, operation, "Safe")].MeanNanoseconds > limit)
                    failures.Add($"Predeclared 1.25x + 250ns budget exceeded: {fixture}/{operation}");
            }
        return new(failures.Count == 0 ? GateOutcome.Passed : GateOutcome.Failed, failures.ToArray());
    }
}
