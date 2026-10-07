using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Packaging;

/// <summary>Fresh candidate outputs and regular source inputs; never replaces an existing output.</summary>
internal static class PackagePaths
{
    internal static Failure? RegularFile(string path, string root)
    {
        string full = Path.GetFullPath(path);
        string boundary = Path.GetFullPath(root);
        if (!ArtifactsPath.IsWithin(full, boundary)) return new Failure("Package input escapes its source directory");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (ArtifactsPath.IsLink(current)) return new Failure("Package input contains a symlink: " + current);
            if (current == boundary) break;
        }
        return Check.That(ArtifactsPath.IsRegularFile(full), "Package input must be a regular file: " + full);
    }

    internal static Result<string> NewArtifactsDirectory(string root, string output)
    {
        Result<string> validated = ArtifactsPath.ValidateDirectory(output, root);
        if (!validated.Succeeded) return validated.Failure;
        return Path.Exists(validated.Value) ? new Failure("Candidate output already exists; choose a new directory") : validated.Value;
    }

    internal static Result<string> NewExternalDirectory(string root, string output)
    {
        string full = Path.GetFullPath(output);
        if (ArtifactsPath.IsWithin(ArtifactsPath.Resolve(full), ArtifactsPath.Resolve(root))) return new Failure("Consumer must be outside the repository");
        if (Path.Exists(full) || ArtifactsPath.IsLink(full)) return new Failure("Consumer output must be new");
        for (string? current = Path.GetDirectoryName(full); current is not null; current = Path.GetDirectoryName(current))
            if (ArtifactsPath.IsLink(current) || File.Exists(current)) return new Failure("Consumer output ancestors must be unredirected directories");
        return full;
    }
}
