using System.Collections.Generic;
using System.Linq;
using Tamp;
using Tamp.Cargo;
using Xunit;

namespace Tamp.Cargo.Tests;

public sealed class CargoTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/cargo"));

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- Build ----

    [Fact]
    public void Build_Bare_Has_Verb()
    {
        var plan = Cargo.Build(FakeTool());
        Assert.Equal("build", plan.Arguments[0]);
    }

    [Fact]
    public void Build_Release_Workspace_Features_TargetTriple()
    {
        var plan = Cargo.Build(FakeTool(), s => s
            .SetRelease()
            .SetWorkspace()
            .AddFeature("integration")
            .AddFeature("native-tls")
            .SetTarget("x86_64-pc-windows-msvc"));
        Assert.Contains("--release", plan.Arguments);
        Assert.Contains("--workspace", plan.Arguments);
        var fi = IndexOf(plan.Arguments, "--features");
        Assert.Equal("integration,native-tls", plan.Arguments[fi + 1]);
        Assert.Equal("x86_64-pc-windows-msvc", plan.Arguments[IndexOf(plan.Arguments, "--target") + 1]);
    }

    [Fact]
    public void Build_Bin_Selection_Plus_TargetDir()
    {
        var plan = Cargo.Build(FakeTool(), s => s
            .SetRelease()
            .AddBin("dasbook-service")
            .SetTargetDir("artifacts/cargo-target"));
        Assert.Equal("dasbook-service", plan.Arguments[IndexOf(plan.Arguments, "--bin") + 1]);
        Assert.Equal("artifacts/cargo-target", plan.Arguments[IndexOf(plan.Arguments, "--target-dir") + 1]);
    }

    [Fact]
    public void Build_Multiple_Packages()
    {
        var plan = Cargo.Build(FakeTool(), s => s
            .AddPackage("dasbook2")
            .AddPackage("dasbook-service"));
        var pIndices = Enumerable.Range(0, plan.Arguments.Count).Where(i => plan.Arguments[i] == "-p").ToList();
        Assert.Equal(2, pIndices.Count);
        Assert.Equal("dasbook2", plan.Arguments[pIndices[0] + 1]);
        Assert.Equal("dasbook-service", plan.Arguments[pIndices[1] + 1]);
    }

    [Fact]
    public void Build_Release_And_Profile_Are_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Cargo.Build(FakeTool(), s => s.SetRelease().SetProfile("custom")).Arguments.ToList());
    }

    [Fact]
    public void Build_AllFeatures_And_NoDefaultFeatures_Are_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Cargo.Build(FakeTool(), s => s.SetAllFeatures().SetNoDefaultFeatures()).Arguments.ToList());
    }

    [Fact]
    public void Build_Lib_AllBins_AllExamples_Flags()
    {
        var plan = Cargo.Build(FakeTool(), s => s.SetLib().SetAllBins().SetAllExamples());
        Assert.Contains("--lib", plan.Arguments);
        Assert.Contains("--bins", plan.Arguments);
        Assert.Contains("--examples", plan.Arguments);
    }

    // ---- Test ----

    [Fact]
    public void Test_With_Filter_And_TestArgs_After_DoubleDash()
    {
        var plan = Cargo.Test(FakeTool(), s => s
            .SetWorkspace()
            .SetTestNameFilter("my_test")
            .AddTestArgs("--test-threads=1", "--nocapture"));
        Assert.Equal("test", plan.Arguments[0]);
        Assert.Contains("my_test", plan.Arguments);
        var dashIdx = IndexOf(plan.Arguments, "--");
        Assert.True(dashIdx >= 0);
        Assert.Equal("--test-threads=1", plan.Arguments[dashIdx + 1]);
        Assert.Equal("--nocapture", plan.Arguments[dashIdx + 2]);
    }

    [Fact]
    public void Test_NoRun_Flag_For_CI_Split()
    {
        var plan = Cargo.Test(FakeTool(), s => s.SetNoRun());
        Assert.Contains("--no-run", plan.Arguments);
    }

    [Fact]
    public void Test_Doc_Only_Mode()
    {
        var plan = Cargo.Test(FakeTool(), s => s.SetDoc());
        Assert.Contains("--doc", plan.Arguments);
    }

    // ---- Check ----

    [Fact]
    public void Check_All_Targets_Workspace()
    {
        var plan = Cargo.Check(FakeTool(), s => s.SetWorkspace().SetAllTargets());
        Assert.Equal("check", plan.Arguments[0]);
        Assert.Contains("--workspace", plan.Arguments);
        Assert.Contains("--all-targets", plan.Arguments);
    }

    // ---- Clippy ----

    [Fact]
    public void Clippy_DenyWarnings_Goes_After_DoubleDash()
    {
        var plan = Cargo.Clippy(FakeTool(), s => s
            .SetWorkspace()
            .SetAllTargets()
            .SetDenyWarnings());
        var dashIdx = IndexOf(plan.Arguments, "--");
        Assert.True(dashIdx >= 0, "Expected -- separator before lint flags");
        // -D warnings comes after --
        Assert.Equal("-D", plan.Arguments[dashIdx + 1]);
        Assert.Equal("warnings", plan.Arguments[dashIdx + 2]);
    }

    [Fact]
    public void Clippy_Extra_Lints_Are_Forwarded_After_DoubleDash()
    {
        var plan = Cargo.Clippy(FakeTool(), s => s
            .SetDenyWarnings()
            .AddLint("-A")
            .AddLint("clippy::module_name_repetitions"));
        Assert.Contains("clippy::module_name_repetitions", plan.Arguments);
    }

    [Fact]
    public void Clippy_Fix_AllowDirty()
    {
        var plan = Cargo.Clippy(FakeTool(), s => s.SetFix().SetAllowDirty());
        Assert.Contains("--fix", plan.Arguments);
        Assert.Contains("--allow-dirty", plan.Arguments);
    }

    // ---- Fmt ----

    [Fact]
    public void Fmt_Check_Mode_For_CI()
    {
        var plan = Cargo.Fmt(FakeTool(), s => s.SetAll().SetCheck());
        Assert.Equal("fmt", plan.Arguments[0]);
        Assert.Contains("--all", plan.Arguments);
        // --check is forwarded to rustfmt AFTER --
        var dashIdx = IndexOf(plan.Arguments, "--");
        Assert.Equal("--check", plan.Arguments[dashIdx + 1]);
    }

    // ---- Run ----

    [Fact]
    public void Run_With_Bin_And_Program_Args()
    {
        var plan = Cargo.Run_(FakeTool(), s => s
            .SetRelease()
            .SetBin("dasbook-service")
            .AddProgramArgs("--port", "3738"));
        Assert.Equal("run", plan.Arguments[0]);
        Assert.Equal("dasbook-service", plan.Arguments[IndexOf(plan.Arguments, "--bin") + 1]);
        var dashIdx = IndexOf(plan.Arguments, "--");
        Assert.Equal("--port", plan.Arguments[dashIdx + 1]);
        Assert.Equal("3738", plan.Arguments[dashIdx + 2]);
    }

    [Fact]
    public void Run_Bin_And_Example_Are_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Cargo.Run_(FakeTool(), s => s.SetBin("a").SetExample("b")).Arguments.ToList());
    }

    // ---- Bench / Doc / Update ----

    [Fact]
    public void Bench_With_Filter()
    {
        var plan = Cargo.Bench(FakeTool(), s => s.SetNameFilter("my_bench"));
        Assert.Equal("bench", plan.Arguments[0]);
        Assert.Contains("my_bench", plan.Arguments);
    }

    [Fact]
    public void Doc_NoDeps_DocumentPrivate()
    {
        var plan = Cargo.Doc(FakeTool(), s => s.SetNoDeps().SetDocumentPrivateItems());
        Assert.Equal("doc", plan.Arguments[0]);
        Assert.Contains("--no-deps", plan.Arguments);
        Assert.Contains("--document-private-items", plan.Arguments);
    }

    [Fact]
    public void Update_With_Specific_Package_And_DryRun()
    {
        var plan = Cargo.Update(FakeTool(), s => s.AddPackage("serde").SetDryRun());
        Assert.Equal("update", plan.Arguments[0]);
        Assert.Equal("serde", plan.Arguments[IndexOf(plan.Arguments, "-p") + 1]);
        Assert.Contains("--dry-run", plan.Arguments);
    }

    // ---- Common knobs ----

    [Fact]
    public void Frozen_Locked_Offline_Flags()
    {
        var plan = Cargo.Build(FakeTool(), s => s.SetFrozen().SetLocked().SetOffline());
        Assert.Contains("--frozen", plan.Arguments);
        Assert.Contains("--locked", plan.Arguments);
        Assert.Contains("--offline", plan.Arguments);
    }

    [Fact]
    public void ManifestPath_Override()
    {
        var plan = Cargo.Build(FakeTool(), s => s.SetManifestPath("src-tauri/Cargo.toml"));
        Assert.Equal("src-tauri/Cargo.toml", plan.Arguments[IndexOf(plan.Arguments, "--manifest-path") + 1]);
    }

    [Fact]
    public void Verbosity_Maps_To_V_Flags()
    {
        var v1 = Cargo.Build(FakeTool(), s => s.SetVerbosity(1));
        var v2 = Cargo.Build(FakeTool(), s => s.SetVerbosity(2));
        Assert.Contains("-v", v1.Arguments);
        Assert.Contains("-vv", v2.Arguments);
    }

    [Fact]
    public void ForceColor_Emits_Color_Always()
    {
        var plan = Cargo.Build(FakeTool(), s => s.SetForceColor());
        Assert.Equal("always", plan.Arguments[IndexOf(plan.Arguments, "--color") + 1]);
    }

    [Fact]
    public void WorkingDirectory_Propagates()
    {
        var plan = Cargo.Build(FakeTool(), s => s.SetWorkingDirectory("/repo/src-tauri"));
        Assert.Equal("/repo/src-tauri", plan.WorkingDirectory);
    }

    [Fact]
    public void Environment_Variables_Propagate()
    {
        var plan = Cargo.Build(FakeTool(), s => s
            .SetEnvironmentVariable("RUSTFLAGS", "-C target-cpu=native")
            .SetEnvironmentVariable("CARGO_TERM_COLOR", "always"));
        Assert.Equal("-C target-cpu=native", plan.Environment["RUSTFLAGS"]);
        Assert.Equal("always", plan.Environment["CARGO_TERM_COLOR"]);
    }

    // ---- Raw ----

    [Fact]
    public void Raw_Allows_Arbitrary_Verb()
    {
        var plan = Cargo.Raw(FakeTool(), "metadata", "--format-version=1", "--no-deps");
        Assert.Equal(new[] { "metadata", "--format-version=1", "--no-deps" }, plan.Arguments.Take(3));
    }

    [Fact]
    public void Raw_Rejects_Empty_Args()
    {
        Assert.Throws<ArgumentException>(() => Cargo.Raw(FakeTool()));
    }

    // ---- Tool path ----

    [Fact]
    public void Executable_Matches_Tool_Path()
    {
        var plan = Cargo.Build(FakeTool());
        Assert.EndsWith("cargo", plan.Executable.TrimEnd(System.IO.Path.DirectorySeparatorChar));
    }
}
