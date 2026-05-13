# Changelog

All notable changes to **Tamp.Cargo** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

## [0.2.0] — pending — typed package-version stamping (TAM-203)

### Added

- **`Cargo.SetPackageVersion(AbsolutePath manifest, string version)`** — write
  the `[package].version` field of a Cargo.toml manifest directly, without
  invoking `cargo set-version` (which requires the separate `cargo-edit`
  plugin to be installed). Tamp ships a minimal surgical TOML editor that
  touches only the version field and preserves every other byte of the
  manifest: comments, whitespace, quote style, line endings, UTF-8 BOM.

  ```csharp
  Target StampVersion => _ => _
      .Before(nameof(BuildService), nameof(BuildDesktop))
      .Executes(() => Cargo.SetPackageVersion(ServiceCrate / "Cargo.toml", Version));
  ```

  **Idempotent by design** — if the version already matches, the file is
  not rewritten (mtime unchanged). Safe to call repeatedly from CI retry
  loops or inner-loop dev iterations.

- **`Cargo.GetPackageVersion(AbsolutePath manifest)`** — symmetric reader,
  returns `string?` (null when manifest has no `[package]` section, no
  version field, or inherits via `version.workspace = true`).

### Why

DasBook canary friction batch #3 #6 (2026-05-13). `cargo set-version` is a
`cargo-edit` plugin command, not a built-in. Adopters who naively call
`Cargo.Raw(CargoBin, "set-version", ...)` get `cargo: no such command:
'set-version'` with no hint about the separate crate. Shipping a native
typed verb removes the cargo-edit prerequisite entirely.

### Format-preservation contract

- Whitespace before / between / after the `=`
- Quote style (`"` or `'`) — preserved exactly
- Inline comments on the version line — preserved
- LF / CRLF line endings — preserved
- UTF-8 BOM — preserved if present, not added if absent
- Unicode content elsewhere in the manifest — preserved

### Error cases

Throws `InvalidOperationException` with a descriptive message:

- Workspace virtual manifest (no `[package]` section)
- Member crate using `version.workspace = true` (edit the workspace root)
- `[package]` section with no `version` field

Throws `ArgumentException` on null / whitespace / quote-bearing / newline-
bearing version strings (prevents TOML-injection attempts).

Throws `FileNotFoundException` on missing manifest paths.

### Tests

26 new tests in `CargoTomlEditorTests`: idempotency (mtime preserved),
format preservation across line endings + quote styles + BOM + inline
comments + Unicode content, error-path coverage for workspace virtual
manifests + workspace inheritance + missing version field + invalid
version strings, isolation (version field in [dependencies] not touched).

## [0.1.0] - 2026-05-13

### Added

- Initial release. Verb surface: `Build`, `Test`, `Check`, `Clippy`, `Fmt`, `Run_`,
  `Bench`, `Doc`, `Update`, `Raw`. Filed under TAM-187.

- Common knobs at the base: `WorkingDirectory`, `ManifestPath`, `Target` (triple),
  `Verbosity`, `Quiet`, `ForceColor`, `Frozen`, `Locked`, `Offline`. Environment
  variable overlay via `SetEnvironmentVariable`.

- Build-like verbs (Build, Test, Check, Clippy, Run, Bench, Doc) share a workspace +
  feature selector set: `Release` / `Profile` (mutually exclusive), `Workspace` /
  `Packages` / `Excludes`, `Features` / `AllFeatures` / `NoDefaultFeatures` (last two
  mutually exclusive), `AllTargets`, `Jobs`. Mutual-exclusivity is enforced at
  plan-build time.

- `Clippy.SetDenyWarnings()` emits `-- -D warnings` for the canonical CI lint gate;
  `Fmt.SetCheck()` emits `-- --check` for the canonical CI format gate.

- `Run_` has the `_` suffix to disambiguate from `TampBuild`'s own `Run` helper.

- 29 unit tests cover positive + negative cases including mutually-exclusive guards.

### Notes

- First non-.NET satellite. Establishes the pattern for future toolchain wrappers
  (Tamp.Tauri.V2, Tamp.Msix, etc.). Same CLI-wrapper conventions as `Tamp.Yarn.V4` /
  `Tamp.Npm.V10` — settings classes derive from a shared base, fluent setters return
  `this`, object-init overloads parallel each fluent overload, `Raw` escape hatch.

- `cargo publish` (with `--token`) is intentionally NOT in v0.1.0. Adding it would
  require `Tamp.Cargo` on `Tamp.Core`'s `InternalsVisibleTo` list (for `Secret.Reveal()`).
  Defer to a future version when an adopter actually needs to publish crates from a
  Tamp script. Until then, `Cargo.Raw(Tool, "publish", "--token", token)` works for
  the rare case (caller-side Reveal).

- Driven by the DasBook adoption brief 2026-05-13. DasBook's build chain (2 Rust
  crates + Node + MSIX packaging) was producing a live shipped bug where a compiled
  binary lands in the wrong staging directory because the file-copy step lives in
  human memory. Tamp.Cargo is the entry point to getting that build graph typed.
