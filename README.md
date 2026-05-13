# Tamp.Cargo

> Wrapper for the `cargo` CLI — the Rust toolchain build driver. First non-.NET satellite in the Tamp ecosystem; same shape and conventions as `Tamp.Yarn.V4` and `Tamp.Npm.V10`.

| Package | Status |
|---|---|
| `Tamp.Cargo` | 0.1.0 (initial) |

## Why Tamp drives Rust

Build scripts that mix Rust + Node + native packaging tend to live half in YAML and half in human memory. The MSIX-sidecar-in-wrong-directory class of bug — *a compiled binary lands at the wrong staging path because the copy step lives in a memo* — exists because the dependency graph is missing.

`Tamp.Cargo` puts cargo verbs into a typed, composable, dependency-graph-aware C# build script. The script *is* the contract: which crate compiles, where the output goes, which step depends on which. Drift between docs and reality stops mattering because there are no docs to drift from.

## Install

```bash
dotnet add package Tamp.Cargo
```

Multi-targets net8 / net9 / net10. Requires `cargo` on PATH (typically installed via [rustup](https://rustup.rs/)).

## Quick start

```csharp
using Tamp;
using Tamp.Cargo;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [FromPath("cargo")] readonly Tool Cargo = null!;

    AbsolutePath ServiceCrate => RootDirectory / "dasbook-service";
    AbsolutePath DesktopCrate => RootDirectory / "src-tauri";

    Target CheckRust => _ => _
        .Description("[CI gate] Fast type-check across both crates")
        .Executes(() => new[]
        {
            Cargo.Check(s => s
                .SetWorkingDirectory(ServiceCrate)
                .SetWorkspace()
                .SetAllTargets()
                .SetLocked()),
            Cargo.Check(s => s
                .SetWorkingDirectory(DesktopCrate)
                .SetWorkspace()
                .SetAllTargets()
                .SetLocked()),
        });

    Target BuildService => _ => _
        .DependsOn(nameof(CheckRust))
        .Description("[Build] dasbook-service release binary for the Tauri sidecar slot")
        .Executes(() => Cargo.Build(s => s
            .SetWorkingDirectory(ServiceCrate)
            .SetRelease()
            .SetTarget("x86_64-pc-windows-msvc")  // sidecar contract requires the suffix
            .SetLocked()));

    Target Clippy => _ => _
        .Description("[CI gate] Lint with warnings-as-errors")
        .Executes(() => Cargo.Clippy(s => s
            .SetWorkspace()
            .SetAllTargets()
            .SetDenyWarnings()));

    Target FmtCheck => _ => _
        .Description("[CI gate] rustfmt --check")
        .Executes(() => Cargo.Fmt(s => s.SetAll().SetCheck()));

    Target Test => _ => _
        .DependsOn(nameof(BuildService))
        .Executes(() => Cargo.Test(s => s
            .SetWorkingDirectory(ServiceCrate)
            .SetWorkspace()
            .SetLocked()));
}
```

## Verb surface

| Tamp method | cargo command | Notes |
|---|---|---|
| `Cargo.Build(...)` | `cargo build` | `--release`, `--target`, `--features`, `--workspace`, `-p`, `--bin`, `--example`, `--lib`, `--all-targets`, `--target-dir`. |
| `Cargo.Test(...)` | `cargo test [filter] [-- args]` | `--no-run` for compile/run CI split; `--doc` for doc-tests only; test-binary args forwarded after `--`. |
| `Cargo.Check(...)` | `cargo check` | Fast type-check without codegen. The recommended pre-build CI gate. |
| `Cargo.Clippy(...)` | `cargo clippy` | `SetDenyWarnings()` adds `-- -D warnings` for the CI gate. `--fix` + `--allow-dirty` for in-place corrections. |
| `Cargo.Fmt(...)` | `cargo fmt` | `SetCheck()` adds `-- --check` for the CI gate (rustfmt arg). |
| `Cargo.Run_(...)` | `cargo run [-- program args]` | `_` suffix to disambiguate from `TampBuild.Run`. |
| `Cargo.Bench(...)` | `cargo bench [filter] [-- args]` | |
| `Cargo.Doc(...)` | `cargo doc` | `SetNoDeps()` / `SetDocumentPrivateItems()`. |
| `Cargo.Update(...)` | `cargo update` | `-p <name>` for specific packages; `--dry-run` for preview. |
| `Cargo.Raw(...)` | `cargo <anything>` | Escape hatch — `metadata`, `install`, `publish`, etc. |

## The "workspace + features + target-triple" set

Every build-like verb (`Build`, `Test`, `Check`, `Clippy`, `Run`, `Bench`, `Doc`) inherits a common selector set:

- `SetRelease()` / `SetProfile("name")` — profile selection (mutually exclusive)
- `SetWorkspace()` / `AddPackage("name")` / `AddExclude("name")` — workspace member selection
- `AddFeature("...")` / `SetAllFeatures()` / `SetNoDefaultFeatures()` — feature flags (the all/none flags are mutually exclusive)
- `SetTarget("x86_64-pc-windows-msvc")` — cross-compile target triple
- `SetAllTargets()` — apply to lib + bins + tests + benches in one go
- `SetJobs(N)` — bound parallel job count
- `SetFrozen()` / `SetLocked()` / `SetOffline()` — reproducibility flags (CI usually wants `SetLocked()`)

## Target-triple handling for downstream packagers

When a Rust binary feeds a downstream packaging step (Tauri externalBin, MSIX sidecar, etc.), the binary path typically includes a target-triple suffix. Always set `SetTarget(...)` explicitly when the contract requires it:

```csharp
// Tauri's externalBin contract: <name>-<target-triple>(.exe)
Cargo.Build(s => s
    .SetWorkingDirectory(ServiceCrate)
    .SetRelease()
    .SetTarget("x86_64-pc-windows-msvc"));   // produces target/x86_64-pc-windows-msvc/release/dasbook-service.exe

// Adopter-side: copy or rename to the externalBin slot.
// (Tauri 2's CLI also does this dance via tauri.conf.json — covered by Tamp.Tauri.V2 in a future wave.)
```

## Reproducibility flags

For CI: `SetLocked()` (refuse to modify `Cargo.lock`) is the right default. Use `SetFrozen()` (offline + locked) when the cache is pre-warmed and you want fail-fast on accidental registry hits.

For local dev: leave both off.

## Environment overlay

Per-invocation env vars chain via `SetEnvironmentVariable(name, value)`. Useful for `RUSTFLAGS`, `CARGO_TERM_COLOR`, `CARGO_BUILD_JOBS`, etc.:

```csharp
Cargo.Build(s => s
    .SetEnvironmentVariable("RUSTFLAGS", "-C target-cpu=native")
    .SetEnvironmentVariable("CARGO_TERM_COLOR", "always")
    .SetRelease());
```

## Raw escape hatch

For verbs not yet typed (`cargo install`, `cargo publish`, `cargo metadata`, custom subcommands like `cargo-deny`):

```csharp
var plan = Cargo.Raw(Cargo, "metadata", "--format-version=1", "--no-deps");
```

File a TAM ticket if you reach for `Raw` frequently — the typed surface should grow to cover real adopter needs.

## Sibling packages

- [`Tamp.Yarn.V4`](https://github.com/tamp-build/tamp-yarn) — Yarn Berry. Same shape, different ecosystem.
- [`Tamp.Npm.V10`](https://github.com/tamp-build/tamp-npm) — npm CLI 10.x. Pairs naturally with Cargo for Tauri-style desktop apps.
- [`Tamp.Docker.V27`](https://github.com/tamp-build/tamp-docker) — When the Rust output ships as a container image.

## Releasing

Releases follow the [Tamp dogfood pattern](MAINTAINERS.md): bump `<Version>` in `Directory.Build.props`, tag `v<X.Y.Z>`, GitHub Actions runs `dotnet tamp Ci` then `dotnet tamp Push`.

## License

MIT. See [LICENSE](LICENSE).
