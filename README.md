![Essentials for Unity](EssentialsForUnityBanner.png)

# Alpha Compatibility

A maintained collection of small, version-scoped workarounds for Unity alpha
releases. Each fix lives with its regression tests and a record of why it
exists, which versions need it, and when it can be removed.

Package: `com.martincalander.alphacompatibility`

Minimum Editor: Unity 7 (`7000.0`)

License: [Proprietary License](LICENSE.md), owned by Martin Calander.

All current code is Editor-only. Each workaround has its own assembly and
package constraints, so an unrelated project does not need to install every
integration supported by this package.

## Installation

The repository is public. Use **Package Manager > Install package from git URL**:

```text
https://github.com/EssentialsForUnity/com.martincalander.alphacompatibility.git
```

The Git URL follows the repository's default `main` branch. To install through
the project manifest, add this entry to `Packages/manifest.json`:

```json
"com.martincalander.alphacompatibility": "https://github.com/EssentialsForUnity/com.martincalander.alphacompatibility.git"
```

Use one installation source per project. Remove an older embedded copy or
submodule and any equivalent host RenderGraph shim when switching to the Git
dependency. Keep `enableLockFile` set to `false` and the generated
`Packages/packages-lock.json` untracked in hosts that follow the latest upstream
Git dependencies. Repository visibility does not change the proprietary license.

The RenderGraph fix requires these packages already installed in the host:

| Package | Required version | Purpose |
| --- | --- | --- |
| `com.unity.pipeline` | Exactly `0.9.0-exp.1` | Command registry being adapted |
| `com.unity.render-pipelines.core` | Exactly `17.7.0` | Original RenderGraph handlers, usually supplied by URP or HDRP |
| `com.martincalander.gitsubmodulemanager` | `0.8.1` or newer with the same Harmony plugin | Shared Editor patching library |

Install Git Submodule Manager from its upstream default branch if needed:

```text
https://github.com/martincalander/GitSubmoduleForUnity.git
```

These are optional integration prerequisites, so the package manifest does not
force their installation or downgrade a host's packages. The RenderGraph
assembly and tests are excluded unless all three constraints are satisfied.
When Pipeline or SRP Core changes version, this fix becomes inactive pending
review. The runtime version check also requires the exact affected pair.

The first release reuses Git Submodule Manager's Harmony provider. It does not
ship a second copy of Harmony. See [Third Party Notices](Third%20Party%20Notices.md).

## Workaround record

### RG-001: RenderGraph command discovery

**Status:** Active for Pipeline `0.9.0-exp.1` and SRP Core `17.7.0`.

**Symptom:** SRP Core declares 15 commands with names such as
`render_graph.graphs`. Pipeline 0.9 requires a separate command group and name,
so these declarations fail validation and disappear from discovery.

**Fix:** A scoped prefix on `CommandRegistry.ReadDeclaration` translates only
declarations from `UnityEditor.Rendering.CLI.RenderGraphDebugCLI` in
`Unity.RenderPipelines.Core.Editor.CLI`. For example, `render_graph.graphs`
becomes group `render_graph`, command `graphs`. Nested groups such as
`render_graph.passes` are preserved. The original handlers, parameters, tags,
thread requirements, and descriptions remain intact. Pipeline still validates
the translated declarations.

Initialization clears the discovery cache and is idempotent. Code unload
removes only this fix's Harmony prefix and clears the cache again. Asset import
worker processes do not apply the patch.

**Verified host:** Unity `7000.0.0a11`, CLI `1.0.0-beta.14`, Pipeline
`0.9.0-exp.1`, SRP Core `17.7.0`, and Git Submodule Manager `0.8.1`.

**Coverage:** All 15 handlers and their metadata, normal validation failures,
unrelated providers, already grouped declarations, version matching, repeated
initialization, cache rebuilding, and cleanup preserving other Harmony owners.

**Extraction verification (2026-10-21):** Unity imported and compiled both
Editor assemblies. All 36 NUnit cases passed with 384 assertions, zero failures,
and zero skips in the running Editor. The normal Unity TestRunner was cancelled
at its save prompt for an unsaved scene; the same synchronous NUnit cases ran
through Pipeline `script run` with an inline main-thread dispatcher instead.
The scene's handle, path, load state, and dirty state were unchanged. This run
does not exercise Unity TestRunner's scene orchestration or log hooks.

Fresh command discovery contained all 15 handlers, and `render_graph graphs`
reached the original handler's expected no-capture response. CompilationPipeline
reported both package assemblies as Editor-only and neither in the player
assembly list. Only the package's new Harmony owner remained registered. The
initial compile request timed out during reload; a fresh request after reconnect
reported up-to-date scripts with no compilation errors. Installation without
the prerequisites and with upgraded package versions was not exercised. Player
builds and PlayMode tests were omitted because the package has no runtime code.

**Remove when:** An upstream SRP Core release uses accepted grouped declarations,
or Pipeline accepts these legacy declarations itself. First verify all 15
handlers are discovered without the patch, then remove its assembly and tests
and record the fixed upstream versions here and in the changelog. No upstream
issue reference is recorded yet.

## Verification

For a Git installation, add `com.martincalander.alphacompatibility` to the host
manifest's `testables` array. Embedded packages expose their tests directly.
Use the host's Unity Test Framework to run the EditMode assembly
`MartinCalander.AlphaCompatibility.Editor.Tests`.

Discover the host's current Pipeline command schemas before invoking commands.
For the verified versions, the focused checks are:

```sh
unity --format json command script recompile --project-path "$PROJECT"
unity --format json command script recompile_status --project-path "$PROJECT"
unity --format json command test run --mode editor --filter MartinCalander.AlphaCompatibility.Editor.Tests --filter_type assembly --project-path "$PROJECT"
unity --format json command --group-by flat --detail full --project-path "$PROJECT"
unity --format json command render_graph graphs --project-path "$PROJECT"
```

Wait for compilation to finish and inspect nested command results. Discovery
must include all 15 handlers from `Unity.RenderPipelines.Core.Editor.CLI`.
A command listing also contains generated group entries; do not count those as
handlers. Integration tests are only available for the affected package pair.

## Adding and retiring fixes

Add each workaround under its own `Editor/` area with focused tests and the
smallest applicable version constraints. Add a record here describing the
symptom, affected Editor and package versions, upstream issue when known,
implementation scope, verification evidence, and explicit removal condition.
Update `CHANGELOG.md` when a fix is added, widened, changed, or removed.

Review the records whenever the Editor or related packages are upgraded. Keep
retired records with the upstream version that fixed the issue, and delete
obsolete patch code after verifying the unpatched behavior. Version changes
must not silently broaden a workaround's scope.
