using System;
using System.Collections;
using System.Globalization;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The mod's version, drawn in the MAIN MENU next to the game's own version number
/// (bottom left).
/// </summary>
/// <remarks>
/// <para>USER REQUEST 2026-08-22, verbatim: "VR mod Version soll im Hauptmenu unten links neben
/// der Versionnummer vom Spiel sichtbar sein."</para>
///
/// <para>WHAT IT ATTACHES TO. <c>MainMenuUIManager.Version</c> is a public serialized
/// <c>TextMeshProUGUI</c> reference (GH.Runtime, GLOOM.MainMenu); the game fills it from
/// <c>MF.SetVersion</c> — a localized prefix (<c>Consoles/GUI_VERSION</c>) plus
/// <c>Application.version</c> — inside <c>MainMenuUIManager.OnLanguageChanged</c>, which runs on
/// every <c>OnEnable</c> AND on every <c>I2.Loc</c> localize event. Its bottom-left placement is
/// authored in the scene and appears nowhere in code, so it is READ at runtime and never
/// assumed.</para>
///
/// <para>WHY A CLONE, NOT A NEW LABEL. "Same style, same baseline, clearly a sibling" is a
/// property of a dozen authored values — font asset, material (outline/underlay), size, colour,
/// style, alignment, anchors, pivot, layer, canvas — and every one of them is scene data this
/// code cannot see. Instantiating the game's own label object copies all of them for free and
/// puts the copy in the SAME canvas, so it goes through the identical draw order and, in VR, the
/// identical FlatScreen capture. Only the localization components are stripped from the clone
/// (they would overwrite our text on the next language change) and only the clone's text, font
/// size and position are then written. THE GAME'S OWN LABEL IS NEVER WRITTEN TO — not its text,
/// not its rect, not its active state. It is measured, and that is all.</para>
///
/// <para>WHY THE PLACEMENT IS MEASURED TWICE. The offset "our left edge = their right edge +
/// gap, our baseline = their baseline" is computed from the GLYPHS (TMP character info) of both
/// labels after a <c>ForceMeshUpdate</c>, not from rect maths. That is what makes it independent
/// of the authored alignment, margins and pivot — all unknown here — and what keeps it correct
/// when the localized prefix changes length. Placement re-copies the authored rect from the
/// source every time before shifting, so a re-place can never compound.</para>
///
/// <para>NO HARMONY PATCH AND NO PER-FRAME FIND. The trigger is
/// <c>MainMenuUIManager.Instance</c> — a static field the game sets in <c>Awake</c> and clears in
/// <c>OnDestroy</c>. A 2 Hz coroutine reads that static, compares two cached strings, and returns;
/// there is no scene query anywhere in this file. The tick body is wrapped in a try/catch that
/// logs once, because a throwing coroutine dies silently and an unguarded throw in the UI path is
/// how VR input gets starved.</para>
///
/// <para>LEGIBILITY. In VR the main menu is not a world-space canvas: it is the desktop composite
/// on the FlatScreen quad, <c>WorldUI/ScreenWidth</c> metres wide at <c>WorldUI/ScreenDistance</c>
/// metres. One authored pixel is therefore <c>scaleFactor * ScreenWidth / Screen.width</c> metres
/// wide, and the cap height of an N px font subtends
/// <c>N * 0.7 * thatWidth / distance</c> radians. The clone keeps the game's own font size unless
/// that lands under <see cref="TargetCapArcMinutes"/> arc-minutes, in which case it is raised —
/// but never past <see cref="MaxUpscale"/>x the game's, because a label that dwarfs the string it
/// sits next to reads as an overlay, not a sibling. On the flat screen the game's size is kept
/// verbatim: the monitor's legibility is the game's own choice to make.</para>
/// </remarks>
internal static class ModVersionLabel
{
    private const string Scope = "ModVersion";

    /// <summary>Name of the cloned GameObject — grep-able, and the "is it already there" test.</summary>
    private const string LabelObjectName = "GloomhavenVR.ModVersionLabel";

    /// <summary>Poll cadence for "is the main menu up / did the game rewrite its string". Two
    /// null-and-string comparisons; the menu is not perf-critical and this is not a scan.</summary>
    private const float PollSeconds = 0.5f;

    /// <summary>Gap between the two strings, in ems of the mod label's font size. 0.75 em is a
    /// wide word space: unmistakably separate, unmistakably the same line.</summary>
    private const float GapEm = 0.75f;

    /// <summary>Cap height as a fraction of TMP font size — close enough for a legibility
    /// budget, and the same 0.7 used to report the arc-minutes in the falsifier line.</summary>
    private const float CapHeightRatio = 0.7f;

    /// <summary>Legibility floor for the cap height at the menu's reading distance. 22' is
    /// roughly twice the acuity limit of a healthy eye and about what a Quest 3 through
    /// Virtual Desktop can resolve comfortably on a flat quad.</summary>
    private const float TargetCapArcMinutes = 22f;

    /// <summary>Hard ceiling on how much bigger than the game's own string the mod's may get.</summary>
    private const float MaxUpscale = 2f;

    /// <summary>Radians to arc-minutes.</summary>
    private const float RadToArcMin = 3437.74677f;

    private static bool s_attached;
    private static GameObject? s_driver;

    private static TextMeshProUGUI? s_label;   // our clone (Unity-null once the scene reloads)
    private static TextMeshProUGUI? s_source;  // the game's label we cloned from
    private static TextMeshProUGUI? s_refused; // a label Build() turned down, so it is not retried
    private static string s_sourceText = string.Empty;
    private static float s_sourceFont = -1f;
    private static bool s_failedLogged;
    private static bool s_missingLogged;
    private static bool s_placedLogged;

    /// <summary>
    /// Installs the 2 Hz watcher. Called once from <c>WorldUIModule.Init</c>, BEFORE its
    /// VR-not-running early-out: the main menu is reachable on the flat screen too, and there
    /// the label simply keeps the game's own font size.
    /// </summary>
    internal static void Attach()
    {
        if (s_attached)
        {
            return;
        }

        s_attached = true;
        try
        {
            s_driver = new GameObject("GloomhavenVR.ModVersionLabelDriver");
            UnityEngine.Object.DontDestroyOnLoad(s_driver);
            s_driver.hideFlags = HideFlags.HideAndDontSave;
            s_driver.AddComponent<Driver>();
            VRLog.Debug(Scope, "watcher installed (2 Hz, MainMenuUIManager.Instance).");
        }
        catch (Exception ex)
        {
            VRLog.Warn(Scope, $"watcher not installed — the mod version will not be shown: {ex.Message}");
        }
    }

    /// <summary>Coroutine host. Its only job is to call <see cref="Tick"/> and swallow.</summary>
    private sealed class Driver : MonoBehaviour
    {
        private IEnumerator Start()
        {
            while (true)
            {
                // Realtime, and a fresh instance every iteration: a cached WaitForSecondsRealtime
                // does not re-arm, and a plain WaitForSeconds stops dead whenever the game parks
                // Time.timeScale at 0 behind a menu.
                yield return new WaitForSecondsRealtime(PollSeconds);
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    if (!s_failedLogged)
                    {
                        s_failedLogged = true;
                        VRLog.Warn(Scope, $"tick threw once, the label may be missing: {ex}");
                    }
                }
            }
        }
    }

    private static void Tick()
    {
        var manager = MainMenuUIManager.Instance;
        if (manager == null)
        {
            return; // not in the main menu — nothing to do, nothing to clean up (scene-owned)
        }

        var source = manager.Version;
        if (source == null)
        {
            if (!s_missingLogged)
            {
                s_missingLogged = true;
                VRLog.Warn(Scope, "MOD VERSION LABEL: NOT ACHIEVED — MainMenuUIManager.Version is "
                                  + "null on this build, so there is no sibling to sit next to. "
                                  + "Nothing is drawn and the menu is untouched.");
            }

            return;
        }

        // A scene reload re-instantiates the menu: the clone is gone and the source is a new
        // object. Both tests are needed — Unity-null for ours, reference identity for theirs.
        if (s_label == null || !ReferenceEquals(s_source, source))
        {
            if (ReferenceEquals(s_refused, source))
            {
                return; // this exact label was already refused, with a reason, once
            }

            if (!Build(source))
            {
                // Remember WHICH object was refused rather than latching a flag: the next main
                // menu brings a new one and deserves its own try. Without this the two warnings
                // inside Build would repeat at 2 Hz for the rest of the session.
                s_refused = source;
                return;
            }
        }

        var label = s_label;
        if (label == null)
        {
            return;
        }

        if (!string.Equals(label.text, GloomhavenVR.Core.BuildInfo.Display, StringComparison.Ordinal))
        {
            label.text = GloomhavenVR.Core.BuildInfo.Display;
            Place(source, label);
            return;
        }

        if (!label.enabled)
        {
            // Still waiting on a measurement (see Place). Retry until there are glyphs — the
            // source string alone would not tell us, since it may not have changed.
            Place(source, label);
            return;
        }

        // The game rewrites its own string on every localize event, and a different language
        // means a different width to sit beside.
        if (!string.Equals(source.text, s_sourceText, StringComparison.Ordinal)
            || !Mathf.Approximately(source.fontSize, s_sourceFont))
        {
            Place(source, label);
        }
    }

    /// <summary>Clones the game's label, strips what would fight us, places it. False = gave up
    /// (already logged); the next tick will simply try again on the next menu.</summary>
    private static bool Build(TextMeshProUGUI source)
    {
        s_label = null;
        s_source = null;
        s_placedLogged = false; // once per main menu, so a return to the menu re-confirms it

        // A SHARED PARENT is a precondition, not a convenience: the whole placement is expressed
        // as a delta in anchoredPosition, which is only comparable between two rects under the
        // same parent. A parentless label (the text sitting on the canvas root itself) is not
        // something to improvise around.
        var parent = source.transform.parent;
        if (parent == null)
        {
            VRLog.Warn(Scope, "MOD VERSION LABEL: NOT ACHIEVED — the game's version label has no "
                              + "parent transform, so there is no shared frame to offset within. "
                              + "Not drawn, rather than placed by guesswork.");
            return false;
        }

        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, worldPositionStays: false);
        clone.name = LabelObjectName;

        var label = clone.GetComponent<TextMeshProUGUI>();
        if (label == null)
        {
            UnityEngine.Object.Destroy(clone);
            VRLog.Warn(Scope, "MOD VERSION LABEL: NOT ACHIEVED — the cloned version label carries "
                              + "no TextMeshProUGUI. Not drawn.");
            return false;
        }

        StripLocalization(clone);

        label.text = GloomhavenVR.Core.BuildInfo.Display;
        // BORN INVISIBLE. The clone starts life exactly on top of the string it must not cover;
        // it is only switched on once a placement has actually measured both labels and moved it
        // out of the way. One rule ("never drawn where it would overlap") instead of a race.
        label.enabled = false;
        label.raycastTarget = false;
        label.enableAutoSizing = false;   // our own floor decides the size, not a fitter
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow; // never clipped by the authored rect
        label.fontSize = LegibleFontSize(source, label);

        // If the parent lays its children out, our measured offset would be overwritten on the
        // next layout pass. Opting out is one component and leaves the game's own row alone.
        if (parent.GetComponent<LayoutGroup>() != null)
        {
            var ignore = clone.GetComponent<LayoutElement>();
            if (ignore == null)
            {
                ignore = clone.AddComponent<LayoutElement>();
            }

            ignore.ignoreLayout = true;
            VRLog.Debug(Scope, "parent has a LayoutGroup — the clone opts out via LayoutElement.ignoreLayout.");
        }

        // Draw immediately after the original: same canvas, adjacent order, no overlay games.
        clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        clone.SetActive(source.gameObject.activeSelf);

        s_label = label;
        s_source = source;
        Place(source, label);
        return true;
    }

    /// <summary>
    /// I2.Loc drives the game's label from a localization key; a clone would inherit that and
    /// have its text replaced on the next language change. Only localization components are
    /// removed — everything else (outline/shadow effects, the material, whatever the scene
    /// author put there) is exactly why we cloned in the first place.
    /// </summary>
    private static void StripLocalization(GameObject clone)
    {
        var components = clone.GetComponentsInChildren<Component>(includeInactive: true);
        for (int i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
            {
                continue;
            }

            var name = component.GetType().FullName ?? string.Empty;
            if (name.StartsWith("I2.Loc.", StringComparison.Ordinal)
                || name.IndexOf("Localize", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Localization", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                VRLog.Debug(Scope, $"stripping {name} from the clone.");
                UnityEngine.Object.Destroy(component);
            }
        }
    }

    /// <summary>
    /// The game's own size, raised only if its cap height lands under the legibility floor at
    /// the menu's reading distance, and never past <see cref="MaxUpscale"/>x.
    /// </summary>
    private static float LegibleFontSize(TextMeshProUGUI source, TextMeshProUGUI label)
    {
        float authored = source.fontSize;
        if (authored <= 0f)
        {
            return authored;
        }

        if (!TryArcMinutesPerPixel(label, out float arcMinPerPx) || arcMinPerPx <= 0f)
        {
            return authored; // flat screen, or no geometry to reason from: keep the game's
        }

        float needed = TargetCapArcMinutes / (CapHeightRatio * arcMinPerPx);
        if (needed <= authored)
        {
            return authored;
        }

        float raised = Mathf.Min(needed, authored * MaxUpscale);
        VRLog.Info(Scope, $"the menu's own version font is {authored:0.#} px = "
                          + $"{authored * CapHeightRatio * arcMinPerPx:0.#} arc-min cap at the VR "
                          + $"reading distance, under the {TargetCapArcMinutes:0.#}' floor — the mod "
                          + $"label is raised to {raised:0.#} px (cap {MaxUpscale:0.#}x).");
        return raised;
    }

    /// <summary>
    /// Arc-minutes subtended by ONE authored canvas pixel of the main menu, as seen in VR.
    /// False on the flat screen (there is no quad, and the monitor's geometry is not ours).
    /// </summary>
    private static bool TryArcMinutesPerPixel(TextMeshProUGUI label, out float arcMinPerPx)
    {
        arcMinPerPx = 0f;
        if (!VRSession.IsRunning)
        {
            return false;
        }

        var width = WorldUIConfig.ScreenWidth;
        var distance = WorldUIConfig.ScreenDistance;
        if (width == null || distance == null || distance.Value <= 0f)
        {
            return false;
        }

        int screenPx = Mathf.Max(Screen.width, 1);
        // The FlatScreen quad shows the whole desktop composite, so one SCREEN pixel is
        // quadWidth/Screen.width metres wide; scaleFactor converts authored canvas px to screen px.
        var canvas = label.canvas != null ? label.canvas : label.GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float metresPerAuthoredPx = scaleFactor * (width.Value / screenPx);
        arcMinPerPx = metresPerAuthoredPx / distance.Value * RadToArcMin;
        return arcMinPerPx > 0f;
    }

    /// <summary>
    /// Puts the mod label's LEFT glyph edge one gap to the right of the game label's RIGHT glyph
    /// edge, on the same baseline. Everything is re-derived from the source each time, so this
    /// is idempotent by construction.
    /// </summary>
    private static void Place(TextMeshProUGUI source, TextMeshProUGUI label)
    {
        var sourceRect = source.rectTransform;
        var labelRect = label.rectTransform;

        // Start from the authored rect, verbatim. Never accumulate on top of a previous place.
        labelRect.anchorMin = sourceRect.anchorMin;
        labelRect.anchorMax = sourceRect.anchorMax;
        labelRect.pivot = sourceRect.pivot;
        labelRect.sizeDelta = sourceRect.sizeDelta;
        labelRect.anchoredPosition = sourceRect.anchoredPosition;
        labelRect.localRotation = sourceRect.localRotation;
        labelRect.localScale = sourceRect.localScale;

        s_sourceText = source.text ?? string.Empty;
        s_sourceFont = source.fontSize;

        if (!Extent(source, out var them) || !Extent(label, out var us))
        {
            // No glyphs to measure (the game has not written its string yet, or the font atlas
            // is not up). Stay invisible on top of the authored rect rather than guessing an
            // offset; the next tick tries again.
            label.enabled = false;
            VRLog.Debug(Scope, "no measurable glyphs yet — placement deferred to the next tick.");
            return;
        }

        float scaleX = Mathf.Approximately(labelRect.localScale.x, 0f) ? 1f : labelRect.localScale.x;
        float scaleY = Mathf.Approximately(labelRect.localScale.y, 0f) ? 1f : labelRect.localScale.y;
        float gap = GapEm * label.fontSize;

        // Both rects share a parent and (now) identical anchors, so a delta in parent-local
        // units IS a delta in anchoredPosition.
        float theirRight = sourceRect.anchoredPosition.x + them.Right * scaleX;
        float ourLeft = labelRect.anchoredPosition.x + us.Left * scaleX;
        float dx = theirRight + gap * scaleX - ourLeft;

        float theirBaseline = sourceRect.anchoredPosition.y + them.Baseline * scaleY;
        float ourBaseline = labelRect.anchoredPosition.y + us.Baseline * scaleY;
        float dy = theirBaseline - ourBaseline;

        bool stacked = false;
        var placed = labelRect.anchoredPosition + new Vector2(dx, dy);
        labelRect.anchoredPosition = placed;

        // Safety: if sitting beside it would push the mod label off the canvas, put it one line
        // ABOVE instead, left-aligned with the game's string. Still adjacent, still never on top.
        if (RunsOffCanvas(label, us))
        {
            float lineStep = Mathf.Max(them.Height, us.Height) * 1.25f;
            float theirLeft = sourceRect.anchoredPosition.x + them.Left * scaleX;
            labelRect.anchoredPosition = new Vector2(
                placed.x - dx + (theirLeft - ourLeft),
                placed.y + lineStep);
            stacked = true;
        }

        label.enabled = true; // measured, moved, and only now drawn

        // The falsifier goes out on the first placement that actually landed (NOT on the build,
        // which may still be waiting for glyphs), and again whenever the fallback branch is taken.
        if (!s_placedLogged || stacked)
        {
            s_placedLogged = true;
            LogFalsifier(source, label, them, us, gap, stacked);
        }
    }

    /// <summary>Glyph extents of a TMP text in its OWN local space (origin = rect pivot).</summary>
    private struct Span
    {
        public float Left;
        public float Right;
        public float Baseline;
        public float Height;
    }

    private static bool Extent(TMP_Text text, out Span span)
    {
        span = default;
        // ignoreActiveState: TMP's OnPreRenderCanvas early-outs on `!IsActive() &&
        // !m_ignoreActiveState`, and the clone is deliberately DISABLED until it has been placed.
        // The plain ForceMeshUpdate() would therefore return without generating a single glyph,
        // the placement would defer forever, and the label would never appear.
        text.ForceMeshUpdate(ignoreActiveState: true);
        var info = text.textInfo;
        if (info == null || info.characterCount <= 0)
        {
            return false;
        }

        float left = float.MaxValue;
        float right = float.MinValue;
        float top = float.MinValue;
        float bottom = float.MaxValue;
        float baseline = 0f;
        bool any = false;

        for (int i = 0; i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible)
            {
                continue;
            }

            left = Mathf.Min(left, ch.bottomLeft.x);
            right = Mathf.Max(right, ch.topRight.x);
            bottom = Mathf.Min(bottom, ch.descender);
            top = Mathf.Max(top, ch.ascender);
            if (!any)
            {
                baseline = ch.baseLine; // first visible character of the first line
            }

            any = true;
        }

        if (!any)
        {
            return false;
        }

        span.Left = left;
        span.Right = right;
        span.Baseline = baseline;
        span.Height = top - bottom;
        return true;
    }

    /// <summary>True when the label's right glyph edge falls outside its canvas.</summary>
    private static bool RunsOffCanvas(TextMeshProUGUI label, Span us)
    {
        var canvas = label.canvas != null ? label.canvas : label.GetComponentInParent<Canvas>();
        var canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null)
        {
            return false;
        }

        var edge = label.rectTransform.TransformPoint(new Vector3(us.Right, us.Baseline, 0f));
        return canvasRect.InverseTransformPoint(edge).x > canvasRect.rect.xMax;
    }

    private static void LogFalsifier(TextMeshProUGUI source, TextMeshProUGUI label,
                                     Span them, Span us, float gap, bool stacked)
    {
        var sourceRect = source.rectTransform;
        var labelRect = label.rectTransform;
        var sourceCanvas = source.canvas != null ? source.canvas : source.GetComponentInParent<Canvas>();
        var labelCanvas = label.canvas != null ? label.canvas : label.GetComponentInParent<Canvas>();
        bool sameCanvas = sourceCanvas != null && ReferenceEquals(sourceCanvas, labelCanvas);

        float measuredGap = labelRect.anchoredPosition.x + us.Left
                            - (sourceRect.anchoredPosition.x + them.Right);

        string geometry;
        if (TryArcMinutesPerPixel(label, out float arcMinPerPx))
        {
            var distance = WorldUIConfig.ScreenDistance;
            float capArcMin = label.fontSize * CapHeightRatio * arcMinPerPx;
            float emArcMin = label.fontSize * arcMinPerPx;
            geometry = string.Format(CultureInfo.InvariantCulture,
                "font {0:0.#} px authored (game {1:0.#} px), cap {2:0.#} arc-min / em {3:0.#} arc-min "
                + "at {4:0.##} m on a {5:0.##} m quad, {6:0.###} arc-min per authored px",
                label.fontSize, source.fontSize, capArcMin, emArcMin,
                distance != null ? distance.Value : 0f,
                WorldUIConfig.ScreenWidth != null ? WorldUIConfig.ScreenWidth.Value : 0f,
                arcMinPerPx);
        }
        else
        {
            geometry = string.Format(CultureInfo.InvariantCulture,
                "font {0:0.#} px authored (game {1:0.#} px), flat screen — no VR quad, arc-min not applicable",
                label.fontSize, source.fontSize);
        }

        VRLog.Note(Scope, string.Format(CultureInfo.InvariantCulture,
            "MOD VERSION LABEL: CONFIRMED — game '{0}' rect x {1:0.#} y {2:0.#} w {3:0.#} h {4:0.#} "
            + "glyphs {5:0.#}..{6:0.#} | mod '{7}' rect x {8:0.#} y {9:0.#} w {10:0.#} h {11:0.#} "
            + "glyphs {12:0.#}..{13:0.#} | gap {14:0.#} px (asked {15:0.#}) | {16} | same canvas: "
            + "{17} '{18}' | placement: {19}",
            source.text, sourceRect.anchoredPosition.x, sourceRect.anchoredPosition.y,
            sourceRect.rect.width, sourceRect.rect.height, them.Left, them.Right,
            label.text, labelRect.anchoredPosition.x, labelRect.anchoredPosition.y,
            labelRect.rect.width, labelRect.rect.height, us.Left, us.Right,
            measuredGap, gap, geometry,
            sameCanvas ? "yes" : "NO",
            labelCanvas != null ? labelCanvas.name : "none",
            stacked ? "one line above (beside would leave the canvas)" : "beside, same baseline"));
    }
}
