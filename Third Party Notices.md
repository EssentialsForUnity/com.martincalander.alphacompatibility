# Third-Party Notices

Martin Calander owns the original compatibility code, tooling, tests, and
documentation in this package. Third-party materials and any upstream-derived
portions are excluded from that ownership claim and retain their own terms.

## Unity source fixes

The source-fix tool reads Unity package sources already installed in the user's
project. It embeds a local copy with the complete original license and metadata,
then applies minimal compatibility edits. This repository does not bundle the
complete Unity package sources or binaries. Source hashes and byte edit records
identify the reviewed upstream versions; they do not grant rights to Unity's
software or replace its license.

- `SourcePatches~/Pipeline` targets `com.unity.pipeline` version `0.9.0-exp.1`,
  copyright Unity Technologies, licensed under the Unity Terms of Service. Its
  original notice is preserved in `SourcePatches~/Pipeline/UPSTREAM-LICENSE.md`.
- `SourcePatches~/URP` targets `com.unity.render-pipelines.universal` version
  `17.7.0`, copyright Unity Technologies ApS, licensed under the Unity Companion
  License for Unity-dependent projects. Its original notice is preserved in
  `SourcePatches~/URP/UPSTREAM-LICENSE.md`.

Any Unity-derived material in these records retains the relevant upstream terms
and is expressly excluded from the proprietary ownership claim. The embedded
packages remain Unity packages with their original licenses and versions.

## External dependencies

- Unity Pipeline, Scriptable Render Pipeline Core, URP, and Aseprite are supplied
  separately by Unity Technologies under their respective package licenses.
  Automatic Editor workarounds adapt their behavior without redistributing their
  source packages or binaries.
- [Git Submodule Manager](https://github.com/martincalander/GitSubmoduleForUnity)
  supplies the existing Editor-only `GitSubmoduleManager.Harmony.dll` plugin.
  Its Mono and CoreCLR configurations select Harmony or HarmonyX and the
  associated dependencies. See its
  [Third Party Notices](https://github.com/martincalander/GitSubmoduleForUnity/blob/main/Third%20Party%20Notices.md)
  for the original authors, licenses, and binary provenance. This package does
  not copy or redistribute those binaries.
- NUnit is supplied by Unity Test Framework for the optional Editor tests.
- The Aseprite test fixture is generated from synthetic tile data owned by
  Martin Calander; no game art or third-party sample image is included.
