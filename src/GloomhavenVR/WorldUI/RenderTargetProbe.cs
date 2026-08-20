using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE FLICKER, ROUND 6 — MEASURE THE THING THAT ACTUALLY FLICKERS.
///
/// <para>WHAT TWO MEASUREMENTS HAVE ALREADY SETTLED, so that nothing here re-opens them:
/// <list type="number">
/// <item><see cref="PanelFlickerProbe"/> ran two full hardware sessions and printed NOTHING —
/// across every floated panel, the two MultiPass eye passes never disagreed inside a frame and
/// successive frames never alternated, on canvas enabled, sortingOrder, overrideSorting,
/// renderMode, worldCamera, layer, host activity or host pose. <b>The panels' own state is
/// steady.</b></item>
/// <item><see cref="CameraOrderProbe"/> (ModBuild 185) logged exactly ONE render-order shape for a
/// whole session: <c>MapCamera[→RT] → UI Camera[→RT] → GUI 3D Camera[→RT] → HeadCamera[Left] →
/// HeadCamera[Right]</c> — <b>zero cameras between the two eye passes.</b> Every RenderTexture in
/// the frame is finished before either eye starts, so both eyes sample the same pixels.</item>
/// </list></para>
///
/// <para>TAKEN TOGETHER THOSE TWO RESULTS CHANGE THE QUESTION. If the panel is steady and both eyes
/// read the same texture, then what he sees cannot be stereo rivalry — <b>it is TEMPORAL, and it
/// is the same in both eyes.</b> Five builds hunted a one-eye bug that the instruments say is not
/// there. And his latest report narrows the subject to a single object: <i>"Nur der Teil mit dem
/// Character flackert, die 4 Charactere in der Character-UI ist nach wie vor ok."</i> The four
/// portraits are plain sprites; the one that flickers is the live render — a
/// <c>RawImage</c> sampling <c>'Character 3D assembly render texture'</c>, written by
/// <c>'GUI 3D Camera'</c>.</para>
///
/// <para>SO THIS PROBE WATCHES EXACTLY THAT, AND IT DOES NOT NEED A RENDER HOOK. Temporal
/// alternation is visible from Update: sample every RawImage inside a floated panel whose texture
/// is a RenderTexture, together with the camera that writes it, and report any field that goes
/// A-B-A across three consecutive ticks — naming the image AND the field. The candidates it can
/// actually distinguish are all live in the game's own code: <c>Character3DDisplayManager.Display</c>
/// flips <c>beautify.enabled</c> (a full-screen image effect) and
/// <c>Character3DDisplayCameraSettings.UpdateCharacter</c> writes <c>_camera.renderingPath</c>,
/// while <c>Character3D.Show/Hide</c> toggles the model's GameObjects through a
/// <c>HashSet&lt;Component&gt;</c> of show-requests that two of our floated windows could easily be
/// fighting over.</para>
///
/// <para>AND IT PRINTS A CENSUS THE FIRST TIME IT SEES EACH TARGET, whether or not anything is
/// wrong: the RenderTexture's size, format, MSAA and sRGB, the camera's clear flags, culling mask,
/// rendering path and depth, and the full list of Behaviours on that camera with their enabled
/// state (an image effect hides in exactly that list). A quiet log must still say what the thing
/// was made of — the alternative is a sixth round that cannot tell "measured and clean" from
/// "never looked".</para>
/// </summary>
internal static class RenderTargetProbe
{
    private const string Scope = "WorldUI";

    /// <summary>How often the watch list is rebuilt. Content pools in late; 30 frames is well
    /// inside a human "it flickers" and far outside a per-frame cost.</summary>
    private const int RefreshIntervalFrames = 30;

    /// <summary>Reports are latched per image and field, so one finding is one line.</summary>
    private static readonly HashSet<string> Reported = new();

    private sealed class Sample
    {
        internal bool Valid;
        internal bool ImageEnabled;
        internal float Alpha;
        internal Color Colour;
        internal int TextureId;
        internal int MaterialId;
        internal bool RtCreated;
        internal int RtWidth;
        internal int RtHeight;
        internal bool CamAlive;
        internal bool CamEnabled;
        internal float CamDepth;
        internal RenderingPath CamPath;
        internal CameraClearFlags CamClear;
        internal int CamMask;
        internal int BehaviourBits;
    }

    private sealed class Watch
    {
        internal RawImage Image = null!;
        internal Camera? Writer;
        internal Behaviour[] WriterBehaviours = System.Array.Empty<Behaviour>();
        internal readonly Sample[] History = { new(), new(), new() };
        internal int Cursor;
        internal bool CensusDone;
    }

    private static readonly List<Watch> Watches = new(4);
    private static readonly List<RawImage> Scratch = new(16);
    private static readonly StringBuilder Sb = new(512);
    private static int _refreshFrame = -1;
    private static bool _armed;

    /// <summary>Arm/disarm with the floated-window layer, and sample once per tick while armed.</summary>
    internal static void Tick(bool wanted)
    {
        if (!wanted)
        {
            if (_armed)
            {
                _armed = false;
                Watches.Clear();
                _refreshFrame = -1;
            }
            return;
        }
        if (!_armed)
        {
            _armed = true;
            _refreshFrame = -1;
            VRLog.Info(Scope, "RENDER TARGET PROBE armed — watching every RawImage inside a floated "
                              + "panel whose texture is a RenderTexture, together with the camera that "
                              + "writes it. It censuses each target once (so the log says what the thing "
                              + "is made of even when nothing is wrong) and then reports any field that "
                              + "goes A-B-A across three ticks. This is the TEMPORAL question: "
                              + "PanelFlickerProbe proved the panels are steady and CameraOrderProbe "
                              + "proved both eyes read the same texture, so what is left is content that "
                              + "changes from frame to frame — identically in both eyes.");
        }

        if (_refreshFrame == -1 || Time.frameCount - _refreshFrame >= RefreshIntervalFrames)
        {
            _refreshFrame = Time.frameCount;
            Refresh();
        }
        for (int i = 0; i < Watches.Count; i++)
            SampleAndJudge(Watches[i]);
    }

    /// <summary>Rebuild the watch list from the live floated panels.</summary>
    private static void Refresh()
    {
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int p = 0; p < panels.Count; p++)
        {
            ConvertedPanel panel = panels[p];
            if (panel == null || panel.HostGo == null)
                continue;
            Scratch.Clear();
            panel.HostGo.GetComponentsInChildren(true, Scratch);
            for (int i = 0; i < Scratch.Count; i++)
            {
                RawImage img = Scratch[i];
                if (img == null || img.texture is not RenderTexture)
                    continue;
                if (Find(img) != null)
                    continue;
                var watch = new Watch { Image = img };
                BindWriter(watch);
                Watches.Add(watch);
            }
        }
        // Drop dead entries (panel released, image destroyed).
        for (int i = Watches.Count - 1; i >= 0; i--)
        {
            if (Watches[i].Image == null)
                Watches.RemoveAt(i);
        }
    }

    private static Watch? Find(RawImage img)
    {
        for (int i = 0; i < Watches.Count; i++)
        {
            if (ReferenceEquals(Watches[i].Image, img))
                return Watches[i];
        }
        return null;
    }

    /// <summary>Find the camera that writes this image's RenderTexture, and cache its Behaviours.</summary>
    private static void BindWriter(Watch watch)
    {
        var rt = watch.Image.texture as RenderTexture;
        if (rt == null)
            return;
        Camera[] all = Object.FindObjectsOfType<Camera>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && ReferenceEquals(all[i].targetTexture, rt))
            {
                watch.Writer = all[i];
                watch.WriterBehaviours = all[i].GetComponents<Behaviour>();
                return;
            }
        }
    }

    private static void SampleAndJudge(Watch watch)
    {
        if (watch.Image == null)
            return;
        Sample now = watch.History[watch.Cursor];
        Take(watch, now);
        Census(watch, now);

        Sample prev = watch.History[(watch.Cursor + 2) % 3];
        Sample before = watch.History[(watch.Cursor + 1) % 3];
        watch.Cursor = (watch.Cursor + 1) % 3;

        // A-B-A: this tick matches two ticks ago and differs from the one between. A steady value
        // and a one-off change both fail that test, which is exactly the point — a flicker is an
        // alternation, not a transition.
        if (!now.Valid || !prev.Valid || !before.Valid)
            return;
        string? changed = FirstDifference(prev, now);
        if (changed != null)
            return; // not back where it was — a transition, not an alternation
        string? swing = FirstDifference(before, now);
        if (swing == null)
            return; // nothing moved at all
        Report(watch, swing);
    }

    /// <summary>One line per image and field — a finding is stated once, not per frame.</summary>
    private static void Report(Watch w, string field)
    {
        string key = $"{w.Image.GetInstanceID()}|{field}";
        if (!Reported.Add(key))
            return;
        var rt = w.Image.texture as RenderTexture;
        VRLog.Warn(Scope, $"RENDER TARGET ALTERNATION on '{w.Image.name}' (RenderTexture "
                          + $"'{(rt != null ? rt.name : "<none>")}', writer "
                          + $"'{(w.Writer != null ? w.Writer.name : "<none>")}'): {field}. "
                          + "It came BACK to its earlier value, so this is an alternation and not a "
                          + "transition — the signature of a flicker. Both eyes see the same thing "
                          + "(CameraOrderProbe) and the panel around it is steady (PanelFlickerProbe), "
                          + "so this field is the remaining explanation for what he reports.");
    }

    private static void Take(Watch w, Sample s)
    {
        RawImage img = w.Image;
        s.Valid = true;
        s.ImageEnabled = img.enabled && img.gameObject.activeInHierarchy;
        s.Alpha = img.canvasRenderer != null ? img.canvasRenderer.GetAlpha() : 1f;
        s.Colour = img.color;
        Texture? tex = img.texture;
        s.TextureId = tex != null ? tex.GetInstanceID() : 0;
        s.MaterialId = img.material != null ? img.material.GetInstanceID() : 0;
        var rt = tex as RenderTexture;
        s.RtCreated = rt != null && rt.IsCreated();
        s.RtWidth = rt != null ? rt.width : 0;
        s.RtHeight = rt != null ? rt.height : 0;

        Camera? cam = w.Writer;
        s.CamAlive = cam != null;
        s.CamEnabled = cam != null && cam.enabled && cam.gameObject.activeInHierarchy;
        s.CamDepth = cam != null ? cam.depth : 0f;
        s.CamPath = cam != null ? cam.renderingPath : RenderingPath.UsePlayerSettings;
        s.CamClear = cam != null ? cam.clearFlags : CameraClearFlags.Nothing;
        s.CamMask = cam != null ? cam.cullingMask : 0;

        // One bit per Behaviour on the writer camera — an image effect toggling on and off
        // (Character3DDisplayManager flips Beautify.enabled) shows up here and nowhere else.
        int bits = 0;
        for (int i = 0; i < w.WriterBehaviours.Length && i < 32; i++)
        {
            Behaviour b = w.WriterBehaviours[i];
            if (b != null && b.enabled)
                bits |= 1 << i;
        }
        s.BehaviourBits = bits;
    }

    private static string? FirstDifference(Sample a, Sample b)
    {
        if (a.ImageEnabled != b.ImageEnabled) return $"RawImage active {a.ImageEnabled}↔{b.ImageEnabled}";
        if (!Mathf.Approximately(a.Alpha, b.Alpha)) return $"CanvasRenderer alpha {a.Alpha:F3}↔{b.Alpha:F3}";
        if (a.Colour != b.Colour) return $"RawImage colour {a.Colour}↔{b.Colour}";
        if (a.TextureId != b.TextureId) return $"texture instance {a.TextureId}↔{b.TextureId}";
        if (a.MaterialId != b.MaterialId) return $"material instance {a.MaterialId}↔{b.MaterialId}";
        if (a.RtCreated != b.RtCreated) return $"RenderTexture.IsCreated {a.RtCreated}↔{b.RtCreated}";
        if (a.RtWidth != b.RtWidth || a.RtHeight != b.RtHeight)
            return $"RenderTexture size {a.RtWidth}x{a.RtHeight}↔{b.RtWidth}x{b.RtHeight}";
        if (a.CamAlive != b.CamAlive) return $"writer camera exists {a.CamAlive}↔{b.CamAlive}";
        if (a.CamEnabled != b.CamEnabled) return $"writer camera active {a.CamEnabled}↔{b.CamEnabled}";
        if (!Mathf.Approximately(a.CamDepth, b.CamDepth)) return $"writer depth {a.CamDepth:F1}↔{b.CamDepth:F1}";
        if (a.CamPath != b.CamPath) return $"writer renderingPath {a.CamPath}↔{b.CamPath}";
        if (a.CamClear != b.CamClear) return $"writer clearFlags {a.CamClear}↔{b.CamClear}";
        if (a.CamMask != b.CamMask) return $"writer cullingMask 0x{a.CamMask:X8}↔0x{b.CamMask:X8}";
        if (a.BehaviourBits != b.BehaviourBits)
            return $"writer camera component enabled-bits 0x{a.BehaviourBits:X}↔0x{b.BehaviourBits:X} "
                   + "(an image effect toggling — see the census line for which component is which bit)";
        return null;
    }

    /// <summary>The one-time inventory of a watched target — printed whether or not it misbehaves.</summary>
    private static void Census(Watch w, Sample s)
    {
        if (w.CensusDone)
            return;
        w.CensusDone = true;
        var rt = w.Image.texture as RenderTexture;
        Camera? cam = w.Writer;
        Sb.Length = 0;
        for (int i = 0; i < w.WriterBehaviours.Length && i < 32; i++)
        {
            Behaviour b = w.WriterBehaviours[i];
            if (i > 0)
                Sb.Append(", ");
            Sb.Append(i).Append(':').Append(b != null ? b.GetType().Name : "<null>")
              .Append(b != null && b.enabled ? "(on)" : "(off)");
        }
        VRLog.Info(Scope, $"RENDER TARGET CENSUS: RawImage '{w.Image.name}' shows RenderTexture "
                          + $"'{(rt != null ? rt.name : "<none>")}' "
                          + $"{s.RtWidth}x{s.RtHeight} fmt={(rt != null ? rt.format.ToString() : "?")} "
                          + $"msaa={(rt != null ? rt.antiAliasing : 0)} "
                          + $"sRGB={(rt != null && rt.sRGB)} depth={(rt != null ? rt.depth : 0)}. "
                          + $"WRITER: {(cam != null ? $"'{cam.name}' enabled={s.CamEnabled} depth={s.CamDepth:F1} "
                              + $"path={s.CamPath} clear={s.CamClear} mask=0x{s.CamMask:X8} "
                              + $"stereoTarget={cam.stereoTargetEye} allowMSAA={cam.allowMSAA} allowHDR={cam.allowHDR}"
                              : "NO CAMERA TARGETS THIS TEXTURE — it is written by something else (a Blit, a "
                                + "video player, or a camera that is currently destroyed)")}. "
                          + $"COMPONENTS ON THE WRITER: [{Sb}]. This line is the baseline: a quiet log "
                          + "still says what the target was made of.");
    }
}
