using System;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

    private static void TestRewardHeading()
    {
        var host = new GameObject("RewardHost");
        host.transform.rect = new Rect(-345, -344, 690, 688);
        var heading = new GameObject("Announcement");
        heading.transform.SetParent(host.transform);
        heading.transform.rect = new Rect(-320, -20, 640, 40);
        var text = heading.AddComponent<TMP_Text>();
        text.text = "Du hast einen Gegenstand erhalten";
        text.textBounds = new Bounds(new Vector3(0, 0, 0), new Vector3(800, 30, 0));
        var panel = new ConvertedPanel { HostRect = host.transform, Target = heading.transform };
        var manager = new UIRewardsManager { rewardAnnouncementText = text };
        Singleton<UIRewardsManager>.Instance = manager;
        Check(PanelInkBounds.TryMeasure(panel, out var ink) && ink.Rect.xMin == -400 && ink.Rect.xMax == 400,
            "reward heading includes overflowing glyphs beyond its 640 pixel layout");
        Check(ink.Rect.yMin == -20 && ink.Rect.yMax == 20,
            "heading keeps its native layout while including horizontal overflow");
        Rect captured = RewardHeadingBounds.Expand(host.transform, text, host.transform.rect);
        Check(captured.width == 800 && captured.height == 688,
            "capture grows to the actual heading without rescaling native content");

        var other = new GameObject("Other TMP");
        other.transform.SetParent(host.transform);
        other.transform.rect = heading.transform.rect;
        var otherText = other.AddComponent<TMP_Text>();
        otherText.text = text.text;
        otherText.textBounds = text.textBounds;
        Check(RewardHeadingBounds.Expand(host.transform, otherText, other.transform.rect).width == 640,
            "exact native heading identity prevents unrelated text layout changes");
        manager.rewardAnnouncementText = null;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && ink.Rect.width == 640,
            "missing native heading retains the original measurement");
        manager.rewardAnnouncementText = text;
        Singleton<UIRewardsManager>.Instance = null;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && ink.Rect.width == 640,
            "uninitialized native reward manager is a no-op");
        Singleton<UIRewardsManager>.Instance = manager;

        // A tilted, scaled nested transform requires all four corners. Opposing corners alone
        // collapse the X extent of this square rotated by 45 degrees.
        var group = new GameObject("Animated parent");
        group.transform.SetParent(host.transform);
        group.transform.localPosition = new Vector3(25, -10, 0);
        group.transform.localScale = new Vector3(2, 2, 1);
        heading.transform.SetParent(group.transform);
        heading.transform.angleDegrees = 45;
        heading.transform.rect = new Rect(-1, -1, 2, 2);
        text.textBounds = new Bounds(new Vector3(0, 0, 0), new Vector3(100, 100, 0));
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.xMin, 25 - 141.42136f)
            && Near(ink.Rect.xMax, 25 + 141.42136f) && Near(ink.Rect.yMin, -10 - 141.42136f),
            "glyph union includes all four rotated corners and the live scale chain");
        host.transform.localPosition = new Vector3(1000, -2000, 0);
        host.transform.localScale = new Vector3(3, 3, 1);
        host.transform.angleDegrees = -70;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.xMin, 25 - 141.42136f)
            && Near(ink.Rect.yMax, -10 + 141.42136f),
            "world pose and scale do not alter host-local glyph geometry");

        heading.transform.angleDegrees = 0;
        heading.transform.rect = new Rect(-320, -20, 640, 40);
        heading.transform.SetParent(host.transform);
        text.textBounds = new Bounds(new Vector3(0, 0, 0), new Vector3(800, 30, 0));
        var clip = new GameObject("Native viewport");
        clip.transform.SetParent(host.transform);
        clip.transform.rect = new Rect(-350, -50, 700, 100);
        var mask = clip.AddComponent<RectMask2D>();
        heading.transform.SetParent(clip.transform);
        panel.Target = clip.transform;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.width, 700),
            "overflow remains clipped by the original native ancestor viewport");
        mask.enabled = false;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.width, 800),
            "disabled native viewport no longer clips the heading");
        var stencil = heading.AddComponent<Mask>();
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.width, 640),
            "self stencil uses the original layout instead of expanding its clip to the glyphs");
        stencil.enabled = false;

        text.enabled = false;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "disabled heading contributes no ink");
        text.enabled = true;
        text.color = new Color { a = 0 };
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "transparent heading cannot enlarge chrome");
        text.color = new Color { a = 1 };
        text.canvasRenderer.cull = true;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "culled heading contributes no ink");
        text.canvasRenderer.cull = false;
        text.canvasRenderer.Alpha = 0;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "inherited hidden alpha remains authoritative for glyphs");
        text.canvasRenderer.Alpha = 1;
        text.text = string.Empty;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "empty heading ignores its previous mesh");
        text.text = "Du hast einen Gegenstand erhalten";
        text.textBounds = default;
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.width, 640),
            "not-yet-built glyph mesh retains native layout bounds");
        text.textBounds = new Bounds(new Vector3(float.NaN, 0, 0), new Vector3(800, 30, 0));
        Check(PanelInkBounds.TryMeasure(panel, out ink) && Near(ink.Rect.width, 640),
            "nonfinite glyph mesh cannot poison panel bounds");
        heading.activeSelf = false;
        Check(!PanelInkBounds.TryMeasure(panel, out ink), "inactive heading contributes no ink");
        Singleton<UIRewardsManager>.Instance = null;
    }
}
