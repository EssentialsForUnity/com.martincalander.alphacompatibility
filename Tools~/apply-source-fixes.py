#!/usr/bin/env python3
"""Apply reviewed source fixes to embedded copies of installed Unity packages.

Run before opening Unity. Source files are supplied by the project's licensed
package installation. This tool never writes to Library/PackageCache.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile


class FixError(Exception):
    pass


class TransactionRecoveryError(FixError):
    pass


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def safe_path(root, relative):
    path = Path(relative)
    if path.is_absolute() or ".." in path.parts or not path.parts:
        raise FixError("Unsafe package path: " + relative)
    target = root / path
    if not target.resolve().is_relative_to(root.resolve()) or target.is_symlink():
        raise FixError("Source target must be a regular file inside its package: " + str(target))
    return target


def patch_bytes(original, spec):
    digest = sha(original)
    accepted = [spec["patchedSha256"]] + spec.get("alreadyPatchedSha256", [])
    if digest in accepted:
        return original, "already-patched"
    if digest != spec["originalSha256"]:
        raise FixError("Unknown source content for " + spec["path"] + ": " + digest)
    edits = sorted(spec["edits"], key=lambda edit: edit["startByte"])
    end = 0
    for edit in edits:
        start, count = edit["startByte"], edit["deleteBytes"]
        if not isinstance(start, int) or not isinstance(count, int) or start < end or count < 0 or start + count > len(original):
            raise FixError("Invalid or overlapping byte edits for " + spec["path"])
        end = start + count
    result = original
    for edit in reversed(edits):
        start, count = edit["startByte"], edit["deleteBytes"]
        result = result[:start] + edit["insert"].encode("utf-8") + result[start + count:]
    if sha(result) != spec["patchedSha256"]:
        raise FixError("Patched hash mismatch for " + spec["path"])
    return result, "patch-required"


def find_source(project, package):
    name, version = package["name"], package["version"]
    if not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", name):
        raise FixError("Invalid package name: " + name)
    embedded = project / "Packages" / name
    if embedded.exists():
        candidates = [embedded]
        if embedded.is_symlink():
            raise FixError("Refusing to replace a linked package: " + str(embedded))
    else:
        dependency = read_json(project / "Packages/manifest.json").get("dependencies", {}).get(name)
        if dependency != version:
            raise FixError(name + " must be installed at the exact registry version " + version + " or embedded")
        candidates = []
        for path in (project / "Library/PackageCache").glob(name + "@*"):
            manifest = path / "package.json"
            if manifest.is_file():
                data = read_json(manifest)
                if data.get("name") == name and data.get("version") == version:
                    candidates.append(path)
        if len(candidates) != 1:
            raise FixError("Expected one installed source for " + name + " " + version + "; resolve packages in Unity first")
    source = candidates[0]
    data = read_json(source / "package.json")
    if data.get("name") != name or data.get("version") != version:
        raise FixError("Package version mismatch at " + str(source))
    return source, embedded


def make_plan(project, specs):
    version_text = (project / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    match = re.search(r"^m_EditorVersion:\s*(\S+)", version_text, re.MULTILINE)
    if match is None:
        raise FixError("Cannot read the project's Unity version")
    packages = {}
    reports = []
    for spec in specs:
        if not match.group(1).startswith(spec["unityVersionPrefix"]):
            raise FixError(spec["id"] + " does not apply to Unity " + match.group(1))
        source, destination = find_source(project, spec["package"])
        package = packages.setdefault(spec["package"]["name"], {
            "source": source, "destination": destination, "changes": {}, "originals": {}})
        if package["source"] != source:
            raise FixError("Fixes resolve conflicting package sources")
        for file in spec["files"]:
            target = safe_path(source, file["path"])
            original = target.read_bytes()
            if file["path"] in package["originals"]:
                raise FixError("Selected fixes overlap on " + file["path"])
            patched, state = patch_bytes(original, file)
            package["originals"][file["path"]] = original
            if patched != original:
                package["changes"][file["path"]] = patched
            reports.append({"fix": spec["id"], "package": spec["package"]["name"], "file": file["path"], "state": state})
    return packages, reports


def tree_signature(root):
    """Detect concurrent changes before replacing an existing embedded package."""
    result = {}
    for path in sorted(root.rglob("*")):
        relative = str(path.relative_to(root))
        if path.is_symlink():
            result[relative] = ("link", os.readlink(path))
        elif path.is_file():
            result[relative] = ("file", sha(path.read_bytes()), path.stat().st_mode)
        elif path.is_dir():
            result[relative] = ("directory",)
    return result


def activate(plans, transaction):
    activated = []
    try:
        for index, plan in enumerate(plans):
            destination, staged = plan["destination"], plan["staged"]
            backup = transaction / ("backup-" + str(index))
            existed = destination.exists()
            if existed:
                destination.rename(backup)
            try:
                staged.rename(destination)
            except BaseException as activation_error:
                if existed:
                    try:
                        backup.rename(destination)
                    except BaseException as restore_error:
                        raise TransactionRecoveryError(
                            "Activation and restoration failed. Recovery copies are preserved at " +
                            str(transaction) + ": " + str(restore_error)) from activation_error
                raise
            activated.append((destination, backup if existed else None))
    except BaseException as original_error:
        try:
            for destination, backup in reversed(activated):
                shutil.rmtree(destination)
                if backup is not None:
                    backup.rename(destination)
        except BaseException as rollback_error:
            raise TransactionRecoveryError(
                "Activation and rollback failed. Recovery copies are preserved at " +
                str(transaction) + ": " + str(rollback_error)) from original_error
        raise


def apply_plan(project, packages):
    changed = [p for p in packages.values() if p["changes"] or p["source"] != p["destination"]]
    if not changed:
        return
    # A single transaction holds every staged package and rollback copy on the
    # same volume. No destination changes until all copies and hashes validate.
    transaction = Path(tempfile.mkdtemp(prefix=".alpha-source-fixes-", dir=project / "Packages"))
    preserve_recovery = False
    try:
        for index, plan in enumerate(changed):
            source = plan["source"]
            plan["signature"] = tree_signature(source)
            staged = transaction / ("stage-" + str(index))
            # Workspace instructions belong at the host root. Do not import
            # upstream agent guidance or its Unity metadata into Packages.
            shutil.copytree(source, staged, symlinks=True,
                            ignore=shutil.ignore_patterns("AGENTS.md", "AGENTS.md.meta"))
            for relative, data in plan["changes"].items():
                target = safe_path(staged, relative)
                if target.read_bytes() != plan["originals"][relative]:
                    raise FixError("Source changed while staging " + relative)
                target.write_bytes(data)
            plan["staged"] = staged
        for plan in changed:
            if tree_signature(plan["source"]) != plan["signature"]:
                raise FixError("Package changed while staging: " + str(plan["source"]))
            if plan["source"] != plan["destination"] and plan["destination"].exists():
                raise FixError("Embedded package appeared while staging: " + str(plan["destination"]))
        activate(changed, transaction)
    except TransactionRecoveryError:
        preserve_recovery = True
        raise
    finally:
        if not preserve_recovery:
            shutil.rmtree(transaction)


def load_specs(root, ids):
    specs = {}
    for path in root.glob("*/fix.json"):
        spec = read_json(path)
        if spec["id"] in specs:
            raise FixError("Duplicate fix ID: " + spec["id"])
        specs[spec["id"]] = spec
    unknown = set(ids) - specs.keys()
    if unknown:
        raise FixError("Unknown fix IDs: " + ", ".join(sorted(unknown)))
    return [specs[key] for key in dict.fromkeys(ids)]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path, help="Exact Unity project root; close its Editor before applying")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="Validate all selected fixes without writing files")
    mode.add_argument("--apply", action="store_true", help="Embed installed packages when needed and apply reviewed source edits")
    parser.add_argument("--fix", action="append", required=True, help="Fix ID to apply; repeat for multiple fixes")
    args = parser.parse_args(argv)
    try:
        project = args.project.expanduser().resolve(strict=True)
        specs = load_specs(Path(__file__).resolve().parents[1] / "SourcePatches~", args.fix)
        packages, reports = make_plan(project, specs)
        if args.apply:
            apply_plan(project, packages)
        print(json.dumps({"success": True, "mode": "apply" if args.apply else "check", "project": str(project), "files": reports}, indent=2))
        return 0
    except (FixError, OSError, ValueError, KeyError) as error:
        print(json.dumps({"success": False, "error": str(error)}, indent=2), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
