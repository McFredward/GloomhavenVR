"""Whole Shader progress stays open across local compiler pass completion."""
import json
from pathlib import Path
from types import SimpleNamespace
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser
import stage_plan


class ShaderTotalTests(unittest.TestCase):
    def setUp(self):
        self.row = {"id": "build", "status": "running"}
        self.clock = 100

    def report(self, phase, done=None, total=None, unit="tasks", detail="Actual observed work", status="progress", owner="player"):
        self.clock += 1
        value = dict(phase=phase, done=done, total=total, unit=unit, detail=detail,
                     percent=None if done is None or not total else 100 * done / total, updatedAt=self.clock)
        self.row["progress"] = stage_plan.advance(self.row, value, owner, status)
        return next(item for group in self.row["progress"]["buildOverview"]["groups"] for item in group["operations"] if item["id"] == owner)

    def start(self, owner="player"):
        self.report("operation:" + owner, status="start", owner=owner)
        self.report("unity-work-invocation", 0, 1, "invocations", status="start", owner=owner)
        self.report("unity-work-plan", 0, 100, "assets", "[asset-coverage] Known assets", owner=owner)
        self.report("unity-work-stage:method", 0, 1, status="start", owner=owner)

    def test_captured_last_pass_never_closes_all_shaders_before_actual_handoff(self):
        self.start()
        self.report("unity-player-shaders", 250, 251, detail="[shader-coverage] 250 declared names observed")
        for detail, status in (("[shader-pass:1557] TextCore variants ready", "progress"),
                               ("[shader-pass:1557] TextCore pass finished; completed passes1557", "complete"),
                               ("[shader-pass:1558] Same Shader; another pass", "progress")):
            compiler = self.report("unity-shader-compile", 6, 6, "variants", detail, status)["compiler"]
            self.assertEqual((compiler["status"], compiler["percent"], compiler["closed"]), ("running", 80., False))
            self.assertEqual((compiler["aggregate"]["done"], compiler["aggregate"]["total"]), (250, 250))
            self.assertEqual(compiler["aggregate"]["scope"], "sources")
            self.assertLess(compiler["percent"], 100)
            self.assertEqual(compiler["currentPass"]["status"], "complete" if status == "complete" else "running")
        finished = self.report("unity-player-platform-handoff", 1, 1, status="complete")["compiler"]
        self.assertEqual((finished["status"], finished["percent"], finished["closed"]), ("complete", 100., True))

    def test_current_unseen_name_partial_contributes_without_resetting_whole_phase(self):
        self.start()
        self.report("unity-player-shaders", 1, 5, detail="[shader-coverage] 1 /4 names observed")
        first = self.report("unity-shader-compile", 2, 10, "variants", "[shader-source:unseen]")["compiler"]["aggregate"]["percent"]
        later = self.report("unity-shader-compile", 7, 10, "variants", "[shader-source:unseen]")["compiler"]["aggregate"]["percent"]
        self.assertGreater(later, first)
        repeated = self.report("unity-shader-compile", 0, 20, "variants", "[shader-source:observed]")["compiler"]["aggregate"]["percent"]
        self.assertEqual(repeated, later)

    def test_content_bank_counts_known_names_and_waits_for_native_return(self):
        self.start("content-bank")
        self.report("unity-bank-shaders", 1, 3, detail="[shader-coverage] 1/2names observed", owner="content-bank")
        first = self.report("unity-shader-compile", 1, 2, "variants", "[shader-source:unseen]", owner="content-bank")["compiler"]
        self.assertAlmostEqual(first["percent"], 80 * (1 + 1 / 3) / 2)
        self.report("unity-bank-shaders", 2, 3, owner="content-bank")
        last = self.report("unity-shader-compile", 2, 2, "variants", status="complete", owner="content-bank")["compiler"]
        self.assertEqual((last["percent"], last["status"]), (80., "running"))
        native = self.report("unity-addressables-build", 1, 1, status="complete", owner="content-bank")["compiler"]
        self.assertEqual((native["percent"], native["status"]), (100., "complete"))

    def test_unknown_namespace_never_borrows_current_variant_denominator(self):
        self.start()
        compiler = self.report("unity-shader-compile", 999998, 999999, "variants", status="complete")["compiler"]
        self.assertIsNone(compiler["aggregate"]["total"])
        self.assertIsNone(compiler["aggregate"]["percent"])
        self.assertFalse(compiler["closed"])
        self.assertLess(compiler["currentPass"]["percent"], 100)

    def test_finite_bank_schedule_stays_global_when_variant_counter_resets(self):
        self.report("operation:mod-banks", status="start", owner="mod-banks")
        self.report("prepare-substage:mod-resource-banks", 3, 4, status="start", owner="mod-banks")
        first = self.report("unity-shader-compile", 10, 10, "variants", status="complete", owner="mod-banks")["compiler"]
        next_pass = self.report("unity-shader-compile", 0, 500, "variants", owner="mod-banks")["compiler"]
        self.assertEqual(first["aggregate"]["scope"], "banks")
        self.assertEqual(first["percent"], next_pass["percent"])
        self.assertLess(first["percent"], 100)
        self.assertEqual((first["status"], next_pass["status"]), ("running", "running"))

    def test_failed_pass_remains_visible_before_whole_owner_exits(self):
        self.start()
        self.report("unity-player-shaders", 1, 3)
        compiler = self.report("unity-shader-compile", 3, 10, "variants", status="failed")["compiler"]
        self.assertEqual(compiler["currentPass"]["status"], "failed")
        self.assertEqual(compiler["aggregate"]["status"], "running")
        self.assertFalse(compiler["closed"])

    def test_bank_parser_reuses_unique_declaration_census_without_counting_passes_twice(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            for name, declaration in (("One", "Original/One"), ("Duplicate", "Original/One"), ("Two", "Original/Two")):
                target = project / ("Assets/" + name + ".shader")
                target.parent.mkdir(exist_ok=True)
                target.write_text('Shader "' + declaration + '" {}')
            parser = ProgressParser()
            source = "unity-build.log"
            parser.parse("GHVRQ_PROGRESS " + json.dumps(dict(schema=1, phase="operation:content-bank", operation="content-bank", status="start")), source)
            parser.import_coverage[source] = SimpleNamespace(known={"Assets/One.shader", "Assets/Duplicate.shader", "Assets/Two.shader"}, plan={"project": str(project)})
            start = parser._shader_coverage(source)
            self.assertEqual((start["phase"], start["done"], start["total"]), ("unity-bank-shaders", 0, 3))
            first = parser._shader_coverage(source, finished="Original/One")
            self.assertEqual((first["done"], first["total"]), (1, 3))
            self.assertIsNone(parser._shader_coverage(source, finished="Original/One"))
            self.assertIsNone(parser._shader_coverage(source, finished="Hidden/Builtin"))


if __name__ == "__main__": unittest.main()
