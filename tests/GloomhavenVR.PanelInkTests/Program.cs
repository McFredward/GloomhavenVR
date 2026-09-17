using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static int _assertions;
    private static void Check(bool value, string reason)
    { _assertions++; if (!value) throw new Exception("Panel ink assertion: " + reason); }

    private static void TestPlacementHintIsolation()
    {
        var host = new GameObject("PartyHost");
        host.transform.rect = Rect.MinMaxRect(-994, -540, 994, 540);
        var owner = new GameObject("Party");
        owner.transform.SetParent(host.transform);
        var column = new GameObject("Column");
        column.transform.SetParent(owner.transform);
        column.transform.rect = Rect.MinMaxRect(-982, -540, -654, 540);
        column.AddComponent<Image>();
        var picker = new GameObject("BattleGoalPicker");
        picker.transform.SetParent(owner.transform);
        picker.transform.rect = Rect.MinMaxRect(-654, -400, -88, 150);
        picker.AddComponent<Image>();
        var hint = new GameObject("Hint");
        hint.transform.SetParent(owner.transform);
        var button = new GameObject("Continue");
        button.transform.SetParent(hint.transform);
        button.transform.rect = Rect.MinMaxRect(-2056, -580, -1856, -540);
        button.AddComponent<Image>();
        HintOnOwnerComposite.Parked = hint.transform;
        var panel = new ConvertedPanel { HostRect = host.transform, Target = owner.transform };
        Check(PanelInkBounds.TryMeasure(panel, out var ink) && ink.Rect.xMin == -2056,
            "ordinary ink retains parked hint and its continue button");
        Check(PanelInkBounds.TryMeasure(panel, out ink, includeParkedHint: false)
            && ink.Rect.xMin == -982 && ink.Rect.xMax == -88 && ink.Graphics == 2,
            "placement fallback excludes hint while retaining native battle-goal picker");
        Check(CanvasConversion.MeasureDrawn(panel, out var rect, true) && rect.xMin == -2056,
            "hit geometry retains parked hint and its continue button");
        Check(CanvasConversion.MeasureDrawn(panel, out rect, false) && rect.xMin == -982 && rect.xMax == -88,
            "visible placement excludes hint without moving owner anchor");
        for (int tick = 0; tick < 100; tick++)
        {
            button.transform.rect = Rect.MinMaxRect(-2056 - tick, -580, -1856, -540);
            Check(CanvasConversion.MeasureDrawn(panel, out rect, false) && rect.xMin == -982,
                "animating hint cannot shift owner corner");
        }
        panel.Target = hint.transform;
        Check(PanelInkBounds.TryMeasure(panel, out ink, includeParkedHint: false) && ink.Graphics == 1,
            "hint itself stays measurable as a root");
        Check(CanvasConversion.MeasureDrawn(panel, out rect, false) && rect.xMin == -2155,
            "hint root is not excluded by visible placement measurement");
        panel.Target = owner.transform;
        HintOnOwnerComposite.Parked = null;
        Check(PanelInkBounds.TryMeasure(panel, out ink, includeParkedHint: false) && ink.Rect.xMin == -2155,
            "unmarked native content outside owner remains placement content");
    }

    static void Main()
    {
        var host = new GameObject("Host");
        var movie = new GameObject("Movie");
        movie.transform.SetParent(host.transform);
        var image = movie.AddComponent<RawImage>();
        var panel = new ConvertedPanel { HostRect = host.transform, Target = movie.transform, ContentGraphic = image };
        Check(PanelInkBounds.TryMeasure(panel, out var ink), "full-frame movie remains measurable content");
        Check(ink.Valid && ink.Graphics == 1 && ink.Plates == 0, "movie is ink, not a backdrop");
        Check(ink.Rect.width == 1280 && ink.Rect.height == 720 && ink.Rect.yMin == -360,
            "movie bounds retain full picture and lower edge for grab bar");
        for (int frame = 0; frame < 100; frame++)
            Check(PanelInkBounds.TryMeasure(panel, out ink), "stable movie cannot acquire empty-window state");

        panel.ContentGraphic = null;
        Check(!PanelInkBounds.TryMeasure(panel, out ink) && ink.Plates == 1,
            "ordinary full-frame backdrop remains excluded");
        panel.ContentGraphic = image;
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(movie.transform);
        backdrop.AddComponent<Image>();
        Check(PanelInkBounds.TryMeasure(panel, out ink) && ink.Graphics == 1 && ink.Plates == 1,
            "content exemption never admits a neighboring backdrop");

        image.enabled = false;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "disabled movie cannot keep grab bar alive");
        image.enabled = true;
        image.canvasRenderer.cull = true;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "culled movie cannot keep grab bar alive");
        image.canvasRenderer.cull = false;
        image.canvasRenderer.Alpha = 0;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "inherited hidden alpha remains authoritative");
        image.canvasRenderer.Alpha = 1;
        image.color = new Color { a = 0 };
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "transparent movie cannot keep grab bar alive");
        image.color = new Color { a = 1 };
        movie.activeSelf = false;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "inactive movie cannot keep grab bar alive");
        movie.activeSelf = true;

        var clip = new GameObject("Clip");
        clip.transform.rect = Rect.MinMaxRect(-100, -50, 100, 50);
        clip.AddComponent<RectMask2D>();
        movie.transform.SetParent(clip.transform);
        panel.Target = clip.transform;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && ink.Rect.width == 200 && ink.Rect.height == 100,
            "explicit content honors native clipping");
        TestPlacementHintIsolation();
        TestRewardHeading();
        TestMirrorBackingInk();
        TestScopedBoardBacking();
        TestBackingVisibility();
        Console.WriteLine($"Panel ink tests: {_assertions} assertions passed.");
    }
}
