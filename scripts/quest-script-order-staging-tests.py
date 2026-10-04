#!/usr/bin/env python3
"""Check production original-order extraction/staging and injected defects.

This proves the portable serialized reader/import staging, not headset UI output
or Unity's imported MonoScript application (checked by its separate Editor lane).
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


def run(source: Path, root: Path) -> int:
    sys.path.insert(0, str(source))
    import script_order
    script_order.SOURCE_ROOT = root
    path = root / "tests/QuestScriptOrderStaging.Tests/test_staging.py"
    spec = importlib.util.spec_from_file_location("quest_script_order_tests", path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromModule(module))
    return 0 if result.wasSuccessful() else 1


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--builder-source", type=Path, default=root / "tools/quest-builder")
    parser.add_argument("--no-controls", action="store_true")
    parser.add_argument("--original-bank", type=Path)
    parser.add_argument("--independent-records", type=Path)
    args = parser.parse_args()
    if run(args.builder_source, root):return 1
    import script_order
    if bool(args.original_bank) != bool(args.independent_records):
        parser.error("--original-bank and --independent-records must be supplied together")
    if args.original_bank:
        expected = json.loads(args.independent_records.read_text())
        actual = script_order.read_script_orders(args.original_bank)
        if actual != expected:raise RuntimeError("Original reader disagrees with independent record evidence.")
        print("Exact independent original-record agreement:", len(actual), "records")
    source = (args.builder_source / "script_order.py").read_text()
    controls = (
        ("zero-order-omitted", "entries.append(entry)", "if entry['executionOrder'] != 0: entries.append(entry)"),
        ("original-order-replaced", '\"executionOrder\": rows[0][\"executionOrder\"]', '\"executionOrder\": 0'),
        ("source-dll-proof-skipped", " or digest(dll) != digest(original)", ""),
        ("conflicting-duplicates-trusted", 'if len({r["executionOrder"] for r in rows}) != 1:', 'if False:'),
        ("nested-type-invented", ' and not t["nested"]', ""),
        ("unknown-pointer-trusted", "if references - mapped:", "if False:"),
        ("package-any-disabled", '"package": plugin[3] in disabled', '\"package\": \"enabled: 0\" in plugin[2]'),
        ("missing-original-allowed", "if referenced:\n", "if False:\n"),
        ("duplicate-object-allowed", "path_id in ids or ", ""),
    )
    proof = {"schema": 1, "sourceSha256": hashlib.sha256(source.encode()).hexdigest(),
             "productionPassed": True, "negativeControls": [], "positiveControlPassed": False}
    if not args.no_controls:
        with tempfile.TemporaryDirectory(prefix="quest-order-controls-") as temporary:
            folder = Path(temporary)
            (folder / "storage.py").write_text((args.builder_source / "storage.py").read_text())
            for name, before, after in (*controls, ("inert-comment", '"""Recover owned Unity', '"""Recover owned Unity')):
                if source.count(before) != 1:raise RuntimeError("Control anchor is not unique: " + name)
                mutated = source.replace(before, after, 1)
                if name == "inert-comment":mutated += "\n# Inert positive control.\n"
                (folder / "script_order.py").write_text(mutated)
                process = subprocess.run([sys.executable, str(Path(__file__).resolve()), "--builder-source", str(folder), "--no-controls"],
                                         text=True, capture_output=True)
                positive = name == "inert-comment"
                if (process.returncode == 0) != positive:
                    print(process.stdout + process.stderr)
                    raise RuntimeError("Unexpected control result: " + name)
                if positive:proof["positiveControlPassed"] = True
                else:proof["negativeControls"].append(name)
                print("Control verified:", name)
        evidence = root / ".planning/debug/quest-script-order-staging/results.json"
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps(proof, indent=2) + "\n")
        print("Proof:", evidence)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
