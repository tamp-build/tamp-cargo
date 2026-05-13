namespace Tamp.Cargo;

/// <summary>Settings for <c>cargo build</c>.</summary>
public sealed class CargoBuildSettings : CargoBuildLikeSettingsBase
{
    /// <summary>Build a specific binary (<c>--bin &lt;name&gt;</c>). Accumulates.</summary>
    public List<string> Bins { get; } = new();

    /// <summary>Build a specific example (<c>--example &lt;name&gt;</c>). Accumulates.</summary>
    public List<string> Examples { get; } = new();

    /// <summary>Build the library (<c>--lib</c>) only.</summary>
    public bool Lib { get; set; }

    /// <summary>Build all binaries (<c>--bins</c>).</summary>
    public bool Bins_All { get; set; }

    /// <summary>Build all examples (<c>--examples</c>).</summary>
    public bool Examples_All { get; set; }

    /// <summary>Override the target directory (<c>--target-dir</c>). Useful for splitting cache locations across CI legs.</summary>
    public string? TargetDir { get; set; }

    public CargoBuildSettings AddBin(string name) { Bins.Add(name); return this; }
    public CargoBuildSettings AddExample(string name) { Examples.Add(name); return this; }
    public CargoBuildSettings SetLib(bool v = true) { Lib = v; return this; }
    public CargoBuildSettings SetAllBins(bool v = true) { Bins_All = v; return this; }
    public CargoBuildSettings SetAllExamples(bool v = true) { Examples_All = v; return this; }
    public CargoBuildSettings SetTargetDir(string? dir) { TargetDir = dir; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "build";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
        foreach (var b in Bins) { yield return "--bin"; yield return b; }
        foreach (var e in Examples) { yield return "--example"; yield return e; }
        if (Lib) yield return "--lib";
        if (Bins_All) yield return "--bins";
        if (Examples_All) yield return "--examples";
        if (!string.IsNullOrEmpty(TargetDir)) { yield return "--target-dir"; yield return TargetDir!; }
    }
}

/// <summary>Settings for <c>cargo test</c>.</summary>
public sealed class CargoTestSettings : CargoBuildLikeSettingsBase
{
    /// <summary>Optional test-name filter (positional after <c>cargo test</c>).</summary>
    public string? TestNameFilter { get; set; }

    /// <summary>Args forwarded to the test binary after <c>--</c> (e.g. <c>--test-threads=1</c>, <c>--nocapture</c>).</summary>
    public List<string> TestArgs { get; } = new();

    /// <summary>Compile tests but don't run (<c>--no-run</c>) — useful for separating compile + run CI stages.</summary>
    public bool NoRun { get; set; }

    /// <summary>Run only ignored tests (<c>--ignored</c>) — paired with <c>cargo test --features integration</c> for the slow lane.</summary>
    public bool Ignored { get; set; }

    /// <summary>Build a specific test target (<c>--test &lt;name&gt;</c>).</summary>
    public List<string> Tests { get; } = new();

    /// <summary>Run only doc tests (<c>--doc</c>).</summary>
    public bool Doc { get; set; }

    public CargoTestSettings SetTestNameFilter(string? name) { TestNameFilter = name; return this; }
    public CargoTestSettings AddTestArg(string arg) { TestArgs.Add(arg); return this; }
    public CargoTestSettings AddTestArgs(params string[] args) { TestArgs.AddRange(args); return this; }
    public CargoTestSettings SetNoRun(bool v = true) { NoRun = v; return this; }
    public CargoTestSettings SetIgnored(bool v = true) { Ignored = v; return this; }
    public CargoTestSettings AddTest(string name) { Tests.Add(name); return this; }
    public CargoTestSettings SetDoc(bool v = true) { Doc = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "test";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
        if (NoRun) yield return "--no-run";
        if (Ignored) yield return "--ignored";
        if (Doc) yield return "--doc";
        foreach (var t in Tests) { yield return "--test"; yield return t; }
        if (!string.IsNullOrEmpty(TestNameFilter)) yield return TestNameFilter!;
        if (TestArgs.Count > 0)
        {
            yield return "--";
            foreach (var a in TestArgs) yield return a;
        }
    }
}

/// <summary>Settings for <c>cargo check</c> — fast type-check without codegen. The recommended CI gate before <c>cargo build</c>.</summary>
public sealed class CargoCheckSettings : CargoBuildLikeSettingsBase
{
    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "check";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
    }
}

/// <summary>Settings for <c>cargo clippy</c> — the Rust linter.</summary>
public sealed class CargoClippySettings : CargoBuildLikeSettingsBase
{
    /// <summary>Treat all warnings as errors via <c>-D warnings</c> after <c>--</c>. The standard CI flag.</summary>
    public bool DenyWarnings { get; set; }

    /// <summary>Apply fixes in-place (<c>--fix</c>). Use with care; pairs with <c>--allow-dirty</c> when not on a clean tree.</summary>
    public bool Fix { get; set; }

    /// <summary>Allow uncommitted changes when running with <c>--fix</c>.</summary>
    public bool AllowDirty { get; set; }

    /// <summary>Extra <c>-D / -A / -W</c> lint flags forwarded after <c>--</c>.</summary>
    public List<string> ExtraLints { get; } = new();

    public CargoClippySettings SetDenyWarnings(bool v = true) { DenyWarnings = v; return this; }
    public CargoClippySettings SetFix(bool v = true) { Fix = v; return this; }
    public CargoClippySettings SetAllowDirty(bool v = true) { AllowDirty = v; return this; }
    public CargoClippySettings AddLint(string flag) { ExtraLints.Add(flag); return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "clippy";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
        if (Fix) yield return "--fix";
        if (AllowDirty) yield return "--allow-dirty";
        if (DenyWarnings || ExtraLints.Count > 0)
        {
            yield return "--";
            if (DenyWarnings) { yield return "-D"; yield return "warnings"; }
            foreach (var l in ExtraLints) yield return l;
        }
    }
}

/// <summary>Settings for <c>cargo fmt</c> — code formatting via rustfmt.</summary>
public sealed class CargoFmtSettings : CargoSettingsBase
{
    /// <summary>Check formatting without writing changes (<c>--check</c>). The standard CI gate.</summary>
    public bool Check { get; set; }

    /// <summary>Apply to all workspace members (<c>--all</c>).</summary>
    public bool All { get; set; }

    /// <summary>Specific workspace packages (<c>-p &lt;name&gt;</c>).</summary>
    public List<string> Packages { get; } = new();

    public CargoFmtSettings SetCheck(bool v = true) { Check = v; return this; }
    public CargoFmtSettings SetAll(bool v = true) { All = v; return this; }
    public CargoFmtSettings AddPackage(string name) { Packages.Add(name); return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "fmt";
        if (All) yield return "--all";
        foreach (var p in Packages) { yield return "-p"; yield return p; }
        if (Check) { yield return "--"; yield return "--check"; }
    }
}

/// <summary>Settings for <c>cargo run</c>.</summary>
public sealed class CargoRunSettings : CargoBuildLikeSettingsBase
{
    /// <summary>Run a specific binary (<c>--bin &lt;name&gt;</c>).</summary>
    public string? Bin { get; set; }

    /// <summary>Run a specific example (<c>--example &lt;name&gt;</c>). Mutually exclusive with <see cref="Bin"/>.</summary>
    public string? Example { get; set; }

    /// <summary>Args forwarded to the target binary after <c>--</c>.</summary>
    public List<string> ProgramArgs { get; } = new();

    public CargoRunSettings SetBin(string? name) { Bin = name; return this; }
    public CargoRunSettings SetExample(string? name) { Example = name; return this; }
    public CargoRunSettings AddProgramArg(string arg) { ProgramArgs.Add(arg); return this; }
    public CargoRunSettings AddProgramArgs(params string[] args) { ProgramArgs.AddRange(args); return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (!string.IsNullOrEmpty(Bin) && !string.IsNullOrEmpty(Example))
            throw new InvalidOperationException("Bin and Example are mutually exclusive on cargo run.");
        yield return "run";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
        if (!string.IsNullOrEmpty(Bin)) { yield return "--bin"; yield return Bin!; }
        if (!string.IsNullOrEmpty(Example)) { yield return "--example"; yield return Example!; }
        if (ProgramArgs.Count > 0)
        {
            yield return "--";
            foreach (var a in ProgramArgs) yield return a;
        }
    }
}

/// <summary>Settings for <c>cargo bench</c>.</summary>
public sealed class CargoBenchSettings : CargoBuildLikeSettingsBase
{
    /// <summary>Bench name filter (positional after <c>cargo bench</c>).</summary>
    public string? NameFilter { get; set; }

    /// <summary>Args forwarded to the bench binary after <c>--</c>.</summary>
    public List<string> BenchArgs { get; } = new();

    public CargoBenchSettings SetNameFilter(string? name) { NameFilter = name; return this; }
    public CargoBenchSettings AddBenchArg(string arg) { BenchArgs.Add(arg); return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "bench";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
        if (!string.IsNullOrEmpty(NameFilter)) yield return NameFilter!;
        if (BenchArgs.Count > 0)
        {
            yield return "--";
            foreach (var a in BenchArgs) yield return a;
        }
    }
}

/// <summary>Settings for <c>cargo doc</c>.</summary>
public sealed class CargoDocSettings : CargoBuildLikeSettingsBase
{
    /// <summary>Open the docs in a browser after building (<c>--open</c>). Skip in CI.</summary>
    public bool Open { get; set; }

    /// <summary>Don't build documentation for dependencies (<c>--no-deps</c>).</summary>
    public bool NoDeps { get; set; }

    /// <summary>Include private items (<c>--document-private-items</c>).</summary>
    public bool DocumentPrivateItems { get; set; }

    public CargoDocSettings SetOpen(bool v = true) { Open = v; return this; }
    public CargoDocSettings SetNoDeps(bool v = true) { NoDeps = v; return this; }
    public CargoDocSettings SetDocumentPrivateItems(bool v = true) { DocumentPrivateItems = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "doc";
        foreach (var a in EmitBuildLikeArguments()) yield return a;
        if (Open) yield return "--open";
        if (NoDeps) yield return "--no-deps";
        if (DocumentPrivateItems) yield return "--document-private-items";
    }
}

/// <summary>Settings for <c>cargo update</c> — refresh Cargo.lock.</summary>
public sealed class CargoUpdateSettings : CargoSettingsBase
{
    /// <summary>Specific package(s) to update (<c>-p &lt;name&gt;</c>). Empty = all.</summary>
    public List<string> Packages { get; } = new();

    /// <summary>Dry-run (<c>--dry-run</c>) — report what would change without writing the lockfile.</summary>
    public bool DryRun { get; set; }

    public CargoUpdateSettings AddPackage(string name) { Packages.Add(name); return this; }
    public CargoUpdateSettings SetDryRun(bool v = true) { DryRun = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "update";
        foreach (var p in Packages) { yield return "-p"; yield return p; }
        if (DryRun) yield return "--dry-run";
    }
}

/// <summary>Raw escape hatch — emits <c>cargo &lt;args...&gt;</c> after the base's common flags. Use only when the typed verbs don't cover what you need.</summary>
public sealed class CargoRawSettings : CargoSettingsBase
{
    private readonly List<string> _args = new();
    public void AddArgs(IEnumerable<string> args) => _args.AddRange(args);
    protected override IEnumerable<string> BuildVerbArguments() => _args;
}
