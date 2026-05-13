namespace Tamp.Cargo;

/// <summary>Top-level facade for <c>cargo</c> verbs.</summary>
/// <remarks>
/// <para>Resolve the tool via <c>[FromPath("cargo")]</c>:</para>
/// <code>
/// [FromPath("cargo")] readonly Tool Cargo = null!;
/// </code>
/// <para>
/// Cargo is normally installed via rustup as part of the Rust toolchain. The Tool resolves
/// to <c>cargo</c> on PATH; the rustup proxy then dispatches to the active toolchain version.
/// </para>
/// </remarks>
public static class Cargo
{
    /// <summary><c>cargo build</c> — compile the current package (or workspace).</summary>
    public static CommandPlan Build(Tool tool, Action<CargoBuildSettings>? configure = null)
        => Run<CargoBuildSettings>(tool, configure);

    /// <summary><c>cargo test</c> — compile + run tests.</summary>
    public static CommandPlan Test(Tool tool, Action<CargoTestSettings>? configure = null)
        => Run<CargoTestSettings>(tool, configure);

    /// <summary><c>cargo check</c> — fast type-check without codegen. The recommended pre-build CI gate.</summary>
    public static CommandPlan Check(Tool tool, Action<CargoCheckSettings>? configure = null)
        => Run<CargoCheckSettings>(tool, configure);

    /// <summary><c>cargo clippy</c> — the Rust linter. Use <c>SetDenyWarnings()</c> for the CI gate.</summary>
    public static CommandPlan Clippy(Tool tool, Action<CargoClippySettings>? configure = null)
        => Run<CargoClippySettings>(tool, configure);

    /// <summary><c>cargo fmt</c> — format code via rustfmt. Use <c>SetCheck()</c> for the CI gate.</summary>
    public static CommandPlan Fmt(Tool tool, Action<CargoFmtSettings>? configure = null)
        => Run<CargoFmtSettings>(tool, configure);

    /// <summary><c>cargo run</c> — compile and run a binary or example.</summary>
    public static CommandPlan Run_(Tool tool, Action<CargoRunSettings>? configure = null)
        => Run<CargoRunSettings>(tool, configure);

    /// <summary><c>cargo bench</c> — compile + run benchmarks.</summary>
    public static CommandPlan Bench(Tool tool, Action<CargoBenchSettings>? configure = null)
        => Run<CargoBenchSettings>(tool, configure);

    /// <summary><c>cargo doc</c> — build the rustdoc HTML for the current crate (and dependencies, unless <c>SetNoDeps()</c>).</summary>
    public static CommandPlan Doc(Tool tool, Action<CargoDocSettings>? configure = null)
        => Run<CargoDocSettings>(tool, configure);

    /// <summary><c>cargo update</c> — refresh <c>Cargo.lock</c>.</summary>
    public static CommandPlan Update(Tool tool, Action<CargoUpdateSettings>? configure = null)
        => Run<CargoUpdateSettings>(tool, configure);

    /// <summary>Raw escape hatch — for verbs the typed surface doesn't cover (e.g. <c>cargo install</c>, <c>cargo publish</c>, <c>cargo metadata</c>).</summary>
    public static CommandPlan Raw(Tool tool, params string[] arguments)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (arguments is null || arguments.Length == 0)
            throw new ArgumentException("Raw requires at least one argument.", nameof(arguments));
        var s = new CargoRawSettings();
        s.AddArgs(arguments);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan Run<T>(Tool tool, Action<T>? configure) where T : CargoSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var s = new T();
        configure?.Invoke(s);
        return s.ToCommandPlan(tool);
    }

    // ---- Object-init overloads ----

    public static CommandPlan Build(Tool tool, CargoBuildSettings settings) => Plan(tool, settings);
    public static CommandPlan Test(Tool tool, CargoTestSettings settings) => Plan(tool, settings);
    public static CommandPlan Check(Tool tool, CargoCheckSettings settings) => Plan(tool, settings);
    public static CommandPlan Clippy(Tool tool, CargoClippySettings settings) => Plan(tool, settings);
    public static CommandPlan Fmt(Tool tool, CargoFmtSettings settings) => Plan(tool, settings);
    public static CommandPlan Run_(Tool tool, CargoRunSettings settings) => Plan(tool, settings);
    public static CommandPlan Bench(Tool tool, CargoBenchSettings settings) => Plan(tool, settings);
    public static CommandPlan Doc(Tool tool, CargoDocSettings settings) => Plan(tool, settings);
    public static CommandPlan Update(Tool tool, CargoUpdateSettings settings) => Plan(tool, settings);

    private static CommandPlan Plan<T>(Tool tool, T settings) where T : CargoSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }
}
