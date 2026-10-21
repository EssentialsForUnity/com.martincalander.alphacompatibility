# Third-Party Notices

This package contains no bundled third-party source code or binaries. The
proprietary license applies to Martin Calander's original package materials.
External dependencies retain their respective ownership and license terms and
are excluded from Martin Calander's ownership claim.

## External dependencies

- Unity Pipeline and Scriptable Render Pipeline Core are supplied separately by
  Unity Technologies under their own package licenses. This package adapts
  command declarations in memory and does not redistribute those packages.
- [Git Submodule Manager](https://github.com/martincalander/GitSubmoduleForUnity)
  supplies the existing Editor-only `GitSubmoduleManager.Harmony.dll` plugin.
  Its Mono and CoreCLR configurations select Harmony or HarmonyX and the
  associated dependencies. See its
  [Third Party Notices](https://github.com/martincalander/GitSubmoduleForUnity/blob/main/Third%20Party%20Notices.md)
  for the original authors, licenses, and binary provenance. This package does
  not copy or redistribute those binaries.
- NUnit is supplied by Unity Test Framework for the optional Editor tests.
