using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>HOW BIG A SHARED WINDOW IS, IN MILLIMETRES, DECIDED WITHOUT ASKING THIS CLIENT ANYTHING.</b>
///
/// <para><b>THE USER RULING THIS FILE IS (2026-09-06, verbatim):</b> <i>"Ich möchte das
/// Multiplayerfenster immer die selbe Größe haben bei allen Spielern, damit die 1:1 Regel hier
/// nicht gebrochen wird. Gewährleiste das."</i> — the shared windows must always be the same size
/// for every player. He did not ask for one window to be repaired; he asked for an assurance that
/// holds for the CLASS. That is why the arithmetic lives in a file of its own, free of Unity beyond
/// <c>Mathf</c> and of the mod beyond the wire codec whose grid it shares
/// (<c>Net.NetProtocol.EncodeStorySize</c> — see <see cref="SharedGrabFactor"/> for why that one
/// reference is the point and not a cost), and is driven by <c>SharedWindowSizeVectors</c> under
/// <c>scripts/wire-tests.sh</c>: the claim "two clients compute the same number" is a claim about a
/// FUNCTION, and a function is testable without a headset, without a second machine and without the
/// two of them being in the same room.</para>
///
/// <para><b>WHAT WENT WRONG, MEASURED (ModBuild 448 logs, one session, two machines).</b> The
/// <c>SHARED WINDOW SIZE TERMS</c> line read, for the same window at the same stage:
/// <code>
///   Map Story Window — HOST: authored 1920x1080 px -> committed 1200 x 675 mm
///                      PEER: authored 2580x1080 px -> committed 1200 x 502 mm
/// </code>
/// The two machines do not have the same display aspect, the game's canvas stretches to the
/// display, and these windows are anchored to the whole canvas. So the AUTHORED frame is a
/// client-local number, and the old rule turned it into a world size directly.</para>
///
/// <para><b>WHY A WIRE FIELD CANNOT FIX THIS, AND THAT IS A FINDING RATHER THAN AN OPINION.</b>
/// A converted panel is drawn at a UNIFORM scale (<c>CanvasConversion.PlaceHost</c> writes
/// <c>localScale = one * (mmPerPx * worldScale)</c>), so its committed size is
/// <c>rectPx * k</c> for a single scalar <c>k</c>. Publishing the owner's <c>k</c>, or the owner's
/// millimetres, chooses ONE dimension: adopt the owner's WIDTH and the heights still differ; adopt
/// the HEIGHT and the widths differ. Two rects of different ASPECT cannot be made the same size by
/// any scalar. The divergence therefore had to be removed from the RECT, which is what
/// <c>SharedWindowSize</c> does, and no wire record was added.</para>
///
/// <para><b>AND THE FOLLOW-UP RULING TURNS THAT PARAGRAPH ON ITS HEAD FOR THE OTHER HALF OF THE
/// SIZE — deliberately, because the premise really is the opposite one.</b> USER, 2026-09-06,
/// verbatim: <i>"Auch beim größer/kleiner ziehen soll die 1:1 Regel gelten."</i> The paragraph
/// above says a scalar cannot reconcile two rects of different ASPECT, and that is why the aspect
/// had to leave the rect. Once it HAS left — once every client's committed rect is the same rect by
/// construction — the two-hand resize is the reconciled case: one uniform factor applied to one
/// agreed rect, which is exactly the shape a scalar can carry. So the resize DOES ride the wire,
/// it rides the <c>sizeCode</c> byte records 19 and 21 have carried since ModBuild 226, and no
/// record was added for it either. <see cref="SharedGrabFactor"/> is the half that was missing.</para>
///
/// <para><b>THE LAW, IN ONE SENTENCE.</b> A shared window's millimetres are
/// <c>committedPx x DesignCanvasScaleMm x Shrink(committedPx.x)</c>, where <c>committedPx</c> comes
/// from a frame pinned to the canvas's own DESIGN resolution and <c>Shrink</c> is the shipped
/// board-relative rule evaluated at its SHIPPED DEFAULT dials — never at this client's.</para>
///
/// <para><b>WHY THE DIALS ARE FROZEN RATHER THAN WIRED.</b> The recorded ruling
/// [[one-to-one-beats-local-legibility]] is that a MIRROR wears the OWNER's dial: a peer's placard
/// is a copy of one player's object, so there is an owner to follow. A shared window is not a
/// mirror and has no owner — every player has their own copy of the same game window and all of
/// them are equally entitled to it. "The owner's value" has no referent here, so the only value
/// every client can agree on without an exchange is the SHIPPED one. The cost is stated rather than
/// hidden: <c>[WorldUI] WindowLegibility</c> and <c>[WorldUI] CanvasScaleMm</c> no longer move
/// these windows while a session is standing. At the shipped defaults — which is what both machines
/// in the 448 logs were running — every committed number is bit-identical to what the user has
/// already accepted, so nothing he approved moves.</para>
///
/// <para><b>THE CONSTANTS BELOW ARE COPIES, AND THE COPY IS CHECKED.</b> They restate
/// <c>ModalFallback.WindowScaleFactor</c>, <c>ModalFallback.ModalTargetWidthMeters</c>,
/// <c>ModalFallback.MinWindowScaleFactor</c>, <c>Defaults.WindowLegibility</c> and
/// <c>Defaults.CanvasScaleMm</c>, which live in files this one must not drag into the test assembly
/// (they carry BepInEx, the game model and half of WorldUI between them).
/// <c>SharedWindowSizeVectors.VerifyAgainstSource</c> reads those five declarations out of the
/// repository and FAILS THE BUILD when a copy has gone stale — the same guarantee
/// <c>Shims.VerifyAgainstSource</c> already gives the quantisation constants.</para>
/// </summary>
internal static class SharedWindowSizeLaw
{
    /// <summary>Copy of <c>ModalFallback.WindowScaleFactor</c> (the small-dialog cap).</summary>
    internal const float DesignWindowScaleFactor = 0.7f;

    /// <summary>Copy of <c>ModalFallback.ModalTargetWidthMeters</c> — the board-sized width a
    /// window wider than the board is shrunk to.</summary>
    internal const float DesignTargetWidthMeters = 0.80f;

    /// <summary>Copy of <c>ModalFallback.MinWindowScaleFactor</c>.</summary>
    internal const float DesignMinWindowScaleFactor = 0.15f;

    /// <summary>Copy of <c>Defaults.WindowLegibility</c> — the SHIPPED dial, deliberately not the
    /// live one. See the class doc for why a shared window has no owner to take it from.</summary>
    internal const float DesignWindowLegibility = 1.5f;

    /// <summary>Copy of <c>Defaults.CanvasScaleMm</c> — millimetres of physical window per authored
    /// uGUI pixel at the shipped dial.</summary>
    internal const float DesignCanvasScaleMm = 1.0f;

    /// <summary>
    /// The board-relative shrink for a shared window of this committed pixel width — i.e.
    /// <c>ModalFallback.DeriveWindowScale</c> with both dials pinned to the shipped defaults.
    ///
    /// <para>Below the crossover the small-dialog cap binds and the window's millimetres grow with
    /// its pixels; above it the board-relative term binds and the window is exactly
    /// <see cref="TargetWidthMm"/> wide no matter how many pixels it has. At the shipped numbers
    /// the crossover is 1143 px, which is why a 512 px card is 538 mm while both a 1920 px and a
    /// 2580 px frame are 1200 mm.</para>
    /// </summary>
    internal static float Shrink(float committedWidthPx)
    {
        float cap = DesignWindowScaleFactor * DesignWindowLegibility;
        const float metersPerPixel = DesignCanvasScaleMm * 0.001f;
        if (committedWidthPx < 1f)
            return cap;
        float boardRelative =
            DesignTargetWidthMeters * DesignWindowLegibility / (committedWidthPx * metersPerPixel);
        return Mathf.Clamp(Mathf.Min(cap, boardRelative), DesignMinWindowScaleFactor, cap);
    }

    /// <summary>The physical width, in millimetres, a window wider than the crossover is pinned to.
    /// One number, the same on every client, and the only place the user's accepted "board-sized"
    /// width is expressed in the unit he actually sees it in.</summary>
    internal static float TargetWidthMm => DesignTargetWidthMeters * DesignWindowLegibility * 1000f;

    /// <summary>
    /// THE COMMITTED PHYSICAL SIZE, in millimetres, for a shared window whose committed pixel rect
    /// is <paramref name="committedPx"/>. Every other term is a constant of this file, so two
    /// clients that agree on <paramref name="committedPx"/> agree on the millimetres exactly.
    /// </summary>
    internal static Vector2 CommittedMm(Vector2 committedPx)
    {
        float shrink = Shrink(committedPx.x);
        return new Vector2(committedPx.x * DesignCanvasScaleMm * shrink,
                           committedPx.y * DesignCanvasScaleMm * shrink);
    }

    /// <summary>
    /// The <c>extraScale</c> the renderer must be given so a host rect of
    /// <paramref name="committedPx"/> lands on <see cref="CommittedMm"/>.
    ///
    /// <para><b>WHY THE LIVE DIAL APPEARS HERE AND ONLY HERE.</b> The host is drawn at
    /// <c>hostPx x liveCanvasScaleMm x extraScale</c> — the live dial is baked into the renderer,
    /// not into this file, so the only way to cancel it is to divide it back out at the one point
    /// where the two meet. It is a CANCELLATION, not a reading: the product below contains no
    /// client-local term, which is exactly what <c>SharedWindowSizeVectors</c> asserts by driving
    /// this function at several different live dials and requiring one answer in millimetres.</para>
    /// </summary>
    internal static float ExtraScale(Vector2 committedPx, float liveCanvasScaleMm)
    {
        if (liveCanvasScaleMm <= 0f)
            return Shrink(committedPx.x);
        return Shrink(committedPx.x) * DesignCanvasScaleMm / liveCanvasScaleMm;
    }

    // =============================================================================================
    // THE SECOND HALF OF THE SIZE: THE TWO-HAND RESIZE (ModBuild 450, the 2026-09-06 follow-up)
    // =============================================================================================

    /// <summary>
    /// <b>THE ONE LEGAL VALUE OF A SHARED WINDOW'S TWO-HAND GRAB FACTOR — WHICH IS, BY
    /// CONSTRUCTION, THE VALUE ON THE WIRE.</b>
    ///
    /// <para><b>THE USER RULING (2026-09-06, verbatim):</b> <i>"Auch beim größer/kleiner ziehen soll
    /// die 1:1 Regel gelten. Alle Spieler sollen immer die selbe Größe sehen, d.h. skalliert ein
    /// Spieler ein Multiplayer fenster sehen alle Spieler wie es skalliert und sehen somit wieder
    /// die exakt gleiche Größe bei allen."</i> Two words decide the shape of this function.
    /// <b>"immer"</b> — there is no interval in which the sizes may differ, so the puller may not
    /// hold a value nobody else can hold. <b>"exakt"</b> — "close enough to see" is not the
    /// standard; the numbers have to be the same numbers.</para>
    ///
    /// <para><b>WHY THIS IS A QUANTISER AND NOT A CLAMP, and it is the whole of the fix.</b> The
    /// resize was ALREADY shared before this build: records 19 and 21 have carried a
    /// <c>sizeCode</c> — the grab factor in hundredths — since ModBuild 226, they publish it
    /// mid-drag at the 15 Hz carry rate, and both appliers write it onto the receiver's grab frame.
    /// What was NOT shared was the puller's own copy of it. The sender kept an unrounded float
    /// (<c>_rootScale0 × d / anchorDistance</c>, lerped every frame) and published
    /// <c>round(f × 100)</c>, so every follower stood at a multiple of 0.01 and the puller stood
    /// wherever the pinch left it — a permanent residual of up to half a code, i.e. 0.5 %, which on
    /// the 1200 mm story window is 6 mm that never healed because there was no edge left to heal
    /// it. That is small, and it is exactly the class of difference the word "exakt" is about.</para>
    ///
    /// <para><b>SO THE GRID IS NOT COPIED HERE, IT IS CALLED.</b> This function is literally
    /// <c>Decode(Encode(x))</c> against the shipped codec. There is no second constant to drift
    /// ([[a-frozen-literal-is-a-consumer]]), no pinned copy to go stale, and the clamp question
    /// answers itself: <c>EncodeStorySize</c> already clamps into
    /// <c>[StorySizeMinCode, StorySizeMaxCode]</c>, so the wire's window IS the window, on the
    /// puller and on every follower, and a future owner that widened
    /// <c>IPanelGrabOwner.GrabScaleLimits</c> could not carry a shared window past it. The
    /// dependency runs WorldUI → Net, which is the direction that already exists (a dozen WorldUI
    /// files read <c>Net.NetProtocol</c> constants) and never the reverse.</para>
    ///
    /// <para>Idempotent by construction, which is what lets it be applied every frame and at both
    /// ends of the wire without ever being a write: <c>f(f(x)) == f(x)</c>, and
    /// <c>Encode(f(x)) == Encode(x)</c> for every x — both asserted in
    /// <c>SharedWindowSizeVectors</c>.</para>
    /// </summary>
    internal static float SharedGrabFactor(float wanted) =>
        Net.NetProtocol.DecodeStorySize(Net.NetProtocol.EncodeStorySize(wanted));

    /// <summary>The wire code <see cref="SharedGrabFactor"/> resolves to — the short integer two
    /// logs are compared on, and the value the change instrument latches so a pull reports steps
    /// rather than frames.</summary>
    internal static byte SharedGrabCode(float wanted) => Net.NetProtocol.EncodeStorySize(wanted);

    /// <summary>The smallest factor a shared window may be pulled to, taken from the wire and not
    /// from <c>PanelGrabHandle</c>: a value the wire cannot carry is a value the other players
    /// cannot stand at.</summary>
    internal static float MinSharedGrabFactor => Net.NetProtocol.StorySizeMinCode / 100f;

    /// <inheritdoc cref="MinSharedGrabFactor"/>
    internal static float MaxSharedGrabFactor => Net.NetProtocol.StorySizeMaxCode / 100f;

    /// <summary>
    /// THE COMMITTED PHYSICAL SIZE with the shared two-hand resize applied. The resize is a
    /// UNIFORM factor on an already-uniform scale, so it multiplies both millimetre figures and
    /// nothing else — which is why a scalar is the right shape HERE even though it was the wrong
    /// shape for the aspect divergence this file was written for. Both clients read the factor off
    /// the same wire code, so both compute the same product.
    /// </summary>
    internal static Vector2 CommittedMm(Vector2 committedPx, float sharedGrabFactor)
    {
        float f = SharedGrabFactor(sharedGrabFactor);
        Vector2 mm = CommittedMm(committedPx);
        return new Vector2(mm.x * f, mm.y * f);
    }

    /// <summary>
    /// <b>THE 1:1 TOKEN.</b> A short, stable, human-comparable fingerprint of what the committed
    /// size came out as. Two clients printing the same token for the same window at the same stage
    /// have the same window to a tenth of a millimetre; two clients printing different tokens
    /// differ, and the log line beside it names the term they differ in.
    ///
    /// <para>Deliberately NOT a hash — this project has a standing note that a hash delta which
    /// cannot be decoded costs a round ([[hash-delta-is-decodable]]). It is the two millimetre
    /// figures at 0.1 mm: finer than anything a rig can show, coarse enough that float noise
    /// cannot split it.</para>
    /// </summary>
    internal static string Token(Vector2 committedPx)
    {
        Vector2 mm = CommittedMm(committedPx);
        return $"{Mathf.RoundToInt(mm.x * 10f)}x{Mathf.RoundToInt(mm.y * 10f)}";
    }

    /// <summary>The 1:1 token WITH the shared two-hand resize in it. <c>Token(px, 1f)</c> is
    /// <c>Token(px)</c> to the bit, so every token already read off a hardware log keeps its
    /// meaning and only a genuinely resized window prints a different one.</summary>
    internal static string Token(Vector2 committedPx, float sharedGrabFactor)
    {
        Vector2 mm = CommittedMm(committedPx, sharedGrabFactor);
        return $"{Mathf.RoundToInt(mm.x * 10f)}x{Mathf.RoundToInt(mm.y * 10f)}";
    }
}
