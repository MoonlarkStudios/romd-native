using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Fixtures;
using Moonlark.Native.Engineering.Generation;
using Moonlark.Native.Engineering.Native;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>Builds the digest-pinned baseline container and validates a clean isolated checkout.</summary>
internal static class LinuxBuilder
{
    internal const string RecipeDirectory = "eng/qualification/linux";
    private sealed record Archive(string Url, string Digest, bool Sha512);
    private sealed record Toolchain(string Platform, Archive Sdk, Archive CMake);

    internal static Failure? Run(string root, string rid, IReadOnlyDictionary<string, string> environment, TextWriter log)
    {
        Result<Toolchain> selected = Select(rid);
        if (!selected.Succeeded) return selected.Failure;
        Result<string> output = ArtifactsPath.ValidateDirectory(Path.Combine(root, "artifacts", "qualification", rid), root);
        if (!output.Succeeded) return output.Failure;
        string directory = output.Value;
        Directory.CreateDirectory(directory);
        string receipt = Path.Combine(directory, "builder.json");
        if (ArtifactsPath.ValidateLogFile(receipt) is { } unsafeReceipt) return unsafeReceipt;
        File.Delete(receipt);
        Result<IReadOnlyDictionary<string, string>> guarded = BuildEnvironment.Create(environment);
        if (!guarded.Succeeded) return guarded.Failure;
        environment = LinuxEvidence.TestEnvironment(guarded.Value);
        Result<string> cachePath = ArtifactsPath.ValidateDirectory(Path.Combine(root, "artifacts", "qualification", rid, "nuget"), root);
        if (!cachePath.Succeeded) return cachePath.Failure;
        if (Check.That(OperatingSystem.IsLinux() && ((rid == "linux-arm64" && RuntimeInformation.OSArchitecture == Architecture.Arm64 && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            || (rid == "linux-x64" && RuntimeInformation.OSArchitecture == Architecture.X64 && RuntimeInformation.ProcessArchitecture == Architecture.X64)),
            "Container qualification requires the matching host and process architecture") is { } architecture) return architecture;
        Result<string> status = ProcessRunner.Run(["git", "status", "--porcelain", "--untracked-files=all"], environment, log, root);
        if (!status.Succeeded) return status.Failure;
        if (Check.That(status.Value.Length == 0, "Container qualification requires a clean source checkout") is { } dirty) return dirty;
        Result<string> revision = ProcessRunner.Run(["git", "rev-parse", "--verify", "HEAD"], environment, log, root);
        if (!revision.Succeeded) return revision.Failure;
        if (Check.That(Authorities.Sha1().IsMatch(revision.Value), "Invalid source revision") is { } commit) return commit;
        string fixtures = Path.Combine(root, "artifacts", "fixtures", "libchdr-final");
        Result<JsonObject> fixtureCheck = FixtureVerification.Verify(root, fixtures);
        if (!fixtureCheck.Succeeded) return fixtureCheck.Failure;
        Result<string> uid = ProcessRunner.Run(["id", "-u"], environment, log);
        Result<string> gid = ProcessRunner.Run(["id", "-g"], environment, log);
        if (!uid.Succeeded) return uid.Failure;
        if (!gid.Succeeded) return gid.Failure;
        if (Check.That(uid.Value.Length > 0 && uid.Value.All(char.IsAsciiDigit)
            && gid.Value.Length > 0 && gid.Value.All(char.IsAsciiDigit), "Cannot identify container file owner") is { } owner) return owner;
        string context = Path.Combine(directory, "context");
        if (Check.That(!Path.Exists(context) && !ArtifactsPath.IsLink(context), "Use a fresh builder context; preserve the earlier attempt explicitly") is { } contextLink) return contextLink;
        Directory.CreateDirectory(context);
        string recipe = Path.Combine(root, RecipeDirectory);
        File.Copy(Path.Combine(recipe, "Dockerfile"), Path.Combine(context, "Dockerfile"), overwrite: true);
        File.Copy(Path.Combine(recipe, rid + ".rpm.txt"), Path.Combine(context, "packages.txt"), overwrite: true);
        if (Download(selected.Value.Sdk, Path.Combine(context, "sdk.tar.gz"), environment, log) is { } sdk) return sdk;
        if (Download(selected.Value.CMake, Path.Combine(context, "cmake.tar.gz"), environment, log) is { } cmake) return cmake;
        File.WriteAllText(Path.Combine(context, "sdk.sha512"), selected.Value.Sdk.Digest + "  sdk.tar.gz\n");
        File.WriteAllText(Path.Combine(context, "cmake.sha256"), selected.Value.CMake.Digest + "  cmake.tar.gz\n");
        string imageFile = Path.Combine(context, "image.id");
        Result<string> build = ProcessRunner.Run(["docker", "build", "--platform", selected.Value.Platform, "--progress", "plain", "--iidfile", imageFile, context],
            environment, log, root, TimeSpan.FromMinutes(20));
        if (!build.Succeeded) return build.Failure;
        if (Check.That(ArtifactsPath.IsRegularFile(imageFile) && !ArtifactsPath.IsLink(imageFile), "Builder did not produce its image identity") is { } noImage) return noImage;
        string imageId = File.ReadAllText(imageFile).Trim();
        if (Check.That(imageId.StartsWith("sha256:", StringComparison.Ordinal) && Authorities.Sha256().IsMatch(imageId[7..]), "Invalid builder image identity") is { } id) return id;
        Result<string> packages = RunContainer(["--platform", selected.Value.Platform, imageId, "cat", "/opt/installed-rpms.txt"], directory, environment, log, TimeSpan.FromMinutes(2));
        if (!packages.Succeeded) return packages.Failure;
        Result<JsonObject> generator = Generate(root, rid, directory, imageId, selected.Value.Platform, uid.Value + ":" + gid.Value, environment, log);
        if (!generator.Succeeded) return generator.Failure;
        string checkout = Path.Combine(directory, "checkout");
        if (Check.That(!Path.Exists(checkout), "Use a fresh qualification directory; preserve or remove the earlier owned checkout explicitly") is { } exists) return exists;
        Result<string> clone = ProcessRunner.Run(["git", "clone", "--no-hardlinks", root, checkout], environment, log, root);
        if (!clone.Succeeded) return clone.Failure;
        Result<string> detached = ProcessRunner.Run(["git", "checkout", "--detach", revision.Value], environment, log, checkout);
        if (!detached.Succeeded) return detached.Failure;
        Result<string> submodule = ProcessRunner.Run(["git", "-c", "protocol.file.allow=always", "-c",
            "submodule.native/libchdr/upstream.url=" + Path.Combine(root, "native", "libchdr", "upstream"),
            "submodule", "update", "--init", "native/libchdr/upstream"], environment, log, checkout);
        if (!submodule.Succeeded) return submodule.Failure;
        string copiedFixtures = Path.Combine(checkout, "artifacts", "fixtures", "libchdr-final");
        Directory.CreateDirectory(copiedFixtures);
        foreach (string file in Directory.EnumerateFiles(fixtures))
        {
            if (Check.That(!ArtifactsPath.IsLink(file) && ArtifactsPath.IsRegularFile(file), "Cannot transfer a linked/nonregular fixture file") is { } fileKind) return fileKind;
            File.Copy(file, Path.Combine(copiedFixtures, Path.GetFileName(file)));
        }
        string cache = cachePath.Value;
        Directory.CreateDirectory(cache);
        string[] docker = ["--platform", selected.Value.Platform,
            "--user", uid.Value + ":" + gid.Value,
            "--env", "CI=true", "--env", "HOME=/tmp/moonlark-home", "--env", "DOTNET_CLI_HOME=/tmp/moonlark-home", "--env", "NUGET_PACKAGES=/nuget",
            "--mount", "type=bind,source=" + checkout + ",target=/repo",
            "--mount", "type=bind,source=" + cache + ",target=/nuget", imageId];
        string[][] commands =
        [
            ["dotnet", "restore", "Moonlark.Native.slnx", "--locked-mode"],
            ["dotnet", "build", "Moonlark.Native.slnx", "-c", "Release", "--no-restore", "-warnaserror"],
            ["dotnet", "run", "--project", "eng/Moonlark.Native.Engineering", "-c", "Release", "--no-build", "--", "qualification", "linux", "--rid", rid],
        ];
        foreach (string[] command in commands)
        {
            Result<string> run = RunContainer([.. docker, .. command], directory, environment, log, TimeSpan.FromMinutes(20));
            if (!run.Succeeded) return run.Failure;
        }
        var result = new JsonObject
        {
            ["schemaVersion"] = 1, ["qualification"] = "local-unqualified", ["attestation"] = null,
            ["rid"] = rid, ["sourceCommit"] = revision.Value, ["imageId"] = imageId, ["sdkSha512"] = selected.Value.Sdk.Digest,
            ["cmakeSha256"] = selected.Value.CMake.Digest, ["installedRpms"] = packages.Value,
            ["dockerfileSha256"] = Digest.Sha256File(Path.Combine(context, "Dockerfile")),
            ["rpmPinsSha256"] = Digest.Sha256File(Path.Combine(context, "packages.txt")),
            ["generatorResources"] = generator.Value,
        };
        File.WriteAllText(receipt, result.ToJsonString(new() { WriteIndented = true }) + "\n");
        return null;
    }

    internal static Result<JsonObject> Generate(string root, string rid, string directory, string image, string platform, string owner,
        IReadOnlyDictionary<string, string> environment, TextWriter log, Func<string[], Result<string>>? container = null, GeneratorTool? tool = null)
    {
        root = ArtifactsPath.Resolve(root);
        container ??= arguments => RunContainer(arguments, directory, environment, log, TimeSpan.FromMinutes(2));
        Result<string> compiler = container(["--platform", platform, image, "clang", "--version"]);
        if (!compiler.Succeeded) return compiler.Failure;
        if (Check.That(compiler.Value.Contains("clang version 21.1.8 ", StringComparison.Ordinal), "Expected the pinned Clang 21.1.8 resource headers") is { } version) return version;
        Result<string> resource = container(["--platform", platform, image, "clang", "-print-resource-dir"]);
        if (!resource.Succeeded) return resource.Failure;
        if (Check.That(Path.IsPathFullyQualified(resource.Value) && !resource.Value.Contains('\n') && !resource.Value.Contains('\r'), "Compiler resource path must be absolute") is { } source) return source;
        Result<string> output = ArtifactsPath.ValidateDirectory(Path.Combine(root, "artifacts", "qualification", rid, "clang-resource"), root);
        if (!output.Succeeded) return output.Failure;
        if (Check.That(!Path.Exists(output.Value), "Preserve the previous Clang resource export before retrying") is { } existing) return existing;
        Directory.CreateDirectory(output.Value);
        Result<string> copy = container(["--platform", platform, "--user", owner,
            "--mount", "type=bind,source=" + output.Value + ",target=/resources", image,
            "cp", "-R", "--", resource.Value + "/include", "/resources/include"]);
        if (!copy.Succeeded) return copy.Failure;
        Result<JsonObject> headers = ClangResources.Verify(root, output.Value);
        if (!headers.Succeeded) return headers.Failure;
        Result<GenerationResult> generated = BindingGenerator.Run(root, "dotnet", true, environment, tool ?? BindingGenerator.PinnedTool,
            (_, _) => ClangResources.Arguments(output.Value));
        if (!generated.Succeeded) return generated.Failure;
        Result<JsonObject> after = ClangResources.Verify(root, output.Value);
        if (!after.Succeeded) return after.Failure;
        if (Check.That(JsonFields.SameCanonical(headers.Value, after.Value), "Clang resource headers changed during generation") is { } changed) return changed;
        return new JsonObject
        {
            ["compiler"] = compiler.Value, ["sourceDirectory"] = resource.Value,
            ["exportDirectory"] = Path.GetRelativePath(root, output.Value), ["headerSha256"] = headers.Value,
            ["generatorCommand"] = JsonFields.Array(generated.Value.Command),
        };
    }

    /// <summary>Removes only this invocation's uniquely named container, including when its attached client times out.</summary>
    internal static Result<string> RunContainer(string[] arguments, string directory,
        IReadOnlyDictionary<string, string> environment, TextWriter log, TimeSpan timeout)
    {
        string name = "moonlark-qualification-" + Guid.NewGuid().ToString("N");
        string cid = Path.Combine(directory, name + ".cid");
        Result<string> run;
        Result<string> cleanup;
        try
        {
            run = ProcessRunner.Run(["docker", "run", "--cidfile", cid, "--name", name, .. arguments], environment, log, directory, timeout);
        }
        finally
        {
            // A Docker client is not the daemon-side process. The unique name also covers a timeout before cidfile creation.
            cleanup = ProcessRunner.Run(["docker", "rm", "--force", name], environment, log, directory, TimeSpan.FromSeconds(30));
        }
        return !cleanup.Succeeded ? new Failure((run.Succeeded ? "" : run.Failure.Message + "\n") + "Container cleanup failed: " + cleanup.Failure.Message) : run;
    }

    private static Failure? Download(Archive archive, string path, IReadOnlyDictionary<string, string> environment, TextWriter log)
    {
        Result<string> download = ProcessRunner.Run(["curl", "--fail", "--location", "--proto", "=https", "--proto-redir", "=https", "--retry", "3",
            "--max-time", "600", "--output", path, archive.Url], environment, log, timeout: TimeSpan.FromMinutes(12));
        return download.Succeeded ? VerifyArchive(path, archive.Digest, archive.Sha512) : download.Failure;
    }

    internal static Failure? VerifyArchive(string path, string digest, bool sha512)
    {
        if (Check.That(File.Exists(path) && !ArtifactsPath.IsLink(path), "Archive must be a regular file") is { } missing) return missing;
        using FileStream input = File.OpenRead(path);
        string actual = Convert.ToHexStringLower(sha512 ? SHA512.HashData(input) : SHA256.HashData(input));
        return Check.That(actual == digest, "Downloaded archive digest mismatch");
    }

    private static Result<Toolchain> Select(string rid) => rid switch
    {
        "linux-arm64" => new Toolchain("linux/arm64",
            new Archive("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.302/dotnet-sdk-10.0.302-linux-arm64.tar.gz",
                "9e409c14e00686d661c78fa4dd9ad0e4dcf695c328bd5ff777d05b4a9c34b42cf89b12573b92e9fb2f565dbe12016b4835f77c7d9a42b55a7494df21634cd5d6", true),
            new Archive("https://github.com/Kitware/CMake/releases/download/v3.31.12/cmake-3.31.12-linux-aarch64.tar.gz",
                "83f8fd91d2038a56556e1400390fcfe42f79602940c494f6c6f1cdae7f9e7f40", false)),
        "linux-x64" => new Toolchain("linux/amd64",
            new Archive("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.302/dotnet-sdk-10.0.302-linux-x64.tar.gz",
                "10069bec8783596484a610332f090d562802a41b9b40e3327a5a5688b572e10c296ae300f940d40461f23c157ed1b0843c2f8e6b3f20d8d8d9d83432d8143bac", true),
            new Archive("https://github.com/Kitware/CMake/releases/download/v3.31.12/cmake-3.31.12-linux-x86_64.tar.gz",
                "0dc2e9a6860f06bf10bd8fadc03e35d9eeb4df46e33763a7e480e987758f385c", false)),
        _ => new Failure("Only linux-x64 and linux-arm64 builders are supported"),
    };
}
