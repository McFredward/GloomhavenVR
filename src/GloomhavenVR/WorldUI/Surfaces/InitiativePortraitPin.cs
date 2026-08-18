using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Pins every initiative portrait on the row's Y axis while the track is adopted into world space.
///
/// USER REPORT (hardware, 2026-08-08): "Beim Drücken auf ein Bild in der Initiativleiste um den
/// Character zu wechseln rücken die Bilder minimal nach oben und unten, aber merkbar. Ich will,
/// dass sie auf die Y-Achse fixiert sind und sie sich dort gar nicht bewegen, auch nicht beim
/// Anklicken." This SUPERSEDES the earlier ruling that the hover highlight had to stay visible
/// (<see cref="InitiativeTrackSurface"/>'s fit-hold block, "the user asked for a fixed PANEL, not a
/// dead one"): the portraits are now required to be geometrically dead.
///
/// ─────────────────────────────────────────────────────────────── WHAT MOVES A PORTRAIT, AND WHY ──
///
/// Read from source, <c>decompiled/GH.Runtime/ExtendedButton.cs</c>. Each entry's clickable
/// <c>avatarButton</c> is an <c>ExtendedButton</c>, and it has exactly THREE geometry writers, all
/// aimed at the same rect (<c>overridedTargetRectScale ?? TargetRect</c>):
///
/// <list type="number">
/// <item><b>Hover</b> — <c>OnPointerEnter</c> → <c>OnHighlight</c> → <c>ToggleHighlight</c>
///   (:429-478) <c>LeanTween.scale</c>s that rect to
///   <c>(highlightScaleFactor, highlightScaleFactor, 1)</c> and back to <c>Vector3.one</c> on exit,
///   over <c>animationDuration</c> with <c>easeOutExpo</c>.</item>
/// <item><b>Press down</b> — <c>OnPointerDown</c> (:215) writes
///   <c>((highlightScaleFactor + 1) / 2, …, 1)</c> IMMEDIATELY (no tween).</item>
/// <item><b>Press up</b> — <c>OnPointerUp</c> (:233) writes
///   <c>(highlightScaleFactor, …, 1)</c> IMMEDIATELY.</item>
/// </list>
/// plus a fourth, positional one: when <c>hoverMovement != Vector3.zero</c>, <c>ToggleHighlight</c>
/// (:471-478) offsets <c>TargetRect.anchoredPosition</c> by <c>hoverMovement.xy</c> on enter and
/// restores it on exit, latched through the private <c>isMoved</c> flag.
///
/// On the flat screen-space canvas those three scale writes are a pure "grow / dip / grow" pulse
/// around a centred pivot and read as button feedback. On the world-space panel the SAME writes are
/// the reported motion twice over:
/// <list type="bullet">
/// <item>the scaled rect's top and bottom edges move, so the portrait's visible Y extent changes —
///   directly, with no help from anything else;</item>
/// <item>and the scaled geometry is INSIDE the fit-measured union (this surface scopes the fit to
///   the game's own <c>initiativeTrackHolder</c>), so whenever the content fit happens to be armed
///   the union change re-sizes and re-centres the host rect and the whole docked row steps — which
///   is why the user says "die BILDER" (plural) move, not "das Bild". The click is precisely the
///   moment the fit used to be armed: <see cref="InitiativeTrackSurface.RowLayoutSignature"/>
///   folded in the selected actor, so switching character re-armed the fit for 2 s — exactly long
///   enough to cover the press-up scale, the pointer-exit tween AND a blinking focus/turn ring
///   (<c>Board.UiRing</c>, 5 % swell on a 156 px ring ⇒ ±7.8 px on a 178 px union, well past the
///   fit's 2 % dirty threshold). That second half is fixed in the surface; this class removes the
///   pointer half at its source.</item>
/// </list>
///
/// ─────────────────────────────────────────────────────────── THE FIX: THE CAUSE, NOT THE SYMPTOM ──
///
/// Every one of those writers computes its value from two serialized AMPLITUDE fields. While the
/// track is adopted this class sets both to their identity:
/// <c>highlightScaleFactor = 1</c> and <c>hoverMovement = Vector3.zero</c>. After that, without a
/// single patch, hook or per-frame fight:
/// <list type="bullet">
/// <item>hover in/out tweens the rect from <c>Vector3.one</c> to <c>Vector3.one</c>;</item>
/// <item>press-down writes <c>((1 + 1) / 2, …) = Vector3.one</c>;</item>
/// <item>press-up writes <c>(1, …) = Vector3.one</c>;</item>
/// <item>the <c>hoverMovement</c> branch is <c>if (hoverMovement != Vector3.zero &amp;&amp; …)</c>,
///   so it never executes and <c>anchoredPosition</c> is never written at all.</item>
/// </list>
/// i.e. the three surviving writes are all the rect's own REST value. Nothing is suppressed,
/// nothing is re-corrected a frame later, and no ordering assumption is made about LeanTween's
/// updater: the writers still run, they simply have nothing left to write.
///
/// Everything else about the button is untouched — <c>isHighlighted</c>, the sounds,
/// <c>HighlightFinished</c> (whose tooltip counter-scale <c>1 / highlightScaleFactor</c> becomes 1,
/// which is now the CORRECT counter-scale), <c>OnPointerClick</c> and therefore
/// <c>InitiativeTrackPlayerAvatar.OnClick</c> → <c>InitiativeTrack.Select</c>. The character switch
/// — the mod's focus seam — goes through the click path and is not on any code path this touches.
///
/// WHY NO WRITER CAN MOVE A PORTRAIT ANY MORE (the full set, each verified against source):
/// <list type="number">
/// <item><b>ExtendedButton hover / press-down / press-up</b> — all three write <c>Vector3.one</c>,
///   see above. Re-asserted each late frame anyway (<see cref="Reassert"/>) so a rect that was
///   mid-tween at the moment of adoption is snapped back once and stays.</item>
/// <item><b><c>hoverMovement</c></b> — the branch cannot run. Any offset vanilla had already
///   latched is subtracted once at adoption (<see cref="Neutralize"/> reads the private-but-
///   publicized <c>isMoved</c>).</item>
/// <item><b><c>Board.UiRing</c> (focus / turn rings, ModBuild 81)</b> — writes
///   <c>_image.transform.localScale</c> (Board/FocusCue.cs:202), and the ring is a LEAF CHILD of
///   the portrait rect (<c>UiRing.Build</c> parents it under the avatar's <c>RawImage</c>). A child
///   cannot move its parent, so the blink swell is not, and never was, a portrait mover. What it
///   COULD do is change the fit-measured union — handled by the surface's fit-hold, not here.</item>
/// <item><b><c>UIFX_MaterialFX_Control</c></b> (<c>InitiativeHover</c>/<c>InitiativeSelect</c>, the
///   avatar's own click/hover FX) — writes material properties only; the file contains no transform
///   write at all.</item>
/// <item><b>The row's <c>HorizontalLayoutGroup</c> and the mod's reorder slide</b> — both write the
///   ENTRY ROOT's position, never a descendant. This class refuses to pin any rect that is the
///   holder or one of its direct children (<see cref="IsRowFrame"/>), so the ModBuild-80 row-axis
///   guard in <see cref="InitiativeReorderSlide"/> keeps sole ownership of that transform and
///   cannot be argued with. The two guards are disjoint by construction, not by threshold.</item>
/// </list>
///
/// ──────────────────────────────────────────────────────────────────────── THE INTEGRITY WATCH ──
///
/// Removing a cause is a claim; <see cref="WatchY"/> is the evidence. Every settled late frame each
/// portrait's world corners are projected into the ROW HOLDER's local space (the same space, and
/// therefore the same units, as the row-axis guard's numbers) and the resulting min/max Y BAND is
/// compared with the band latched while the row was settled. The band — not the centre — is
/// measured on purpose: a scale write moves the edges without moving the centre, so a centre-only
/// check would have been blind to exactly the writer this class exists to remove.
/// <see cref="EpsilonPixels"/> is 1.5 px, an order of magnitude under the row guard's deliberately
/// coarse 16 px, which is why that guard could never have caught this. The log is change-gated on
/// the quantised deviation set and rate-limited, so a healthy session prints nothing.
///
/// SCOPE: only ever runs while <see cref="InitiativeTrackSurface"/> holds a converted panel; the
/// vanilla amplitudes are handed back on release / shutdown, so flat and [Dev] play are 100 %
/// vanilla. MULTIPLAYER: every write lands on this client's own UI fields — nothing on the wire, no
/// rules state, no game-side callback suppressed. <see cref="Net.RemoteInitiativeTrack"/> reads the
/// PEER-facing hover grow through <see cref="VanillaHighlightScale"/>/
/// <see cref="VanillaHoverMovement"/> rather than off the live (now neutralised) fields, so a
/// peer's mirrored track still grows the portrait ITS owner points at, exactly as before.
/// </summary>
internal sealed class InitiativePortraitPin
{
    /// <summary>
    /// Y deviation (holder-local uGUI px) that counts as movement. "Minimal aber merkbar" on
    /// hardware was the press pulse on a ~130 px portrait — a few px. Deliberately far under the
    /// row-axis guard's 16 px so this catches what that guard is built to ignore.
    /// </summary>
    private const float EpsilonPixels = 1.5f;

    /// <summary>
    /// After a reorder / a row rebuild the layout group re-lays the row out and a portrait's
    /// legitimate band may genuinely change; the watch neither compares nor latches for this long
    /// afterwards, then re-latches whatever the layout settled on.
    /// </summary>
    private const float SettleSeconds = 1f;

    /// <summary>At most one deviation report per this many seconds (this is per-frame code).</summary>
    private const float LogIntervalSeconds = 5f;

    /// <summary>How often a row with un-resolved portraits (async avatar art) is re-scanned.</summary>
    private const float CoverageRetrySeconds = 1f;

    /// <summary>Deviating nodes named in one report before it degrades to a count.</summary>
    private const int NamedInReport = 3;

    /// <summary>Buttons described in the one-shot arm line before it degrades to a count.</summary>
    private const int NamedInArmLine = 3;

    /// <summary>One button's VANILLA amplitudes, latched before they are zeroed.</summary>
    private struct Vanilla
    {
        public float Highlight;
        public Vector3 HoverMove;
    }

    /// <summary>One neutralised button and the rect its three scale writers aim at.</summary>
    private struct Pinned
    {
        public ExtendedButton Button;
        public RectTransform ScaleRect;
    }

    /// <summary>One watched portrait and its latched settled Y band, in holder-local px.</summary>
    private struct Watched
    {
        public RectTransform Rect;
        public string Node;
        public float RestMin;
        public float RestMax;
        public bool HasRest;
    }

    /// <summary>
    /// The vanilla amplitudes of every button this mod has ever neutralised, keyed by the button.
    /// STATIC because <see cref="Net.RemoteInitiativeTrack"/> has to read a peer-facing grow factor
    /// that no longer exists on the live component, and because the track's entries are POOLED: a
    /// button pooled out and back in must not have its already-neutralised value re-latched as
    /// "vanilla". Only Unity-destroyed keys are pruned (<see cref="PruneDestroyed"/>), so the map is
    /// bounded by the number of row entries the pool ever hands out.
    /// </summary>
    private static readonly Dictionary<ExtendedButton, Vanilla> Vanillas = new(32);

    private readonly List<Pinned> _pins = new(16);
    private readonly List<Watched> _watch = new(16);

    /// <summary>Active row entries the caches were built against (reference compare, no alloc).</summary>
    private readonly List<Transform> _entries = new(16);

    /// <summary>Reused corner buffer — <c>GetWorldCorners</c> fills, never allocates.</summary>
    private readonly Vector3[] _corners = new Vector3[4];

    /// <summary>Indices into <see cref="_watch"/> that deviate this frame, and by how much.</summary>
    private readonly List<int> _devIndex = new(16);
    private readonly List<float> _devAmount = new(16);

    /// <summary>Scratch for the destroyed-key prune (mutating a dictionary while enumerating).</summary>
    private static readonly List<ExtendedButton> DeadKeys = new(16);

    /// <summary><see cref="Time.unscaledTime"/> before which the watch neither compares nor latches.</summary>
    private float _settleUntil;

    /// <summary>Rate limit for the deviation report.</summary>
    private float _nextLog;

    /// <summary>Quantised signature of the last reported deviation set (0 = row is clean).</summary>
    private int _loggedDeviation;

    /// <summary>True while at least one button is neutralised (drives the release log).</summary>
    private bool _armed;

    /// <summary>Active row entries carrying an <c>InitiativeTrackActorBehaviour</c> at the last
    /// rebuild. When <see cref="_watch"/> is short of this, an avatar had not been pooled in (or its
    /// portrait art had not arrived) yet and the rebuild is retried — the row's CHILD SET does not
    /// change when that happens, so <see cref="RowChanged"/> alone would never notice.</summary>
    private int _entryActors;

    /// <summary>Rate limit for the incomplete-coverage rebuild retry.</summary>
    private float _nextCoverageRetry;

    /// <summary>
    /// Per-frame from <see cref="InitiativeTrackSurface.LateTick"/>, AFTER the reorder slide: the
    /// slide owns the entry roots and this class measures what it left behind.
    /// <paramref name="rowBusy"/> is the surface's reorder hold OR'd with the mod-owned slide — the
    /// one state in which a portrait's band is allowed to change.
    /// </summary>
    public void Tick(InitiativeTrack? track, bool rowBusy)
    {
        Transform? holder = track != null ? track.initiativeTrackHolder : null;
        if (track == null || holder == null)
        {
            Forget();
            return;
        }

        float now = Time.unscaledTime;
        bool coverageShort = _watch.Count < _entryActors && now >= _nextCoverageRetry;
        if (RowChanged(holder) || coverageShort)
        {
            if (coverageShort)
                _nextCoverageRetry = now + CoverageRetrySeconds;
            Rebuild(holder);
            _settleUntil = now + SettleSeconds;
        }

        int reasserted = Reassert();

        if (rowBusy)
        {
            _settleUntil = Time.unscaledTime + SettleSeconds;
            return;
        }
        WatchY(holder, reasserted);
    }

    /// <summary>
    /// Hand every neutralised button its authored amplitudes back. Called when the panel is
    /// released and on surface shutdown — while the track objects are still alive, so the restore
    /// is a real write and not a no-op on a dead reference. Full-restore contract: the 2D track
    /// gets its own hover/press feedback back the moment the canvas returns to the game.
    /// </summary>
    public void Release(string why)
    {
        int restored = 0;
        for (int i = 0; i < _pins.Count; i++)
        {
            ExtendedButton btn = _pins[i].Button;
            if (btn == null || !Vanillas.TryGetValue(btn, out Vanilla v))
                continue;
            btn.highlightScaleFactor = v.Highlight;
            btn.hoverMovement = v.HoverMove;
            restored++;
        }
        if (_armed && restored > 0)
        {
            VRLog.Info("WorldUI", $"INITIATIVE PORTRAIT PIN released ({why}): {restored} " +
                                  "ExtendedButton(s) handed their authored highlightScaleFactor / " +
                                  "hoverMovement back — the flat track's hover grow and press dip " +
                                  "are vanilla again.");
        }
        Forget();
    }

    /// <summary>Drop the caches without touching the game (track gone / rebuilt under us).</summary>
    private void Forget()
    {
        _pins.Clear();
        _watch.Clear();
        _entries.Clear();
        _entryActors = 0;
        _armed = false;
        _loggedDeviation = 0;
    }

    // ------------------------------------------------------------------ vanilla amplitude reads --

    /// <summary>
    /// The button's authored <c>highlightScaleFactor</c> — the value vanilla would use if this
    /// class had never touched it. <see cref="Net.RemoteInitiativeTrack"/> re-decides a PEER's
    /// hover grow on its mirrored clone from this, so neutralising the LOCAL track (which the peer
    /// never sees) cannot silently delete the remote cue.
    /// </summary>
    internal static float VanillaHighlightScale(ExtendedButton? button)
    {
        if (button == null)
            return 1f;
        return Vanillas.TryGetValue(button, out Vanilla v) ? v.Highlight : button.highlightScaleFactor;
    }

    /// <summary>The button's authored <c>hoverMovement</c> — see <see cref="VanillaHighlightScale"/>.</summary>
    internal static Vector3 VanillaHoverMovement(ExtendedButton? button)
    {
        if (button == null)
            return Vector3.zero;
        return Vanillas.TryGetValue(button, out Vanilla v) ? v.HoverMove : button.hoverMovement;
    }

    // ------------------------------------------------------------------------------- cache build --

    /// <summary>True when the holder's ACTIVE direct children differ from what the caches were
    /// built against — the only event that can introduce an un-neutralised button or retire a
    /// watched portrait. Reference compare over ≤ a dozen children; no allocation.</summary>
    private bool RowChanged(Transform holder)
    {
        int seen = 0;
        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            if (seen >= _entries.Count || !ReferenceEquals(_entries[seen], child))
                return true;
            seen++;
        }
        return seen != _entries.Count;
    }

    /// <summary>
    /// Re-resolve the pins and the watch list against the live row, neutralising every button that
    /// has not been seen before.
    ///
    /// The sweep is the whole ENTRY SUBTREE, not just <c>avatarButton</c>: any
    /// <c>ExtendedButton</c> under a row entry scales a rect that is an ancestor or a sibling of
    /// the portrait, which is both a direct Y mover and a change in the fit-measured union. The
    /// user's requirement is that the row is geometrically dead, so the sweep matches it.
    /// Inactive components are included — a button pooled in disabled must arrive neutralised.
    /// </summary>
    private void Rebuild(Transform holder)
    {
        _pins.Clear();
        _watch.Clear();
        _entries.Clear();
        _entryActors = 0;
        PruneDestroyed();

        var fresh = new StringBuilder(160);
        int freshCount = 0;

        for (int i = 0; i < holder.childCount; i++)
        {
            Transform entry = holder.GetChild(i);
            if (!entry.gameObject.activeSelf)
                continue;
            _entries.Add(entry);

            ExtendedButton[] buttons = entry.GetComponentsInChildren<ExtendedButton>(includeInactive: true);
            for (int b = 0; b < buttons.Length; b++)
            {
                ExtendedButton btn = buttons[b];
                if (btn == null)
                    continue;
                if (Neutralize(btn, holder, out string note) && freshCount < NamedInArmLine)
                {
                    if (freshCount > 0)
                        fresh.Append("; ");
                    fresh.Append(note);
                }
                if (note.Length > 0)
                    freshCount++;
            }

            // The visible portrait, resolved the same way the local rings and the remote mirror
            // resolve it (the avatar's only RawImage) so all three surfaces frame the same rect.
            // Read off the entry itself rather than off actorsUI: that list is re-sorted mid
            // reorder, while the holder's children are the row as it actually stands.
            var beh = entry.GetComponent<InitiativeTrackActorBehaviour>();
            if (beh == null)
                continue; // not a row entry (hotkey tips and friends) — nothing to watch
            _entryActors++;
            RectTransform? portrait = PortraitRect(beh);
            if (portrait != null)
                _watch.Add(new Watched { Rect = portrait, Node = DescribeNode(portrait, entry) });
        }

        _armed = _pins.Count > 0;
        if (freshCount == 0)
            return;

        VRLog.Info("WorldUI",
            $"INITIATIVE PORTRAIT PIN armed: {freshCount} newly seen ExtendedButton(s) over " +
            $"{_entries.Count} row entr(y/ies) ({_pins.Count} pinned, {_watch.Count} portrait(s) " +
            $"watched) — {fresh}" + (freshCount > NamedInArmLine ? ", …" : "") + ". " +
            "highlightScaleFactor→1 and hoverMovement→0 make vanilla's own three geometry writers " +
            "IDENTITY: ToggleHighlight tweens Vector3.one→Vector3.one, OnPointerDown writes " +
            "((1+1)/2)=1, OnPointerUp writes 1, and the hoverMovement branch (guarded by " +
            "'hoverMovement != Vector3.zero') never runs. The writers still fire — they have " +
            "nothing left to write. Click/OnPointerClick → InitiativeTrack.Select is untouched, so " +
            "the character switch still works; the authored values are handed back on release.");
    }

    /// <summary>
    /// Zero one button's amplitudes and undo anything vanilla had already latched. Returns true
    /// when this button had not been seen before (i.e. <paramref name="note"/> describes a real
    /// change); <paramref name="note"/> is empty when the button could not be pinned at all.
    /// </summary>
    private bool Neutralize(ExtendedButton button, Transform holder, out string note)
    {
        note = string.Empty;
        RectTransform? scaleRect = button.overridedTargetRectScale != null
            ? button.overridedTargetRectScale
            : button.TargetRect;
        if (scaleRect == null)
            return false;

        bool first = !Vanillas.TryGetValue(button, out Vanilla vanilla);
        if (first)
        {
            vanilla = new Vanilla { Highlight = button.highlightScaleFactor, HoverMove = button.hoverMovement };
            Vanillas[button] = vanilla;
            note = $"'{button.name}' highlightScaleFactor {vanilla.Highlight:F3}→1" +
                   (vanilla.HoverMove != Vector3.zero
                       ? $", hoverMovement ({vanilla.HoverMove.x:F1},{vanilla.HoverMove.y:F1})→0"
                       : "") +
                   $", scale rect '{scaleRect.name}'" +
                   (button.overridedTargetRectScale != null ? " (overridedTargetRectScale)" : " (TargetRect)");
        }

        // Undo a latched hoverMovement BEFORE the amplitude goes away: once hoverMovement is zero
        // vanilla's own restore branch can never run, so an offset applied a moment earlier would
        // become permanent. isMoved is the game's own latch (private, publicized).
        RectTransform? moveRect = button.TargetRect;
        if (button.isMoved && vanilla.HoverMove != Vector3.zero && moveRect != null && !IsRowFrame(moveRect, holder))
        {
            moveRect.anchoredPosition -= new Vector2(vanilla.HoverMove.x, vanilla.HoverMove.y);
            button.isMoved = false;
        }

        button.highlightScaleFactor = 1f;
        button.hoverMovement = Vector3.zero;

        // A tween created before this frame still carries the OLD target; cancelling it is exactly
        // what OnPointerDown/ToggleHighlight do with these handles, so it is a vanilla-reachable
        // state. Then the rect starts from its documented rest value.
        if (LeanTween.isTweening(scaleRect))
            LeanTween.cancel(scaleRect, "GloomhavenVR.InitiativePortraitPin");
        if (scaleRect.localScale != Vector3.one)
            scaleRect.localScale = Vector3.one;

        _pins.Add(new Pinned { Button = button, ScaleRect = scaleRect });
        return first;
    }

    /// <summary>
    /// True when <paramref name="rect"/> is the row holder or one of its direct children (an entry
    /// root). Those transforms belong to the game's <c>HorizontalLayoutGroup</c> and to
    /// <see cref="InitiativeReorderSlide"/>'s row-axis guard; this class never writes a position on
    /// one, which is what keeps the ModBuild-80 fix structurally un-regressable rather than
    /// merely un-triggered.
    /// </summary>
    private static bool IsRowFrame(Transform rect, Transform holder)
        => ReferenceEquals(rect, holder) || ReferenceEquals(rect.parent, holder);

    /// <summary>Drop Unity-destroyed keys from the static amplitude map. Pooled-but-alive buttons
    /// are deliberately KEPT: re-latching an already-neutralised button would record 1 as its
    /// "vanilla" factor and quietly delete both the restore and the peer-facing remote grow.</summary>
    private static void PruneDestroyed()
    {
        DeadKeys.Clear();
        foreach (KeyValuePair<ExtendedButton, Vanilla> kv in Vanillas)
        {
            // Unity's overloaded == : true for a DESTROYED object whose managed reference is still
            // a perfectly good dictionary key, which is exactly the case being collected here.
            if (kv.Key == null)
                DeadKeys.Add(kv.Key!);
        }
        for (int i = 0; i < DeadKeys.Count; i++)
            Vanillas.Remove(DeadKeys[i]);
        DeadKeys.Clear();
    }

    /// <summary>The VISIBLE portrait rect of a live entry — the avatar face, which is the avatar's
    /// only <c>RawImage</c> (every other graphic under it is a TMP or an Image). Identical
    /// resolution to <c>Board.FocusDriver</c>'s rings and to the remote mirror's, so all three
    /// surfaces are talking about the same rect.</summary>
    private static RectTransform? PortraitRect(InitiativeTrackActorBehaviour entry)
    {
        InitiativeTrackActorAvatar avatar = entry.Avatar;
        if (avatar == null)
            return null;
        RawImage[] raws = avatar.GetComponentsInChildren<RawImage>(includeInactive: false);
        for (int i = 0; i < raws.Length; i++)
        {
            if (raws[i] != null && raws[i].transform is RectTransform rt)
                return rt;
        }
        return null;
    }

    /// <summary>"Avatar' (entry 'InitiativeTrackPlayer_new')" — the report has to name a node the
    /// next hardware log can be grepped for, in the same shape the ring lines already use.</summary>
    private static string DescribeNode(Transform node, Transform entry)
        => $"'{node.name}' (entry '{entry.name}')";

    // ------------------------------------------------------------------------- per-frame assert --

    /// <summary>
    /// Belt to the braces: put any pinned scale rect back on <c>Vector3.one</c>. With the
    /// amplitudes zeroed nothing should ever be found off it — this exists for the ONE frame in
    /// which a button was adopted mid-hover, and as the number the deviation report quotes when it
    /// has to say whether the pointer was involved.
    /// </summary>
    private int Reassert()
    {
        int fixedUp = 0;
        for (int i = 0; i < _pins.Count; i++)
        {
            RectTransform rect = _pins[i].ScaleRect;
            if (rect == null || rect.localScale == Vector3.one)
                continue;
            rect.localScale = Vector3.one;
            fixedUp++;
        }
        return fixedUp;
    }

    // ------------------------------------------------------------------------- Y integrity watch --

    /// <summary>
    /// Latch each portrait's settled Y band once, then report any frame in which it moves by more
    /// than <see cref="EpsilonPixels"/>. Measured as the min/max of the rect's four world corners
    /// projected into the ROW HOLDER's local space: same space and same units as the row-axis
    /// guard's numbers, and — unlike a centre-only probe — sensitive to a pure SCALE write, which
    /// is the writer family this class removes.
    /// </summary>
    private void WatchY(Transform holder, int reasserted)
    {
        bool settled = Time.unscaledTime >= _settleUntil;
        if (!settled)
            return;

        _devIndex.Clear();
        _devAmount.Clear();

        for (int i = 0; i < _watch.Count; i++)
        {
            Watched w = _watch[i];
            if (w.Rect == null || !w.Rect.gameObject.activeInHierarchy)
                continue;
            if (!Band(holder, w.Rect, out float min, out float max))
                continue; // degenerate rect — not laid out yet, nothing to compare

            if (!w.HasRest)
            {
                w.RestMin = min;
                w.RestMax = max;
                w.HasRest = true;
                _watch[i] = w;
                continue;
            }

            float dev = Mathf.Max(Mathf.Abs(min - w.RestMin), Mathf.Abs(max - w.RestMax));
            if (dev <= EpsilonPixels)
                continue;
            _devIndex.Add(i);
            _devAmount.Add(dev);
        }

        if (_devIndex.Count == 0)
        {
            _loggedDeviation = 0; // clean row — the next real deviation reports immediately
            return;
        }

        // Change gate: the quantised deviation SET. A steady offset reports once; a growing or
        // wandering one reports again when it crosses another epsilon step.
        int key = 17;
        for (int i = 0; i < _devIndex.Count; i++)
            key = unchecked(key * 31 + _devIndex[i] * 7 + Mathf.RoundToInt(_devAmount[i] / EpsilonPixels));
        if (key == _loggedDeviation || Time.unscaledTime < _nextLog)
            return;
        _loggedDeviation = key;
        _nextLog = Time.unscaledTime + LogIntervalSeconds;

        var sb = new StringBuilder(200);
        int named = Mathf.Min(_devIndex.Count, NamedInReport);
        for (int i = 0; i < named; i++)
        {
            if (i > 0)
                sb.Append("; ");
            Watched w = _watch[_devIndex[i]];
            sb.Append(w.Node).Append(" by ").Append(_devAmount[i].ToString("F2"))
              .Append(" px (band ").Append(w.RestMin.ToString("F1")).Append("..")
              .Append(w.RestMax.ToString("F1")).Append(" px latched)");
        }
        if (_devIndex.Count > named)
            sb.Append(" (+").Append(_devIndex.Count - named).Append(" more)");

        VRLog.Info("WorldUI",
            $"INITIATIVE PORTRAIT Y DEVIATION ({_devIndex.Count}/{_watch.Count} portrait(s) past " +
            $"the {EpsilonPixels:F1} px pin): {sb}. Measured as the rect's world-corner Y band in " +
            "the row holder's local space, against the band latched while the row was settled — so " +
            "a pure scale write shows up as well as a move. " +
            (reasserted > 0
                ? $"{reasserted} pinned ExtendedButton scale rect(s) were off Vector3.one this " +
                  "frame and were put back: the POINTER is involved — a button was adopted " +
                  "mid-hover, or a new one was pooled in un-neutralised."
                : "No pinned scale rect was off Vector3.one this frame, so this is NOT the " +
                  "ExtendedButton hover/press family — look at the entry root (the row-axis guard's " +
                  "own log line) or at a graphic that changed size under the avatar."));
    }

    /// <summary>The rect's Y extent in <paramref name="holder"/>-local uGUI px. False for a
    /// degenerate (not yet laid out) rect, which must not be latched as a rest pose.</summary>
    private bool Band(Transform holder, RectTransform rect, out float min, out float max)
    {
        rect.GetWorldCorners(_corners);
        min = float.MaxValue;
        max = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float y = holder.InverseTransformPoint(_corners[i]).y;
            if (y < min)
                min = y;
            if (y > max)
                max = y;
        }
        return max - min > Mathf.Epsilon;
    }
}
