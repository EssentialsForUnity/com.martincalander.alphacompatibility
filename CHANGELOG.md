# Changelog

## 0.1.0 - 2026-10-21

- Extracted the RenderGraph command compatibility fix and regression tests from Essentials Workspace into an Editor-only UPM package.
- Preserved all 15 original RenderGraph handlers and their metadata for Pipeline `0.9.0-exp.1` with SRP Core `17.7.0`.
- Added assembly constraints and exact version checks so this workaround only runs with its affected packages and the existing Git Submodule Manager Harmony provider.
- Added a maintained workaround record with symptoms, prerequisites, verification steps, and removal conditions.
