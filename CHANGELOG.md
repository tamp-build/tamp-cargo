# Changelog

All notable changes to **Tamp.Cargo** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

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
