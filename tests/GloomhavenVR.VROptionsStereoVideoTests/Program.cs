using System;
using System.Linq;
using System.Text.Json;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string reason)
    { _assertions++; if (!value) throw new Exception("Stereo menu assertion: " + reason); }
    private static TMP_Text Label(GameObject row) => row.GetComponentInChildren<TMP_Text>(true)!;
    private static void State(string state)
    { StereoVideoCalibration.State = state; VROptionsTab.Frame(); }
    private static int Main()
    {
        try
        {
            Check(VROptionsTab.BuildFixture(false) == 0 && VROptionsTab.FixtureRows.Length == 0, "missing content root builds no orphan action");
            int rows = VROptionsTab.BuildFixture();
            var action = VROptionsTab.FixtureRows.FirstOrDefault(r => r.GetComponent<Button>() != null);
            Check(rows == 6 && action != null, "Advanced directly exposes stereo action with both feedback rows");
            var all = VROptionsTab.FixtureRows;
            Check(ReferenceEquals(all[0], action), "stereo action is directly in Advanced before topic navigation");
            var button = action!.GetComponent<Button>()!;
            Check(button != null && !action.GetComponentsInChildren<Toggle>(false).Any(), "stereo trigger is a real action button without a persistent toggle");
            var title = Label(action); var hint = Label(all[1]); var status = Label(all[2]);
            Check(title.text == Loc.StereoVideoCalibrationStart && hint.text == Loc.StereoVideoCalibrationMenuHint && status.text == StereoVideoCalibration.Status,
                "action hint and runtime state are initially bound");
            Check(StereoVideoCalibration.Requests == 0 && StereoVideoCalibration.Decoders == 0, "opening Advanced never requests playback");
            button!.onClick.Invoke();
            Check(StereoVideoCalibration.Requests == 1 && status.text.EndsWith(":preparing"), "press calls actual runtime request and repaints status in place");
            Check(VROptionsTab.Rebuilds == 0 && ReferenceEquals(action, VROptionsTab.FixtureRows[0]), "press never rebuilds or navigates the menu");
            for (int i = 0; i < 20; i++) button.onClick.Invoke();
            Check(StereoVideoCalibration.Requests == 21 && StereoVideoCalibration.Decoders == 1 && ReferenceEquals(action, VROptionsTab.FixtureRows[0]), "repeated presses stay one action callback on the same row");
            State("playing"); Check(status.text.EndsWith(":playing"), "asynchronous preparation refreshes visible status");
            StereoVideoCalibration.Active = false; State("decoder error");
            Check(status.text.EndsWith(":decoder error"), "asynchronous decoder failure refreshes visible status");
            button.onClick.Invoke(); Check(StereoVideoCalibration.Decoders == 2 && status.text.EndsWith(":preparing"), "next press after error reaches runtime retry");
            StereoVideoCalibration.Active = false; State("closed");
            Check(status.text.EndsWith(":closed"), "picture closure refreshes menu feedback");
            button.onClick.Invoke(); Check(StereoVideoCalibration.Decoders == 3, "next press after picture closure reaches runtime retry");
            StereoVideoCalibration.Active = false; StereoVideoCalibration.Allowed = false; button.onClick.Invoke();
            Check(action.activeSelf && status.text.Contains("requires offline main menu"), "disallowed scene remains visible and displays runtime refusal");
            StereoVideoCalibration.Allowed = true; StereoVideoCalibration.Missing = true; button.onClick.Invoke();
            Check(status.text.Contains("movie files missing"), "missing-media refusal is immediately visible");
            StereoVideoCalibration.Missing = false; button.onClick.Invoke();
            Check(StereoVideoCalibration.Decoders == 4, "fixed media can be retried from the same button");
            Loc.Language = "German"; VROptionsTab.Frame();
            Check(title.text.StartsWith("German:") && hint.text.StartsWith("German:") && status.text.StartsWith("German:"), "language change updates action hint and status without rebuilding");
            string longStatus = new('x', 400); State(longStatus);
            Check(status.text == "German:" + longStatus && all[2].GetComponent<LayoutElement>()!.preferredHeight > 40,
                "long asynchronous status grows its note instead of overlapping later rows");
            float expanded = all[2].GetComponent<LayoutElement>()!.preferredHeight;
            int writes = title.Writes + hint.Writes + status.Writes;
            for (int i = 0; i < 100; i++) VROptionsTab.Frame();
            Check(title.Writes + hint.Writes + status.Writes == writes, "unchanged state does not rewrite text each frame");
            State("ready");
            float compact = all[2].GetComponent<LayoutElement>()!.preferredHeight;
            Check(compact >= 40 && compact < expanded, "short status shrinks feedback while preserving its authored minimum height");
            int requests = StereoVideoCalibration.Requests, decoders = StereoVideoCalibration.Decoders;
            VROptionsTab.DestroyFixture(); VROptionsTab.BuildFixture();
            Check(StereoVideoCalibration.Requests == requests && StereoVideoCalibration.Decoders == decoders, "reopening Advanced never restarts the active runtime");
            Check(Label(VROptionsTab.FixtureRows[2]).text == StereoVideoCalibration.Status, "reopened Advanced binds current runtime feedback");
            VROptionsTab.DestroyFixture(); int reads = StereoVideoCalibration.StatusReads;
            for (int i = 0; i < 20; i++) VROptionsTab.Frame();
            Check(StereoVideoCalibration.StatusReads == reads, "destroying the row ends its feedback updater without a static subscription");
            Check(VROptionsTab.Rebuilds == 0, "playback and language feedback never navigate or rebuild native options");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions = _assertions })); return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
        finally { VROptionsTab.DestroyFixture(); }
    }
}
