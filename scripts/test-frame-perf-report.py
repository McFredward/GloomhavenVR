#!/usr/bin/env python3
"""Focused tests for frame-perf-report's evidence boundaries."""

import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("frame-perf-report.py")
spec = importlib.util.spec_from_file_location("frame_perf_report", SCRIPT)
reporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reporter)


def frame(mean="40.00", distance="45.0", visible="7", tag=""):
    return (f"[Info:GloomhavenVR] [Perf] FRAME 30.0s n=300 | display 24.0Hz budget 41.67ms "
            f"(XRDisplaySubsystem.TryGetDisplayRefreshRate) | frametime mean {mean} p50 38.00 "
            f"p95 55.00 p99 70.00 max 80.00ms | over-budget 100/300 | "
            f"mod 10.00ms/frame avg, worst 20.00ms (25%) | view height p50 5.0 "
            f"(4.0..6.0) dist p50 {distance} (44.0..46.0) wu above/from the board plane "
            f"| visible p50 {visible} renderer(s) | xr gpu 40.00ms [NOT usable as GPU busy time]{tag}\n")


SPLIT = ("[Info:GloomhavenVR] [Perf] SPLIT 30.0s n=300 — where the 40.00ms frame goes "
         "on the MAIN THREAD | logic (Update→LateUpdate) 20.00 p50 18.00 | "
         "render loop (cull+submit) 8.00 p50 7.00 | "
         "blocked (waiting on GPU/compositor) 12.00ms\n")
SPLIT_REVISED = SPLIT.replace("blocked (waiting on GPU/compositor)",
                              "blocked (unbracketed engine work/waits)").replace(
                                  "where the 40.00ms frame goes on the MAIN THREAD",
                                  "clock-bracketed spans of the 40.00ms frame "
                                  "(MAIN THREAD callbacks; residual is unbracketed)")


def heartbeat(state):
    return (f"[Info:GloomhavenVR] [Core] Heartbeat #1: frames+100 | rigDriver=ok "
            f"head=ok | display=running hmd={state} L=tracked R=tracked\n")


def eye(width=3408, scale="1.00"):
    return (f"[Info:GloomhavenVR] [Rig] EYE-TARGET DIAG (menu rig built): "
            f"eyeTextureDesc {width}x{width} msaaSamples=1; eyeTexture {width}x{width}, "
            f"resolutionScale={scale}, viewportScale=1.00, stereo=MultiPass\n")


def quality():
    return ("[Info:GloomhavenVR] [Perf] GFX — render state | quality level 0 'Fastest' "
            "of 6 | shadows=Disable cascades=1 antiAliasing=0\n")


def figure_tag(players=0, enemies=0, fx=0, cloth=False, state="steady", revision=1):
    return (f" | figure players={players} enemies={enemies} fx={fx} "
            f"cloth={cloth} state={state} revision={revision}")


def figure_begin(players=0, enemies=0, fx=0, cloth=False, revision=1):
    return (f"[Info:GloomhavenVR] [Perf] FIGURE-MEASURE begin revision={revision} "
            f"players={players} enemies={enemies} fx={fx} cloth={cloth} state=steady\n")


def figure_snapshot(players=0, enemies=0, fx=0, cloth=False):
    return (f"[Debug:GloomhavenVR] [Perf] Scenario figure detail: players={players}% "
            f"enemies={enemies}% nativeCloth={cloth}; 17 actor(s). "
            f"Ambient FX density={fx}%; 53 owned renderer(s) masked.\n")


def start_log():
    return ("[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'ProcGen'.\n"
            + eye() + frame() + SPLIT + quality())


def tracked_frame(**kwargs):
    return heartbeat("tracked") + heartbeat("tracked") + frame(**kwargs) + SPLIT + quality()


class FrameReportTests(unittest.TestCase):
    def parse(self, content):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "LogOutput.log"
            path.write_text(content, encoding="utf-8")
            return reporter.read_log(path)

    def test_actual_scene_is_not_guessed_from_renderer_count(self):
        log = ("[Info:GloomhavenVR] [Core] GloomhavenVR ModBuild 598 (assembly 1.1.0.0).\n"
               "[Info:GloomhavenVR] v1.1.0 build 53d7ca509 [dev] loaded\n"
               "[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'ProcGen'.\n"
               + eye() + frame() + SPLIT + quality()
               + heartbeat("tracked") + heartbeat("tracked") + frame(visible="7")
               + SPLIT + quality())
        report = self.parse(log)
        window = report["windows"][1]
        self.assertEqual((report["mod_build"], report["commit"]), (598, "53d7ca509"))
        self.assertEqual(window["scene"], "ProcGen")
        self.assertEqual(window["visible_p50"], 7)
        self.assertEqual(window["tracking"], "tracked")
        self.assertEqual(window["eye"]["width"], 3408)
        self.assertEqual((window["logic_ms"], window["render_ms"], window["blocked_ms"]),
                         (20.0, 8.0, 12.0))
        self.assertIsNone(window["gpu_busy_ms"])

    def test_old_and_revised_blocked_labels_preserve_the_same_measured_fields(self):
        log = ("[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'ProcGen'.\n"
               + eye() + frame() + SPLIT
               + heartbeat("tracked") + frame() + SPLIT_REVISED)
        old, revised = self.parse(log)["windows"]
        for window in (old, revised):
            self.assertEqual((window["logic_ms"], window["render_ms"], window["blocked_ms"]),
                             (20.0, 8.0, 12.0))
            self.assertIsNone(window["gpu_busy_ms"])

    def test_transition_and_tracking_samples_belong_to_completed_window(self):
        log = ("[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'MainMenu'.\n"
               + eye(2728, "0.80") + frame() + SPLIT
               + heartbeat("UNTRACKED") + frame() + SPLIT
               + "[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'ProcGen'.\n"
               + heartbeat("tracked") + eye(3408, "1.00") + frame() + SPLIT
               + heartbeat("tracked") + heartbeat("tracked") + frame() + SPLIT)
        windows = self.parse(log)["windows"]
        self.assertEqual(windows[1]["tracking"], "untracked")
        self.assertEqual(windows[1]["scene"], "MainMenu")
        self.assertEqual(windows[2]["scene"], "mixed")
        self.assertEqual(windows[2]["eye_status"], "mixed")
        self.assertEqual(windows[3]["scene"], "ProcGen")
        self.assertEqual(windows[3]["eye"]["scale"], 1.0)
        self.assertEqual(windows[3]["tracking_samples"], 2)

    def test_comparison_rejects_missing_or_mismatched_conditions(self):
        log = ("[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'ProcGen'.\n"
               + eye() + frame() + SPLIT + quality()
               + heartbeat("tracked") + heartbeat("tracked") + frame() + SPLIT + quality()
               + heartbeat("tracked") + heartbeat("tracked") + frame(mean="35.00") + SPLIT + quality())
        _, a, b = self.parse(log)["windows"]
        comparison = reporter.compare(a, b, same_file=True)
        self.assertTrue(comparison["comparable"])
        self.assertEqual(comparison["mean_delta_ms"], -5.0)
        self.assertIn("scenario_identity_unverified_across_logs", reporter.compare(a, b)["flags"])
        altered = dict(b, tracking="untracked", pose=dict(b["pose"], distance_p50=60.0))
        comparison = reporter.compare(a, altered, same_file=True)
        self.assertFalse(comparison["comparable"])
        self.assertIn("hmd_not_tracked", comparison["reject"])
        self.assertIn("pose_mismatch", comparison["reject"])
        self.assertIsNone(comparison["mean_delta_ms"])
        altered = dict(b, eye=dict(b["eye"], scale=0.8))
        self.assertIn("eye_mismatch", reporter.compare(a, altered)["reject"])
        altered = dict(b, eye=None, eye_status="unknown", pose=None)
        comparison = reporter.compare(a, altered)
        self.assertIn("eye_unknown_or_mixed", comparison["reject"])
        self.assertIn("pose_unavailable", comparison["reject"])

    def test_steady_figure_ab_is_comparable_at_the_same_view(self):
        log = start_log() + tracked_frame(tag=figure_tag()) + tracked_frame(
            mean="45.00", tag=figure_tag(players=100, revision=2))
        _, a, b = self.parse(log)["windows"]
        self.assertEqual(a["figure"], {"players": 0.0, "enemies": 0.0, "fx": 0.0,
                                      "cloth": False, "revision": 1})
        self.assertEqual((a["figure_status"], a["figure_evidence"]), ("steady", "frame_window"))
        result = reporter.compare(a, b, same_file=True)
        self.assertTrue(result["comparable"])
        self.assertTrue(result["figure_settings_verified"])
        self.assertEqual(result["figure_changes"], {"players": {"baseline": 0.0, "candidate": 100.0}})
        self.assertEqual(result["mean_delta_ms"], 5.0)
        self.assertNotIn("figure_state_unverified", result["flags"])
        self.assertIsNone(a["gpu_busy_ms"])

    def test_figure_fx_and_cloth_changes_are_confounds(self):
        for tag, reason in ((figure_tag(fx=100), "figure_fx_mismatch"),
                            (figure_tag(cloth=True), "figure_cloth_mismatch")):
            with self.subTest(reason=reason):
                _, a, b = self.parse(start_log() + tracked_frame(tag=figure_tag())
                                     + tracked_frame(tag=tag))["windows"]
                result = reporter.compare(a, b, same_file=True)
                self.assertFalse(result["comparable"])
                self.assertIn(reason, result["reject"])
                self.assertIsNone(result["mean_delta_ms"])

    def test_tagged_nonsteady_windows_are_not_valid_figure_ab(self):
        for status in ("transition", "warmup", "mixed", "unknown"):
            with self.subTest(status=status):
                _, a, b = self.parse(start_log() + tracked_frame(tag=figure_tag())
                                     + tracked_frame(tag=figure_tag(state=status)))["windows"]
                result = reporter.compare(a, b, same_file=True)
                self.assertFalse(result["comparable"])
                self.assertIn("figure_not_steady", result["reject"])

    def test_legacy_windows_preserve_comparison_but_flag_unknown_figure_state(self):
        _, a, b = self.parse(start_log() + tracked_frame() + tracked_frame())["windows"]
        self.assertIsNone(a["figure"])
        self.assertEqual(a["figure_status"], "unknown")
        result = reporter.compare(a, b, same_file=True)
        self.assertTrue(result["comparable"])
        self.assertFalse(result["figure_settings_verified"])
        self.assertIn("figure_state_unverified", result["flags"])

    def test_late_legacy_snapshot_does_not_prove_earlier_frames(self):
        _, a, b = self.parse(start_log() + figure_snapshot(players=100)
                             + tracked_frame() + tracked_frame())["windows"]
        self.assertIsNone(a["figure"])
        self.assertEqual(a["figure_status"], "unknown")
        self.assertEqual(b["figure_status"], "observed")
        self.assertEqual(b["figure"]["players"], 100.0)
        result = reporter.compare(a, b, same_file=True)
        self.assertTrue(result["comparable"])
        self.assertIn("figure_state_unverified", result["flags"])

    def test_ordered_legacy_changes_reject_mixed_window_and_reset_next(self):
        _, seed, mixed, settled = self.parse(start_log() + figure_snapshot()
            + tracked_frame() + figure_snapshot(players=100) + figure_snapshot()
            + tracked_frame() + tracked_frame())["windows"]
        self.assertEqual(seed["figure_status"], "unknown")
        self.assertEqual(mixed["figure_status"], "mixed")
        self.assertEqual(len(mixed["figure_events"]), 2)
        self.assertEqual(settled["figure_status"], "observed")
        self.assertEqual(settled["figure"]["players"], 0.0)
        self.assertEqual(settled["figure_events"], [])
        result = reporter.compare(mixed, settled, same_file=True)
        self.assertIn("figure_not_steady", result["reject"])

    def test_later_gfx_figure_snapshot_cannot_label_completed_window(self):
        gfx_snapshot = quality().rstrip("\n") + " figurePlayers=100 figureEnemies=100 figureFx=100\n"
        report = self.parse(start_log() + tracked_frame(tag=figure_tag())
                            + gfx_snapshot + tracked_frame())
        self.assertEqual(report["windows"][1]["figure"]["players"], 0.0)
        self.assertIsNone(report["windows"][2]["figure"])
        self.assertEqual(report["windows"][2]["figure_status"], "unknown")

    def test_changing_both_figure_sliders_is_allowed_but_cannot_isolate_either(self):
        _, a, b = self.parse(start_log() + tracked_frame(tag=figure_tag())
                             + tracked_frame(tag=figure_tag(players=100, enemies=100)))["windows"]
        result = reporter.compare(a, b, same_file=True)
        self.assertTrue(result["comparable"])
        self.assertEqual(set(result["figure_changes"]), {"players", "enemies"})
        self.assertIn("multiple_figure_settings_changed", result["flags"])

    def test_table_shows_complete_window_figure_tag(self):
        report = self.parse(start_log() + tracked_frame(tag=figure_tag(players=100, revision=8)))
        table = reporter._table(report)
        self.assertIn("figure(players/enemies/fx/cloth) state revision", table)
        self.assertIn("100/0/0/False steady 8", table)

    def test_steady_begin_discards_preparation_evidence_but_keeps_current_state(self):
        preparing = ("[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'MainMenu'.\n"
                     + heartbeat("UNTRACKED") + eye(2728, "0.80")
                     + quality().replace("'Fastest'", "'Beautiful'") + figure_snapshot(players=100)
                     + "[Info:GloomhavenVR] [Rig] LIGHT STABILISER released — scene changed to 'ProcGen'.\n"
                     + eye() + quality() + figure_begin(revision=3))
        report = self.parse(start_log() + preparing + tracked_frame(tag=figure_tag(revision=3)))
        current = report["windows"][1]
        self.assertEqual((current["scene"], current["tracking"], current["tracking_samples"]),
                         ("ProcGen", "tracked", 2))
        self.assertEqual(current["eye_status"], "known")
        self.assertEqual(current["eye"]["width"], 3408)
        self.assertEqual(current["quality_status"], "known")
        self.assertEqual(current["quality"]["level"], "Fastest")
        self.assertEqual(current["scene_events"], [])
        self.assertEqual(current["eye_events"], [])
        self.assertEqual(current["figure_events"], [])
        self.assertEqual(current["figure_status"], "steady")
        self.assertEqual(current["figure_measure_begin_line"], report["figure_measurements"][0]["line"])
        self.assertEqual(report["figure_measurements"][0]["revision"], 3)

    def test_begin_marker_does_not_attach_discarded_split_to_previous_frame(self):
        report = self.parse(start_log() + figure_begin()
                            + SPLIT.replace("20.00 p50 18.00", "99.00 p50 18.00")
                            + tracked_frame(tag=figure_tag()))
        self.assertEqual(report["windows"][0]["logic_ms"], 20.0)
        self.assertEqual(report["windows"][1]["logic_ms"], 20.0)

    def test_preparing_tag_is_rejected_even_if_legacy_snapshot_looks_consistent(self):
        _, a, b = self.parse(start_log() + tracked_frame(tag=figure_tag())
                             + figure_snapshot() + tracked_frame(tag=figure_tag(state="preparing")))["windows"]
        self.assertEqual(b["figure_status"], "preparing")
        self.assertIn("figure_not_steady", reporter.compare(a, b, same_file=True)["reject"])


if __name__ == "__main__":
    unittest.main()
