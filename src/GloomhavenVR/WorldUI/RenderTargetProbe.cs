using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE FLICKER — THE INSTRUMENT ITSELF WAS WRONG (ModBuild 188), and this is the repair.
///
/// <para>WHAT ModBuild 186 BELIEVED IT HAD MEASURED: <c>Beautify</c> on the 'GUI 3D Camera' going
/// ON, OFF, ON, OFF every few frames, and with it the graded/ungraded render alternating inside
/// 'Character 3D assembly wide render texture'. ModBuild 187 patched the game's show-request
/// refcount on that basis and the warnings came back unchanged. They came back because
/// <b>this probe never detected an alternation in its life</b>. Its own doc said "A-B-A: this tick
/// matches two ticks ago and differs from the one between"; its code said the opposite:</para>
///
/// <code>
///     string? changed = FirstDifference(prev, now);
///     if (changed != null) return;          // REQUIRES now == prev  (t-1)
///     string? swing = FirstDifference(before, now);
///     if (swing == null) return;            // REQUIRES now != before (t-2)
/// </code>
///
/// <para>now == t-1 and now != t-2 is a <b>TRANSITION that then held</b> — the exact opposite of a
/// flicker. A value alternating every frame never satisfies <c>now == prev</c> at all, so the old
/// test was BLIND to the thing it was written to find and LOUD about the thing it was written to
/// ignore.</para>
///
/// <para>AND THE 187 HARDWARE LOG PROVES IT, by position alone. The assembly screen opened once and
/// closed once in the whole session, and the six warnings sit exactly on those two frames:
/// <c>UIWindow SHOWN: 'Campaign Adventure Party Assembly Variant'</c> at log line 4297 → three
/// <c>0xD↔0xF</c> warnings at 4305-4307; <c>UIWindow hidden</c> at 4364/4365 → three
/// <c>0xF↔0xD</c> warnings at 4367-4369. Nothing in between. (Three warnings per event because
/// three separate RawImages sample that one RenderTexture — see the three identical CENSUS lines at
/// 3069-3071.) That is <c>Display()</c> setting <c>beautify.enabled = true</c> when the window
/// opened and <c>Hide()</c> clearing it when the window closed: one legitimate ON and one
/// legitimate OFF, correctly reported as such by a test that could only describe them as a flicker.
/// <b>Beautify is not the flicker, and the sixth round never had a subject.</b></para>
///
/// <para>WHAT THIS VERSION MEASURES, and how it says so:
/// <list type="bullet">
/// <item><b>ALTERNATION</b> — <c>now == t-2 &amp;&amp; now != t-1</c>: the value left and came back
/// inside three ticks. This is the flicker signature, and it is what the doc always claimed.</item>
/// <item><b>TRANSITION</b> — <c>now == t-1 &amp;&amp; now != t-2</c>: it changed once and held. Logged
/// at Info and labelled NOT A FLICKER, so no future round can promote one into evidence again.</item>
/// <item><b>SWEEP</b> — three different values in three ticks. Counted, not narrated.</item>
/// <item>A PERIODIC BASELINE every <see cref="SummaryIntervalSeconds"/> seconds per watched image,
/// printed whether or not anything moved: ticks sampled, alternations, transitions, sweeps, and the
/// live values. A silent probe and a probe that never ran must never look the same again.</item>
/// </list></para>
///
/// <para>IT ALSO WATCHES WHAT NO CAMERA BIT CAN SEE. <c>Character3DDisplayManager.Hide(request)</c>
/// calls <c>character3D.Hide()</c> — <c>SetActive(false)</c> on the model GameObjects under
/// <c>character3DHolder</c> — and unlike the <c>beautify.enabled = false</c> line beside it that
/// call is NOT gated on <c>showRequests.Count == 0</c>. A model switched off under a window that
/// still wants it drains the character out of the render texture while every component on the
/// writer camera stays exactly as it was. So each sample now also carries the manager's own state:
/// how many models are active, how many show-requests are held, and <c>isHidden</c>. If the
/// character is what blinks, that is where it will show.</para>
///
/// <para>WHAT REMAINS SETTLED FROM EARLIER ROUNDS, so nothing here re-opens it:
/// the ModBuild 182 panel-state probe (since removed) ran three hardware sessions silent (the floated panels' own
/// state is steady), and <see cref="CameraOrderProbe"/> logged exactly one camera-order shape for a
/// whole session with ZERO cameras between the two eye passes (both eyes sample the same pixels).
/// Whatever the flicker is, it is temporal and identical in both eyes.</para>
///
/// <para>=====================================================================================</para>
///
/// <para>ModBuild 189 — AND ITS OWN RESULT RETIRES ITS OWN FAMILY. The 188 hardware run printed
/// <c>3665 ticks sampled, 0 alternation(s)</c>. Zero. Every field this class watches — the RawImage,
/// its texture handle, its material, the RenderTexture's created/size state, the writer camera's
/// enabled/depth/path/clearFlags/cullingMask, every Behaviour's enabled bit, and the display
/// manager's model count, show-request refcount and isHidden — was STEADY for the whole session
/// while he watched the thing flicker. Together with the two probes above that closes the entire
/// C# STATE family: nothing about how the image is WIRED alternates. So this round stops measuring
/// state and measures two things it never had:</para>
/// <list type="bullet">
/// <item><b>THE SAMPLING</b> — <see cref="PanelSamplingProbe"/>, driven from <see cref="Tick"/>
/// below. It computes source texels per rendered pixel for every graphic on every floated panel and
/// prints the complained-about surface next to the quiet ones on the same canvas. Read its class
/// doc: it also carries the finding that killed the "the RenderTexture is special" framing — the
/// story picture he reports is NOT a RenderTexture at all, it is an Addressables <c>.png</c> Sprite
/// on a plain <c>Image</c> (decompiled <c>StoryImageViewer</c>), which THIS class could never see
/// because it only ever enumerates RawImages whose texture is a RenderTexture.</item>
/// <item><b>THE CONTENT</b> — the CONTENT block below. Every tick the RenderTexture is downsampled
/// by one <c>Graphics.Blit</c> into a mod-owned 32x32 and pulled back with
/// <c>AsyncGPUReadback</c> (no <c>ReadPixels</c>, no GPU stall, no per-frame pipeline flush). That
/// yields a per-frame content signature, and with it the question no state probe could answer:
/// <b>do the PIXELS INSIDE the texture change from frame to frame while everything around them
/// holds still?</b></item>
/// </list>
///
/// <para>WHY THAT SECOND MEASUREMENT DECIDES SOMETHING. The two candidate mechanisms make opposite
/// predictions and the log will separate them without a single further hypothesis:</para>
/// <list type="bullet">
/// <item>If the content is BIT-IDENTICAL frame to frame (high <c>identical</c> count, luma range
/// ~0.000) and he still sees it flicker, then nothing is changing in the texture and the flicker is
/// created downstream, when that unchanging texture is SAMPLED onto a moving world-space quad. That
/// is texture-space shimmer and the sampling numbers are where the answer is.</item>
/// <item>If the signature ALTERNATES — hash equal to two frames ago and different from one frame
/// ago — then the source really is switching between two states, and the exact-alternation count is
/// the first direct evidence of it in eight rounds.</item>
/// <item>If the content simply CHANGES every frame (an idle animation), the mean-luma range and the
/// direction-reversal count say whether it changes by a lot and back, or drifts smoothly. A smooth
/// idle produces few large reversals; a flicker produces many.</item>
/// </list>
///
/// <para>THE CENSUS ALSO GREW THE FIELDS THAT SHOULD HAVE BEEN THERE IN ROUND ONE:
/// <c>filterMode</c>, <c>useMipMap</c>, <c>autoGenerateMips</c>, <c>mipmapCount</c>,
/// <c>anisoLevel</c>, <c>wrapMode</c>, plus the project's colour space and global aniso setting.
/// The 187 census reported <c>msaa=1 sRGB=False</c> and stopped there, which left the two properties
/// that actually govern minification unmeasured for six rounds.</para>
/// </summary>
internal static class RenderTargetProbe
{
    private const string Scope = "WorldUI";

    /// <summary>How often the watch list is rebuilt. Content pools in late; 30 frames is well
    /// inside a human "it flickers" and far outside a per-frame cost.</summary>
    private const int RefreshIntervalFrames = 30;

    /// <summary>How often each watch prints its baseline, hit or no hit.</summary>
    private const float SummaryIntervalSeconds = 10f;

    /// <summary>Findings are latched per image, per kind and per field, so one finding is one line;
    /// the running counts live in the periodic summary instead.</summary>
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

        // Character3DDisplayManager's own state — the model toggle is invisible to everything above.
        internal bool MgrValid;
        internal int ModelChildren;
        internal int ModelActive;
        internal int ShowRequests;
        internal bool IsHidden;
    }

    private sealed class Watch
    {
        internal RawImage Image = null!;
        internal Camera? Writer;
        internal Behaviour[] WriterBehaviours = System.Array.Empty<Behaviour>();
        internal Character3DDisplayManager? Manager;
        internal Transform? Holder;
        internal readonly Sample[] History = { new(), new(), new() };
        internal int Cursor;
        internal bool CensusDone;

        // Accounting for the periodic baseline.
        internal int Ticks;
        internal int Alternations;
        internal int Transitions;
        internal int Sweeps;
        internal float NextSummary;
    }

    private static readonly List<Watch> Watches = new(4);
    private static readonly List<RawImage> Scratch = new(16);
    private static readonly StringBuilder Sb = new(512);
    private static readonly StringBuilder Diff = new(256);
    private static int _refreshFrame = -1;
    private static bool _armed;

    // Reflection into Character3DDisplayManager, resolved once. All three are private fields of a
    // game type we do not own; if any of them is ever renamed the manager half of the sample simply
    // stops being taken (MgrValid = false) and the probe carries on measuring the camera half.
    private static FieldInfo? _holderField;
    private static FieldInfo? _showRequestsField;
    private static FieldInfo? _isHiddenField;
    private static bool _mgrResolved;

    /// <summary>Arm/disarm with the floated-window layer, and sample once per tick while armed.</summary>
    /// <summary>
    /// STOOD DOWN AT ModBuild 195 — the question this probe was built for is ANSWERED.
    /// <para>It watched the character-preview RenderTexture for the "es flackert" report and, across
    /// ten hardware rounds, reported <c>44200 ticks sampled, 0 alternation(s), 0 transition(s),
    /// 0 sweep(s)</c>. It was CORRECT and it is now SPENT: the flicker was undersampled
    /// RASTERIZATION, confirmed on hardware when <c>[WorldUI] PanelSupersample</c> ended it. No
    /// remaining question needs a per-frame readback of that texture.</para>
    /// <para>WHAT IT COSTS TO LEAVE ARMED, and why that decides it: the ModalFallback step runs at
    /// <b>12.4-13.0 ms average on 100% of frames</b> against an 11.11 ms budget, i.e. the whole mod
    /// sits at roughly 50 Hz in the map room. The perf lane bounded the search space by measurement —
    /// it is NOT CanvasConversion (0.58 ms), NOT the flatness guarantee (0.3 ms), NOT any subtree
    /// walk (0.5 ms calibrated for 15,000 transforms), NOT the catch-all recursion (one refusal in
    /// 46k frames) — and named this class the leading suspect: a <c>Graphic.Blit</c> plus an
    /// <c>AsyncGPUReadback</c> issued FROM UPDATE every frame, outside the render loop, plus three
    /// reflection field reads per watch. Its arming frame is also the frame the 3.9 -> 11.5 ms ramp
    /// begins.</para>
    /// <para>THAT SUSPICION IS NOT PROOF, and the honest move is not to guess either way: the
    /// ModBuild 195 sub-step instrument will PRICE this class exactly, as
    /// <c>ModalFallback.Probe.RenderTarget</c>. But a diagnostic that has answered its question does
    /// not get to keep a multi-millisecond benefit of the doubt on a user's frame time. Standing it
    /// down costs nothing we still need and, if the next log shows the frame budget recovered, that
    /// IS the measurement. Re-arming is deleting this early return.</para>
    /// </summary>
    internal static void Tick(bool wanted)
    {
        // The stand-down runs through the normal disarm path (wanted:false), so the restore, the
        // in-flight readback generation bump and the final baseline all happen exactly as they would
        // on a real teardown — never by simply ceasing to be called.
        wanted = false;
        // ModBuild 189: the sampling instrument rides the same arm condition. It lives here rather
        // than at the tick site because the Update seam that arms these probes is in
        // ModalFallback.4.Tick.cs, which this lane does not own — and this method is already called
        // from there on exactly the right condition. See PanelSamplingProbe.
        PanelSamplingProbe.Tick(wanted);

        if (!wanted)
        {
            if (_armed)
            {
                for (int i = 0; i < Watches.Count; i++)
                    Summarise(Watches[i], "STAND-DOWN");
                _armed = false;
                Watches.Clear();
                _refreshFrame = -1;
                // Bump the generation so any AsyncGPUReadback still in flight is discarded on
                // arrival instead of being folded into the next arming's statistics. The queue is
                // NOT cleared: it is the FIFO that pairs each callback with the request that
                // caused it, and dropping entries would mispair every later readback.
                _contentGen++;
            }
            return;
        }
        if (!_armed)
        {
            _armed = true;
            _refreshFrame = -1;
            VRLog.Info(Scope, "RENDER TARGET PROBE armed (ModBuild 188 — the ALTERNATION TEST IS "
                              + "FIXED). It watches every RawImage inside a floated panel whose texture "
                              + "is a RenderTexture, together with the camera that writes it AND the "
                              + "Character3DDisplayManager on that camera (model count, show-request "
                              + "refcount, isHidden — the model toggle is invisible to the camera's "
                              + "component bits). A finding is now classified: ALTERNATION = the value "
                              + "left and CAME BACK inside three ticks (a flicker); TRANSITION = it "
                              + "changed once and held (NOT a flicker — ModBuild 186's whole case was "
                              + "six of these, one window opening and one closing). Every watch also "
                              + "prints a baseline every "
                              + $"{SummaryIntervalSeconds:F0}s whether or not anything moved.");
        }

        if (_refreshFrame == -1 || Time.frameCount - _refreshFrame >= RefreshIntervalFrames)
        {
            _refreshFrame = Time.frameCount;
            Refresh();
        }
        for (int i = 0; i < Watches.Count; i++)
            SampleAndJudge(Watches[i]);
        TickContent();
    }

    /// <summary>
    /// Full teardown (module Shutdown / ScriptEngine hot reload): stand the sampling probe down so
    /// every panel gets its authored graphics back, drain any readback still in flight, and release
    /// the mod-owned probe target. <c>WaitAllRequests</c> is a one-time stall on a path that is
    /// already tearing the whole module down — it is the price of never destroying a RenderTexture
    /// a driver is still copying out of.
    /// </summary>
    internal static void Shutdown()
    {
        PanelSamplingProbe.Shutdown();
        _armed = false;
        Watches.Clear();
        _refreshFrame = -1;
        _contentGen++;
        if (_contentInFlight > 0)
        {
            AsyncGPUReadback.WaitAllRequests();
            _contentInFlight = 0;
        }
        ContentPending.Clear();
        ContentProbes.Clear();
        if (_contentRt != null)
        {
            _contentRt.Release();
            Object.Destroy(_contentRt);
            _contentRt = null;
        }
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
                var watch = new Watch { Image = img, NextSummary = Time.unscaledTime + SummaryIntervalSeconds };
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

    /// <summary>Find the camera that writes this image's RenderTexture, and cache its Behaviours and
    /// (if it carries one) the character-display manager whose private state we also sample.</summary>
    private static void BindWriter(Watch watch)
    {
        var rt = watch.Image.texture as RenderTexture;
        if (rt == null)
            return;
        Camera[] all = Object.FindObjectsOfType<Camera>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !ReferenceEquals(all[i].targetTexture, rt))
                continue;
            watch.Writer = all[i];
            watch.WriterBehaviours = all[i].GetComponents<Behaviour>();
            watch.Manager = all[i].GetComponent<Character3DDisplayManager>();
            if (watch.Manager != null)
            {
                ResolveManagerFields();
                watch.Holder = _holderField?.GetValue(watch.Manager) as Transform;
            }
            return;
        }
    }

    /// <summary>Resolve the manager's three private fields once, and say so once — including when
    /// one of them is missing, because a silently half-blind sample is how ModBuild 186 happened.</summary>
    private static void ResolveManagerFields()
    {
        if (_mgrResolved)
            return;
        _mgrResolved = true;
        _holderField = AccessTools.Field(typeof(Character3DDisplayManager), "character3DHolder");
        _showRequestsField = AccessTools.Field(typeof(Character3DDisplayManager), "showRequests");
        _isHiddenField = AccessTools.Field(typeof(Character3DDisplayManager), "isHidden");
        if (_holderField != null && _showRequestsField != null && _isHiddenField != null)
            return;
        VRLog.Warn(Scope, "RENDER TARGET PROBE: Character3DDisplayManager's private state is not fully "
                          + "reflectable — character3DHolder="
                          + $"{(_holderField != null ? "found" : "MISSING")}, showRequests="
                          + $"{(_showRequestsField != null ? "found" : "MISSING")}, isHidden="
                          + $"{(_isHiddenField != null ? "found" : "MISSING")}. The consequence is that "
                          + "the MODEL half of every sample is dropped (the probe still measures the "
                          + "RawImage, the RenderTexture and every component on the writer camera), so a "
                          + "character switched off under a window that still wants it would go "
                          + "unreported. Nothing else changes and nothing is written.");
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
        watch.Ticks++;

        if (now.Valid && prev.Valid && before.Valid)
        {
            // THE THREE SHAPES, and the reason this class exists. ModBuild 186 tested for the
            // second one and called it the first.
            bool sameAsPrev = Differences(prev, now) == null;
            bool sameAsBefore = Differences(before, now) == null;
            if (sameAsBefore && !sameAsPrev)
            {
                // now == t-2, now != t-1 — it left and came back. THIS is a flicker.
                watch.Alternations++;
                Report(watch, Differences(prev, now)!, alternation: true);
            }
            else if (sameAsPrev && !sameAsBefore)
            {
                // now == t-1, now != t-2 — one change that held. NOT a flicker.
                watch.Transitions++;
                Report(watch, Differences(before, now)!, alternation: false);
            }
            else if (!sameAsPrev && !sameAsBefore)
            {
                // Three different values in three ticks: a sweep (a fade, a resize). Counted only.
                watch.Sweeps++;
            }
        }

        if (Time.unscaledTime >= watch.NextSummary)
        {
            watch.NextSummary = Time.unscaledTime + SummaryIntervalSeconds;
            Summarise(watch, "BASELINE");
        }
    }

    /// <summary>One line per image, kind and field — a finding is stated once; the counts continue
    /// in the periodic summary.</summary>
    private static void Report(Watch w, string field, bool alternation)
    {
        string key = $"{w.Image.GetInstanceID()}|{(alternation ? "A" : "T")}|{field}";
        if (!Reported.Add(key))
            return;
        var rt = w.Image.texture as RenderTexture;
        string where = $"on '{w.Image.name}' (RenderTexture '{(rt != null ? rt.name : "<none>")}', writer "
                       + $"'{(w.Writer != null ? w.Writer.name : "<none>")}'): {field}";
        if (alternation)
        {
            VRLog.Warn(Scope, $"RENDER TARGET ALTERNATION {where}. The value LEFT and CAME BACK within "
                              + "three ticks — this is the flicker signature, not a state change. Both "
                              + "eyes see the same texture (CameraOrderProbe) and the panel around it is "
                              + "steady (the panel-state probe), so this field is a live explanation for what "
                              + "he reports. Alternation #"
                              + $"{w.Alternations} on this image; see the next BASELINE line for the rate.");
            return;
        }
        VRLog.Info(Scope, $"RENDER TARGET TRANSITION {where}. It changed ONCE and HELD — this is NOT a "
                          + "flicker and must not be read as one. ModBuild 186's entire case was six lines "
                          + "of exactly this shape (one window opening, one window closing) reported by an "
                          + "inverted A-B-A test. Transition #"
                          + $"{w.Transitions} on this image.");
    }

    /// <summary>The periodic baseline — printed whether or not anything moved.</summary>
    private static void Summarise(Watch w, string why)
    {
        if (w.Image == null)
            return;
        Sample last = w.History[(w.Cursor + 2) % 3];
        var rt = w.Image.texture as RenderTexture;
        VRLog.Info(Scope, $"RENDER TARGET {why} '{w.Image.name}' (RenderTexture "
                          + $"'{(rt != null ? rt.name : "<none>")}', writer "
                          + $"'{(w.Writer != null ? w.Writer.name : "<none>")}'): {w.Ticks} ticks sampled, "
                          + $"{w.Alternations} alternation(s), {w.Transitions} transition(s), {w.Sweeps} "
                          + "sweep(s). LIVE: image active="
                          + $"{last.ImageEnabled} alpha={last.Alpha:F2} camera enabled={last.CamEnabled} "
                          + $"path={last.CamPath} components=[{BehaviourList(w, last)}]"
                          + (last.MgrValid
                              ? $" | manager: {last.ModelActive}/{last.ModelChildren} model object(s) active, "
                                + $"{last.ShowRequests} show-request(s), isHidden={last.IsHidden}"
                              : " | manager state NOT sampled (no Character3DDisplayManager on the writer, "
                                + "or its private fields were not reflectable)")
                          + ContentSentence(w)
                          + " ZERO STATE ALTERNATIONS ON THIS LINE MEANS THIS TARGET WAS MEASURED AND "
                          + "IS STEADY — it does not mean nothing looked. And read the content clause "
                          + "against it: state steady + content static is the reading that hands the "
                          + "whole question to the PANEL SAMPLING line.");
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
        // (Character3DDisplayManager writes beautify.enabled) shows up here and nowhere else.
        int bits = 0;
        for (int i = 0; i < w.WriterBehaviours.Length && i < 32; i++)
        {
            Behaviour b = w.WriterBehaviours[i];
            if (b != null && b.enabled)
                bits |= 1 << i;
        }
        s.BehaviourBits = bits;

        // The manager's own state. character3D.Show()/Hide() are SetActive() on the models under
        // character3DHolder and leave every field above untouched, so without this the probe cannot
        // see the character leave the render texture at all.
        s.MgrValid = false;
        s.ModelChildren = 0;
        s.ModelActive = 0;
        s.ShowRequests = 0;
        s.IsHidden = false;
        if (w.Manager == null)
            return;
        if (w.Holder == null && _holderField != null)
            w.Holder = _holderField.GetValue(w.Manager) as Transform; // pooled in late
        if (w.Holder == null || _showRequestsField == null || _isHiddenField == null)
            return;
        s.MgrValid = true;
        s.ModelChildren = w.Holder.childCount;
        for (int i = 0; i < s.ModelChildren; i++)
        {
            Transform child = w.Holder.GetChild(i);
            if (child != null && child.gameObject.activeSelf)
                s.ModelActive++;
        }
        s.ShowRequests = _showRequestsField.GetValue(w.Manager) is ICollection<Component> set ? set.Count : -1;
        s.IsHidden = _isHiddenField.GetValue(w.Manager) is true;
    }

    /// <summary>Every field that differs between two samples, or null when they are identical. It
    /// lists ALL of them: naming only the first is how a model toggle hides behind an image effect.</summary>
    private static string? Differences(Sample a, Sample b)
    {
        Diff.Length = 0;
        if (a.ImageEnabled != b.ImageEnabled) Add($"RawImage active {a.ImageEnabled}→{b.ImageEnabled}");
        if (!Mathf.Approximately(a.Alpha, b.Alpha)) Add($"CanvasRenderer alpha {a.Alpha:F3}→{b.Alpha:F3}");
        if (a.Colour != b.Colour) Add($"RawImage colour {a.Colour}→{b.Colour}");
        if (a.TextureId != b.TextureId) Add($"texture instance {a.TextureId}→{b.TextureId}");
        if (a.MaterialId != b.MaterialId) Add($"material instance {a.MaterialId}→{b.MaterialId}");
        if (a.RtCreated != b.RtCreated) Add($"RenderTexture.IsCreated {a.RtCreated}→{b.RtCreated}");
        if (a.RtWidth != b.RtWidth || a.RtHeight != b.RtHeight)
            Add($"RenderTexture size {a.RtWidth}x{a.RtHeight}→{b.RtWidth}x{b.RtHeight}");
        if (a.CamAlive != b.CamAlive) Add($"writer camera exists {a.CamAlive}→{b.CamAlive}");
        if (a.CamEnabled != b.CamEnabled) Add($"writer camera active {a.CamEnabled}→{b.CamEnabled}");
        if (!Mathf.Approximately(a.CamDepth, b.CamDepth)) Add($"writer depth {a.CamDepth:F1}→{b.CamDepth:F1}");
        if (a.CamPath != b.CamPath) Add($"writer renderingPath {a.CamPath}→{b.CamPath}");
        if (a.CamClear != b.CamClear) Add($"writer clearFlags {a.CamClear}→{b.CamClear}");
        if (a.CamMask != b.CamMask) Add($"writer cullingMask 0x{a.CamMask:X8}→0x{b.CamMask:X8}");
        if (a.BehaviourBits != b.BehaviourBits) Add(BitsChanged(a.BehaviourBits, b.BehaviourBits));
        if (a.MgrValid && b.MgrValid)
        {
            if (a.ModelActive != b.ModelActive || a.ModelChildren != b.ModelChildren)
                Add($"CHARACTER MODELS active {a.ModelActive}/{a.ModelChildren}→"
                    + $"{b.ModelActive}/{b.ModelChildren} (Character3D.Show/Hide SetActive's the models "
                    + "under character3DHolder — no component on the writer camera moves when it does)");
            if (a.ShowRequests != b.ShowRequests)
                Add($"show-request refcount {a.ShowRequests}→{b.ShowRequests}");
            if (a.IsHidden != b.IsHidden)
                Add($"manager isHidden {a.IsHidden}→{b.IsHidden}");
        }
        return Diff.Length > 0 ? Diff.ToString() : null;
    }

    private static void Add(string what)
    {
        if (Diff.Length > 0)
            Diff.Append("; ");
        Diff.Append(what);
    }

    /// <summary>Name the components whose enabled flag moved, instead of printing a hex mask the
    /// reader has to decode against a census line printed thousands of lines earlier.</summary>
    private static string BitsChanged(int a, int b)
    {
        int moved = a ^ b;
        Sb.Length = 0;
        Sb.Append("writer component enabled ");
        bool first = true;
        for (int i = 0; i < 32; i++)
        {
            if ((moved & (1 << i)) == 0)
                continue;
            if (!first)
                Sb.Append(", ");
            first = false;
            Sb.Append('#').Append(i).Append(' ')
              .Append((a & (1 << i)) != 0).Append("→").Append((b & (1 << i)) != 0);
        }
        Sb.Append(" (mask 0x").Append(a.ToString("X")).Append("→0x").Append(b.ToString("X")).Append(')');
        return Sb.ToString();
    }

    /// <summary>The writer camera's Behaviours with their live enabled state — used by the census
    /// and repeated in every baseline so a reader never has to scroll back for the bit meanings.</summary>
    private static string BehaviourList(Watch w, Sample s)
    {
        Sb.Length = 0;
        for (int i = 0; i < w.WriterBehaviours.Length && i < 32; i++)
        {
            Behaviour b = w.WriterBehaviours[i];
            if (i > 0)
                Sb.Append(", ");
            Sb.Append(i).Append(':').Append(b != null ? b.GetType().Name : "<null>")
              .Append((s.BehaviourBits & (1 << i)) != 0 ? "(on)" : "(off)");
        }
        return Sb.ToString();
    }

    // =====================================================================================
    // CONTENT — the only part of this class that looks at PIXELS.
    //
    // Every state probe in rounds 1-7 answered "is the wiring steady?" and every one of them said
    // yes. This answers the question none of them could: does the picture INSIDE the texture change
    // from frame to frame? A mod-owned 32x32 RenderTexture takes one Graphics.Blit of the game's
    // render target per tick and AsyncGPUReadback pulls those 1024 texels back off the render
    // thread. There is deliberately NO ReadPixels anywhere on this path: ReadPixels flushes the
    // pipeline and would cost more frame time than the flicker it is looking for, and a probe that
    // changes the frame timing of the thing it measures is worthless.
    //
    // ORDERING, which is the one subtle thing here. Several readbacks are allowed in flight at once
    // (a readback lands 2-3 frames after its request, and a flicker is a FRAME-TO-FRAME phenomenon,
    // so a one-in-flight probe would sample every third frame and could not see one). Each
    // Blit+Request pair is enqueued into the command stream together, so request N always captures
    // the state Blit N produced, and the callbacks complete in submission order. A FIFO queue of
    // pending entries therefore pairs each callback with its own request, and ONE shared 32x32
    // target is correct for all of them — the copies are already serialized by the stream.
    //
    // WHAT IS DELIBERATELY NOT CLAIMED: a 32x32 average cannot see a small detail changing, and it
    // is not meant to. It is sized to answer "is this texture's content moving at all, and does it
    // move away and come straight back", which is exactly the discrimination the sampling
    // hypothesis needs and exactly what a per-texel readback would cost too much to provide.
    // =====================================================================================

    /// <summary>Side of the mod-owned downsample target. 32x32 = 4 KB per readback; at 90 Hz that is
    /// ~350 KB/s off the render thread, which is noise next to a 3072x3264 per-eye frame.</summary>
    private const int ContentProbeSize = 32;

    /// <summary>Readbacks allowed in flight. Enough to cover the 2-3 frame latency at 90 Hz so the
    /// sequence is genuinely per-frame, bounded so a stalled driver cannot grow the queue.</summary>
    private const int MaxContentInFlight = 8;

    /// <summary>Mean-luma change (0..1) below which a frame-to-frame move is treated as sampling
    /// noise rather than a direction reversal. 0.004 is 1 part in 250 of full range: a flicker the
    /// eye calls "es flackert" moves the mean far more, a dithered gradient far less.</summary>
    private const float LumaReversalThreshold = 0.004f;

    private sealed class ContentProbe
    {
        internal string RtName = string.Empty;
        internal int OwnerImageId;   // only this watch reports/resets the window, so N RawImages
                                     // sharing one RenderTexture produce ONE content line, not N.
        internal int Frames;
        internal int Identical;      // signature equal to the previous frame's
        internal int Alternations;   // equal to t-2 and different from t-1 — the flicker signature
        internal int Reversals;      // mean-luma direction reversals of at least the threshold
        internal int Errors;
        internal ulong Hash1, Hash2;
        internal bool Have1, Have2;
        internal float Luma1, LumaLast, LastDelta;
        internal float LumaMin = float.MaxValue;
        internal float LumaMax = float.MinValue;
    }

    private readonly struct PendingRead
    {
        internal readonly ContentProbe Probe;
        internal readonly int Gen;
        internal PendingRead(ContentProbe probe, int gen)
        {
            Probe = probe;
            Gen = gen;
        }
    }

    private static readonly Dictionary<int, ContentProbe> ContentProbes = new(4);
    private static readonly Queue<PendingRead> ContentPending = new();
    private static readonly HashSet<int> ContentSeenThisTick = new(4);
    private static RenderTexture? _contentRt;
    private static int _contentGen;
    private static int _contentInFlight;
    private static bool _contentSupported = true;
    private static bool _contentDisabledLogged;

    /// <summary>Downsample + request one readback per DISTINCT RenderTexture on the watch list.
    /// Distinct, not per watch: the 187 log carried three RawImages sampling one texture and
    /// therefore reported everything three times.</summary>
    private static void TickContent()
    {
        if (!_contentSupported)
            return;
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            _contentSupported = false;
            if (!_contentDisabledLogged)
            {
                _contentDisabledLogged = true;
                VRLog.Warn(Scope, "RENDER TARGET CONTENT probe stood down: this graphics device "
                                  + $"({SystemInfo.graphicsDeviceType}) reports no AsyncGPUReadback "
                                  + "support. The consequence is that the BASELINE lines carry no "
                                  + "content numbers, so the log cannot say whether the pixels inside "
                                  + "the render texture change from frame to frame — every other "
                                  + "measurement is unaffected. A blocking ReadPixels is deliberately "
                                  + "NOT substituted: it would flush the pipeline every frame and "
                                  + "change the frame timing of the very thing being measured.");
            }
            return;
        }

        ContentSeenThisTick.Clear();
        for (int i = 0; i < Watches.Count; i++)
        {
            Watch w = Watches[i];
            if (w.Image == null || w.Image.texture is not RenderTexture rt || rt == null || !rt.IsCreated())
                continue;
            int id = rt.GetInstanceID();
            if (!ContentSeenThisTick.Add(id))
                continue;
            if (_contentInFlight >= MaxContentInFlight)
                continue;
            if (!ContentProbes.TryGetValue(id, out ContentProbe probe))
            {
                probe = new ContentProbe { RtName = rt.name, OwnerImageId = w.Image.GetInstanceID() };
                ContentProbes[id] = probe;
            }
            if (!EnsureContentRt())
                return;
            try
            {
                Graphics.Blit(rt, _contentRt);
                ContentPending.Enqueue(new PendingRead(probe, _contentGen));
                _contentInFlight++;
                AsyncGPUReadback.Request(_contentRt, 0, TextureFormat.RGBA32, OnContentRead);
            }
            catch (System.Exception ex)
            {
                _contentSupported = false;
                if (!_contentDisabledLogged)
                {
                    _contentDisabledLogged = true;
                    VRLog.Warn(Scope, "RENDER TARGET CONTENT probe stood down after a failed "
                                      + $"downsample/readback ({ex.GetType().Name}: {ex.Message}). The "
                                      + "consequence is that the BASELINE lines carry no content "
                                      + "numbers; every other measurement is unaffected and nothing "
                                      + "the game owns was written.");
                }
                return;
            }
        }
    }

    private static bool EnsureContentRt()
    {
        if (_contentRt != null)
            return true;
        _contentRt = new RenderTexture(ContentProbeSize, ContentProbeSize, 0, RenderTextureFormat.ARGB32)
        {
            name = "GloomhavenVR.RenderTargetProbe.Content",
            antiAliasing = 1,
            filterMode = FilterMode.Bilinear,
        };
        if (_contentRt.Create())
            return true;
        _contentRt.Release();
        Object.Destroy(_contentRt);
        _contentRt = null;
        _contentSupported = false;
        if (!_contentDisabledLogged)
        {
            _contentDisabledLogged = true;
            VRLog.Warn(Scope, $"RENDER TARGET CONTENT probe stood down: the {ContentProbeSize}x"
                              + $"{ContentProbeSize} downsample target could not be created. The "
                              + "consequence is that the BASELINE lines carry no content numbers; "
                              + "nothing else changes.");
        }
        return false;
    }

    /// <summary>FIFO-paired readback completion. Runs on the main thread; touches nothing the game
    /// owns.</summary>
    private static void OnContentRead(AsyncGPUReadbackRequest req)
    {
        if (ContentPending.Count == 0)
            return; // cannot happen while the queue is never cleared out of order; belt and braces
        PendingRead pending = ContentPending.Dequeue();
        if (_contentInFlight > 0)
            _contentInFlight--;
        if (pending.Gen != _contentGen)
            return; // from a previous arming — discarded rather than folded into this one's numbers
        if (req.hasError)
        {
            pending.Probe.Errors++;
            return;
        }

        Unity.Collections.NativeArray<Color32> data = req.GetData<Color32>();
        if (data.Length == 0)
        {
            pending.Probe.Errors++;
            return;
        }

        // FNV-1a over every channel of every texel: any change at all moves the hash. Mean luma
        // alongside it, because a hash says THAT something changed and luma says HOW MUCH.
        ulong hash = 14695981039346656037UL;
        double lumaSum = 0.0;
        for (int i = 0; i < data.Length; i++)
        {
            Color32 c = data[i];
            hash = (hash ^ c.r) * 1099511628211UL;
            hash = (hash ^ c.g) * 1099511628211UL;
            hash = (hash ^ c.b) * 1099511628211UL;
            hash = (hash ^ c.a) * 1099511628211UL;
            lumaSum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
        }
        var luma = (float)(lumaSum / (data.Length * 255.0));
        JudgeContent(pending.Probe, hash, luma);
    }

    private static void JudgeContent(ContentProbe p, ulong hash, float luma)
    {
        p.Frames++;
        p.LumaLast = luma;
        if (luma < p.LumaMin) p.LumaMin = luma;
        if (luma > p.LumaMax) p.LumaMax = luma;

        if (p.Have1)
        {
            if (hash == p.Hash1)
            {
                p.Identical++;
            }
            else if (p.Have2 && hash == p.Hash2)
            {
                // Bit-identical to two frames ago and different from one frame ago: the content
                // LEFT and CAME BACK. Same classification rule as the state half of this class,
                // applied for the first time to pixels.
                p.Alternations++;
            }

            float delta = luma - p.Luma1;
            if (Mathf.Abs(delta) >= LumaReversalThreshold)
            {
                if (p.LastDelta != 0f && Mathf.Sign(delta) != Mathf.Sign(p.LastDelta))
                    p.Reversals++;
                p.LastDelta = delta;
            }
        }

        p.Hash2 = p.Hash1;
        p.Have2 = p.Have1;
        p.Hash1 = hash;
        p.Have1 = true;
        p.Luma1 = luma;
    }

    /// <summary>The content sentence appended to a watch's BASELINE line, and the window reset that
    /// goes with it. Only the OWNING watch prints and resets; the others say where to look, so one
    /// RenderTexture behind three RawImages still produces one set of numbers.</summary>
    private static string ContentSentence(Watch w)
    {
        if (!_contentSupported)
            return " | content: NOT SAMPLED (AsyncGPUReadback unavailable or stood down — see the "
                   + "CONTENT probe warning).";
        if (w.Image == null || w.Image.texture is not RenderTexture rt || rt == null)
            return " | content: NOT SAMPLED (no live RenderTexture on this image).";
        if (!ContentProbes.TryGetValue(rt.GetInstanceID(), out ContentProbe p))
            return " | content: NOT SAMPLED YET (no readback has completed for this texture).";
        if (p.OwnerImageId != w.Image.GetInstanceID())
            return $" | content: measured once for '{p.RtName}' — see the BASELINE line whose image "
                   + "owns it (several RawImages share this one texture).";
        if (p.Frames == 0)
        {
            return $" | content: 0 readback(s) completed this window ({p.Errors} error(s)). NOT "
                   + "MEASURED — this is the one content reading that proves nothing.";
        }

        float range = p.LumaMax - p.LumaMin;
        int frames = p.Frames;
        int identical = p.Identical;
        int alternations = p.Alternations;
        int reversals = p.Reversals;
        int errors = p.Errors;
        float min = p.LumaMin;
        float max = p.LumaMax;
        float last = p.LumaLast;

        // Reset the WINDOW counters, keep the frame-to-frame continuity (Hash1/Hash2/Luma1) so the
        // sequence is not broken at every report boundary.
        p.Frames = 0;
        p.Identical = 0;
        p.Alternations = 0;
        p.Reversals = 0;
        p.Errors = 0;
        p.LumaMin = float.MaxValue;
        p.LumaMax = float.MinValue;

        string verdict = alternations > 0
            ? "CONTENT ALTERNATES — the pixels left and came back bit-for-bit. This is a source "
              + "flicker and it is the first direct evidence of one in eight rounds."
            : identical == frames - 1 || (frames > 1 && identical >= frames - 2)
                ? "CONTENT IS STATIC — consecutive readbacks are bit-identical. Nothing inside this "
                  + "texture is changing, so whatever he sees is created when this unchanging image "
                  + "is SAMPLED onto the world-space quad. Read the PANEL SAMPLING line."
                : range < 0.01f
                    ? "CONTENT DRIFTS SLIGHTLY — it changes every frame but the mean holds within 1% "
                      + "of full range (an idle animation, not a flicker)."
                    : "CONTENT CHANGES — judge it by the reversal count: a smooth animation produces "
                      + "few large direction reversals, a flicker produces many.";

        return $" | content ({ContentProbeSize}x{ContentProbeSize} downsample of '{p.RtName}', "
               + $"AsyncGPUReadback): {frames} frame(s) read back, {identical} bit-identical to the "
               + $"previous frame, {alternations} exact alternation(s), {reversals} luma reversal(s) "
               + $"of >= {LumaReversalThreshold:F3}, mean luma {last:F4} (window {min:F4}..{max:F4}, "
               + $"range {range:F4}), {errors} readback error(s). {verdict}";
    }

    /// <summary>The one-time inventory of a watched target — printed whether or not it misbehaves.</summary>
    private static void Census(Watch w, Sample s)
    {
        if (w.CensusDone)
            return;
        w.CensusDone = true;
        var rt = w.Image.texture as RenderTexture;
        Camera? cam = w.Writer;
        string components = BehaviourList(w, s);
        // ModBuild 189: the SAMPLING properties are in the census now. The 187 census stopped at
        // "msaa=1 sRGB=False", which left filterMode, useMipMap, autoGenerateMips, mipmapCount and
        // anisoLevel — the five properties that between them decide whether a minified texture
        // shimmers — unmeasured for six rounds. A RenderTexture built without useMipMap has exactly
        // one mip level no matter how large it is, and bilinear sampling of one mip level under
        // minification IS texture-space aliasing. See PanelSamplingProbe for the minification ratio
        // these properties act on; neither number means anything without the other.
        string sampling = rt != null
            ? $"filter={rt.filterMode} aniso={rt.anisoLevel} wrap={rt.wrapMode} "
              + $"useMipMap={rt.useMipMap} autoGenerateMips={rt.autoGenerateMips} "
              + $"mipmapCount={rt.mipmapCount}"
            : "sampling properties unavailable (no RenderTexture)";
        VRLog.Info(Scope, $"RENDER TARGET CENSUS: RawImage '{w.Image.name}' (uvRect "
                          + $"{w.Image.uvRect}) shows RenderTexture "
                          + $"'{(rt != null ? rt.name : "<none>")}' "
                          + $"{s.RtWidth}x{s.RtHeight} fmt={(rt != null ? rt.format.ToString() : "?")} "
                          + $"msaa={(rt != null ? rt.antiAliasing : 0)} "
                          + $"sRGB={(rt != null && rt.sRGB)} depth={(rt != null ? rt.depth : 0)}; "
                          + $"SAMPLING: {sampling} (project colour space "
                          + $"{QualitySettings.activeColorSpace}, global aniso "
                          + $"{QualitySettings.anisotropicFiltering}). "
                          + $"WRITER: {(cam != null ? $"'{cam.name}' enabled={s.CamEnabled} depth={s.CamDepth:F1} "
                              + $"path={s.CamPath} clear={s.CamClear} mask=0x{s.CamMask:X8} "
                              + $"stereoTarget={cam.stereoTargetEye} allowMSAA={cam.allowMSAA} allowHDR={cam.allowHDR}"
                              : "NO CAMERA TARGETS THIS TEXTURE — it is written by something else (a Blit, a "
                                + "video player, or a camera that is currently destroyed)")}. "
                          + $"COMPONENTS ON THE WRITER: [{components}]. "
                          + $"MANAGER: {(s.MgrValid ? $"{s.ModelActive}/{s.ModelChildren} model object(s) active, "
                              + $"{s.ShowRequests} show-request(s), isHidden={s.IsHidden}"
                              : "none on this camera (or private fields not reflectable)")}. "
                          + "This line is the baseline: a quiet log still says what the target was made of. "
                          + "NOTE that several RawImages can share one RenderTexture — the 187 log carried "
                          + "three identical census lines and therefore reported every finding three times.");
    }
}
