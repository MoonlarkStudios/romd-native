using System.Text.Json;
using BenchmarkDotNet.Toolchains.Results;
using System.Text.Json.Nodes;
using Moonlark.Libchdr.Benchmarks;
using BenchmarkProgram = Moonlark.Libchdr.Benchmarks.Program;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Benchmarks;

/// <summary>Performance qualification fails closed on incomplete evidence, regressions and allocations; noise remains inconclusive.</summary>
public sealed class BenchmarkGateTests
{
    /// <summary>A complete deterministic matrix with small uncertainty and zero allocation meets the predeclared budget.</summary>
    [Fact]
    public void CompleteMatrixPasses() => Assert.Equal(GateOutcome.Passed, BenchmarkGate.Evaluate(Valid()).Outcome);

    /// <summary>A single malformed row invalidates the matrix even if every other case passes.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("fixture")]
    [InlineData("operation")]
    [InlineData("method")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("zero-mean")]
    [InlineData("negative-mean")]
    [InlineData("negative-uncertainty")]
    [InlineData("nan-uncertainty")]
    [InlineData("negative-allocation")]
    [InlineData("nan-allocation")]
    [InlineData("launches")]
    [InlineData("allocation")]
    [InlineData("baseline-allocation")]
    [InlineData("regression")]
    [InlineData("overflow-baseline")]
    public void RejectsInvalidEvidence(string change)
    {
        List<CaseMeasurement> rows = Valid();
        CaseMeasurement row = rows[1];
        switch (change)
        {
            case "missing": rows.RemoveAt(1); break;
            case "duplicate": rows.Add(row); break;
            case "fixture": rows[1] = row with { Fixture = "other" }; break;
            case "operation": rows[1] = row with { Operation = "other" }; break;
            case "method": rows[1] = row with { Method = "other" }; break;
            case "nan": rows[1] = row with { MeanNanoseconds = double.NaN }; break;
            case "infinity": rows[1] = row with { MeanNanoseconds = double.PositiveInfinity }; break;
            case "zero-mean": rows[1] = row with { MeanNanoseconds = 0 }; break;
            case "negative-mean": rows[1] = row with { MeanNanoseconds = -1 }; break;
            case "negative-uncertainty": rows[1] = row with { Confidence95HalfWidthNanoseconds = -1 }; break;
            case "nan-uncertainty": rows[1] = row with { Confidence95HalfWidthNanoseconds = double.NaN }; break;
            case "negative-allocation": rows[1] = row with { AllocatedBytesPerOperation = -1 }; break;
            case "nan-allocation": rows[1] = row with { AllocatedBytesPerOperation = double.NaN }; break;
            case "launches": rows[1] = row with { Launches = 2 }; break;
            case "allocation": rows[1] = row with { AllocatedBytesPerOperation = 1 }; break;
            case "baseline-allocation": rows[0] = rows[0] with { AllocatedBytesPerOperation = 1 }; break;
            case "regression": rows[1] = row with { MeanNanoseconds = 1500.01 }; break;
            case "overflow-baseline": rows[0] = rows[0] with { MeanNanoseconds = double.MaxValue }; break;
            default: throw new InvalidOperationException(change);
        }
        Assert.Equal(GateOutcome.Failed, BenchmarkGate.Evaluate(rows).Outcome);
    }

    /// <summary>Both paired means must be precise; an uncertain run is not promoted to pass.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExcessiveUncertaintyIsInconclusive(int index)
    {
        List<CaseMeasurement> rows = Valid();
        rows[index] = rows[index] with { Confidence95HalfWidthNanoseconds = rows[index].MeanNanoseconds * 0.05001 };
        Assert.Equal(GateOutcome.Inconclusive, BenchmarkGate.Evaluate(rows).Outcome);
    }

    /// <summary>The declared limits are inclusive and do not shift after measurements.</summary>
    [Fact]
    public void ExactBudgetAndNoiseLimitPass()
    {
        List<CaseMeasurement> rows = Valid();
        rows[0] = rows[0] with { Confidence95HalfWidthNanoseconds = 50 };
        rows[1] = rows[1] with { MeanNanoseconds = 1500, Confidence95HalfWidthNanoseconds = 75 };
        Assert.Equal(GateOutcome.Passed, BenchmarkGate.Evaluate(rows).Outcome);
    }

    /// <summary>Absent constructor fields must not silently turn uncertainty or allocation into zero.</summary>
    [Theory]
    [InlineData("Fixture")]
    [InlineData("Operation")]
    [InlineData("Method")]
    [InlineData("MeanNanoseconds")]
    [InlineData("Confidence95HalfWidthNanoseconds")]
    [InlineData("AllocatedBytesPerOperation")]
    [InlineData("Launches")]
    public void RejectsEveryMissingJsonField(string field)
    {
        JsonArray matrix = JsonNode.Parse(JsonSerializer.Serialize(Valid(), BenchmarkGate.JsonOptions))!.AsArray();
        Assert.True(matrix[1]!.AsObject().Remove(field));
        Assert.Equal(GateOutcome.Failed, BenchmarkGate.EvaluateJson(matrix.ToJsonString()).Outcome);
    }

    /// <summary>Malformed, absent, incomplete and unknown JSON cannot become qualification evidence.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"unknown\":1}]")]
    public void RejectsMalformedJson(string json) => Assert.Equal(GateOutcome.Failed, BenchmarkGate.EvaluateJson(json).Outcome);

    /// <summary>Noise does not establish a regression or a pass, while an allocation is independently disqualifying.</summary>
    [Fact]
    public void NoiseRemainsInconclusiveUnlessIndependentMetricFails()
    {
        List<CaseMeasurement> rows = Valid();
        rows[1] = rows[1] with { MeanNanoseconds = 2000, Confidence95HalfWidthNanoseconds = 200 };
        Assert.Equal(GateOutcome.Inconclusive, BenchmarkGate.Evaluate(rows).Outcome);
        rows[1] = rows[1] with { AllocatedBytesPerOperation = 0.01 };
        Assert.Equal(GateOutcome.Failed, BenchmarkGate.Evaluate(rows).Outcome);
    }

    /// <summary>Unavailable allocation counters must remain explicit, serializable failure evidence.</summary>
    [Theory]
    [InlineData(null, 100L)]
    [InlineData(0L, 0L)]
    [InlineData(1L, -1L)]
    public void MissingAllocationCanBePersistedAsFailedEvidence(long? allocated, long operations)
    {
        List<CaseMeasurement> rows = Valid();
        rows[1] = rows[1] with { AllocatedBytesPerOperation = BenchmarkProgram.AllocationPerOperation(allocated, operations) };
        string json = JsonSerializer.Serialize(rows, BenchmarkGate.JsonOptions);
        Assert.Null(JsonNode.Parse(json)![1]!["AllocatedBytesPerOperation"]);
        Assert.Equal(GateOutcome.Failed, BenchmarkGate.EvaluateJson(json).Outcome);
    }

    /// <summary>The actual ARM 32-byte window must fail regardless of which launch reports it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryLaunchAllocationSurvivesSerialization(int positive)
    {
        ExecuteResult[] launches = Enumerable.Range(0, 3).Select(index => Launch(index + 1, index == positive ? 32 : 0)).ToArray();
        double? allocation = BenchmarkProgram.AllocationForLaunches(launches, out BenchmarkProgram.LaunchGcEvidence[] evidence);
        Assert.Equal(3, evidence.Length);
        Assert.Equal(32, evidence[positive].AllocatedBytes);
        Assert.Equal(8388608, evidence[positive].Operations);
        Assert.Equal([positive + 1], evidence[positive].LaunchIndices);
        Assert.Equal("// GC:  0 0 0 32 8388608", Assert.Single(evidence[positive].RawGcLines));
        Assert.Equal(32.0 / 8388608, allocation);
        List<CaseMeasurement> rows = Valid();
        rows[1] = rows[1] with { AllocatedBytesPerOperation = allocation };
        string json = JsonSerializer.Serialize(rows, BenchmarkGate.JsonOptions);
        Assert.Equal(32.0 / 8388608, (double?)JsonNode.Parse(json)![1]!["AllocatedBytesPerOperation"]);
        Assert.Equal(GateOutcome.Failed, BenchmarkGate.EvaluateJson(json).Outcome);
    }

    /// <summary>All three exact-zero GC windows are required to prove zero allocation.</summary>
    [Fact]
    public void ThreeZeroAllocationWindowsPass() => Assert.Equal(0, BenchmarkProgram.AllocationForLaunches([Launch(1), Launch(2), Launch(3)], out _));

    /// <summary>Missing, duplicated or invalid launch windows cannot be replaced by the final launch's valid zero.</summary>
    [Theory]
    [InlineData("missing-launch")]
    [InlineData("extra-launch")]
    [InlineData("duplicate-launch")]
    [InlineData("unknown-launch")]
    [InlineData("missing-result")]
    [InlineData("failed-process")]
    [InlineData("missing-gc")]
    [InlineData("duplicate-gc")]
    [InlineData("missing-counter")]
    [InlineData("negative-counter")]
    [InlineData("negative-gc")]
    [InlineData("zero-operations")]
    [InlineData("extra-counter")]
    public void InvalidLaunchWindowFails(string change)
    {
        List<ExecuteResult> launches = [Launch(1), Launch(2), Launch(3)];
        switch (change)
        {
            case "missing-launch": launches.RemoveAt(0); break;
            case "extra-launch": launches.Insert(0, Launch(4)); break;
            case "duplicate-launch": launches[0] = Launch(2); break;
            case "unknown-launch": launches[0] = Launch(0); break;
            case "missing-result": launches[0] = Launch(1, results: []); break;
            case "failed-process": launches[0] = Launch(1, exitCode: 1); break;
            case "missing-gc": launches[0] = Launch(1, gc: []); break;
            case "duplicate-gc": launches[0] = Launch(1, gc: ["// GC:  0 0 0 0 8388608", "// GC:  0 0 0 0 8388608"]); break;
            case "missing-counter": launches[0] = Launch(1, gc: ["// GC:  0 0 0 ? 8388608"]); break;
            case "negative-counter": launches[0] = Launch(1, gc: ["// GC:  0 0 0 -1 8388608"]); break;
            case "negative-gc": launches[0] = Launch(1, gc: ["// GC:  -1 0 0 0 8388608"]); break;
            case "zero-operations": launches[0] = Launch(1, gc: ["// GC:  0 0 0 0 0"]); break;
            case "extra-counter": launches[0] = Launch(1, gc: ["// GC:  0 0 0 0 8388608 extra"]); break;
            default: throw new InvalidOperationException(change);
        }
        double? allocation = BenchmarkProgram.AllocationForLaunches(launches, out _);
        Assert.Null(allocation);
        List<CaseMeasurement> rows = Valid(); rows[1] = rows[1] with { AllocatedBytesPerOperation = allocation };
        Assert.Equal(GateOutcome.Failed, BenchmarkGate.EvaluateJson(JsonSerializer.Serialize(rows, BenchmarkGate.JsonOptions)).Outcome);
    }

    /// <summary>The pinned BDN execution parser itself rejects overflow and nonfinite GC counters before projection.</summary>
    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("NaN")]
    public void InvalidGcCannotBecomeExecutionResult(string counter) =>
        Assert.Throws<NotSupportedException>(() => Launch(1, gc: ["// GC:  0 0 0 " + counter + " 8388608"]));

    private static ExecuteResult Launch(int index, long allocated = 0, string[]? gc = null, string[]? results = null, int exitCode = 0)
    {
        gc ??= ["// GC:  0 0 0 " + allocated + " 8388608"];
        results ??= ["WorkloadResult   1: 8388608 op, 8388608.00 ns, 1.0000 ns/op"];
        return new ExecuteResult(true, exitCode, 1000 + index, results, gc, [.. results, .. gc], index);
    }

    private static List<CaseMeasurement> Valid() => BenchmarkGate.Fixtures.SelectMany(fixture =>
        BenchmarkGate.Operations.SelectMany(operation => new[]
        {
            new CaseMeasurement(fixture, operation, "Raw", 1000, 10, 0, 3),
            new CaseMeasurement(fixture, operation, "Safe", 1200, 10, 0, 3),
        })).ToList();
}
