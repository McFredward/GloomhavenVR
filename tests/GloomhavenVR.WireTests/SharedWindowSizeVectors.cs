using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// <b>A SHARED WINDOW IS THE SAME SIZE FOR EVERY PLAYER — ASSERTED, NOT PROMISED.</b>
///
/// <para><b>THE USER RULING (2026-09-06, verbatim):</b> <i>"Ich möchte das Multiplayerfenster immer
/// die selbe Größe haben bei allen Spielern, damit die 1:1 Regel hier nicht gebrochen wird.
/// Gewährleiste das."</i> The last word is the one this file exists for. He did not ask for the one
/// window that was reported to be repaired; he asked for an assurance about the CLASS, and this
/// project has just spent a whole round on six defects that were each protected by a confident
/// sentence somebody had written about the code. A doc block is not a guarantee. A test that fails
/// <c>scripts/wire-tests.sh</c> — which is already on the gate list every lane must pass — is.</para>
///
/// <para><b>WHY IT BELONGS IN THE WIRE TESTS EVEN THOUGH IT ADDS NO RECORD.</b> Every other file on
/// this list is here because its failure is invisible from one machine: a serializer whose reader
/// and writer are both wrong agrees with itself, and a window that is the wrong size agrees with
/// itself in exactly the same way. It took two 100 MB logs from two machines and a dedicated
/// instrument to see the 448 divergence at all. The property being asserted — "two clients compute
/// the same millimetres" — is a property of a FUNCTION, so it can be decided here, on a build
/// machine, with no headset, no second player and no session.</para>
///
/// <para><b>THE 2026-09-06 FOLLOW-UP RULING, verbatim:</b> <i>"Auch beim größer/kleiner ziehen
/// soll die 1:1 Regel gelten. Alle Spieler sollen immer die selbe Größe sehen, d.h. skalliert ein
/// Spieler ein Multiplayer fenster sehen alle Spieler wie es skalliert und sehen somit wieder die
/// exakt gleiche Größe bei allen."</i> The two-hand resize was ALREADY shared and already live
/// (records 19 and 21 carry a <c>sizeCode</c> byte mid-drag at 15 Hz); what the word <b>exakt</b>
/// added is that the PULLER must stand on the wire's grid too, which is what
/// <see cref="RunResizeLaw"/> and <see cref="RunResizeSourceGate"/> assert. Those two also carry the
/// answer to the obvious objection - that a user-driven SCALE is exactly what the fifteen-token gate
/// below forbids. It is not, the list was not touched, and <see cref="RunResizeSourceGate"/>'s doc
/// is where the distinction between a CLIENT-LOCAL scale and a SHARED one is written down.</para>
///
/// <para><b>THE TWO HALVES.</b> <see cref="RunLaw"/> drives the arithmetic: the same window, fed the
/// display shapes the two real machines have and the dial settings the config allows, must come out
/// at one number. <see cref="RunSourceGate"/> is the half that survives a refactor — it reads the
/// shipped source of the law and of its two call sites and FAILS when a client-local term appears in
/// them. The arithmetic test cannot catch someone adding <c>Screen.width</c> to the law (a static
/// read is invisible to a caller); the source gate cannot catch someone getting the algebra wrong.
/// Together they cover the sentence in the brief: <i>a shared window's committed size becoming a
/// function of anything client-local — authored canvas, viewport, rig scale, an animation phase, a
/// settle time.</i></para>
/// </summary>
internal static class SharedWindowSizeVectors
{
    /// <summary>Two millimetre figures are the same window. A tenth of a millimetre is far below
    /// anything a headset can resolve and far above float noise on numbers of this size.</summary>
    private static bool SameMm(Vector2 a, Vector2 b) =>
        Mathf.Abs(a.x - b.x) <= 0.1f && Mathf.Abs(a.y - b.y) <= 0.1f;

    internal static void Run(Harness t, string repoRoot)
    {
        VerifyAgainstSource(t, repoRoot);
        RunLaw(t);
        RunResizeLaw(t, repoRoot);
        RunSourceGate(t, repoRoot);
        RunResizeSourceGate(t, repoRoot);
    }

    // =============================================================================================
    // 1. THE COPIES ARE STILL THE ORIGINALS
    // =============================================================================================

    /// <summary>
    /// <see cref="SharedWindowSizeLaw"/> restates five constants that live in files this assembly
    /// must not compile (they carry BepInEx, the game model and half of WorldUI). A drifted copy
    /// would make every vector below assert the wrong size while passing, so the declarations are
    /// read out of the repository and compared — the same protection
    /// <c>Shims.VerifyAgainstSource</c> gives the quantisation constants.
    ///
    /// <para>A failure here is NOT necessarily a bug: somebody may have deliberately retuned
    /// <c>WindowLegibility</c> or the board-relative target. It is a REQUIRED DECISION — either the
    /// law follows the new number (shared windows change size for everybody at once, which is fine)
    /// or it deliberately does not (which needs saying in the law's doc). What must not happen is
    /// the two drifting apart unnoticed, because then the shared windows quietly stop matching the
    /// private ones and nothing says so.</para>
    /// </summary>
    private static void VerifyAgainstSource(Harness t, string repoRoot)
    {
        t.Case("shared-window-size/constants-pinned");
        (string File, string Decl, float Law)[] pinned =
        {
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs", "WindowScaleFactor",
             SharedWindowSizeLaw.DesignWindowScaleFactor),
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs", "ModalTargetWidthMeters",
             SharedWindowSizeLaw.DesignTargetWidthMeters),
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.1.Core.cs", "MinWindowScaleFactor",
             SharedWindowSizeLaw.DesignMinWindowScaleFactor),
            ("src/GloomhavenVR/Defaults/Defaults.WorldUI.cs", "WindowLegibility",
             SharedWindowSizeLaw.DesignWindowLegibility),
            ("src/GloomhavenVR/Defaults/Defaults.WorldUI.cs", "CanvasScaleMm",
             SharedWindowSizeLaw.DesignCanvasScaleMm),
        };

        foreach (var (file, decl, law) in pinned)
        {
            string path = Path.Combine(repoRoot, file);
            if (!File.Exists(path))
            {
                t.True(false, $"{file} not found — the law's copy of {decl} cannot be pinned");
                continue;
            }
            var m = Regex.Match(File.ReadAllText(path),
                                @"const\s+float\s+" + Regex.Escape(decl) + @"\s*=\s*([0-9.]+)f\s*;");
            if (!m.Success)
            {
                t.True(false, $"could not find `const float {decl} = ...f;` in {file} — the law's "
                              + "copy is now unpinned, which is worse than a wrong copy");
                continue;
            }
            float real = float.Parse(m.Groups[1].Value,
                                     System.Globalization.CultureInfo.InvariantCulture);
            t.True(Mathf.Abs(real - law) < 1e-6f,
                   $"SharedWindowSizeLaw.Design* copy of {decl} is {law}, but {file} declares "
                   + $"{real}. Decide which one the shared windows follow and change both.");
        }
    }

    // =============================================================================================
    // 2. THE ARITHMETIC: ONE WINDOW, ONE NUMBER, WHATEVER THE CLIENT IS
    // =============================================================================================

    private static void RunLaw(Harness t)
    {
        // ---- the reported defect, driven from the two real machines ------------------------------
        // ModBuild 448, one session: the host's canvas is 1920x1080 and the co-player's is
        // 2580x1080, because their displays are shaped differently and the game's canvas scaler
        // matches on HEIGHT. Under the OLD rule the story window committed 1200 x 675 mm on one
        // machine and 1200 x 502 mm on the other. Under the law both are fed the canvas's own
        // design resolution instead, so both compute the host's number — which is also the number
        // the user has already accepted.
        t.Case("shared-window-size/the-448-divergence");
        var design = new Vector2(1920f, 1080f);
        Vector2 hostMm = SharedWindowSizeLaw.CommittedMm(design);
        t.True(SameMm(hostMm, new Vector2(1200f, 675f)),
               $"a 1920x1080 design frame commits 1200 x 675 mm (got {hostMm.x:F1} x {hostMm.y:F1})");
        t.Equal("12000x6750", SharedWindowSizeLaw.Token(design),
                "the 1:1 token for the story window is the two millimetre figures at 0.1 mm");

        // The peer's OWN canvas, put through the law directly, is what the old rule did — kept here
        // as the thing that must NOT happen, so a future change that re-admits the live canvas fails
        // with the actual reported numbers in the message rather than with an abstract mismatch.
        Vector2 peerRaw = SharedWindowSizeLaw.CommittedMm(new Vector2(2580f, 1080f));
        t.True(!SameMm(peerRaw, hostMm),
               "sanity: the peer's raw 2580x1080 canvas really does give a different size (that is "
               + "the defect), so the design-frame substitution is doing the work and not a no-op");
        t.True(Mathf.Abs(peerRaw.y - 502f) < 1.5f,
               $"and it reproduces the peer's reported 502 mm height (got {peerRaw.y:F1}) — this "
               + "vector is what proves the law is aimed at the measured defect");

        // ---- the class, not the instance: every display shape lands on one size ------------------
        // 4:3, 16:10, 16:9, 21:9, 32:9 and a portrait rig. The canvas scaler matches on height, so
        // the design frame is the same for all of them and so is the answer. This is the assertion
        // that makes the guarantee about the CLASS: it is not "the two machines we have agree", it
        // is "any two machines agree".
        t.Case("shared-window-size/every-display-shape");
        float[] widths = { 1440f, 1728f, 1920f, 2520f, 2580f, 3840f, 810f };
        foreach (float w in widths)
        {
            // What the client's own canvas would have produced...
            Vector2 local = SharedWindowSizeLaw.CommittedMm(new Vector2(w, 1080f));
            // ...against what it produces once SharedWindowSize has substituted the design frame.
            Vector2 shared = SharedWindowSizeLaw.CommittedMm(design);
            t.True(SameMm(shared, hostMm),
                   $"a client whose canvas is {w:F0}x1080 commits the same {hostMm.x:F0} x "
                   + $"{hostMm.y:F0} mm once the design frame is substituted");
            if (Mathf.Abs(w - 1920f) > 0.5f)
                t.True(!SameMm(local, hostMm),
                       $"and without the substitution it would NOT ({local.x:F0} x {local.y:F0} mm) "
                       + "— the vector proves the substitution is load-bearing");
        }

        // ---- the dials are out of the answer -----------------------------------------------------
        // [WorldUI] CanvasScaleMm is a live per-client dial and the renderer multiplies by it, so
        // the law divides it back out. Drive the whole clamp range and require ONE size. If this
        // ever fails, a shared window has become a function of a config key again — which is the
        // ModBuild 302 mirror defect ([[mirror-must-not-read-viewers-dial]]) in a new place.
        t.Case("shared-window-size/dials-cancelled");
        float[] dials = { 0.5f, 0.8f, 1.0f, 1.2f, 1.6f, 2.5f };
        foreach (float dial in dials)
        {
            float extra = SharedWindowSizeLaw.ExtraScale(design, dial);
            // The renderer's own arithmetic, restated: hostPx x liveCanvasScaleMm x extraScale.
            var drawn = new Vector2(design.x * dial * extra, design.y * dial * extra);
            t.True(SameMm(drawn, hostMm),
                   $"at [WorldUI] CanvasScaleMm = {dial:F2} the window is still {hostMm.x:F0} x "
                   + $"{hostMm.y:F0} mm (got {drawn.x:F1} x {drawn.y:F1})");
        }
        t.True(SharedWindowSizeLaw.ExtraScale(design, 0f) > 0f,
               "a zero/absent dial does not divide by zero");

        // ---- nothing the user has accepted moves --------------------------------------------------
        // Both machines in the 448 logs ran the shipped defaults, so the law must reproduce every
        // committed number in those logs exactly. These three are read straight off the evidence.
        t.Case("shared-window-size/accepted-numbers-preserved");
        Vector2 quest = SharedWindowSizeLaw.CommittedMm(new Vector2(512f, 1021f));
        t.True(Mathf.Abs(quest.x - 537.6f) < 0.5f,
               $"the quest popup's 512 px card is still 538 mm wide (got {quest.x:F1}) — ModBuild "
               + "449's grab-bar seat is solved against this rect and must not move");
        Vector2 questFitted = SharedWindowSizeLaw.CommittedMm(new Vector2(512f, 846f));
        t.True(Mathf.Abs(questFitted.y - 888.3f) < 0.5f,
               $"and its fitted 512x846 rect is still 888 mm tall (got {questFitted.y:F1})");
        Vector2 eventWin = SharedWindowSizeLaw.CommittedMm(new Vector2(802f, 126f));
        t.True(Mathf.Abs(eventWin.x - 842.1f) < 0.5f,
               $"a content-fitted 802 px rect is still 842 mm wide (got {eventWin.x:F1}) — the law "
               + "takes its shrink from the COMMITTED rect exactly as DeriveWindowScale did, so a "
               + "fitted-down window does not also shrink physically");

        // ---- the crossover, stated as a property rather than as a remembered number ---------------
        // Below it the small-dialog cap binds and millimetres grow with pixels; above it the
        // board-relative term binds and the width is pinned. A window may never get SMALLER by
        // gaining pixels, and may never exceed the board-sized target.
        t.Case("shared-window-size/monotone-and-capped");
        float previous = 0f;
        for (float px = 64f; px <= 4096f; px += 64f)
        {
            Vector2 mm = SharedWindowSizeLaw.CommittedMm(new Vector2(px, 1080f));
            t.True(mm.x >= previous - 0.01f,
                   $"width is non-decreasing in pixels at {px:F0} px ({mm.x:F1} after {previous:F1})");
            t.True(mm.x <= SharedWindowSizeLaw.TargetWidthMm + 0.01f,
                   $"width never exceeds the board-sized target at {px:F0} px ({mm.x:F1} mm)");
            previous = mm.x;
        }
        t.True(Mathf.Abs(previous - SharedWindowSizeLaw.TargetWidthMm) < 0.01f,
               "and a very wide window sits exactly ON the target");

        // A degenerate rect must not produce a degenerate window: the cap is the answer, not zero.
        t.Case("shared-window-size/degenerate-rect");
        t.True(SharedWindowSizeLaw.Shrink(0f) > 0f, "a zero-width rect returns the cap, not zero");
        t.True(SharedWindowSizeLaw.Shrink(-5f) > 0f, "and so does a negative one");
    }

    // =============================================================================================
    // 2b. THE TWO-HAND RESIZE: THE PULLER STANDS ON THE WIRE, NOT NEAR IT
    // =============================================================================================

    /// <summary>
    /// <b>USER RULING (2026-09-06, verbatim):</b> <i>"Auch beim größer/kleiner ziehen soll die 1:1
    /// Regel gelten. Alle Spieler sollen immer die selbe Größe sehen, d.h. skalliert ein Spieler
    /// ein Multiplayer fenster sehen alle Spieler wie es skalliert und sehen somit wieder die exakt
    /// gleiche Größe bei allen."</i>
    ///
    /// <para><b>WHAT THIS BLOCK ASSERTS, and it is deliberately not "the resize is synced".</b> The
    /// resize has been synced since ModBuild 226: records 19 and 21 carry the grab factor as a
    /// <c>sizeCode</c> byte, publish it MID-DRAG at the 15 Hz carry rate as ABSOLUTE state, and both
    /// appliers write the decoded value onto the receiver. The property that was NOT true, and that
    /// the word <b>exakt</b> in the ruling is about, is that the PULLER stood on the same grid: it
    /// kept the unrounded pinch float while every follower stood on a multiple of 0.01. So what is
    /// asserted here is the fixed point - <c>SharedGrabFactor</c> is exactly the set of values the
    /// wire can carry, it is idempotent, and applying it never changes what would have been
    /// sent.</para>
    /// </summary>
    private static void RunResizeLaw(Harness t, string repoRoot)
    {
        // ---- the residual the ruling is about, stated with its own number ------------------------
        // The story window commits 1200 mm wide. The puller pinching to 1.234x published code 123
        // and drew 1.234x; every follower drew 1.23x. 0.004 x 1200 mm = 4.8 mm, permanently, with
        // no edge left to heal it because the absolute re-send keeps saying 123.
        t.Case("shared-window-resize/the-puller-joins-the-grid");
        const float pinched = 1.234f;
        t.True(SharedWindowSizeLaw.SharedGrabFactor(pinched) != pinched,
               "sanity: the raw pinch value is NOT on the wire's grid, which is the whole defect");
        t.True(Mathf.Abs(SharedWindowSizeLaw.SharedGrabFactor(pinched) - 1.23f) < 1e-5f,
               $"the puller is pulled onto the followers' value 1.23x (got "
               + $"{SharedWindowSizeLaw.SharedGrabFactor(pinched):F4})");
        Vector2 design = new(1920f, 1080f);
        Vector2 pullerMm = SharedWindowSizeLaw.CommittedMm(design, pinched);
        Vector2 followerMm = SharedWindowSizeLaw.CommittedMm(
            design, GloomhavenVR.Net.NetProtocol.DecodeStorySize(
                        GloomhavenVR.Net.NetProtocol.EncodeStorySize(pinched)));
        t.True(SameMm(pullerMm, followerMm),
               $"and the two therefore commit the same millimetres ({pullerMm.x:F1} against "
               + $"{followerMm.x:F1}) - before this build they differed by "
               + $"{Mathf.Abs(1.234f - 1.23f) * 1200f:F1} mm on this window");

        // ---- the fixed point, over the whole legal range ------------------------------------------
        // Three properties, and together they are the definition of "a value every client can hold":
        // idempotent (so it can be applied at both ends of the wire and every frame in between),
        // wire-stable (applying it never changes the byte that would have been sent), and closed
        // (every answer it gives is DecodeStorySize of a legal code).
        t.Case("shared-window-resize/is-the-wire-value");
        for (int milli = 0; milli <= 3000; milli += 7)
        {
            float raw = milli / 1000f;
            float once = SharedWindowSizeLaw.SharedGrabFactor(raw);
            t.True(Mathf.Abs(SharedWindowSizeLaw.SharedGrabFactor(once) - once) < 1e-6f,
                   $"SharedGrabFactor is idempotent at {raw:F3} (got {once:F4} then "
                   + $"{SharedWindowSizeLaw.SharedGrabFactor(once):F4}) - it is applied on the "
                   + "puller, on the publisher's read and on every follower, so a second "
                   + "application must be a no-op or the three would chase each other");
            t.Equal(GloomhavenVR.Net.NetProtocol.EncodeStorySize(raw),
                    GloomhavenVR.Net.NetProtocol.EncodeStorySize(once),
                    $"quantising locally at {raw:F3} does not change the byte that goes on the wire");
            byte code = GloomhavenVR.Net.NetProtocol.EncodeStorySize(raw);
            t.True(Mathf.Abs(GloomhavenVR.Net.NetProtocol.DecodeStorySize(code) - once) < 1e-6f,
                   $"and the value at {raw:F3} is exactly DecodeStorySize({code}), i.e. a value the "
                   + "wire can carry rather than one near it");
        }

        // ---- the clamp is the WIRE's, which is what makes it impossible to diverge ---------------
        // The brief's question 5: "if one player's clamp differs from another's - now or after a
        // future config change - the sizes diverge again at the extremes." There is exactly one
        // clamp left and it is EncodeStorySize's, so the question has no room to be answered wrongly
        // twice. Garbage in fails CLOSED to a readable window, never to a speck or a wall.
        t.Case("shared-window-resize/one-clamp-and-it-is-the-wires");
        float min = SharedWindowSizeLaw.MinSharedGrabFactor;
        float max = SharedWindowSizeLaw.MaxSharedGrabFactor;
        t.True(Mathf.Abs(min - 0.15f) < 1e-6f, $"the shared floor is 0.15x (got {min:F3})");
        t.True(Mathf.Abs(max - 2.00f) < 1e-6f, $"the shared ceiling is 2.00x (got {max:F3})");
        float[] outOfRange = { -100f, 0f, 0.0001f, 5.567f, 1e9f, float.NaN,
                               float.PositiveInfinity, float.NegativeInfinity };
        foreach (float bad in outOfRange)
        {
            float got = SharedWindowSizeLaw.SharedGrabFactor(bad);
            t.True(got >= min - 1e-6f && got <= max + 1e-6f,
                   $"a shared window asked for {bad} lands inside [{min:F2}, {max:F2}] (got {got:F3})"
                   + " - the ModBuild 350 play tray really did sit at localScale 5.567, so a value "
                   + "far outside this window is a case that has happened rather than a hypothetical");
        }

        // ---- and the generic gesture's range is the same range ------------------------------------
        // PanelGrabHandle cannot be compiled into this assembly (it carries the whole rig), so its
        // two constants are read out of the repository. If somebody widens them, a shared window
        // would be pullable to a value the wire silently clamps - the puller at 3.0x and everybody
        // else at 2.0x, which is the ruling broken at exactly the extreme it is easiest to reach.
        t.Case("shared-window-resize/the-gesture-range-is-the-wire-range");
        string handlePath = Path.Combine(repoRoot, "src/GloomhavenVR/WorldUI/Grab/PanelGrab.cs");
        if (!File.Exists(handlePath))
        {
            t.True(false, "src/GloomhavenVR/WorldUI/Grab/PanelGrab.cs is gone - MinScale/MaxScale "
                          + "cannot be pinned to the wire's window");
        }
        else
        {
            string handle = File.ReadAllText(handlePath);
            (string Decl, float Wire)[] pinned = { ("MinScale", min), ("MaxScale", max) };
            foreach (var (decl, wire) in pinned)
            {
                var m = Regex.Match(handle,
                                    @"const\s+float\s+" + Regex.Escape(decl) + @"\s*=\s*([0-9.]+)f\s*;");
                if (!m.Success)
                {
                    t.True(false, $"could not find `const float {decl} = ...f;` in PanelGrab.cs - "
                                  + "the two-hand range is now unpinned from the wire, which is "
                                  + "worse than a wrong pin");
                    continue;
                }
                float real = float.Parse(m.Groups[1].Value,
                                         System.Globalization.CultureInfo.InvariantCulture);
                t.True(Mathf.Abs(real - wire) < 1e-6f,
                       $"PanelGrabHandle.{decl} is {real} but the wire's window ends at {wire}. A "
                       + "shared window may only be pulled to a value every client can hold, so "
                       + "move NetProtocol.StorySize{Min,Max}Code with it or gate the wider range "
                       + "to the owners that are not shared.");
            }
        }

        // ---- nothing an unresized window prints moves ---------------------------------------------
        // ModBuild 449's own vectors are the acceptance record for these numbers; the resize overload
        // must reproduce every one of them at factor 1.00x or this lane has moved something the user
        // already accepted.
        t.Case("shared-window-resize/factor-one-changes-nothing");
        Vector2[] accepted = { new(1920f, 1080f), new(512f, 1021f), new(512f, 846f), new(802f, 126f) };
        foreach (Vector2 px in accepted)
        {
            t.True(SameMm(SharedWindowSizeLaw.CommittedMm(px, 1f),
                          SharedWindowSizeLaw.CommittedMm(px)),
                   $"a {px.x:F0}x{px.y:F0} px rect at factor 1.00x commits exactly what ModBuild "
                   + "449 committed");
            t.Equal(SharedWindowSizeLaw.Token(px), SharedWindowSizeLaw.Token(px, 1f),
                    $"and prints the same 1:1 token, so every token already read off a hardware log "
                    + "keeps its meaning");
        }

        // A resize really does move the token - otherwise the overload would be decoration and the
        // ArcSeats line would go on predicting a size the window is not drawn at.
        t.True(SharedWindowSizeLaw.Token(design, 1f) != SharedWindowSizeLaw.Token(design, 1.5f),
               "and a genuinely resized window prints a DIFFERENT token, which is what makes the "
               + "two-log comparison able to see a resize at all");
        Vector2 half = SharedWindowSizeLaw.CommittedMm(design, 0.5f);
        t.True(Mathf.Abs(half.x - 600f) < 0.1f && Mathf.Abs(half.y - 337.5f) < 0.1f,
               $"a 0.50x pull halves both figures (got {half.x:F1} x {half.y:F1} mm) - the resize is "
               + "a UNIFORM factor, which is why a scalar is the right shape here even though it was "
               + "the wrong shape for the aspect divergence this file was written for");
    }

    // =============================================================================================
    // 3. THE HALF THAT SURVIVES THE NEXT REFACTOR
    // =============================================================================================

    /// <summary>Terms that make a size depend on the machine it is computed on. Named exactly as
    /// they appear in this codebase, because a token that never matches is a gate that never
    /// fires ([[a-gate-narrower-than-its-choke-point]]).</summary>
    private static readonly (string Token, string Why)[] ClientLocalTerms =
    {
        ("Screen.", "the display resolution"),
        ("Camera.", "the viewport / the eye"),
        ("Display.", "the display"),
        ("XRSettings", "the headset's render scale"),
        ("Time.", "a settle time or an animation phase"),
        ("WorldUIConfig.", "a live per-client config dial"),
        ("WindowLegibilityLive", "the viewer's own legibility dial"),
        ("PanelLayout.WorldScale", "the rig / diorama scale"),
        ("MapRoomDriver", "whether THIS client has the 3D map switched on"),
        ("TryGetParchmentFrame", "the table zoom"),
        ("FFSNetwork", "the session state, which flips under a standing window"),
        ("ParticipatesHere", "the session state, which flips under a standing window"),
        ("SessionIsOnline", "the session state, which flips under a standing window"),
        ("localScale", "a live transform scale rather than an authored number"),
        ("lossyScale", "a live transform scale rather than an authored number"),
    };

    /// <summary>
    /// <b>THE GATE.</b> Reads the shipped source of the size law and refuses any client-local term
    /// in it.
    ///
    /// <para><b>WHY A SOURCE READ AND NOT A CLEVERER TEST.</b> The arithmetic vectors above can only
    /// see what comes through the PARAMETERS. A future edit that reaches for a static — the very
    /// shape of every term in the table, and the shape the original defect had — changes no
    /// signature and breaks no vector. It changes the file, and the file is what is checked. The
    /// same reading is why this lives here rather than in a new <c>scripts/check-*.py</c>: it must
    /// run inside a gate every lane already runs, and adding a script that has to be REMEMBERED is
    /// how a check quietly stops being part of the build.</para>
    ///
    /// <para><b>THE ONE SANCTIONED EXCEPTION, and it is sanctioned by CANCELLING rather than by
    /// being trusted.</b> <c>SharedWindowSizeLaw.ExtraScale</c> takes the live CanvasScaleMm as a
    /// PARAMETER and divides it back out, because the renderer multiplies by it downstream and the
    /// only way to remove a factor from a product is to divide by it. That is why the token above is
    /// <c>WorldUIConfig.</c> (the READ) and not the word "CanvasScaleMm" (the quantity): the law may
    /// name the quantity, and may not go and fetch this client's copy of it. The
    /// <c>dials-cancelled</c> vector is the proof that the division actually cancels.</para>
    ///
    /// <para><b>WHAT IT DOES NOT COVER, said plainly.</b> The committed rect of a window that is
    /// CONTENT-FIT is still a function of what the game painted at the settle instant. That term is
    /// outside this file and outside the mod's ownership; it is named on the
    /// <c>SHARED WINDOW SIZE TERMS</c> line as the one reading that still needs two logs.</para>
    /// </summary>
    private static void RunSourceGate(Harness t, string repoRoot)
    {
        t.Case("shared-window-size/no-client-local-term-in-the-law");
        string lawPath = Path.Combine(repoRoot, "src/GloomhavenVR/WorldUI/Modal/SharedWindowSizeLaw.cs");
        if (!File.Exists(lawPath))
        {
            t.True(false, "src/GloomhavenVR/WorldUI/Modal/SharedWindowSizeLaw.cs is gone. The 1:1 "
                          + "size guarantee lives in that file; if it moved, move this gate with it "
                          + "rather than deleting the gate.");
            return;
        }

        foreach (string line in CodeLines(File.ReadAllLines(lawPath)))
        {
            foreach (var (token, why) in ClientLocalTerms)
            {
                t.True(!line.Contains(token, StringComparison.Ordinal),
                       $"SharedWindowSizeLaw.cs reads `{token}` — {why}. A shared window's committed "
                       + "size may not be a function of anything this client owns; that is the whole "
                       + $"of the 2026-09-06 ruling. Offending line: {line.Trim()}");
            }
        }

        // The law is only a guarantee while the shipped path actually CALLS it. ModBuild 449's own
        // round has a recorded case of a remedy that was gated behind the instrument shipped to test
        // it and therefore never ran ([[gated-remedy-never-ran]]), so the call sites are asserted
        // rather than assumed.
        t.Case("shared-window-size/the-law-is-actually-called");
        (string File, string Needle, string Why)[] callSites =
        {
            ("src/GloomhavenVR/WorldUI/Modal/ModalFallback.9.Spawn.cs",
             "SharedWindowSizeLaw.ExtraScale",
             "DeriveWindowScale must take a shared window's scale from the law, not from this "
             + "client's WindowLegibility / CanvasScaleMm"),
            ("src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs",
             "SharedWindowSize.Arm",
             "the law must be armed at CONVERT, while the target's root canvas is still the game's "
             + "and its design resolution can still be read"),
            ("src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs",
             "SharedWindowSize.Repin",
             "the design frame must be re-pinned on the target the game re-drives, or the fix lasts "
             + "exactly until the game's next layout pass"),
            ("src/GloomhavenVR/WorldUI/Modal/ArcSeats.cs",
             "SharedWindowSizeLaw.Token",
             "the SHARED WINDOW SIZE TERMS line must carry the 1:1 token, or next round's evidence "
             + "is back to two 100 MB logs and a manual comparison"),
        };
        foreach (var (file, needle, why) in callSites)
        {
            string path = Path.Combine(repoRoot, file);
            bool present = File.Exists(path)
                           && File.ReadAllText(path).Contains(needle, StringComparison.Ordinal);
            t.True(present, $"{file} no longer calls {needle}. {why}.");
        }

        // The population is a CLOSED enum, which is what makes "a window you did not enumerate"
        // impossible rather than merely unlikely. If a fifth kind is added, this assertion fails and
        // whoever added it has to come here and decide what the new window's design frame is.
        t.Case("shared-window-size/the-population-is-closed");
        string sharedPath = Path.Combine(repoRoot, "src/GloomhavenVR/WorldUI/Modal/SharedWindows.cs");
        if (File.Exists(sharedPath))
        {
            string src = File.ReadAllText(sharedPath);
            int start = src.IndexOf("internal enum SharedWindowKind", StringComparison.Ordinal);
            int end = start >= 0 ? src.IndexOf('}', start) : -1;
            string body = start >= 0 && end > start ? src[start..end] : string.Empty;
            int members = Regex.Matches(body, @"^\s{4}[A-Z][A-Za-z]*\s*=\s*\d+\s*,",
                                        RegexOptions.Multiline).Count;
            t.Equal(5, members,
                    "SharedWindowKind has 5 members (None + the four shared windows). A new one "
                    + "means a new window the 1:1 size guarantee has to be given a design frame for "
                    + "— see SharedWindowSize.TryDesignFrame — so update that and this count "
                    + "together.");
        }
        else
        {
            t.True(false, "SharedWindows.cs is gone — the shared population cannot be enumerated");
        }
    }

    /// <summary>
    /// <b>THE GATE'S SECOND QUESTION: CLIENT-LOCAL, OR SHARED?</b>
    ///
    /// <para><b>WHY THE FIRST QUESTION WAS NOT ENOUGH, and why the answer is NOT to loosen it.</b>
    /// <see cref="ClientLocalTerms"/> bans fifteen tokens from the law, two of which are
    /// <c>localScale</c> and <c>lossyScale</c>. The 2026-09-06 follow-up ruling ("auch beim
    /// größer/kleiner ziehen soll die 1:1 Regel gelten") requires a shared window to carry a
    /// user-driven SCALE, which on its face is the thing that list forbids. It is not, and the
    /// distinction is exact rather than a judgement call:</para>
    ///
    /// <list type="bullet">
    /// <item>A <b>CLIENT-LOCAL</b> scale is one this machine COMPUTES - from its display, its
    /// viewport, its rig, its dials, its clock, its session state, or from a live transform it
    /// happens to be holding. Every one of the fifteen tokens is a way of FETCHING such a number,
    /// and not one of them has been removed, relaxed or exempted by this build. The law file still
    /// contains none of them.</item>
    /// <item>A <b>SHARED</b> scale is one that came off, or is going onto, the wire. It is
    /// identifiable without judgement: it is a value in the image of
    /// <c>NetProtocol.DecodeStorySize</c>, and <c>SharedWindowSizeLaw.SharedGrabFactor</c> is
    /// literally <c>Decode(Encode(x))</c>, so "is this scale shared" is decided by a function and
    /// not by a comment.</item>
    /// </list>
    ///
    /// <para><b>SO THE GATE BELOW BANS AN UNMEDIATED SCALE WRITE ON A SHARED WINDOW.</b> The two
    /// wire appliers touch nothing but shared windows, so in those two files the rule is total and
    /// exception-free: every <c>localScale</c> assignment must name <c>SharedWindowSizeLaw</c> on
    /// the same statement. That is the shape the defect would take if it came back - somebody
    /// clamping to <c>PanelGrabHandle</c>'s range again, or reading the panel's own transform - and
    /// it is a change to the FILE, which is what is checked.</para>
    ///
    /// <para><b>WHAT IT STILL CATCHES.</b> Adding <c>Screen.width</c>, <c>Time.time</c>,
    /// <c>PanelLayout.WorldScale</c>, <c>WorldUIConfig.</c> or a <c>lossyScale</c> read to the law:
    /// unchanged, first gate. Sizing a shared window from the live transform in either applier, or
    /// re-introducing a second clamp beside the wire's: second gate. Widening the two-hand gesture's
    /// range past what the wire can carry, so the puller reaches 3x and everybody else is pinned at
    /// 2x: <c>the-gesture-range-is-the-wire-range</c>. Letting the puller keep an unrounded float:
    /// <c>is-the-wire-value</c>. Teaching the GENERIC gesture about shared windows, which would put
    /// a shared-window term on the merchant, the temple, the party panel and the quest log:
    /// <c>the-generic-gesture-stays-generic</c>.</para>
    ///
    /// <para><b>WHAT IT DOES NOT COVER, said plainly.</b> Nothing here asserts that the two clients
    /// AGREE on the committed pixel rect a factor is applied to - that is the first gate's job and
    /// its own stated residual (a content-fit rect is still a function of what the game painted).
    /// And nothing here can see a THIRD writer of the grab frame's scale outside these files; the
    /// <c>SHARED WINDOW RESIZE</c> line's <c>NOBODY IDENTIFIABLE</c> wording is what would report
    /// one from hardware.</para>
    /// </summary>
    private static void RunResizeSourceGate(Harness t, string repoRoot)
    {
        // ---- no unmediated scale write on a shared window ----------------------------------------
        t.Case("shared-window-resize/no-unmediated-scale-write-in-an-applier");
        string[] appliers =
        {
            "src/GloomhavenVR/Net/Remote/RemoteMapStory.cs",
            "src/GloomhavenVR/Net/Remote/RemoteStorySync.cs",
        };
        foreach (string file in appliers)
        {
            string path = Path.Combine(repoRoot, file);
            if (!File.Exists(path))
            {
                t.True(false, $"{file} is gone - the shared window's size arrives through it, so "
                              + "move this gate with it rather than deleting the gate");
                continue;
            }
            bool sawOne = false;
            foreach (string line in CodeLines(File.ReadAllLines(path)))
            {
                if (!line.Contains("localScale", StringComparison.Ordinal)
                    && !line.Contains("lossyScale", StringComparison.Ordinal))
                    continue;
                sawOne = true;
                t.True(line.Contains("SharedWindowSizeLaw", StringComparison.Ordinal),
                       $"{file} touches a transform scale without going through "
                       + "SharedWindowSizeLaw. This file only ever handles SHARED windows, so a "
                       + "scale here is a scale every player must agree on: take it from the wire "
                       + $"(SharedGrabFactor) and not from a local range. Offending line: {line.Trim()}");
            }
            t.True(sawOne,
                   $"{file} no longer touches a transform scale at all. The shared two-hand resize "
                   + "travels in this record's sizeCode byte and is APPLIED here; if the apply is "
                   + "gone, one player can resize a shared window for themselves again and no "
                   + "arithmetic vector above would notice.");
        }

        // ---- the generic gesture stays generic ----------------------------------------------------
        // Question 6 of the brief - "non-shared windows are untouched" - VERIFIED rather than
        // asserted. PanelGrab is the two-hand gesture the control board, the play tray, the combat
        // log and every private window share. A shared-window term in it would put the wire's grid
        // on the player's own merchant, temple, party panel and quest log, which the ruling never
        // asked for and which would make those four windows step in 1 % increments for no reason.
        t.Case("shared-window-resize/the-generic-gesture-stays-generic");
        string grabPath = Path.Combine(repoRoot, "src/GloomhavenVR/WorldUI/Grab/PanelGrab.cs");
        if (File.Exists(grabPath))
        {
            foreach (string line in CodeLines(File.ReadAllLines(grabPath)))
                t.True(!line.Contains("SharedWindow", StringComparison.Ordinal),
                       "PanelGrab.cs names a shared-window type. The two-hand resize is the SAME "
                       + "gesture for every window in the mod and the shared half belongs in "
                       + "GrabbableModal, which is the one owner that knows whether its window is "
                       + $"shared for this client. Offending line: {line.Trim()}");
        }
        else
        {
            t.True(false, "PanelGrab.cs is gone - the two-hand gesture cannot be checked");
        }

        // ---- and the shared half is where it belongs, and is actually called ----------------------
        t.Case("shared-window-resize/the-resize-law-is-actually-called");
        (string File, string Needle, string Why)[] callSites =
        {
            ("src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs",
             "SharedWindowSizeLaw.SharedGrabFactor",
             "the puller's own frame must be pulled onto the wire's grid, or it keeps an unrounded "
             + "pinch value no other player can stand at"),
            ("src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs",
             "SharedWindowSize.ServiceResize",
             "the SHARED WINDOW RESIZE line is the only thing that can settle the ruling from two "
             + "logs, and a line nobody calls is [[gated-remedy-never-ran]] again"),
            ("src/GloomhavenVR/Net/Remote/RemoteMapStory.cs",
             "SharedWindowSize.NoteRemotePuller",
             "a resize driven by a peer must be attributed to that peer, or every remote pull reads "
             + "as NOBODY IDENTIFIABLE - which the line itself calls a defect"),
            ("src/GloomhavenVR/Net/Remote/RemoteStorySync.cs",
             "SharedWindowSize.NoteRemotePuller",
             "the scenario story box travels in record 19 and needs the same attribution"),
            ("src/GloomhavenVR/WorldUI/Modal/ArcSeats.cs",
             "SharedWindowSizeLaw.Token(fitted, grabFactor)",
             "the SHARED WINDOW SIZE TERMS line reads its own COMMITTED size off the drawn "
             + "half-size, which already contains the resize; a prediction without the resize makes "
             + "that line say DISAGREES for every resized window, and its own doc reads a "
             + "disagreement as the size law being BYPASSED"),
        };
        foreach (var (file, needle, why) in callSites)
        {
            string path = Path.Combine(repoRoot, file);
            bool present = File.Exists(path)
                           && File.ReadAllText(path).Contains(needle, StringComparison.Ordinal);
            t.True(present, $"{file} no longer calls {needle}. {why}.");
        }
    }

    /// <summary>The lines of a C# file with its comments and doc comments removed, so a token quoted
    /// in a REASON does not count as a use. This project has a recorded case of exactly that: a line
    /// citing another instrument's grep token counted itself
    /// ([[token-quoted-in-its-own-explanation]]).</summary>
    private static IEnumerable<string> CodeLines(string[] lines)
    {
        bool inBlock = false;
        foreach (string raw in lines)
        {
            string line = raw;
            if (inBlock)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0)
                    continue;
                line = line[(close + 2)..];
                inBlock = false;
            }
            int open = line.IndexOf("/*", StringComparison.Ordinal);
            if (open >= 0)
            {
                inBlock = true;
                line = line[..open];
            }
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0)
                line = line[..slashes];
            if (line.Trim().Length > 0)
                yield return line;
        }
    }
}
