import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

loader = importlib.util.spec_from_file_location("source_fixes", Path(__file__).with_name("apply-source-fixes.py"))
fixes = importlib.util.module_from_spec(loader)
loader.loader.exec_module(fixes)


class SourceFixTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.project = Path(self.directory.name)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 7000.0.0a11\n")
        (self.project / "Packages").mkdir()
        self.dependencies = {}

    def package(self, name="com.example.fixture", embedded=False):
        source = self.project / ("Packages/" + name if embedded else "Library/PackageCache/" + name + "@123")
        source.mkdir(parents=True)
        (source / "package.json").write_text(json.dumps({"name": name, "version": "1.0.0"}))
        (source / "LICENSE.md").write_text("Original vendor license\n")
        (source / "target.cs").write_text("before\n")
        (source / "target.cs.meta").write_text("guid: untouched\n")
        self.dependencies[name] = "1.0.0"
        (self.project / "Packages/manifest.json").write_text(json.dumps({"dependencies": self.dependencies}))
        spec = {"id": name, "package": {"name": name, "version": "1.0.0"}, "unityVersionPrefix": "7000.", "files": [{
            "path": "target.cs", "originalSha256": fixes.sha(b"before\n"), "patchedSha256": fixes.sha(b"after\n"),
            "edits": [{"startByte": 0, "deleteBytes": 6, "insert": "after"}]}]}
        return source, spec

    def test_check_does_not_embed_or_write(self):
        source, spec = self.package()
        before = fixes.tree_signature(self.project)
        plans, report = fixes.make_plan(self.project, [spec])
        self.assertEqual(report[0]["state"], "patch-required")
        self.assertEqual(fixes.tree_signature(self.project), before)

    def test_apply_preserves_cache_license_metadata_and_is_idempotent(self):
        source, spec = self.package()
        cache = fixes.tree_signature(source)
        plans, _ = fixes.make_plan(self.project, [spec])
        fixes.apply_plan(self.project, plans)
        embedded = self.project / "Packages/com.example.fixture"
        self.assertEqual((embedded / "target.cs").read_bytes(), b"after\n")
        self.assertEqual((embedded / "LICENSE.md").read_bytes(), (source / "LICENSE.md").read_bytes())
        self.assertEqual((embedded / "target.cs.meta").read_bytes(), (source / "target.cs.meta").read_bytes())
        self.assertEqual(fixes.tree_signature(source), cache)
        before = fixes.tree_signature(self.project)
        plans, report = fixes.make_plan(self.project, [spec])
        fixes.apply_plan(self.project, plans)
        self.assertEqual(report[0]["state"], "already-patched")
        self.assertEqual(fixes.tree_signature(self.project), before)

    def test_embedded_unrelated_customizations_survive(self):
        source, spec = self.package(embedded=True)
        (source / "custom.cs").write_text("user changes\n")
        plans, _ = fixes.make_plan(self.project, [spec])
        fixes.apply_plan(self.project, plans)
        self.assertEqual((source / "custom.cs").read_text(), "user changes\n")

    def test_embedding_omits_upstream_agent_guidance_at_every_depth(self):
        for embedded in (False, True):
            with self.subTest(embedded=embedded):
                name = "com.example.guidance" + str(int(embedded))
                source, spec = self.package(name, embedded=embedded)
                for folder in [source, source / "Nested"]:
                    folder.mkdir(exist_ok=True)
                    (folder / "AGENTS.md").write_text("Upstream agent instructions\n")
                    (folder / "AGENTS.md.meta").write_text("guid: instructions\n")
                    (folder / "README.md").write_text("Keep vendor documentation\n")
                original = fixes.tree_signature(source)
                plans, _ = fixes.make_plan(self.project, [spec])
                fixes.apply_plan(self.project, plans)
                destination = self.project / "Packages" / name
                self.assertFalse(list(destination.rglob("AGENTS.md")))
                self.assertFalse(list(destination.rglob("AGENTS.md.meta")))
                self.assertEqual((destination / "Nested/README.md").read_text(), "Keep vendor documentation\n")
                self.assertEqual((destination / "LICENSE.md").read_text(), "Original vendor license\n")
                self.assertEqual((destination / "target.cs.meta").read_text(), "guid: untouched\n")
                if not embedded:
                    self.assertEqual(fixes.tree_signature(source), original)

    def test_known_patched_cache_is_embedded_without_byte_edits(self):
        source, spec = self.package()
        (source / "target.cs").write_bytes(b"after\n")
        cache = fixes.tree_signature(source)
        plans, report = fixes.make_plan(self.project, [spec])
        self.assertEqual(report[0]["state"], "already-patched")
        self.assertFalse(plans["com.example.fixture"]["changes"])
        fixes.apply_plan(self.project, plans)
        destination = self.project / "Packages/com.example.fixture"
        self.assertEqual(fixes.tree_signature(destination), cache)
        self.assertEqual(fixes.tree_signature(source), cache)
        before = fixes.tree_signature(self.project)
        plans, _ = fixes.make_plan(self.project, [spec])
        fixes.apply_plan(self.project, plans)
        self.assertEqual(fixes.tree_signature(self.project), before)

    def test_unknown_target_refuses_without_changing_any_package(self):
        first, one = self.package()
        second, two = self.package("com.example.second", embedded=True)
        (second / "target.cs").write_text("custom target\n")
        before = fixes.tree_signature(self.project)
        with self.assertRaisesRegex(fixes.FixError, "Unknown source"):
            fixes.make_plan(self.project, [one, two])
        self.assertEqual(fixes.tree_signature(self.project), before)

    def test_already_patched_customized_hash_is_preserved(self):
        source, spec = self.package(embedded=True)
        (source / "target.cs").write_bytes(b"custom plus fix\n")
        spec["files"][0]["alreadyPatchedSha256"] = [fixes.sha(b"custom plus fix\n")]
        plans, report = fixes.make_plan(self.project, [spec])
        fixes.apply_plan(self.project, plans)
        self.assertEqual(report[0]["state"], "already-patched")
        self.assertEqual((source / "target.cs").read_bytes(), b"custom plus fix\n")

    def test_wrong_unity_and_package_versions_refuse(self):
        source, spec = self.package()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.6.0f1\n")
        with self.assertRaisesRegex(fixes.FixError, "does not apply"):
            fixes.make_plan(self.project, [spec])
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 7000.0.0a11\n")
        spec["package"]["version"] = "2.0.0"
        with self.assertRaisesRegex(fixes.FixError, "exact registry version"):
            fixes.make_plan(self.project, [spec])

    def test_bad_result_hash_and_overlapping_edits_refuse(self):
        source, spec = self.package()
        file = spec["files"][0]
        file["patchedSha256"] = "incorrect"
        with self.assertRaisesRegex(fixes.FixError, "Patched hash mismatch"):
            fixes.make_plan(self.project, [spec])
        file["edits"].append({"startByte": 2, "deleteBytes": 1, "insert": "x"})
        with self.assertRaisesRegex(fixes.FixError, "overlapping"):
            fixes.make_plan(self.project, [spec])

    def test_concurrent_target_change_refuses_before_activation(self):
        source, spec = self.package()
        plans, _ = fixes.make_plan(self.project, [spec])
        (source / "target.cs").write_text("changed after check\n")
        with self.assertRaisesRegex(fixes.FixError, "Source changed while staging"):
            fixes.apply_plan(self.project, plans)
        self.assertFalse((self.project / "Packages/com.example.fixture").exists())

    def test_activation_failure_rolls_back_all_existing_packages(self):
        first, one = self.package(embedded=True)
        second, two = self.package("com.example.second", embedded=True)
        before = fixes.tree_signature(self.project)
        plans, _ = fixes.make_plan(self.project, [one, two])
        rename = Path.rename
        def fail_second_stage(path, destination):
            if path.name == "stage-1":
                raise OSError("simulated activation failure")
            return rename(path, destination)
        with mock.patch.object(Path, "rename", fail_second_stage):
            with self.assertRaisesRegex(OSError, "simulated"):
                fixes.apply_plan(self.project, plans)
        self.assertEqual(fixes.tree_signature(self.project), before)

    def test_path_escape_is_rejected(self):
        source, spec = self.package()
        spec["files"][0]["path"] = "../target.cs"
        with self.assertRaisesRegex(fixes.FixError, "Unsafe"):
            fixes.make_plan(self.project, [spec])


if __name__ == "__main__":
    unittest.main()
