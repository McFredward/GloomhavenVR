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


def frame(mean="40.00", distance="45.0", visible="7"):
    return (f"[Info:GloomhavenVR] [Perf] FRAME 30.0s n=300 | display 24.0Hz budget 41.67ms "
            f"(XRDisplaySubsystem.TryGetDisplayRefreshRate) | frametime mean {mean} p50 38.00 "
            f"p95 55.00 p99 70.00 max 80.00ms | over-budget 100/300 | "
            f"mod 10.00ms/frame avg, worst 20.00ms (25%) | view height p50 5.0 "
            f"(4.0..6.0) dist p50 {distance} (44.0..46.0) wu above/from the board plane "
            f"| visible p50 {visible} renderer(s) | xr gpu 40.00ms [NOT usable as GPU busy time]\n")


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


if __name__ == "__main__":
    unittest.main()
