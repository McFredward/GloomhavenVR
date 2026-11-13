"""Player DAG/progress boundaries witnessed in the running231755 capture."""
import json
import os
from pathlib import Path
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import PlayerBeePlan, ProgressParser

SOURCE = "unity-build-3367946efeab.log"
PROGRAM = ('Starting: C:\\Program Files\\Unity\\Hub\\Editor\\2021.3.5f1\\Editor\\Data\\Tools\\netcorerun\\netcorerun.exe '
           '"C:\\Program Files\\Unity\\Hub\\Editor\\2021.3.5f1\\Editor\\Data\\PlaybackEngines\\AndroidPlayer\\AndroidPlayerBuildProgram.exe" '
           '"Library/Bee/Player60404965.dag.json" "Library/Bee/Player60404965-inputdata.json" "Library/Bee/buildprogram2.traceevents"')
BACKEND = ('Starting: C:\\owned\\QuestBuildHost.exe --profile="Library/Bee/backend_profiler3.traceevents" '
           '--stdin-canary --dagfile="Library/Bee/Player60404965.dag" --continue-on-failure '
           '--dagfilejson="Library/Bee/Player60404965.dag.json" Player')
RELATIVE = "Library/Bee/Player60404965.dag.json"


def event(phase, **values):
    return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": phase, **values})


def graph(project, *, native=True):
    # Same NamedNodes/Nodes/ToBuildDependencies/ToUseDependencies format as the
    # retained pinned2021.3 Android graph. Target closure excludes unused nodes.
    annotations = ["CopyFiles Data/level0", "IL2CPP_CodeGen Library/Bee/convert.traceevents",
                   "C_Android_arm64 Library/Bee/Game.o" if native else "WriteText Library/Bee/manifest",
                   "Link_Android_arm64 Library/Bee/libil2cpp.so" if native else "CopyFiles Data/level1",
                   "Player", "all_tundra_nodes", "C_Android_arm64 unrelated/unused.o"]
    nodes = [{"Annotation": annotation} for annotation in annotations]
    nodes[4].update(ToBuildDependencies=[0, 1, 2], ToUseDependencies=[3])
    value = {"Nodes": nodes, "NamedNodes": {"Player": 4, "all_tundra_nodes": 5},
             "StructuredLogFileName": "Library/Bee/tundra.log.json"}
    path = project / RELATIVE; path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")
    return path, value


def result(index=2, annotation="C_Android_arm64 Library/Bee/Game.o", done=3, queued=5, code=0):
    return {"msg": "noderesult", "index": index, "annotation": annotation,
            "processed_node_count": done, "number_of_nodes_ever_queued": queued, "exitcode": code}


def log(project, *rows, append=False):
    path = project / "Library/Bee/tundra.log.json"
    with path.open("a" if append else "w", encoding="utf-8") as stream:
        if not append: stream.write(json.dumps({"msg": "init", "dagFile": RELATIVE[:-5], "targets": ["Player"]}) + "\n")
        for row in rows: stream.write(json.dumps(row) + "\n")
    return path


def profile(project, *, timestamp=None):
    path = project / "Library/Bee/backend_profiler3.traceevents"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"name": "DriverInitData", "ts": time.time_ns() // 1000 if timestamp is None else timestamp}) + "\n")
    return path


def parser():
    value = ProgressParser()
    value.parse(event("operation:player", operation="player", status="start"), "build.log")
    return value


class PlayerGraphCounterTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.project = Path(self.directory.name)

    def start(self, *, native=True):
        graph(self.project, native=native)
        profile(self.project)
        value = parser(); value.parse(BACKEND, SOURCE)
        value.parse("WorkingDir: " + str(self.project), SOURCE)
        return value

    def test_target_closure_is_finite_and_excludes_unrelated_nodes(self):
        graph(self.project)
        value = PlayerBeePlan(self.project, RELATIVE, "fixture")
        self.assertEqual(value.total, 5)
        self.assertEqual(value.counts, {"staging": 2, "il2cpp": 1, "native": 2})
        self.assertTrue(value.qualified)
        self.assertEqual((value.fields()["done"], value.fields()["total"]), (0, 6))

    def test_real_processed_count_includes_cached_nodes_not_just_executed_lines(self):
        value = self.start()
        log(self.project, result(done=3))
        fields = value.poll_player_bee()[0]
        self.assertEqual((fields["phase"], fields["done"], fields["total"], fields["operation"]),
                         ("unity-native-build-plan", 3, 6, "player"))
        self.assertIn("backend result pending", fields["detail"])
        self.assertEqual(value.poll_player_bee(), [])
        log(self.project, result(index=3, annotation="Link_Android_arm64 Library/Bee/libil2cpp.so", done=5), append=True)
        self.assertEqual(value.poll_player_bee()[0]["done"], 5)

    def test_every_node_done_leaves_one_actual_backend_result_task(self):
        value = self.start(); log(self.project, result(done=5))
        fields = value.poll_player_bee()[0]
        self.assertEqual((fields["done"], fields["total"], fields["status"]), (5, 6, "progress"))
        value.parse("ExitCode: 0 Duration: 0s56ms", SOURCE)
        terminal = value.parse("*** Tundra build success (6.66 seconds), 2 items updated, 5 evaluated", SOURCE)
        self.assertEqual((terminal["done"], terminal["total"], terminal["status"]), (6, 6, "complete"))
        self.assertNotIn("player", value.closed_operations)
        self.assertEqual(value.poll_player_bee(), [])

    def test_exit_four_and_regeneration_do_not_complete_native_part(self):
        value = self.start(); log(self.project, result(done=5)); value.poll_player_bee()
        value.parse("ExitCode: 4 Duration: 19s", SOURCE)
        terminal = value.parse("*** Tundra requires additional run (19.14 seconds), 2 items updated, 5 evaluated", SOURCE)
        self.assertEqual((terminal["done"], terminal["total"], terminal["status"]), (5, 6, "progress"))
        old = terminal["detail"]
        value.parse(PROGRAM, SOURCE); value.parse(BACKEND, SOURCE)
        value.parse("WorkingDir: " + str(self.project), SOURCE)
        next_graph = value.poll_player_bee()[0]
        self.assertNotEqual(old.split("[bee-graph:")[1].split("]")[0],
                            next_graph["detail"].split("[bee-graph:")[1].split("]")[0])

    def test_copy_graph_cannot_credit_cpp_native_compilation(self):
        value = self.start(native=False); log(self.project, result(index=0, annotation="CopyFiles Data/level0", done=5))
        self.assertEqual(value.poll_player_bee(), [])
        local = value.parse("[5/5 0s] CopyFiles Data/level0", SOURCE)
        self.assertTrue(local["phase"].startswith("bee-actions:"))
        self.assertIn("[bee-scope:staging]", local["detail"])

    def test_failed_node_remains_failed_even_with_successful_graph_banner(self):
        value = self.start(); log(self.project, result(done=3, code=1))
        self.assertEqual(value.poll_player_bee()[0]["status"], "failed")
        value.parse("ExitCode: 0 Duration: 1s", SOURCE)
        terminal = value.parse("*** Tundra build success (1.00 seconds), 2 items updated, 5 evaluated", SOURCE)
        self.assertNotEqual(terminal["status"], "complete")

    def test_stale_wrong_graph_target_annotation_bad_or_regressing_counts_rejected(self):
        value = self.start()
        path = log(self.project, result(done=2))
        self.assertEqual(value.poll_player_bee()[0]["done"], 2)
        for row in (result(index=2, annotation="wrong", done=4), result(done=6, queued=6),
                    result(done=1), result(done=True), result(done=3, code="0")):
            log(self.project, row, append=True)
        self.assertEqual(value.poll_player_bee(), [])
        log(self.project, {"msg": "init", "dagFile": "Library/Bee/other.dag", "targets": ["Player"]}, result(done=4), append=True)
        self.assertEqual(value.poll_player_bee(), [])
        # A previous backend's structured log cannot annotate a newer DAG.
        os.utime(path, (1, 1)); self.assertEqual(value.poll_player_bee(), [])

    def test_raw_counter_uses_qualified_graph_plan_and_busy_never_adds_work(self):
        value = self.start()
        fields = value.parse("[3/5 0s] C_Android_arm64 Library/Bee/Game.o", SOURCE)
        self.assertEqual((fields["phase"], fields["done"], fields["total"]), ("unity-native-build-plan", 3, 6))
        busy = value.parse("[BUSY 33s] C_Android_arm64 Library/Bee/Game.o", SOURCE)
        self.assertEqual((busy["done"], busy["total"]), (3, 6))
        self.assertIn("33 s", busy["detail"])

    def test_buffered_node_results_remain_authoritative_when_raw_counter_is_ahead(self):
        value = self.start()
        value.parse("[5/5 0s] C_Android_arm64 Library/Bee/Game.o", SOURCE)
        log(self.project, result(index=1, annotation="IL2CPP_CodeGen Library/Bee/convert.traceevents", done=2))
        fields = value.poll_player_bee()
        self.assertEqual((fields[0]["phase"], fields[0]["status"]), ("unity-il2cpp", "complete"))
        self.assertEqual(fields[1]["done"], 5)
        log(self.project, result(done=3, code=1), append=True)
        failed = value.poll_player_bee()[0]
        self.assertEqual((failed["done"], failed["status"]), (5, "failed"))

    def test_invalid_missing_large_or_linked_graph_is_optional_only(self):
        value = parser(); value.parse(BACKEND, SOURCE)
        value.parse("WorkingDir: " + str(self.project), SOURCE)
        self.assertEqual(value.poll_player_bee(), [])
        path, original = graph(self.project)
        with patch("processes._BEE_DAG_LIMIT", 16):
            self.assertRaises(ValueError, PlayerBeePlan, self.project, RELATIVE, "fixture")
        self.assertRaises(ValueError, PlayerBeePlan, self.project, "Library/Bee/../../private.json", "fixture")
        path.unlink(); path.symlink_to(self.project / "absent.json")
        value.parse(BACKEND, SOURCE); value.parse("WorkingDir: " + str(self.project), SOURCE)
        self.assertEqual(value.poll_player_bee(), [])

    def test_preliminary_make_lump_graph_has_no_actual_compiler_plan(self):
        path, data = graph(self.project, native=False)
        data["Nodes"][2]["Annotation"] = "MakeLump Library/Bee/Game.lump.cpp"
        data["Nodes"][3]["Annotation"] = "ClassRegistrationGenerator Library/Bee/register.cpp"
        path.write_text(json.dumps(data))
        census = PlayerBeePlan(self.project, RELATIVE, "fixture")
        self.assertEqual(census.counts["native"], 2)
        self.assertFalse(census.qualified)

    def test_preliminary_registration_compile_and_libunity_link_do_not_qualify_game_cpp(self):
        path, data = graph(self.project, native=False)
        # Actual pinned Android intermediate graph304 includes these actions
        # and regenerates before graph452 schedules the generated game CPP.
        data["Nodes"][2]["Annotation"] = "Compile UnityICallRegistration Library/Bee/UnityICallRegistration.o"
        data["Nodes"][3]["Annotation"] = "Link libunity Library/Bee/unstripped/libunity.so"
        path.write_text(json.dumps(data))
        census = PlayerBeePlan(self.project, RELATIVE, "fixture")
        self.assertEqual(census.counts["native"], 2)
        self.assertFalse(census.qualified)
        value = parser(); value.parse(BACKEND, SOURCE)
        value.parse("WorkingDir: " + str(self.project), SOURCE)
        for annotation in (data["Nodes"][2]["Annotation"], data["Nodes"][3]["Annotation"]):
            fields = value.parse("[5/5 0s] " + annotation, SOURCE)
            self.assertTrue(fields["phase"].startswith("bee-actions:"))
            self.assertFalse(value.player_graphs[SOURCE].get("nativeSeen", False))

    def test_profiler_epoch_refuses_previous_log_before_new_backend_rewrites_it(self):
        graph(self.project); path = log(self.project, result(done=5))
        os.utime(path, (1, 1)); profile(self.project)
        value = parser(); value.parse(BACKEND, SOURCE); value.parse("WorkingDir: " + str(self.project), SOURCE)
        self.assertEqual(value.poll_player_bee(), [])
        log(self.project, result(done=1))
        self.assertEqual(value.poll_player_bee()[0]["done"], 1)

    def test_relative_windows_clock_uses_fresh_init_not_last_profiler_flush(self):
        graph(self.project); profile(self.project, timestamp=1254.5)
        path = log(self.project, result(done=5))
        value = parser(); value.parse(BACKEND, SOURCE); value.parse("WorkingDir: " + str(self.project), SOURCE)
        self.assertEqual(value.poll_player_bee(), [])
        log(self.project, result(done=1))
        self.assertEqual(value.poll_player_bee()[0]["done"], 1)
        # Flushing the same profiler later cannot make a valid live log stale.
        os.utime(self.project / "Library/Bee/backend_profiler3.traceevents", None)
        log(self.project, result(done=2), append=True)
        self.assertEqual(value.poll_player_bee()[0]["done"], 2)

    def test_unflushed_profiler_still_allows_current_live_structured_progress(self):
        graph(self.project); log(self.project, result(done=5))
        value = parser(); value.parse(BACKEND, SOURCE); value.parse("WorkingDir: " + str(self.project), SOURCE)
        self.assertEqual(value.poll_player_bee(), [])
        # The current backend rewrites its own init/results before its profiler
        # publishes DriverInitData. A retained5/5 result was never credited.
        log(self.project, result(done=1))
        self.assertEqual(value.poll_player_bee()[0]["done"], 1)
        path = self.project / "Library/Bee/backend_profiler3.traceevents"
        path.write_text(json.dumps({"ph": "M", "name": "process_name"}) + "\n")
        log(self.project, result(done=2), append=True)
        self.assertEqual(value.poll_player_bee()[0]["done"], 2)

    def test_new_relative_profiler_epoch_resets_old_high_counter(self):
        value = self.start(); log(self.project, result(done=5)); value.poll_player_bee()
        profile(self.project, timestamp=2345)
        self.assertEqual(value.poll_player_bee(), [])
        log(self.project, result(done=1))
        self.assertEqual(value.poll_player_bee()[0]["done"], 1)

    def test_overflowing_optional_profiler_clock_cannot_stop_live_progress(self):
        for timestamp in (10 ** 400, 1e308):
            with self.subTest(timestampType=type(timestamp).__name__):
                graph(self.project)
                value = parser(); value.parse(BACKEND, SOURCE)
                value.parse("WorkingDir: " + str(self.project), SOURCE)
                profile(self.project, timestamp=timestamp)
                log(self.project, result(done=1))
                self.assertEqual(value.poll_player_bee()[0]["done"], 1)

    def test_missing_publication_retries_bounded_and_large_graph_falls_back_to_real_cpp_queue(self):
        profile(self.project)
        value = parser(); value.parse(BACKEND, SOURCE); value.parse("WorkingDir: " + str(self.project), SOURCE)
        self.assertEqual(value.poll_player_bee(), [])
        graph(self.project); profile(self.project); log(self.project, result(done=2))
        self.assertEqual(value.poll_player_bee()[0]["done"], 2)
        other = parser(); other.parse(BACKEND, SOURCE)
        with patch("processes.PlayerBeePlan", side_effect=MemoryError):
            other.parse("WorkingDir: " + str(self.project), SOURCE)
            self.assertEqual(other.poll_player_bee(), [])
        raw = other.parse("[3/5 0s] C_Android_arm64 Library/Bee/Game.o", SOURCE)
        self.assertEqual((raw["phase"], raw["done"], raw["total"]), ("unity-native-build-plan", 3, 6))
        other.parse("ExitCode: 0 Duration: 1s", SOURCE)
        terminal = other.parse("*** Tundra build success (1.00 seconds), 2 items updated, 5 evaluated", SOURCE)
        self.assertEqual((terminal["done"], terminal["total"], terminal["status"]), (6, 6, "complete"))

    def test_closed_player_never_reopens_from_live_native_telemetry(self):
        value = self.start(); log(self.project, result(done=4))
        value.parse(event("operation:player", operation="player", status="complete"), "build.log")
        self.assertEqual(value.poll_player_bee(), [])
        self.assertIsNone(value.parse(PROGRAM, SOURCE))


class CaptureBoundaryReplayTests(unittest.TestCase):
    def test_exact_captured_platform_command_closes_previous_scope_only(self):
        value = parser(); handoff = value.parse(PROGRAM, SOURCE)
        self.assertEqual((handoff["phase"], handoff["status"], handoff["operation"]),
                         ("unity-player-platform-handoff", "complete", "player"))
        self.assertNotIn("player", value.closed_operations)
        for line in (PROGRAM.replace("Starting:", "Assets:"),
                     "Starting: bee_backend.exe --dagfile=Library/Bee/1300b0aE.dag ScriptAssemblies",
                     "CopyFiles AndroidPlayerBuildProgram.exe"):
            self.assertIsNone(value.parse(line, SOURCE))

    def test_captured_dynamic_copy_codegen_copy_order_is_not_native_cpp_order(self):
        value = parser()
        copying = value.parse("[9231/9233 0s] CopyFiles Data/0000000000000000f000000000000000", SOURCE)
        value.parse(PROGRAM, SOURCE); value.parse(BACKEND, SOURCE)
        initial = value.parse("[27/10069 0s] WriteResponseFile Library/Bee/artifacts/rsp/4239106210144759979.rsp", SOURCE)
        conversion = value.parse("[408/10069 11s] IL2CPP_CodeGen Library/Bee/artifacts/il2cpp_conv_x155.traceevents", SOURCE)
        later = value.parse("[416/10069 0s] ExtractUsedFeatures Library/Bee/artifacts/Android/Features/Manatee.Trello-FeaturesChecked.txt", SOURCE)
        self.assertIn("[bee-scope:staging]", copying["detail"])
        self.assertIn("[bee-scope:staging]", initial["detail"])
        self.assertEqual((conversion["phase"], conversion["done"], conversion["total"], conversion["status"]),
                         ("unity-il2cpp", 1, 2, "progress"))
        self.assertIn("[bee-scope:il2cpp]", later["detail"])
        self.assertNotEqual(copying["phase"], later["phase"])
        value.parse("ExitCode: 4 Duration: 19s", SOURCE)
        value.parse("*** Tundra requires additional run (19.14 seconds), 872 items updated, 10069 evaluated", SOURCE)
        final = value.take_observations()[0]
        self.assertEqual((final["phase"], final["done"], final["total"], final["status"]), ("unity-il2cpp", 2, 2, "complete"))

    def test_failed_codegen_annotation_cannot_close_conversion(self):
        value = parser(); value.parse(BACKEND, SOURCE)
        value.parse("[408/10069 11s] IL2CPP_CodeGen Library/Bee/artifacts/il2cpp_conv_x155.traceevents", SOURCE)
        value.parse("ExitCode: 1 Duration: 19s", SOURCE)
        value.parse("*** Tundra build failed (19.14 seconds), 872 items updated, 10069 evaluated", SOURCE)
        self.assertEqual(value.take_observations(), [])

    def test_finished_six_variant_pass_is_terminal_locally_not_owning_player(self):
        value = parser()
        value.parse('Compiling shader "Hidden/TextCore/Distance Field SSD" pass "" (vp)', SOURCE)
        value.parse("6 / 6 variants left after stripping, processed in 0.00 seconds", SOURCE)
        finished = value.parse("finished in 0.00 seconds. Local cache hits 6 (0.00s CPU time), remote cache hits 0 (0.00s CPU time), compiled 0 variants (0.00s CPU time), skipped 0 variants", SOURCE)
        self.assertEqual((finished["done"], finished["total"], finished["status"]), (6, 6, "complete"))
        self.assertIn("[shader-pass:1]", finished["detail"])
        self.assertNotIn("player", value.closed_operations)
        next_activity = value.parse(PROGRAM, SOURCE)
        self.assertEqual(next_activity["phase"], "unity-player-platform-handoff")


class ShaderCoverageTests(unittest.TestCase):
    def test_known_declared_names_duplicates_comments_and_unknown_builtin_are_scoped(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            paths = {"Assets/One.shader", "Assets/Duplicate.shader", "Assets/Second.shader"}
            sources = ('// Shader "CommentFake"\n/*\nShader "BlockCommentFake"\n*/\nShader "Original/One" {}',
                       'Shader "Original/One" {}', '\ufeffShader "Original/Ä" {}')
            for path, source in zip(sorted(paths), sources):
                target = project / path; target.parent.mkdir(parents=True, exist_ok=True); target.write_text(source)
            value = parser()
            value.import_coverage[SOURCE] = SimpleNamespace(known=paths, plan={"project": str(project)})
            start = value._shader_coverage(SOURCE)
            self.assertEqual((start["done"], start["total"]), (0, 3))
            self.assertIn("[shader-coverage]", start["detail"])
            self.assertIsNone(value._shader_coverage(SOURCE))
            first = value._shader_coverage(SOURCE, finished="Original/One")
            self.assertEqual((first["done"], first["total"]), (1, 3))
            self.assertIsNone(value._shader_coverage(SOURCE, finished="Original/One"))
            self.assertIsNone(value._shader_coverage(SOURCE, finished="Hidden/TextCore/Distance Field SSD"))
            second = value._shader_coverage(SOURCE, finished="Original/Ä")
            self.assertEqual((second["done"], second["total"], second["status"]), (2, 3, "progress"))

    def test_headers_are_read_once_not_repeated_for_each_shader_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory); target = project / "Assets/One.shader"
            target.parent.mkdir(); target.write_text('Shader "Original/One" {}')
            value = parser(); value.import_coverage[SOURCE] = SimpleNamespace(known={"Assets/One.shader"}, plan={"project": str(project)})
            value._shader_coverage(SOURCE)
            target.unlink()
            self.assertEqual(value._shader_coverage(SOURCE, finished="Original/One")["done"], 1)
            self.assertIsNone(value._shader_coverage(SOURCE, finished="Original/One"))

    def test_actual_finished_pass_publishes_both_local_complete_and_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory); target = project / "Assets/One.shader"
            target.parent.mkdir(); target.write_text('Shader "Original/One" {}')
            value = parser(); value.import_coverage[SOURCE] = SimpleNamespace(known={"Assets/One.shader"}, plan={"project": str(project)})
            value.parse('Compiling shader "Original/One" pass "" (vp)', SOURCE)
            self.assertEqual(value.take_observations()[0]["done"], 0)
            retained = value.parse("6 / 6 variants left after stripping, processed in 0.00 seconds", SOURCE)
            self.assertIn("[shader-source:unseen]", retained["detail"])
            ready = value.parse("[60s] 3 / 6 variants ready", SOURCE)
            self.assertEqual((ready["done"], ready["total"]), (3, 6))
            self.assertIn("[shader-source:unseen]", ready["detail"])
            local = value.parse("finished in 0.00 seconds. Local cache hits 6 (0.00s CPU time), remote cache hits 0 (0.00s CPU time), compiled 0 variants (0.00s CPU time), skipped 0 variants", SOURCE)
            self.assertEqual(local["status"], "complete")
            self.assertIn("[shader-source:unseen]", local["detail"])
            whole = value.take_observations()[0]
            self.assertEqual((whole["phase"], whole["done"], whole["total"], whole["status"]), ("unity-player-shaders", 1, 2, "progress"))
            repeated = value.parse('Compiling shader "Original/One" pass "next" (vp)', SOURCE)
            self.assertIn("[shader-source:observed]", repeated["detail"])
            outside = value.parse('Compiling shader "Hidden/Builtin" pass "" (vp)', SOURCE)
            self.assertIn("[shader-source:outside]", outside["detail"])
            self.assertEqual(value.take_observations(), [])


if __name__ == "__main__": unittest.main()
