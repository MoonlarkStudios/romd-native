using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Moonlark.Libchdr.Internal;
using Moonlark.Libchdr.Interop;
using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Compares generated layouts with an executed probe of the authentic pinned C headers.</summary>
public sealed unsafe class InteropLayoutTests
{
    /// <summary>Every structure and field retains the C size, alignment and offset on this native platform.</summary>
    [Fact]
    public void AllGeneratedStructureFieldsMatchActualNativeLayout()
    {
        using JsonDocument receipt = ReadReceipt();
        JsonElement structures = receipt.RootElement.GetProperty("measurements").GetProperty("structures");
        Assert.Equal(5, structures.EnumerateObject().Count());
        CheckStructure<chd_header>(structures.GetProperty("chd_header"));
        CheckStructure<core_file_callbacks>(structures.GetProperty("core_file_callbacks"));
        CheckStructure<core_file_callbacks_and_argp>(structures.GetProperty("core_file_callbacks_and_argp"));
        CheckStructure<core_file>(structures.GetProperty("core_file"));
        CheckStructure<_chd_verify_result>(structures.GetProperty("chd_verify_result"));
    }

    /// <summary>Inline arrays occupy their full C array extent, including all codec and digest elements.</summary>
    [Fact]
    public void InlineArraysMatchCompleteNativeArrayStorage()
    {
        using JsonDocument receipt = ReadReceipt();
        JsonElement structures = receipt.RootElement.GetProperty("measurements").GetProperty("structures");
        JsonElement header = structures.GetProperty("chd_header").GetProperty("fields");
        CheckArray<chd_header._compression_e__FixedBuffer>(header.GetProperty("compression"), 4, sizeof(uint));
        CheckArray<chd_header._md5_e__FixedBuffer>(header.GetProperty("md5"), 16, sizeof(byte));
        CheckArray<chd_header._parentmd5_e__FixedBuffer>(header.GetProperty("parentmd5"), 16, sizeof(byte));
        CheckArray<chd_header._sha1_e__FixedBuffer>(header.GetProperty("sha1"), 20, sizeof(byte));
        CheckArray<chd_header._rawsha1_e__FixedBuffer>(header.GetProperty("rawsha1"), 20, sizeof(byte));
        CheckArray<chd_header._parentsha1_e__FixedBuffer>(header.GetProperty("parentsha1"), 20, sizeof(byte));
        JsonElement verify = structures.GetProperty("chd_verify_result").GetProperty("fields");
        CheckArray<_chd_verify_result._md5_e__FixedBuffer>(verify.GetProperty("md5"), 16, sizeof(byte));
        CheckArray<_chd_verify_result._sha1_e__FixedBuffer>(verify.GetProperty("sha1"), 20, sizeof(byte));
        CheckArray<_chd_verify_result._rawsha1_e__FixedBuffer>(verify.GetProperty("rawsha1"), 20, sizeof(byte));
        CheckArray<_chd_verify_result._metasha1_e__FixedBuffer>(verify.GetProperty("metasha1"), 20, sizeof(byte));
    }

    /// <summary>Native integer widths and all 28 error values match the generated and safe contracts.</summary>
    [Fact]
    public void NativeIntegerWidthsAndEveryErrorValueMatch()
    {
        using JsonDocument receipt = ReadReceipt();
        JsonElement measurements = receipt.RootElement.GetProperty("measurements");
        JsonElement primitives = measurements.GetProperty("primitives");
        CheckPrimitive<uint>(primitives.GetProperty("uint32_t"));
        CheckPrimitive<ulong>(primitives.GetProperty("uint64_t"));
        CheckPrimitive<long>(primitives.GetProperty("int64_t"));
        CheckPrimitive<nuint>(primitives.GetProperty("size_t"));
        CheckPrimitive<int>(primitives.GetProperty("int"));
        CheckPrimitive<nint>(primitives.GetProperty("pointer"));
        CheckPrimitive<chd_error>(primitives.GetProperty("chd_error"));
        Assert.Equal(sizeof(ChdError), primitives.GetProperty("chd_error").GetProperty("size").GetInt32());
        JsonElement errors = measurements.GetProperty("errors");
        chd_error[] generated = Enum.GetValues<chd_error>();
        ChdError[] safe = Enum.GetValues<ChdError>();
        Assert.Equal(28, errors.EnumerateObject().Count());
        Assert.Equal(28, generated.Length);
        Assert.Equal(28, safe.Length);
        foreach (chd_error value in generated)
            Assert.Equal(errors.GetProperty(value.ToString()).GetUInt32(), (uint)value);
        Assert.Equal(generated.Select(value => (uint)value), safe.Select(value => checked((uint)value)));
    }

    /// <summary>Both callback tables use explicit Cdecl function pointers and exact native parameter types.</summary>
    [Fact]
    public void CallbackSignaturesUseCdeclAndNativeWidths()
    {
        CheckCallback(typeof(core_file_callbacks), "fsize", typeof(ulong), typeof(void*));
        CheckCallback(typeof(core_file_callbacks), "fread", typeof(nuint), typeof(void*), typeof(nuint), typeof(nuint), typeof(void*));
        CheckCallback(typeof(core_file_callbacks), "fclose", typeof(int), typeof(void*));
        CheckCallback(typeof(core_file_callbacks), "fseek", typeof(int), typeof(void*), typeof(long), typeof(int));
        CheckCallback(typeof(core_file), "fsize", typeof(ulong), typeof(core_file*));
        CheckCallback(typeof(core_file), "fread", typeof(nuint), typeof(void*), typeof(nuint), typeof(nuint), typeof(core_file*));
        CheckCallback(typeof(core_file), "fclose", typeof(int), typeof(core_file*));
        CheckCallback(typeof(core_file), "fseek", typeof(int), typeof(core_file*), typeof(long), typeof(int));
    }

    private static JsonDocument ReadReceipt()
    {
        string root = NativeTestEnvironment.Root;
        (string rid, _) = NativePlatform.Current();
        string directory = Path.Combine(root, "artifacts", "native", "libchdr", rid);
        JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "layout-probe.json")));
        using JsonDocument pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng", "pins", "libchdr.json")));
        Assert.Equal(1, receipt.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(rid, receipt.RootElement.GetProperty("rid").GetString());
        Assert.Equal(pin.RootElement.GetProperty("commit").GetString(), receipt.RootElement.GetProperty("upstreamCommit").GetString());
        foreach (JsonProperty header in pin.RootElement.GetProperty("headers").EnumerateObject())
            Assert.Equal(header.Value.GetString(), receipt.RootElement.GetProperty("headers").GetProperty(header.Name).GetString());
        string binary = Path.Combine(directory, OperatingSystem.IsWindows() ? "layout-probe.exe" : "layout-probe");
        Assert.Equal(receipt.RootElement.GetProperty("binarySha256").GetString(), Digest(binary));
        Assert.Equal(receipt.RootElement.GetProperty("programSha256").GetString(), Digest(Path.Combine(root, "tests", "native", "libchdr_layout.c")));
        JsonElement macros = receipt.RootElement.GetProperty("measurements").GetProperty("platformMacros");
        Assert.Equal(OperatingSystem.IsMacOS(), macros.GetProperty("apple").GetBoolean());
        Assert.Equal(OperatingSystem.IsLinux(), macros.GetProperty("linux").GetBoolean());
        Assert.Equal(OperatingSystem.IsWindows(), macros.GetProperty("windows").GetBoolean());
        JsonElement architecture = receipt.RootElement.GetProperty("measurements").GetProperty("architectureMacros");
        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm64, architecture.GetProperty("arm64").GetBoolean());
        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.X64, architecture.GetProperty("x64").GetBoolean());
        return receipt;
    }

    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CheckStructure<T>(JsonElement native) where T : unmanaged
    {
        CheckPrimitive<T>(native);
        JsonElement fields = native.GetProperty("fields");
        FieldInfo[] managed = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(fields.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal),
            managed.Select(field => field.Name).Order(StringComparer.Ordinal));
        foreach (FieldInfo field in managed)
        {
            JsonElement measured = fields.GetProperty(field.Name);
            Assert.Equal(measured.GetProperty("offset").GetInt64(), Marshal.OffsetOf<T>(field.Name).ToInt64());
            Assert.Equal(measured.GetProperty("size").GetInt32(), FieldSize(field.FieldType));
            Assert.Equal(measured.GetProperty("alignment").GetInt32(), FieldAlignment(field.FieldType));
        }
    }

    private static void CheckPrimitive<T>(JsonElement native) where T : unmanaged
    {
        Assert.Equal(native.GetProperty("size").GetInt32(), sizeof(T));
        Assert.Equal(native.GetProperty("alignment").GetInt32(), AlignmentOf<T>());
    }

    private static void CheckArray<T>(JsonElement native, int count, int elementSize) where T : unmanaged
    {
        Assert.Equal(count, typeof(T).GetCustomAttribute<InlineArrayAttribute>()!.Length);
        Assert.Equal(checked(count * elementSize), sizeof(T));
        CheckPrimitive<T>(native);
    }

    private static int FieldSize(Type type) => type switch
    {
        _ when type.IsPointer || type.IsFunctionPointer => sizeof(nint),
        _ when type == typeof(uint) => sizeof(uint),
        _ when type == typeof(ulong) => sizeof(ulong),
        _ when type == typeof(byte) => sizeof(byte),
        _ when type.GetCustomAttribute<InlineArrayAttribute>() is { } array => checked(array.Length * FieldSize(type.GetFields()[0].FieldType)),
        _ => throw new InvalidDataException($"Unexpected generated field type: {type}."),
    };

    private static int FieldAlignment(Type type) => type switch
    {
        _ when type.IsPointer || type.IsFunctionPointer => AlignmentOf<nint>(),
        _ when type == typeof(uint) => AlignmentOf<uint>(),
        _ when type == typeof(ulong) => AlignmentOf<ulong>(),
        _ when type == typeof(byte) => AlignmentOf<byte>(),
        _ when type.GetCustomAttribute<InlineArrayAttribute>() is not null => FieldAlignment(type.GetFields()[0].FieldType),
        _ => throw new InvalidDataException($"Unexpected generated field type: {type}."),
    };

    private static int AlignmentOf<T>() where T : unmanaged
    {
        var probe = new AlignmentProbe<T> { Prefix = 0, Value = default };
        return checked((int)Unsafe.ByteOffset(ref probe.Prefix, ref Unsafe.As<T, byte>(ref probe.Value)));
    }

    private static void CheckCallback(Type owner, string name, Type result, params Type[] parameters)
    {
        FieldInfo field = owner.GetField(name)!;
        Type pointer = field.FieldType;
        Assert.True(pointer.IsFunctionPointer);
        Assert.True(pointer.IsUnmanagedFunctionPointer);
        // Calling-convention modifiers exist only on the modified type; type identity uses the unmodified one.
        Assert.Equal([typeof(CallConvCdecl)], field.GetModifiedFieldType().GetFunctionPointerCallingConventions());
        Assert.Equal(result, pointer.GetFunctionPointerReturnType());
        Assert.Equal(parameters, pointer.GetFunctionPointerParameterTypes());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T> where T : unmanaged
    {
        public byte Prefix;
        public T Value;
    }
}
