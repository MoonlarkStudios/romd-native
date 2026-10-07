using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Repository;

/// <summary>Offline checks for family identity and the closed CI and scoped provenance contracts.</summary>
internal static partial class RepositoryCheck
{
    internal static readonly FrozenDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["actions/checkout"] = "3d3c42e5aac5ba805825da76410c181273ba90b1",
        ["actions/setup-dotnet"] = "a98b56852c35b8e3190ac28c8c2271da59106c68",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> WorkflowKeys = new[]
    {
        "name", "on", "pull_request", "push", "branches", "tags", "permissions",
        "contents", "jobs", "foundation", "generation", "source", "preflight", "runs-on",
        "timeout-minutes", "env", "CI", "steps", "uses", "with",
        "persist-credentials", "submodules", "fetch-depth", "global-json-file", "run", "if",
        "workflow_dispatch", "inputs", "tag", "description", "required", "type",
        "EXPECTED_TAG",
    }.ToFrozenSet(StringComparer.Ordinal);

    private const string Engineering = "dotnet run --project eng/Moonlark.Native.Engineering -c Release --no-build -- ";

    internal const string DriftCheck = Engineering + "generate --check";

    internal static readonly FrozenSet<string> SourceCommands = new[]
    {
        "dotnet restore Moonlark.Native.slnx --locked-mode",
        "dotnet build Moonlark.Native.slnx -c Release --no-restore -warnaserror",
        "dotnet test tests/Moonlark.Native.Engineering.Tests -c Release --no-build --no-restore",
        "dotnet tool restore",
        Engineering + "repo check",
        Engineering + "repo check --tag \"$EXPECTED_TAG\"",
        DriftCheck,
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenSet<string> RequiredCiLines = new[]
    {
        "  push:",
        "    tags: ['libchdr-*', 'chdman-*']",
        "      - run: " + Engineering + "repo check --tag \"$EXPECTED_TAG\"",
        "        if: github.ref_type == 'tag'",
        "          EXPECTED_TAG: ${{ github.ref_name }}",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly SearchValues<char> PrintableAsciiOrLineFeed =
        SearchValues.Create([.. Enumerable.Range(0x20, 0x5F).Select(code => (char)code), '\n']);

    private static readonly FrozenSet<string> ContainerKeys = new[]
    {
        "on", "pull_request", "push", "jobs", "foundation", "generation", "source", "preflight",
        "env", "steps", "with", "workflow_dispatch", "inputs", "tag",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ApprovedFlowSequences =
        new[] { "branches: [main]", "tags: ['libchdr-*', 'chdman-*']" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, FrozenSet<string>> ExactValues = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["branches"] = ["[main]"],
        ["tags"] = ["['libchdr-*', 'chdman-*']"],
        ["runs-on"] = ["ubuntu-24.04", "macos-15"],
        ["timeout-minutes"] = ["5", "10", "15"],
        ["CI"] = ["true"],
        ["persist-credentials"] = ["false"],
        ["submodules"] = ["true"],
        ["fetch-depth"] = ["0"],
        ["global-json-file"] = ["global.json"],
        ["required"] = ["true"],
        ["type"] = ["string"],
    }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet(StringComparer.Ordinal), StringComparer.Ordinal);

    private static readonly FrozenSet<string> RequiredPermissionLines =
        new[] { "permissions:", "  contents: read" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ExpectedWorkflows =
        new[] { "ci.yml", "native-libchdr.yml", "native-chdman.yml", "release.yml" }.ToFrozenSet(StringComparer.Ordinal);

    internal static Failure? Run(string root, string? tag)
    {
        Result<LibchdrAuthority> authority = Authorities.ReadLibchdr(root);
        if (!authority.Succeeded) return authority.Failure;
        if (ValidateAssemblyVersions(authority.Value) is { } versions) return versions;
        using JsonDocument mame = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng/pins/mame.json")));
        if (ValidateMame(mame.RootElement) is { } mamePin) return mamePin;
        if (tag is not null && ValidateTag(tag, authority.Value.ManagedVersion, mame.RootElement) is { } tagFailure) return tagFailure;
        return ValidateWorkflows(Path.Combine(root, ".github/workflows"));
    }

    internal static Failure? ValidateTag(string tag, string version, JsonElement mame)
    {
        string[] expected = [$"libchdr-v{version}", $"chdman-{mame.GetProperty("version").GetString()}-r{mame.GetProperty("rebuildRevision").GetRawText()}"];
        return Check.That(expected.Contains(tag, StringComparer.Ordinal), $"Tag '{tag}' disagrees with version authorities");
    }

    private static Failure? ValidateAssemblyVersions(LibchdrAuthority authority)
    {
        string[] numbers = authority.ManagedVersion.Split('-', 2)[0].Split('+', 2)[0].Split('.');
        Result<string> assembly = Authorities.PropertyValue(authority.Props, "AssemblyVersion");
        if (!assembly.Succeeded) return assembly.Failure;
        if (Check.That(assembly.Value == $"{numbers[0]}.0.0.0", "AssemblyVersion must remain major.0.0.0") is { } major) return major;
        Result<string> file = Authorities.PropertyValue(authority.Props, "FileVersion");
        if (!file.Succeeded) return file.Failure;
        return Check.That(file.Value == $"{numbers[0]}.{numbers[1]}.{numbers[2]}.0", "FileVersion must carry the numeric family release");
    }

    private static Failure? ValidateMame(JsonElement mame)
    {
        string text(string name) => mame.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        bool positive(string name) => mame.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number) && number > 0;
        if (Check.That(Authorities.Sha1().IsMatch(text("commit")) && Authorities.Sha1().IsMatch(text("tagObject")),
            "MAME requires full tag/commit identities") is { } identities) return identities;
        if (Check.That(Authorities.Sha256().IsMatch(text("sourceSha256")), "MAME requires a source archive SHA256") is { } digest) return digest;
        if (Check.That(positive("sourceBytes"), "MAME requires a positive source archive byte length") is { } bytes) return bytes;
        if (Check.That(positive("rebuildRevision"), "chdman rebuild revision must be positive") is { } revision) return revision;
        if (Check.That(text("tag") == "mame" + text("version").Replace(".", "", StringComparison.Ordinal), "MAME tag/version disagree") is { } tag) return tag;
        if (Check.That(text("sourceUrl") == $"https://github.com/mamedev/mame/archive/refs/tags/{text("tag")}.tar.gz",
            "MAME source URL must match the pinned tag") is { } url) return url;
        string[] rids = mame.TryGetProperty("supportedRids", out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : [];
        return Check.That(Authorities.SameSet(rids, ["linux-x64", "linux-arm64", "osx-arm64"]), "chdman RID qualification contract changed");
    }

    internal static Failure? ValidateWorkflows(string directory)
    {
        string[] paths = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path) is ".yml" or ".yaml").Order(StringComparer.Ordinal).ToArray();
        if (Check.That(Authorities.SameSet(paths.Select(Path.GetFileName)!, ExpectedWorkflows),
            "Missing or unexpected foundation workflow") is { } inventory) return inventory;
        foreach (string path in paths)
            if (ValidateWorkflow(Path.GetFileName(path), File.ReadAllText(path)) is { } workflow) return workflow;
        HashSet<string> ci = ActiveLines(File.ReadAllText(Path.Combine(directory, "ci.yml")).Split('\n'));
        if (Check.That(RequiredCiLines.IsSubsetOf(ci),
            "CI must validate actual library/tool tag refs, including family-native tag rejection") is { } tags) return tags;
        // Bindings regenerate from the pinned headers; CI must refuse any commit whose bindings drifted.
        return Check.That(ci.Contains("      - run: " + DriftCheck), "CI must run the generation drift check");
    }

    /// <summary>
    /// A deliberately closed skeleton grammar, not a YAML parser: unrecognized forms fail instead of being ignored.
    /// Workflows are LF-terminated printable ASCII, so every YAML line break is one checked line; every key has an exact
    /// value rule; continuation lines and flow collections, which a YAML parser would fold into reviewed values, are refused.
    /// </summary>
    private static Failure? ValidateWorkflow(string name, string text)
    {
        int unsupported = text.AsSpan().IndexOfAnyExcept(PrintableAsciiOrLineFeed);
        if (unsupported >= 0)
        {
            int line = text.AsSpan(0, unsupported).Count('\n') + 1;
            return new Failure($"{name}:{line}: only LF-terminated printable ASCII is supported (found U+{(int)text[unsupported]:X4})");
        }
        // This pipeline's jobs, artifact transfer and runner/RID pairs are reviewed as one exact contract.
        // Keep the general source-workflow grammar closed; any native workflow edit requires updating this pin.
        if (name == "native-libchdr.yml")
            return Check.That(Digest.Sha256(Encoding.UTF8.GetBytes(text)) == "b34b1bef292ab08b2fed8421605d63e3c0b221cbfbb8ee5d04709231c65adbff",
                "native-libchdr.yml differs from the reviewed native evidence and Linux provenance workflow");
        string[] lines = text.Split('\n');
        (string Key, string Value, int Column)? previous = null;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            string at = $"{name}:{index + 1}";
            if (Expression().Replace(line, "").AsSpan().IndexOfAny('{', '}') >= 0) return new Failure(at + ": flow mappings are unsupported");
            Match entry = Entry().Match(line);
            if (!entry.Success) return new Failure(at + ": unsupported skeleton YAML form");
            int column = entry.Groups[1].Length + entry.Groups[2].Length;
            string key = entry.Groups[3].Value;
            string value = entry.Groups[4].Value;
            int comment = value.IndexOf(" #", StringComparison.Ordinal);
            value = (comment >= 0 ? value[..comment] : value).Trim();
            // A line indented beneath a scalar continues that scalar in YAML, smuggling text into a reviewed value.
            if (previous is { Value.Length: > 0 } scalar && column > scalar.Column)
                return new Failure(at + ": continuation lines are unsupported");
            if (!WorkflowKeys.Contains(key)) return new Failure($"{at}: unreviewed skeleton key {key}");
            if (value.Length > 0 && "&*!|>".Contains(value[0], StringComparison.Ordinal))
                return new Failure(at + ": YAML references/tags/block scalars are unsupported");
            if (value.Contains("secrets.", StringComparison.Ordinal)) return new Failure(at + ": source skeleton cannot access secrets");
            if (value.Contains("${{", StringComparison.Ordinal) && key != "EXPECTED_TAG")
                return new Failure(at + ": expressions are allowed only in the approved tag environment");
            if (value.AsSpan().IndexOfAny('[', ']') >= 0 && !ApprovedFlowSequences.Contains(key + ": " + value))
                return new Failure(at + ": flow sequences are unsupported");
            if (ValidateValue(name, at, key, value, column, previous) is { } invalid) return invalid;
            previous = (key, value, column);
        }
        if (Check.That(RequiredPermissionLines.IsSubsetOf(ActiveLines(lines)),
            name + ": require active read-only permissions") is { } permissions) return permissions;
        if (Check.That(!WritePermission().IsMatch(text), name + ": publishing permissions require approval") is { } write) return write;
        if (Check.That(!Publishing().IsMatch(text), name + ": publishing commands require approval") is { } publishing) return publishing;
        string[] uses = Uses().Matches(text).Select(match => match.Groups[1].Value).ToArray();
        if (Check.That(uses.Length > 0, name + ": expected pinned checkout") is { } checkout) return checkout;
        foreach (string action in uses)
            if (ValidateAction(name, action) is { } unreviewed) return unreviewed;
        return null;
    }

    private static Failure? ValidateValue(string name, string at, string key, string value, int column, (string Key, string Value, int Column)? previous) => key switch
    {
        _ when ContainerKeys.Contains(key) => Check.That(value.Length == 0, $"{at}: {key} must be a block mapping"),
        "name" or "description" => Check.That(value.Length > 0, $"{at}: {key} requires a value"),
        "permissions" => Check.That(value.Length == 0, name + ": permissions must use the explicit read-only mapping"),
        "contents" => Check.That(value == "read", name + ": contents permission must be read-only"),
        "run" => Check.That(SourceCommands.Contains(value), at + ": unreviewed source command"),
        "uses" => ValidateAction(name, value),
        // Tag routing belongs only to the tag-check step, so no job (such as the drift check) can be gated off pull requests.
        "if" => Check.That(value == "github.ref_type == 'tag'", name + ": unreviewed tag routing")
            ?? Check.That(previous is { Key: "run" } step && step.Value == Engineering + "repo check --tag \"$EXPECTED_TAG\"" && step.Column == column,
                at + ": tag routing is allowed only on the tag-check step"),
        "EXPECTED_TAG" => Check.That(value is "${{ inputs.tag }}" or "${{ github.ref_name }}",
            name + ": tag must come from the dispatch input or actual tag ref"),
        _ => Check.That(ExactValues.TryGetValue(key, out FrozenSet<string>? allowed) && allowed.Contains(value),
            $"{at}: unreviewed value for {key}: {value}"),
    };

    private static Failure? ValidateAction(string name, string action)
    {
        string[] parts = action.Split('@', 2);
        return Check.That(parts.Length == 2 && Actions.TryGetValue(parts[0], out string? sha) && parts[1] == sha, $"{name}: unreviewed action {action}");
    }

    private static HashSet<string> ActiveLines(IEnumerable<string> lines) =>
        lines.Where(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#')).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\$\{\{[^}\n]+\}\}")]
    private static partial Regex Expression();

    [GeneratedRegex(@"\A( *)(- )?([A-Za-z_][A-Za-z0-9_-]*):(?: (.*))?\z")]
    private static partial Regex Entry();

    [GeneratedRegex(@"^\s*[\w-]+:\s*write\s*$", RegexOptions.Multiline)]
    private static partial Regex WritePermission();

    [GeneratedRegex(@"\bnuget\b[^\n]*\bpush\b|\bgh\b[^\n]*\brelease\b|\bgit\b[^\n]*\bpush\b")]
    private static partial Regex Publishing();

    [GeneratedRegex(@"^\s*(?:-\s*)?uses:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex Uses();
}
