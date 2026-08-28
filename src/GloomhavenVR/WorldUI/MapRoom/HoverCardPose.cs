using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE HOVER CARD'S POSE — and the two things that were fighting it (ModBuild 188).
///
/// <para>User ruling, verbatim: <i>"Bei Mouseovers über ein Symbol soll es über dem Symbol
/// entsprechend fliegen ohne ein separates Fenster zu sein das man verschieben kann (immer zum Kopf
/// gedreht) und nur solange der Mouseover anhält."</i> 187 got the card to APPEAR; it appeared
/// OFFSET, and this class is why it did.</para>
///
/// <para>CAUSE 1 — THE GAME IS STILL DRIVING THE SAME TRANSFORM. <c>UIQuestPreviewPopup</c> carries
/// its own follow component, <c>UIFollowMapLocationInsideArea</c>, and <c>Show(location, offset)</c>
/// switches it ON (decompiled UIQuestPreviewPopup.cs:514-530). Every LateUpdate it then does two
/// things to the popup's own RectTransform — the exact rect <c>CanvasConversion.Convert</c>
/// re-parents under the mod's world-space host and normalises to
/// <c>pivot (0.5,0.5) / anchoredPosition 0</c>:
/// <list type="number">
/// <item>it REWRITES THE PIVOT (<c>rectTransform.pivot = (0.5, 0)</c>, and to (0,0.5)/(1,0.5)/(x,1)
///   when its "keep inside the area" branches fire — UIFollowMapLocationInsideArea.cs:321-355),
///   which slides the drawn card half its own width or height off the host it is centred in;</item>
/// <item>it WRITES <c>transform.localPosition</c> from a SCREEN point
///   (<c>CameraController…m_Camera.WorldToScreenPoint(target.position + offset)</c> →
///   <c>ScreenPointToLocalPointInRectangle</c>, UIFollowMapLocation.cs:222-306) — a point in the
///   1920x1080 screen canvas it captured in OnEnable, now applied as a local offset inside a
///   world-space host of a completely different scale, computed through a camera the mod has
///   FROZEN (the map room prefix-skips <c>CameraController.LateUpdate</c>). It is a different
///   wrong number for every icon, because it is that icon's projection on a camera that is no
///   longer looking anywhere in particular. That is precisely the reported shape: "usually
///   offset", and differently offset per symbol.</item>
/// </list>
/// The fix is the one the game itself uses when it puts the popup away
/// (<c>Hide</c>/<c>onHidden</c>: <c>followTarget.enabled = false</c>, UIQuestPreviewPopup.cs:427-432,
/// :537-547): while the card is mod-owned, the follow stands down — and the pivot/anchor it already
/// wrote are put back, since disabling a component does not undo what it wrote.</para>
///
/// <para>CAUSE 2 — THE CARD WAS MEASURED BY A RECT THAT NEED NOT BE THE CARD. The old seat used
/// <c>hostRect.rect.height * 0.5 * lossyScale.y</c>. A hover card is deliberately NOT content-fit
/// (187 made it non-pokeable so it could not eat its own hover, and <c>CanvasConversion.Convert</c>
/// enrols only <c>fitContent ?? pokeable</c>), so that height is the window ROOT's authored rect —
/// which is only the visible card if the popup's root happens to hug its content, and is out by
/// half a screen if it does not. It also assumes the content is CENTRED in that rect, which is
/// exactly what cause 1 breaks. This class instead measures the union of what is actually being
/// DRAWN, in host-local space, through the same visibility predicate the content fit uses
/// (<c>CanvasConversion.CountsAsFitContent</c>) — so a <c>ContentSizeFitter</c> that has not settled
/// yet, a pivot that is not centred, and pooled reward/enemy rows arriving late all resolve
/// themselves on the next frame instead of becoming a permanent offset.</para>
///
/// <para>WHAT IS DELIBERATELY KEPT. The card is billboarded to the head — that is the one thing the
/// ruling asks to re-orient, and the standing "nothing may re-orient with head movement" rule names
/// it as the exception. Yaw only, flattened to the horizon, so a card read from above stays upright.
/// And the pose is WRITTEN, not parented: the map rebuilds its icons wholesale on every
/// <c>InitMap</c>, and a host parented into that hierarchy would be destroyed with it mid-frame.</para>
///
/// <para><b>AND IT NOW POSES A PEER'S PLACARD TOO, WHICH IS WHY THE BILLBOARD TAKES A POINT AND NOT
/// A CAMERA.</b> User ruling (report 6, verbatim): <i>"Mouseover der Symbole in der Map soll nicht
/// das Steam-Symbol sein, sondern das richtige Mouseover das der Spieler auch sieht, zu dem
/// jeweiligen Spieler hingedreht, direkt über dem jeweiligen Symbol."</i> A peer's placard is the
/// GAME'S OWN preview popup, instantiated per peer and fed the same <c>IQuest</c>
/// (<c>Net.RemoteMapRoom.Placards</c>), and it is seated by THIS code so that "above the symbol"
/// means the identical geometry for a foreign card and a local one — the same neutralisation of the
/// game's follow component, the same measured-content bottom edge, the same anchor. The single
/// difference is the point it is turned to: the local card faces the local head, a peer's faces
/// THAT PEER's head. Since the name row and the Steam picture are gone, that facing is the only
/// thing that says whose placard it is, so it is not decoration.</para>
///
/// <para><b>AND ITS SIZE IS THE OWNER'S DIAL, NOT THE VIEWER'S — THE OTHER ANSWER WAS ARGUED HERE
/// AND OVERRULED (2026-08-28).</b> This class used to publish <c>LocalCardHostScale</c>: the world
/// scale MEASURED off the local player's own hover card, handed to <c>Net.RemoteMapRoom</c> so that
/// every peer's placard was built at it. The argument for it is recorded verbatim because it was a
/// real one, not an oversight: <i>a peer's placard must be the SAME SIZE as the card the local
/// player gets for the same icon</i> — one room, one apparent size for one kind of object, and the
/// viewer's <c>[WorldUI] WindowLegibility</c> is an ACCESSIBILITY dial, so a viewer who enlarged
/// their windows to read them arguably enlarged all of them. The user decided the other way and the
/// wording leaves no room: <i>"Auch hier soll die 1:1 Regel gelten, also die Größe des
/// Besitzers."</i> A peer's placard is a picture of what THEY are looking at, so it is drawn at the
/// size THEY are looking at it — the same rule <c>Net.RemoteBoardTooltip</c> already followed, and
/// the 1:1 rule this project applies to everything a remote player renders, with the exceptions
/// living in <c>Net/RevealGate.cs</c> and nowhere else.</para>
///
/// <para>THE COST IS WORTH STATING, BECAUSE IT IS THE PRICE OF THE RULING AND NOT A DEFECT: the
/// dial is clamped 1.0..1.75 (<c>ModalFallback.WindowLegibilityMin</c>/<c>Max</c>), so two players
/// sitting at opposite ends of it see the SAME icon's placard at sizes 75 % apart. A viewer at 1.0
/// reading a peer at 1.75 gets a placard larger than any window of their own. That is the ruling,
/// deliberately. Do not re-derive the local-measurement version: it is gone from this file, the
/// size now rides the wire as <c>NetProtocol.TuneWindowLegibility</c> (id 180, record 28), and the
/// only thing the removal cost was the "NaN until this viewer has hovered an icon once" state that
/// the measurement needed and nothing else ever wanted.</para>
/// </summary>
internal static class HoverCardPose
{
    private const string Scope = "MapRoom";

    /// <summary>Frames between re-scans for the game's follow components on a card's subtree. The
    /// cached components are re-CHECKED every frame (a field read); the allocating
    /// <c>GetComponentsInChildren</c> only re-runs on this cadence, in case the game adds one late
    /// (<c>UIQuestPreviewPopup.Awake</c> will <c>AddComponent</c> its own if the serialized one is
    /// missing).</summary>
    private const int FollowScanIntervalFrames = 30;

    /// <summary>Smallest measured content extent, host-local units (canvas px), that counts as a
    /// card. Below this the popup is mid fade/scale-in and measuring it would seat the card on a
    /// size it is about to leave.</summary>
    private const float MinContentExtent = 8f;

    private static readonly List<Graphic> GraphicScratch = new(64);
    private static readonly Vector3[] CornerScratch = new Vector3[4];
    private static readonly List<UIFollowMapLocation> FollowScratch = new(4);

    private sealed class CardState
    {
        internal readonly List<UIFollowMapLocation> Follows = new(2);
        internal int FollowScanFrame = int.MinValue;
        internal int FollowsDisabled;
        internal bool LoggedFollow;
        internal bool LoggedDrift;
        internal bool LoggedSeat;
        internal bool WarnedNoContent;
        internal float LastSeen;
    }

    private static readonly Dictionary<int, CardState> States = new(4);

    /// <summary>Drop all per-card state (the room came down / the module detached).</summary>
    internal static void Reset()
    {
        States.Clear();
    }

    /// <summary>
    /// The name every PEER PLACARD's cloned popup carries, and the only thing that separates one
    /// from the game's own preview popup at runtime.
    ///
    /// <para>A peer's placard IS a <c>UIQuestPreviewPopup</c> instance (report 6: "es soll 1:1 so
    /// aussehen wie es für den Spieler auch aussieht"), so every by-type lookup in this project now
    /// has to be able to say which one it means. The prefix lives HERE rather than in the Net class
    /// that creates the clones because the two readers — <see cref="MapHoverVerdict"/>'s popup
    /// lookup and the clone builder itself — must not each carry their own copy of the string.</para>
    /// </summary>
    internal const string PeerPlacardNamePrefix = "GloomhavenVR.MapPeerPlacard";

    /// <inheritdoc cref="PeerPlacardNamePrefix"/>
    internal static bool IsPeerPlacard(GameObject? go) =>
        go != null && go.name.StartsWith(PeerPlacardNamePrefix, System.StringComparison.Ordinal);

    // NO LocalCardHostScale HERE ANY MORE. It published the world scale measured off the local
    // player's own card so that peer placards could be built at it; the 2026-08-28 ruling makes a
    // placard the OWNER's size, so there is nothing left to measure and nothing left to be NaN
    // until this viewer has hovered once. The argument it was built on, and the ruling that
    // overruled it, are recorded in the class doc so nobody re-runs either.

    /// <summary>
    /// Put one hover card where it belongs this frame, billboarded to the LOCAL head — the local
    /// player's own card.
    /// </summary>
    internal static void Place(ConvertedPanel panel, bool hasAnchor, Vector3 anchor, Camera? head)
    {
        Place(panel, hasAnchor, anchor,
              head != null ? head.transform.position : (Vector3?)null);
    }

    /// <summary>
    /// Put one hover card where it belongs this frame.
    ///
    /// <para><paramref name="hasAnchor"/> false means the pointer has already left the icon while
    /// the game still has the popup open for a frame or two: the card is left EXACTLY where it was
    /// (it is about to close, and snapping it to a fallback spot on the way out would be a visible
    /// jump) — but the game's follow component is still stood down, because a card being taken away
    /// must not be yanked across the room on its last frames.</para>
    ///
    /// <para><paramref name="viewer"/> IS WHO THE CARD IS TURNED TO, and for a peer's placard that
    /// is the whole of its identity (report 6): the name row and the Steam picture are gone, so
    /// "whose placard is this" is answered by the fact that it faces THEM and not you. Null leaves
    /// the rotation alone — a card whose reader cannot be located keeps the facing it had rather
    /// than snapping to a direction nobody chose.</para>
    ///
    /// <para>THERE IS NO LONGER AN <c>isLocalCard</c> FLAG. It existed only to decide whether this
    /// card's measured host scale was recorded as the reference every peer placard was built at,
    /// and the 2026-08-28 ruling ("also die Größe des Besitzers") took that job away — a placard is
    /// sized from its OWNER's dial off the wire, so nothing about a card is measured for anyone
    /// else's benefit. Nothing about the pose ever depended on it, which is why removing it changes
    /// no geometry.</para>
    /// </summary>
    internal static void Place(ConvertedPanel panel, bool hasAnchor, Vector3 anchor,
                               Vector3? viewer)
    {
        if (panel == null || !panel.IsAlive || panel.HostGo == null)
            return;
        Transform host = panel.HostGo.transform;
        CardState state = StateFor(panel.HostGo);

        Neutralise(panel, state);

        if (!hasAnchor)
            return;

        // Billboard: a world-space canvas's FRONT is its -forward (the spawn placer's convention),
        // so the host's forward points AWAY from its reader. Flattened to the horizon.
        if (viewer.HasValue)
        {
            Vector3 flat = anchor - viewer.Value;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-6f)
                host.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        // Seat the card's BOTTOM EDGE on the anchor, so the symbol it describes stays visible under
        // it instead of being covered by its own card.
        if (TryMeasureLocal(panel, host, out Vector3 bottomCentre, out Vector2 size))
        {
            host.position = anchor - host.TransformVector(bottomCentre);
            if (!state.LoggedSeat)
            {
                state.LoggedSeat = true;
                VRLog.Info(Scope, $"MAP ROOM hover card '{panel.HostGo.name}' seated on its icon — measured "
                                  + $"content {size.x:F0}x{size.y:F0} px, bottom-centre at "
                                  + $"({bottomCentre.x:F0},{bottomCentre.y:F0}) in host-local units, i.e. the "
                                  + $"host is offset by that much from the anchor rather than by half its own "
                                  + $"rect. {state.FollowsDisabled} game follow component(s) stood down. The "
                                  + "card's bottom edge sits ON the anchor and the anchor is the drawn icon's "
                                  + "top face (MapIconHoverPads), so 'above the symbol' is now geometry, not "
                                  + "a coincidence of two numbers.");
            }
            return;
        }

        // FALLBACK — the content could not be measured yet (still fading in, or a card that draws
        // nothing this predicate accepts). Use the host rect's own half height, which is what every
        // build up to 187 used unconditionally, and say so ONCE so a wrong seat is attributable.
        var rect = host as RectTransform;
        float half = rect != null ? rect.rect.height * 0.5f * Mathf.Abs(rect.lossyScale.y) : 0f;
        host.position = anchor + Vector3.up * half;
        if (!state.WarnedNoContent)
        {
            state.WarnedNoContent = true;
            VRLog.Warn(Scope, $"MAP ROOM hover card '{panel.HostGo.name}': no visible content could be "
                              + "measured, so it is seated on HALF ITS HOST RECT instead of on its drawn "
                              + "content. CONSEQUENCE: if this card looks too high or too low, this is why — "
                              + "the host rect is the window's authored root and a hover card is deliberately "
                              + "not content-fit. The card is still shown and still follows the icon.");
        }
    }

    /// <summary>
    /// Stand the game's own follow component down and undo what it already wrote. Runs every frame
    /// and is a no-op once quiet: a bool read per cached component, plus two RectTransform
    /// comparisons. See the class doc for why this is not optional.
    /// </summary>
    private static void Neutralise(ConvertedPanel panel, CardState state)
    {
        if (panel.HostGo == null)
            return;
        if (state.FollowScanFrame == int.MinValue
            || Time.frameCount - state.FollowScanFrame >= FollowScanIntervalFrames)
        {
            state.FollowScanFrame = Time.frameCount;
            FollowScratch.Clear();
            panel.HostGo.GetComponentsInChildren(includeInactive: true, FollowScratch);
            state.Follows.Clear();
            for (int i = 0; i < FollowScratch.Count; i++)
            {
                if (FollowScratch[i] != null)
                    state.Follows.Add(FollowScratch[i]);
            }
            FollowScratch.Clear();
        }

        for (int i = 0; i < state.Follows.Count; i++)
        {
            UIFollowMapLocation f = state.Follows[i];
            if (f == null || !f.enabled)
                continue;
            f.enabled = false;
            state.FollowsDisabled++;
            if (!state.LoggedFollow)
            {
                state.LoggedFollow = true;
                VRLog.Info(Scope, $"MAP ROOM hover card: the game's own '{f.GetType().Name}' on "
                                  + $"'{f.gameObject.name}' was still running on the floated card and is now "
                                  + "DISABLED for as long as the mod owns the pose — the same thing the game "
                                  + "does when it hides the popup (UIQuestPreviewPopup: followTarget.enabled = "
                                  + "false). It was writing a SCREEN-derived localPosition, through the map "
                                  + "camera this mod freezes, into a world-space host: a different wrong offset "
                                  + "for every icon, which is the reported 'card is not above its symbol'.");
            }
        }

        // Disabling does not undo what it already wrote. Put the conversion's own contract back on
        // the converted rect: centred pivot, no anchored offset, z on the host plane.
        RectTransform t = panel.Target;
        if (t == null)
            return;
        bool pivotOff = Mathf.Abs(t.pivot.x - 0.5f) > 0.001f || Mathf.Abs(t.pivot.y - 0.5f) > 0.001f;
        bool posOff = t.anchoredPosition.sqrMagnitude > 0.01f || Mathf.Abs(t.localPosition.z) > 0.001f;
        if (!pivotOff && !posOff)
            return;
        Vector2 hadPivot = t.pivot;
        Vector2 hadPos = t.anchoredPosition;
        t.pivot = new Vector2(0.5f, 0.5f);
        t.anchoredPosition = Vector2.zero;
        t.localPosition = new Vector3(0f, 0f, 0f);
        if (state.LoggedDrift)
            return;
        state.LoggedDrift = true;
        VRLog.Info(Scope, $"MAP ROOM hover card: the converted rect had DRIFTED off its host — pivot "
                              + $"({hadPivot.x:F2},{hadPivot.y:F2}) and anchoredPosition "
                              + $"({hadPos.x:F0},{hadPos.y:F0}) px, both written by the game's follow "
                              + "component after the conversion normalised them. Reset to the conversion's "
                              + "contract (centred pivot, zero offset). A non-zero pair here is the offset the "
                              + "player sees, measured.");
    }

    /// <summary>
    /// The union of everything the card actually DRAWS, expressed in host-local units, as the
    /// bottom-centre point plus the size. Host-local rather than world so the answer does not depend
    /// on where the host currently is — which is what this method is being asked to decide.
    /// </summary>
    private static bool TryMeasureLocal(ConvertedPanel panel, Transform host,
                                        out Vector3 bottomCentre, out Vector2 size)
    {
        bottomCentre = Vector3.zero;
        size = Vector2.zero;
        if (panel.HostGo == null || panel.HostRect == null)
            return false;

        // The fit's per-pass memos are keyed by Transform and must be cleared by whoever opens a
        // run of queries — transforms move between passes (CanvasConversion.BeginContentQuery).
        CanvasConversion.BeginContentQuery();

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int contributing = 0;

        GraphicScratch.Clear();
        panel.HostGo.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null || !CanvasConversion.CountsAsFitContent(panel, g))
                continue;
            RectTransform rt = g.rectTransform;
            if (rt == null)
                continue;
            rt.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = host.InverseTransformPoint(CornerScratch[c]);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            contributing++;
        }
        GraphicScratch.Clear();

        if (contributing == 0 || max.x - min.x < MinContentExtent || max.y - min.y < MinContentExtent)
            return false;
        size = new Vector2(max.x - min.x, max.y - min.y);
        bottomCentre = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
        return true;
    }

    private static CardState StateFor(GameObject host)
    {
        int id = host.GetInstanceID();
        if (!States.TryGetValue(id, out CardState? state) || state == null)
        {
            state = new CardState();
            States[id] = state;
            Prune();
        }
        state.LastSeen = Time.unscaledTime;
        return state;
    }

    /// <summary>Hover cards churn on every single mouseover, so the per-card state must not
    /// accumulate one entry per icon the player has ever pointed at. Entries untouched for a few
    /// seconds belong to a host that has been released and destroyed.</summary>
    private static void Prune()
    {
        if (States.Count <= 4)
            return;
        float now = Time.unscaledTime;
        PruneScratch.Clear();
        foreach (KeyValuePair<int, CardState> kv in States)
        {
            if (now - kv.Value.LastSeen > 5f)
                PruneScratch.Add(kv.Key);
        }
        for (int i = 0; i < PruneScratch.Count; i++)
            States.Remove(PruneScratch[i]);
        PruneScratch.Clear();
    }

    private static readonly List<int> PruneScratch = new(8);
}
