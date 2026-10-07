using System.Text.Json;
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

    private static List<CaseMeasurement> Valid() => BenchmarkGate.Fixtures.SelectMany(fixture =>
        BenchmarkGate.Operations.SelectMany(operation => new[]
        {
            new CaseMeasurement(fixture, operation, "Raw", 1000, 10, 0, 3),
            new CaseMeasurement(fixture, operation, "Safe", 1200, 10, 0, 3),
        })).ToList();
}
