using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Checks assembly boundaries independently of native availability.</summary>
public sealed class AssemblyContractTests
{
    private static readonly Assembly Library = Assembly.Load("Moonlark.Libchdr");

    /// <summary>The generated C layer must never enter the supported public API.</summary>
    [Fact]
    public void RawInteropIsNotPublic() =>
        Assert.DoesNotContain(Library.GetExportedTypes(), type =>
            type.Namespace?.StartsWith("Moonlark.Libchdr.Interop", StringComparison.Ordinal) == true);

    /// <summary>Runtime marshalling must remain disabled for the blittable boundary.</summary>
    [Fact]
    public void RuntimeMarshallingIsDisabled() =>
        Assert.NotNull(Library.GetCustomAttribute<DisableRuntimeMarshallingAttribute>());

    /// <summary>Version metadata must identify the actual untagged upstream pin.</summary>
    [Fact]
    public void UpstreamIdentityIsExplicit()
    {
        var metadata = Library.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        Assert.Equal("607694ca0812edfc9cc2030c64634fc2393668de", metadata["UpstreamCommit"]);
        Assert.Equal("v0.3.0-116-g607694c", metadata["UpstreamVersion"]);
        Assert.Equal("Family", metadata["NativeVersionMode"]);
        Assert.Equal(new Version(1, 0, 0, 0), Library.GetName().Version);
    }
}
