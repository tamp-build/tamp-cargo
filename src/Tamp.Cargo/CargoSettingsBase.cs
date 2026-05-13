namespace Tamp.Cargo;

/// <summary>
/// Common knobs shared by every <c>cargo</c> verb's settings class. Mirrors the shape used by
/// <c>Tamp.Yarn.V4</c> and <c>Tamp.Npm.V10</c> — working directory + environment overlay +
/// the toolchain-scope knobs cargo wants on most commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Working directory matters.</b> Cargo resolves the crate or workspace it operates on from
/// the current directory upward. Set <see cref="WorkingDirectory"/> to your crate or workspace
/// root explicitly — the runner's own cwd is the wrong default for monorepo cases.
/// </para>
/// <para>
/// <b>Target triple matters for cross-compile.</b> Builds that produce binaries consumed by a
/// downstream packaging step (Tauri externalBin, MSIX sidecar, etc.) typically require a
/// specific target like <c>x86_64-pc-windows-msvc</c>. Set via <see cref="Target"/> per call;
/// don't rely on the host default if any downstream artifact contract depends on the suffix.
/// </para>
/// </remarks>
public abstract class CargoSettingsBase
{
    /// <summary>Working directory for the spawned <c>cargo</c> process. Typically the crate or workspace root.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables on top of the inherited environment.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Override the path to <c>Cargo.toml</c> (<c>--manifest-path</c>). Optional; cwd-resolution covers most cases.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Target triple (<c>--target</c>) — e.g. <c>x86_64-pc-windows-msvc</c>, <c>aarch64-unknown-linux-musl</c>. Optional; defaults to host.</summary>
    public string? Target { get; set; }

    /// <summary>Verbosity flag(s). 0 = default, 1 = <c>-v</c>, 2 = <c>-vv</c>.</summary>
    public int Verbosity { get; set; }

    /// <summary>Quiet mode (<c>--quiet</c>) — suppress non-error output.</summary>
    public bool Quiet { get; set; }

    /// <summary>Force colored output even when not a TTY (<c>--color always</c>). Useful in CI logs that render ANSI.</summary>
    public bool ForceColor { get; set; }

    /// <summary>Use frozen lockfile (<c>--frozen</c>). Implies <c>--locked</c> + <c>--offline</c>. CI-recommended for reproducible builds.</summary>
    public bool Frozen { get; set; }

    /// <summary>Refuse to update Cargo.lock (<c>--locked</c>). Lighter than <c>--frozen</c>; allows network.</summary>
    public bool Locked { get; set; }

    /// <summary>Run cargo in offline mode (<c>--offline</c>) — fails fast on registry lookups.</summary>
    public bool Offline { get; set; }

    /// <summary>Subclasses produce the per-verb argument list. The <c>cargo</c> verb (and any
    /// pre-verb global flags) come from this method; common flags are appended by the base.</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    /// <summary>Subclasses extend the secret list (e.g. <c>cargo publish</c>'s registry token). Default empty.</summary>
    protected virtual IEnumerable<Secret> CollectSecrets() => Array.Empty<Secret>();

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        var args = new List<string>();
        args.AddRange(BuildVerbArguments());

        // Common knob emission. We emit AFTER the verb because cargo wants subcommand-scoped
        // flags positioned after the verb (e.g. `cargo build --release`, not `cargo --release build`).
        if (!string.IsNullOrEmpty(ManifestPath)) { args.Add("--manifest-path"); args.Add(ManifestPath!); }
        if (!string.IsNullOrEmpty(Target)) { args.Add("--target"); args.Add(Target!); }
        if (Verbosity >= 2) args.Add("-vv");
        else if (Verbosity == 1) args.Add("-v");
        if (Quiet) args.Add("--quiet");
        if (ForceColor) { args.Add("--color"); args.Add("always"); }
        if (Frozen) args.Add("--frozen");
        if (Locked) args.Add("--locked");
        if (Offline) args.Add("--offline");

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = CollectSecrets().ToList(),
        };
    }
}

/// <summary>Fluent setters for the common knobs.</summary>
public static class CargoSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : CargoSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : CargoSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetManifestPath<T>(this T s, string? path) where T : CargoSettingsBase { s.ManifestPath = path; return s; }
    public static T SetTarget<T>(this T s, string? triple) where T : CargoSettingsBase { s.Target = triple; return s; }
    public static T SetVerbosity<T>(this T s, int level) where T : CargoSettingsBase { s.Verbosity = level; return s; }
    public static T SetQuiet<T>(this T s, bool v = true) where T : CargoSettingsBase { s.Quiet = v; return s; }
    public static T SetForceColor<T>(this T s, bool v = true) where T : CargoSettingsBase { s.ForceColor = v; return s; }
    public static T SetFrozen<T>(this T s, bool v = true) where T : CargoSettingsBase { s.Frozen = v; return s; }
    public static T SetLocked<T>(this T s, bool v = true) where T : CargoSettingsBase { s.Locked = v; return s; }
    public static T SetOffline<T>(this T s, bool v = true) where T : CargoSettingsBase { s.Offline = v; return s; }
}

/// <summary>
/// Convenience base for verbs that share the workspace-selection / feature-flag set:
/// build, test, check, run, clippy, doc, bench. The selector flags emit BEFORE the
/// common base flags so they bind to the right subcommand.
/// </summary>
public abstract class CargoBuildLikeSettingsBase : CargoSettingsBase
{
    /// <summary>Build the release profile (<c>--release</c>). Mutually exclusive with <see cref="Profile"/>.</summary>
    public bool Release { get; set; }

    /// <summary>Explicit profile name (<c>--profile</c>). Use for custom profiles defined in Cargo.toml. Mutually exclusive with <see cref="Release"/>.</summary>
    public string? Profile { get; set; }

    /// <summary>Select all workspace members (<c>--workspace</c>).</summary>
    public bool Workspace { get; set; }

    /// <summary>Specific workspace package(s) (<c>-p &lt;name&gt;</c>). Accumulates.</summary>
    public List<string> Packages { get; } = new();

    /// <summary>Exclude workspace members when <see cref="Workspace"/> is set (<c>--exclude &lt;name&gt;</c>).</summary>
    public List<string> Excludes { get; } = new();

    /// <summary>Feature flags to enable (<c>--features</c>, comma-joined).</summary>
    public List<string> Features { get; } = new();

    /// <summary>Enable all features (<c>--all-features</c>). Mutually exclusive with <see cref="NoDefaultFeatures"/>.</summary>
    public bool AllFeatures { get; set; }

    /// <summary>Disable default features (<c>--no-default-features</c>).</summary>
    public bool NoDefaultFeatures { get; set; }

    /// <summary>Build all targets (<c>--all-targets</c>). Useful with check / clippy / test to cover lib + bin + tests + benches.</summary>
    public bool AllTargets { get; set; }

    /// <summary>Number of parallel jobs (<c>-j N</c>). Default is unbounded (cargo picks).</summary>
    public int? Jobs { get; set; }

    public T SetRelease<T>(bool v = true) where T : CargoBuildLikeSettingsBase
    {
        if (v && !string.IsNullOrEmpty(Profile))
            throw new InvalidOperationException("Release and Profile are mutually exclusive.");
        Release = v;
        return (T)(object)this;
    }

    /// <summary>Append the selector + feature flags BEFORE the base's common flags so they bind to the subcommand.</summary>
    protected IEnumerable<string> EmitBuildLikeArguments()
    {
        if (Release && !string.IsNullOrEmpty(Profile))
            throw new InvalidOperationException("Release and Profile are mutually exclusive.");
        if (AllFeatures && NoDefaultFeatures)
            throw new InvalidOperationException("AllFeatures and NoDefaultFeatures are mutually exclusive.");

        if (Release) yield return "--release";
        if (!string.IsNullOrEmpty(Profile)) { yield return "--profile"; yield return Profile!; }
        if (Workspace) yield return "--workspace";
        foreach (var p in Packages) { yield return "-p"; yield return p; }
        foreach (var e in Excludes) { yield return "--exclude"; yield return e; }
        if (Features.Count > 0) { yield return "--features"; yield return string.Join(",", Features); }
        if (AllFeatures) yield return "--all-features";
        if (NoDefaultFeatures) yield return "--no-default-features";
        if (AllTargets) yield return "--all-targets";
        if (Jobs is { } j) { yield return "-j"; yield return j.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }
}

/// <summary>Fluent setters for the build-like knobs.</summary>
public static class CargoBuildLikeSettingsBaseExtensions
{
    public static T SetRelease<T>(this T s, bool v = true) where T : CargoBuildLikeSettingsBase { s.Release = v; return s; }
    public static T SetProfile<T>(this T s, string? profile) where T : CargoBuildLikeSettingsBase { s.Profile = profile; return s; }
    public static T SetWorkspace<T>(this T s, bool v = true) where T : CargoBuildLikeSettingsBase { s.Workspace = v; return s; }
    public static T AddPackage<T>(this T s, string name) where T : CargoBuildLikeSettingsBase { s.Packages.Add(name); return s; }
    public static T AddExclude<T>(this T s, string name) where T : CargoBuildLikeSettingsBase { s.Excludes.Add(name); return s; }
    public static T AddFeature<T>(this T s, string feature) where T : CargoBuildLikeSettingsBase { s.Features.Add(feature); return s; }
    public static T AddFeatures<T>(this T s, params string[] features) where T : CargoBuildLikeSettingsBase { s.Features.AddRange(features); return s; }
    public static T SetAllFeatures<T>(this T s, bool v = true) where T : CargoBuildLikeSettingsBase { s.AllFeatures = v; return s; }
    public static T SetNoDefaultFeatures<T>(this T s, bool v = true) where T : CargoBuildLikeSettingsBase { s.NoDefaultFeatures = v; return s; }
    public static T SetAllTargets<T>(this T s, bool v = true) where T : CargoBuildLikeSettingsBase { s.AllTargets = v; return s; }
    public static T SetJobs<T>(this T s, int? jobs) where T : CargoBuildLikeSettingsBase { s.Jobs = jobs; return s; }
}
