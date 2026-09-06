using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE PLACE A CLIENT-LOCAL PIXEL COUNT IS ALLOWED TO REACH A SHARED WINDOW'S WORLD SIZE —
/// AND THE PLACE THAT REFUSES IT.</b> <see cref="SharedWindowSizeLaw"/> holds the arithmetic and
/// the user ruling; this file holds the Unity half: which frame the arithmetic is fed, and how that
/// frame is made the same on every machine.
///
/// <para><b>THE POPULATION, ENUMERATED — this is the list, and it is CLOSED.</b> A shared window is
/// exactly a window <see cref="SharedWindows.KindOf"/> answers a non-<c>None</c>
/// <see cref="SharedWindowKind"/> for, and that enum has four members: the scenario story box
/// (<c>StoryController.window</c>), the campaign map's story box
/// (<c>MapStoryController.window</c>, or the composed host while <c>StoryComposite</c>'s claim
/// stands), the quest-confirmation popup (<c>UIWindowID.QuestPopup</c>) and the road/city encounter
/// (<c>Singleton&lt;UIEventPanel&gt;</c>). There is no fifth, and a fifth cannot be added without
/// adding an enum member — which is what makes "a window you did not enumerate" impossible here
/// rather than merely unlikely. The multiplayer DIALOG is not on the list and must not be: it is
/// the mod's own <c>SelfUpdateDialog</c>/version box, built by this mod at a size this mod already
/// chooses identically on every client.</para>
///
/// <para><b>WHAT DIVERGED, AND WHY IT IS NOT ONE WINDOW'S BUG.</b> The 448 logs have the host on a
/// 1920x1080 canvas and the co-player on a 2580x1080 one. The game's UI canvas is a
/// <c>ScaleWithScreenSize</c> scaler matched on HEIGHT, so the canvas is always the design HEIGHT
/// tall and as many pixels WIDE as the display is shaped. Every window in the list above except the
/// quest popup is anchored to that whole canvas. So their authored rect carries the display's
/// aspect ratio, and any rule that turns authored pixels into metres inherits it. That is a
/// property of the RIG, not a misconfiguration, and it will keep producing reports until the frame
/// itself is normalised — which is what <see cref="TryDesignFrame"/> and <see cref="Repin"/>
/// do.</para>
///
/// <para><b>THE FRAME IS PINNED, NOT THE SIZE, AND THAT DISTINCTION IS THE WHOLE FIX.</b> Pinning
/// only the committed millimetres would leave the peer drawing a 2580 px wide plate inside a
/// 1920 px wide box — the same window at the same "size" and visibly not the same window. Pinning
/// the conversion TARGET's rect to the canvas's own <c>referenceResolution</c> makes the game lay
/// the window out at the design width, so the content fit, the ink bounds, the grab bar
/// (<c>GrabBarLayout.Solve</c>) and the shared seat are all fed identical inputs and none of them
/// needs a fix of its own. It is the same operation the ESC/Options family already gets from
/// <c>CanvasConversion.ResolveStableHeightCap</c> — a serialized design number standing in for a
/// live one — applied to the width as well and gated to this list.</para>
///
/// <para><b>IT IS ARMED ON THE KIND, NOT ON PARTICIPATION, AND THAT IS DELIBERATE.</b>
/// <see cref="SharedWindows.ParticipatesHere"/> flips while a window stands — the session comes up,
/// the player toggles the 3D map — and a window that CHANGES SIZE when somebody joins is the same
/// class of report this file exists to end. The size of a window in this list is therefore a
/// constant of the window, in singleplayer and in a session alike. The cost, stated rather than
/// hidden: <c>[WorldUI] WindowLegibility</c> and <c>[WorldUI] CanvasScaleMm</c> do not move these
/// four windows at all. At the shipped defaults nothing the user has accepted changes by a
/// micrometre (see <see cref="SharedWindowSizeLaw"/>), and the two-hand resize still works on them,
/// because that is a per-window gesture and not a client dial.</para>
/// </summary>
internal static class SharedWindowSize
{
    /// <summary>How close a window's rect has to be to the root canvas's rect, in pixels, before it
    /// counts as a full-canvas stretch. Generous on purpose: a window anchored to the canvas with a
    /// small inset is still a stretch window and still carries the display's aspect.</summary>
    private const float StretchTolerancePx = 64f;

    /// <summary>A design resolution below this is not a design resolution — refuse rather than
    /// pin a window to garbage.</summary>
    private const float MinDesignPx = 64f;

    /// <summary>Panels whose arming has already been reported, so the Note below is one line per
    /// window per session and not one per open. Keyed by the host name, which is stable across
    /// opens and is what every other WorldUI line prints.</summary>
    private static readonly System.Collections.Generic.HashSet<string> ArmLogged = new();

    /// <summary>
    /// THE DESIGN FRAME for a window about to be converted, or false when this window is not in the
    /// shared population / the canvas cannot be read.
    ///
    /// <para><b>CALL THIS BEFORE <c>CanvasConversion.Convert</c> REPARENTS THE TARGET.</b> The
    /// design resolution is read off the target's ROOT CANVAS, and once the target hangs under the
    /// mod's world-space host its root canvas is the mod's own — which has no
    /// <see cref="CanvasScaler"/> and would answer nothing. This is the same ordering constraint
    /// <c>CanvasConversion.ResolveStableHeightCap</c> already lives under, and it is stated here so
    /// a future caller does not have to rediscover it from an empty answer.</para>
    ///
    /// <para>Two outcomes, and the second one is not a failure:</para>
    /// <list type="number">
    /// <item><b>A FULL-CANVAS STRETCH WINDOW</b> — its rect matches the live root-canvas rect. The
    /// design frame is the scaler's <c>referenceResolution</c>: a serialized asset value, the same
    /// on every install, and the number the game's own artists laid the window out against.</item>
    /// <item><b>A FIXED-SIZE AUTHORED CARD</b> — the quest popup is 512x1021 px on both machines in
    /// the 448 logs, because its rect comes from the prefab and not from the screen. It is already
    /// client-independent, so it is returned UNCHANGED and nothing is pinned. Reshaping it would
    /// disturb geometry the user has accepted (ModBuild 449's <c>Min(frame, ink, plate)</c> grab-bar
    /// seat is solved against exactly this rect) for no gain.</item>
    /// </list>
    /// </summary>
    internal static bool TryDesignFrame(UIWindow? window, RectTransform? target,
                                        out Vector2 designPx, out SharedWindowKind kind,
                                        out string source)
    {
        designPx = Vector2.zero;
        kind = SharedWindowKind.None;
        source = "not a shared window";
        if (window == null || target == null)
            return false;

        kind = SharedWindows.KindOf(window);
        if (kind == SharedWindowKind.None)
            return false;

        Vector2 authored = target.rect.size;
        if (authored.x < MinDesignPx || authored.y < MinDesignPx)
        {
            source = $"the authored rect is degenerate ({authored.x:F0}x{authored.y:F0} px)";
            return false;
        }

        Canvas? nearest = target.GetComponentInParent<Canvas>();
        Canvas? root = nearest != null ? nearest.rootCanvas : null;
        var rootRect = root != null ? root.transform as RectTransform : null;
        if (rootRect == null)
        {
            source = "no root canvas above the target — the stretch test cannot be made";
            return false;
        }

        Vector2 canvasPx = rootRect.rect.size;
        bool stretch = Mathf.Abs(canvasPx.x - authored.x) <= StretchTolerancePx
                       && Mathf.Abs(canvasPx.y - authored.y) <= StretchTolerancePx;
        if (!stretch)
        {
            // A fixed-size authored card: the prefab's own numbers, identical on every install.
            designPx = authored;
            source = $"the window's own authored rect ({authored.x:F0}x{authored.y:F0} px) — it is "
                     + $"NOT a full-canvas stretch (canvas is {canvasPx.x:F0}x{canvasPx.y:F0} px), "
                     + "so its size comes from the prefab and is already the same on every client";
            return true;
        }

        var scaler = root != null ? root.GetComponent<CanvasScaler>() : null;
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
            || scaler.referenceResolution.x < MinDesignPx
            || scaler.referenceResolution.y < MinDesignPx)
        {
            source = "a FULL-CANVAS STRETCH window whose root canvas has no usable "
                     + "CanvasScaler.referenceResolution — REFUSED, the size stays client-local";
            designPx = Vector2.zero;
            return false;
        }

        designPx = scaler.referenceResolution;
        source = $"CanvasScaler.referenceResolution ({designPx.x:F0}x{designPx.y:F0} px) standing "
                 + $"in for this client's canvas ({canvasPx.x:F0}x{canvasPx.y:F0} px)";
        return true;
    }

    /// <summary>
    /// Record the design frame on the panel and say so once. The panel field is what every
    /// downstream reader consults — <c>ModalFallback.DeriveWindowScale</c> for the scale,
    /// <c>CanvasConversion.ReassertConversionFrame</c> for the pin, <c>ArcSeats</c> for the log —
    /// so there is exactly one arming decision per conversion and no second mechanism beside it.
    /// </summary>
    internal static void Arm(ConvertedPanel? panel, UIWindow? window, RectTransform? target,
                             string name)
    {
        if (panel == null)
            return;
        if (!TryDesignFrame(window, target, out Vector2 designPx, out SharedWindowKind kind,
                            out string source))
        {
            panel.SharedDesignFrame = Vector2.zero;
            panel.SharedDesignSource = source;
            if (kind != SharedWindowKind.None && ArmLogged.Add(name))
            {
                // HW-VERIFY
                VRLog.Note("WorldUI",
                    $"SHARED WINDOW SIZE LAW NOT ARMED — '{name}' carries SharedWindowKind.{kind} "
                    + $"but no design frame could be resolved: {source}. THIS IS THE FALSIFIER FOR "
                    + "THE WHOLE GUARANTEE: the window keeps the pre-ModBuild-450 sizing, which is "
                    + "a function of THIS client's canvas and THIS client's [WorldUI] "
                    + "WindowLegibility / CanvasScaleMm, so its committed millimetres may differ "
                    + "from every other player's. If this line appears, do not read the matching "
                    + "SHARED WINDOW SIZE TERMS line as proof of 1:1 — it is only proof of what "
                    + "this one client did.");
            }
            return;
        }

        panel.SharedDesignFrame = designPx;
        panel.SharedDesignSource = source;
        if (!ArmLogged.Add(name))
            return;
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"SHARED WINDOW SIZE LAW ARMED — '{name}' (SharedWindowKind.{kind}) will be sized from "
            + $"a DESIGN FRAME of {designPx.x:F0}x{designPx.y:F0} px, taken from {source}. Its "
            + "committed millimetres are now a pure function of (that frame, the shipped "
            + "WindowLegibility, the shipped CanvasScaleMm) and of nothing this client owns — not "
            + "the display aspect, not the live dials, not the rig scale, not the session state. "
            + $"At this frame the law commits {SharedWindowSizeLaw.CommittedMm(designPx).x:F0} x "
            + $"{SharedWindowSizeLaw.CommittedMm(designPx).y:F0} mm (token "
            + $"{SharedWindowSizeLaw.Token(designPx)}) before the content fit has its say. "
            + "Reported once per window per session.");
    }

    /// <summary>
    /// RE-PIN the conversion target's rect to the armed design frame, returning a log-ready note
    /// when it had to write. Called from <c>CanvasConversion.ReassertConversionFrame</c>, i.e. from
    /// the one function that already owns "the frame Convert pinned, which the game re-drives".
    ///
    /// <para>Requires the target's anchors to be collapsed to the centre — which is step 4 of that
    /// same function, and runs before this one. With stretch anchors <c>sizeDelta</c> is an inset
    /// against the parent rather than a size, so writing it there would silently do nothing:
    /// [[anchors-own-a-stretch-child-size]] is the recorded version of that mistake.</para>
    /// </summary>
    internal static bool Repin(ConvertedPanel panel, RectTransform target, out string note)
    {
        note = string.Empty;
        Vector2 want = panel.SharedDesignFrame;
        if (want.x < MinDesignPx || want.y < MinDesignPx)
            return false;
        if ((target.anchorMin - new Vector2(0.5f, 0.5f)).sqrMagnitude > 1e-6f
            || (target.anchorMax - new Vector2(0.5f, 0.5f)).sqrMagnitude > 1e-6f)
            return false;               // not centre-anchored yet: sizeDelta is not a size here
        Vector2 had = target.rect.size;
        if (Mathf.Abs(had.x - want.x) <= 0.5f && Mathf.Abs(had.y - want.y) <= 0.5f)
            return false;
        target.sizeDelta = want;
        note = $"shared design frame re-pinned {had.x:F0}x{had.y:F0} -> {want.x:F0}x{want.y:F0} px";
        return true;
    }

    /// <summary>Is this panel under the shared size law? One field test, safe on a null panel, and
    /// the single predicate every caller outside this file uses.</summary>
    internal static bool IsArmed(ConvertedPanel? panel) =>
        panel != null && panel.SharedDesignFrame.x >= MinDesignPx
        && panel.SharedDesignFrame.y >= MinDesignPx;

    /// <summary>Forget the per-session arming log. Called from the WorldUI module teardown so a
    /// second session in the same process reports its arming again rather than looking silent.
    /// [[held-instrument-reads-as-dead]] is the recorded version of that reading.</summary>
    internal static void ResetForSession() => ArmLogged.Clear();
}
