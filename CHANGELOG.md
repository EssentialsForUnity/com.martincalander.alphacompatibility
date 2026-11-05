# Changelog

## Unreleased

- Record the Unity 7 alpha 12 workaround review and the need to refresh embedded URP contents even when its package version is unchanged.

## 0.2.4 - 2026-11-05

- Add a separate exact-source lifecycle recipe for Pipeline 0.10.0-exp.1, preserving its upstream changes and the existing 0.9 recipe.
- Drop the three redundant immutable-metadata field edits from the 0.10 recipe because upstream now protects their owning types.
- Verify native discovery of all 15 RenderGraph handlers with the old shim excluded, and extend the lifecycle suite to the reviewed 0.10 source.

## 0.2.3 - 2026-11-05

- Resolve two removed help-box color aliases in alpha 16's native Package Manager stylesheet using its existing theme colors.
- Restrict the repair to the affected Editor, transient native stylesheet, exact expressions and unambiguous color definitions; preserve other styles and normal error reporting.
- Add regression coverage for both themes, unsupported expressions, version and asset boundaries, repeated application and patch cleanup.
- Record Addressables 4.1.2's official replacements for the earlier local compatibility patches.

## 0.2.2 - 2026-11-05

- Support explicitly reviewed source variants for packages whose contents change without a version change; retain exact input and output hash checks.
- Add Unity 7 alpha 16's URP RenderingUtils source while preserving its new upstream helpers and the required lifecycle cleanup.
- Add coverage for selecting, repeating and refusing source variants, without widening the Pipeline or RenderGraph workaround gates.

## 0.2.1 - 2026-10-22

- Exclude upstream agent instruction files and their metadata when embedding vendor packages, keeping workspace guidance at the root.
- Embed recognized patched cache sources even when no source edits are required, so the fix survives a cache rebuild.

## 0.2.0 - 2026-10-22

- Share the version-gated Aseprite tile identity workaround and import regression tests.
- Add explicit, content-checked Pipeline and URP source fixes for Unity 7 lifecycle behavior, preserving upstream package metadata and licenses.
- Add focused lifecycle coverage and document which compatibility fixes remain with their owning packages.
- Keep optional integrations inactive when their affected packages are absent or outside the supported versions.

## 0.1.1 - 2026-10-22

- Made the repository public while retaining the proprietary license.
- Documented installation as a Git dependency following the default branch and migration from embedded copies, submodules, or host shims.

## 0.1.0 - 2026-10-21

- Extracted the RenderGraph command compatibility fix and regression tests from Essentials Workspace into an Editor-only UPM package.
- Preserved all 15 original RenderGraph handlers and their metadata for Pipeline `0.9.0-exp.1` with SRP Core `17.7.0`.
- Added assembly constraints and exact version checks so this workaround only runs with its affected packages and the existing Git Submodule Manager Harmony provider.
- Added a maintained workaround record with symptoms, prerequisites, verification steps, and removal conditions.
