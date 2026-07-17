using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Enemy round reveal — the monster ability cards the game shows after every player
/// confirmed card selection ("what will the enemies do") — as a TRANSIENT world panel
/// floating above the diorama (test #20: the reveal was lost on the control board).
///
/// Mechanism (verified in decompiled GH.Runtime): Choreographer's
/// <c>MonsterClassesToSelectAbilityCards</c> handler calls
/// <c>InitiativeTrack.ShowMonsterClassesForSelectingRoundAbilityCards</c>
/// (Choreographer.cs:3715), which sets <c>enemyCardsBlocker.raycastTarget = true</c>
/// (InitiativeTrack.cs:747) and animates one <c>MonsterBaseUI</c> card per monster
/// class — all pooled under the serialized <c>enemyCardsHolder</c> Transform
/// (InitiativeTrack.cs:47; re-parented there by
/// <c>InitiativeTrackEnemyBehaviour.SetCardHolder</c>, :188-195) — via fade/scale/move
/// (MonsterBaseUI.AnimateAppearance, :239-271). The Continue ready button ends it:
/// <c>PostEnemyCardAnimationProceed</c> clears the blocker and <c>Deselect()</c>s every
/// card (<c>SetActive(false)</c>, InitiativeTrack.cs:519-524) before
/// <c>Choreographer.Pass()</c>.
///
/// Where it rendered before: <c>enemyCardsHolder</c> is a child of the InitiativeTrack
/// root, so the reveal rode the tray-docked <see cref="InitiativeTrackSurface"/> host —
/// unfitted (the track's content fit is scoped to <c>initiativeTrackHolder</c>, test
/// #16), positioned relative to the shrunk portrait row: effectively invisible in VR.
///
/// LEVEL-TRIGGERED on the game's own visibility state, polled per frame (no edge
/// events): reveal visible ⇔ <c>enemyCardsBlocker.raycastTarget</c> (flips exactly at
/// reveal start/end, the only two writes in the decompile) AND at least one active card
/// under <c>enemyCardsHolder</c> (the blocker rises a few frames before the staggered
/// card animations start). While visible, the holder subtree is converted onto its own
/// host (<see cref="CanvasConversion"/> — the REAL widgets, native art/text) floating
/// above the board center, upright, yawed toward the head at spawn, at the shared tray
/// density; on hide it is released back to its exact 2D home inside the track (whether
/// that home is currently the docked track host or the screen-space canvas — the
/// restore is parent-relative either way). Nested canvases inside the subtree are
/// handled entirely by CanvasConversion's neutralization + periodic sweep; this class
/// deliberately never touches Canvas components, so it is agnostic to how the
/// neutralization is implemented (disable vs override-off).
///
/// DISPLAY-ONLY: converted non-pokeable (never registered in UguiPokeSurfaces, so
/// neither the laser nor the fingertip nor the IsPointerOverUI patch ever see it), the
/// host raycaster is kept dark against the lock-mirror re-enable (StatPanelSurface
/// pattern), and no mode is asserted — the game's own reveal modality (the blocker +
/// the Continue ready button, mirrored on the button cluster) stays in charge.
/// </summary>
internal sealed class EnemyRevealSurface
{
    /// <summary>Panel center height above the board-center anchor, real meters.</summary>
    private const float HeightMeters = 0.55f;

    /// <summary>
    /// Per-panel multiplier on the shared tray density (test #17 pattern): 0.6 renders
    /// the same content pixels ~1.67x larger — the objectives' verified readability
    /// bump, sensible here too since the cards are read from board distance.
    /// </summary>
    private const float DensityScale = 0.6f;

    /// <summary>Hard width cap, real meters — many monster classes must not span the room.</summary>
    private const float MaxWidthMeters = 1.2f;

    private static readonly StringBuilder NameScratch = new(128);

    private ConvertedPanel? _panel;
    private Quaternion _spawnYaw = Quaternion.identity;
    private bool _lastVisible;

    public string Name => "EnemyReveal";

    public void Tick()
    {
        // Scene unload killed the target — the framework pruned the host already.
        if (_panel != null && !_panel.IsAlive)
            _panel = null;

        InitiativeTrack track = InitiativeTrack.Instance;
        bool visible = WorldUIConfig.EnemyReveal.Value && WorldUIConfig.ConversionActive
                       && Choreographer.s_Choreographer != null && RevealVisible(track);

        // Once per flip, with what is shown (the card names carry the round info).
        if (visible != _lastVisible)
        {
            _lastVisible = visible;
            VRLog.Info("WorldUI", visible
                ? $"ENEMY REVEAL shown: {DescribeCards(track)} — floating over the board center."
                : "ENEMY REVEAL hidden — cards restored to their 2D home in the initiative track.");
        }

        if (visible && _panel == null)
        {
            var holder = track.enemyCardsHolder as RectTransform;
            if (holder != null)
            {
                _panel = CanvasConversion.Convert(holder, Name, pokeable: false, fitContent: true);
                if (_panel != null)
                    CaptureSpawnYaw();
            }
        }
        else if (!visible && _panel != null)
        {
            CanvasConversion.Release(_panel);
            _panel = null;
        }

        if (_panel != null)
        {
            // The lock mirror in CanvasConversion.Tick may re-enable host raycasters
            // wholesale — keep this one dark so the vanilla EventSystem never hits it.
            if (_panel.HostRaycaster != null && _panel.HostRaycaster.enabled)
                _panel.HostRaycaster.enabled = false;
            Place();
        }
    }

    /// <summary>
    /// The game's own reveal state (see class doc for the decompiled evidence):
    /// blocker raised AND at least one card widget actually active under the holder
    /// (also keeps the panel down for the phase-gated avatar HOVER preview,
    /// InitiativeTrackEnemyBehaviour.OnAvatarHighlight — the blocker stays false there).
    /// </summary>
    private static bool RevealVisible(InitiativeTrack track)
    {
        if (track == null || track.enemyCardsHolder == null || track.enemyCardsBlocker == null)
            return false;
        if (!track.enemyCardsBlocker.raycastTarget)
            return false;
        Transform holder = track.enemyCardsHolder;
        for (int i = 0; i < holder.childCount; i++)
        {
            if (holder.GetChild(i).gameObject.activeSelf)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Card names for the flip log. The reveal animation is staggered
    /// (delayAnimationDraw per card), so the show-flip may list only the first
    /// card(s) already active — the count still attributes the round.
    /// </summary>
    private static string DescribeCards(InitiativeTrack track)
    {
        if (track == null)
            return "<no track>";
        NameScratch.Clear();
        int count = 0;
        for (int i = 0; i < track.enemiesUI.Count; i++)
        {
            InitiativeTrackEnemyBehaviour enemy = track.enemiesUI[i];
            if (enemy == null || enemy.monsterBaseUI == null
                || !enemy.monsterBaseUI.gameObject.activeSelf)
                continue;
            count++;
            if (NameScratch.Length > 0)
                NameScratch.Append(", ");
            NameScratch.Append(enemy.monsterAbilityCard != null
                ? enemy.monsterAbilityCard.Name
                : enemy.name);
        }
        return $"{count} monster card(s) [{NameScratch}]";
    }

    /// <summary>
    /// Yaw toward the head, captured ONCE at spawn (upright, yaw-only — no per-frame
    /// billboard: the reveal is a brief, stationary moment and a swiveling panel over
    /// the board reads as attached to the head). Falls back to the seat yaw.
    /// </summary>
    private void CaptureSpawnYaw()
    {
        _spawnYaw = Quaternion.identity;
        if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion seatYaw))
            return;
        _spawnYaw = seatYaw; // fallback: face the recentered seat
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        Vector3 pos = anchor + Vector3.up * (HeightMeters * PanelLayout.WorldScale);
        // Canvas front faces -forward: +Z away from the head makes it face the head.
        Vector3 facing = pos - head.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f)
            return;
        _spawnYaw = Quaternion.LookRotation(facing.normalized, Vector3.up);
    }

    /// <summary>
    /// Above the board center (the orbit focus the panels anchor to — live, so the
    /// panel follows a diorama move/rescale), at the SHARED tray density (test #16)
    /// with the objectives' readability multiplier, width-capped.
    /// </summary>
    private void Place()
    {
        if (_panel == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out _))
            return;

        float scale = PanelLayout.WorldScale;
        float metersPerPx = 1f / (PlayTray.TrayPixelsPerMeter * DensityScale);
        Rect rect = _panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width > 1f)
            metersPerPx = Mathf.Min(metersPerPx, MaxWidthMeters / rect.width);

        Transform host = _panel.HostTransform;
        host.SetPositionAndRotation(
            anchor + Vector3.up * (HeightMeters * scale), _spawnYaw);
        host.localScale = Vector3.one * (metersPerPx * scale);
    }

    public void Shutdown()
    {
        if (_panel != null)
        {
            CanvasConversion.Release(_panel);
            _panel = null;
        }
        _lastVisible = false;
    }
}
