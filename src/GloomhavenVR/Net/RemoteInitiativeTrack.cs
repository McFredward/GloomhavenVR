using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Initiative TRACK — GLOBAL, mirrored from the game's own widget
// =================================================================================================

/// <summary>
/// The scenario's INITIATIVE TRACK, drawn above a peer's board — the mirror of the local board's
/// docked <c>InitiativeTrack</c> canvas (<c>PlayTray.InitiativeMount</c> /
/// <c>InitiativeTrackSurface</c>).
///
/// ─── DEFECT (a) OF THE 1:1-PARITY ROUND, AND WHAT IT ACTUALLY WAS ──────────────────────────────
/// The hardware screenshot showed this track as a row of plain GREEN and RED rectangles. That was
/// not a failure to find the data — the log said <c>track=8 entr(y/ies)</c>, so the entries were
/// resolved correctly all along — it was the RENDERING: this class drew its own tinted plate + name
/// + number per actor, and explicitly declared the portrait out of scope ("NOT REPRODUCED — the
/// PORTRAIT … reaching that per-actor texture from the mod is neither cheap nor cheat-relevant").
///
/// That premise was wrong, and in the same way <see cref="RemoteAbilityCardSource"/> proved the
/// "full card art only exists for the local hand" premise wrong. The portrait does not have to be
/// reached at all: <c>CharacterPortraitsProvider</c> has ALREADY assigned it to the live track's
/// <c>RawImage</c> on this client, and <c>Object.Instantiate</c> copies live component state. So
/// cloning the game's own track brings the portraits, the class colours, the initiative discs, the
/// selection frame, the hover state and the reorder animation across for free.
///
/// ─── WHAT IT DRAWS NOW ─────────────────────────────────────────────────────────────────────────
/// PRIMARY — <see cref="RemoteWidgetMirror"/> over <c>InitiativeTrack.Instance.transform</c>: the
/// REAL widget, cloned once and puppeteered per frame from the original (see that class for why the
/// clone runs none of the game's code and can never be interacted with). This is the same single
/// track instance the local board docks, so it is by construction the same ordering, the same
/// numbers and the same "?"s the owner sees — including vanilla's own online gate
/// (<c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" while
/// <c>FFSNetwork.IsOnline &amp;&amp; phase == SelectAbilityCardsOrLongRest &amp;&amp;
/// !actor.IsUnderMyControl</c>). No mod-side gate is needed or wanted: the pixels being copied are
/// pixels this client is already displaying.
///
/// FALLBACK — the previous mod-drawn chip strip, kept verbatim for the frames where
/// <c>InitiativeTrack.Instance</c> does not exist (menu, mid-load, a scenario tearing down). It
/// reads the same entry list in the same on-screen order (ascending sibling index under the track
/// holder, which is where vanilla's <c>UpdateSortingOrder</c> writes the display order) and falls
/// back again to <c>ScenarioManager.Scenario.AllAliveActors</c> + a label sort when even that is
/// gone. Which of the two is live is stated in the <c>Remote board content</c> log line, so a
/// hardware log PROVES which one the user is looking at.
///
/// SEAT — <see cref="RemoteBoardLayout.InitiativeMount"/>, i.e. the authored per-board
/// <c>InitiativeOffset</c> the owner's own <c>PlayTray.BuildMounts</c> assigns to its initiative
/// mount, keyed by that peer's synced style. The old remote-only constant (y 0.165, the flat-board
/// estimate) is gone: on the Steel board the owner's track sits at (0, 0.200, −0.070), which is
/// 9 mm up and 16 mm proud of where this used to draw it — part of defect (c).
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — ZERO wire. The mirrored widget is a scenario-wide singleton the
/// local client already renders; the fallback's actor LIST is
/// <c>ScenarioManager.Scenario.AllAliveActors</c> and its per-actor NUMBERS are PER-ACTOR MODEL
/// gated by <see cref="RevealGate"/> exactly as vanilla gates its own. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteInitiativeTrack
{
    /// <summary>Track width budget — <c>PlayTray.InitiativeMountWidth</c> (= the board width), the
    /// same budget the owner's own docked track is fitted into.</summary>
    private const float Width = PlayTray.InitiativeMountWidth;

    private const float ChipH = 0.052f;
    private const float MaxChipW = 0.082f;
    private const int MaxChips = 8;

    private readonly Transform _root;
    private readonly Transform _fallbackRoot;
    private readonly RemoteWidgetMirror _mirror;
    private readonly Chip[] _chips = new Chip[MaxChips];
    private readonly List<CActor> _entries = new(MaxChips);
    private readonly List<CClass> _seen = new(MaxChips);
    private readonly List<InitiativeTrackActorBehaviour> _gameEntries = new(16);

    private string _signature = string.Empty;

    /// <summary>How many entries the track currently represents (diagnostics). With the mirror live
    /// this is the game track's own entry count; on the fallback it is the chip count.</summary>
    public int Count { get; private set; }

    /// <summary>Which mechanism is drawing the track right now (diagnostics — see
    /// <see cref="RemoteWidgetMirror.Fidelity"/>).</summary>
    public RemoteWidgetMirror.Fidelity Source { get; private set; } = RemoteWidgetMirror.Fidelity.None;

    /// <summary>Why the real widget is not being mirrored, for the diagnostic line (empty when it is).</summary>
    public string Reason => _mirror.Reason;

    /// <summary>The RESOLVED seat of this dock for the per-peer board log: the board-local mount
    /// position the authored layout put it at, and which measure sized the mirrored panel that
    /// grows up from it (the two numbers a "sits far too high" report is decided by).</summary>
    public string SeatLine => $"mount={_root.localPosition:F3} via {_mirror.MeasurePath}";

    public RemoteInitiativeTrack(Transform boardRoot, in RemoteBoardLayout layout)
    {
        _root = new GameObject("InitiativeTrack").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        // The mount convention (PlayTray: bottom-centre of the initiative panel, grows UP above the
        // board's top edge) is reproduced verbatim — the mirror grows up from here, and so do the
        // fallback chips.
        _root.localPosition = layout.InitiativeMount;

        _mirror = new RemoteWidgetMirror("InitiativeTrack", _root,
            PlayTray.InitiativeMountWidth, PlayTray.InitiativeMountMaxHeight, Vector2.up);

        _fallbackRoot = new GameObject("Fallback").transform;
        _fallbackRoot.SetParent(_root, worldPositionStays: false);
        _fallbackRoot.localPosition = new Vector3(0f, ChipH * 0.5f, 0f);
        for (int i = 0; i < MaxChips; i++)
            _chips[i] = new Chip(_fallbackRoot);
        _fallbackRoot.gameObject.SetActive(false);

        _root.gameObject.SetActive(false);
    }

    /// <summary>Per-FRAME: keep the mirrored widget in step with the original, so the track's
    /// reorder slide and selection pop play out on a peer's board instead of stepping at the 4 Hz
    /// content cadence. No-op while the fallback is live.</summary>
    public void TickLive() => _mirror.TickLive();

    /// <summary>Content-cadence refresh: mirror the real widget when it exists, else repaint the
    /// fallback chips on an actual change. Wrapped whole — a half-initialised scenario must degrade
    /// to an empty track, never throw.</summary>
    public void Refresh()
    {
        InitiativeTrack? track = null;
        try { track = InitiativeTrack.Instance; }
        catch { track = null; }

        if (_mirror.Refresh(track != null ? track.transform : null))
        {
            Source = RemoteWidgetMirror.Fidelity.MirroredWidget;
            _mirror.SetShown(true);
            if (_fallbackRoot.gameObject.activeSelf)
                _fallbackRoot.gameObject.SetActive(false);
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            Count = CountGameEntries(track);
            _signature = string.Empty; // a later fallback must repaint from scratch
            return;
        }

        Source = RemoteWidgetMirror.Fidelity.ModDrawn;
        _mirror.SetShown(false);
        RefreshFallback();
    }

    public void Destroy() => _mirror.Destroy();

    /// <summary>Entry count of the LIVE game track (the number the mirrored picture is showing) —
    /// diagnostics only, and null-safe for the frames where the track is mid-rebuild.</summary>
    private static int CountGameEntries(InitiativeTrack? track)
    {
        try
        {
            List<InitiativeTrackActorBehaviour>? ui = track != null ? track.actorsUI : null;
            if (ui == null)
                return 0;
            int n = 0;
            for (int i = 0; i < ui.Count; i++)
                if (ui[i] != null && ui[i].gameObject.activeSelf)
                    n++;
            return n;
        }
        catch { return 0; }
    }

    // ------------------------------------------------------------------ fallback --

    /// <summary>The pre-mirror chip strip, kept for the frames where the game track does not exist.
    /// Re-reads the entries + initiatives and repaints on an actual change.</summary>
    private void RefreshFallback()
    {
        _entries.Clear();
        _seen.Clear();
        try
        {
            // The game track's own entries in their LIVE display order when it exists but could not
            // be mirrored; the model derivation when it does not.
            if (!CollectFromGameTrack())
            {
                Collect();
                _entries.Sort(static (a, b) => SortKey(a).CompareTo(SortKey(b)));
            }
        }
        catch { _entries.Clear(); }

        var sb = new StringBuilder(96);
        for (int i = 0; i < _entries.Count; i++)
            sb.Append(Text(_entries[i])).Append(':').Append(InitiativeLabel(_entries[i])).Append(';');
        string sig = sb.ToString();
        if (sig == _signature)
            return;
        _signature = sig;

        Count = _entries.Count;
        bool any = Count > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);
        if (_fallbackRoot.gameObject.activeSelf != any)
            _fallbackRoot.gameObject.SetActive(any);

        float chipW = Mathf.Min(MaxChipW, Count > 0 ? Width / Count : MaxChipW);
        float left = -(Count - 1) * 0.5f * chipW;
        for (int i = 0; i < MaxChips; i++)
        {
            if (i >= Count)
            {
                _chips[i].SetShown(false);
                continue;
            }
            CActor a = _entries[i];
            _chips[i].Set(new Vector3(left + i * chipW, 0f, 0f), chipW, Text(a), InitiativeLabel(a),
                a is CPlayerActor);
        }
    }

    /// <summary>
    /// Read <c>InitiativeTrack.Instance.actorsUI</c> — the exact entries the shared 2D track
    /// shows — in their ON-SCREEN order (ascending sibling index; vanilla's sort writes the
    /// display order into the sibling order). Returns false when the game track is unavailable
    /// or empty so the caller can fall back to the model derivation.
    /// </summary>
    private bool CollectFromGameTrack()
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
            return false;
        List<InitiativeTrackActorBehaviour> ui = track.actorsUI;
        if (ui == null || ui.Count == 0)
            return false;

        _gameEntries.Clear();
        for (int i = 0; i < ui.Count; i++)
        {
            InitiativeTrackActorBehaviour beh = ui[i];
            if (beh == null || !beh.gameObject.activeSelf || beh.Actor == null)
                continue;
            _gameEntries.Add(beh);
        }
        if (_gameEntries.Count == 0)
            return false;

        // Display order = sibling order (left → right = acting order on the shared track).
        _gameEntries.Sort(static (a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        for (int i = 0; i < _gameEntries.Count && _entries.Count < MaxChips; i++)
            _entries.Add(_gameEntries[i].Actor);
        return true;
    }

    /// <summary>FALLBACK: filter + dedupe like <c>InitiativeTrack.UpdateInitiativeTrack</c> (alive
    /// actors, one entry per <c>CClass</c>, no hero summons and no prop/object actors) — used only
    /// while the game track itself does not exist.</summary>
    private void Collect()
    {
        CScenario? scenario = ScenarioManager.Scenario;
        List<CActor>? actors = scenario?.AllAliveActors;
        if (actors == null)
            return;
        for (int i = 0; i < actors.Count && _entries.Count < MaxChips; i++)
        {
            CActor a = actors[i];
            if (a == null || a is CHeroSummonActor || a is CObjectActor)
                continue;
            CClass cls = a.Class;
            if (cls != null)
            {
                if (_seen.Contains(cls))
                    continue;
                _seen.Add(cls);
            }
            _entries.Add(a);
        }
    }

    /// <summary>Sort key: the actor's initiative, or a value past every real initiative when it is
    /// not knowable (gated player / monster card not yet revealed).</summary>
    private static int SortKey(CActor a)
    {
        string label = InitiativeLabel(a);
        return int.TryParse(label, out int v) ? v : int.MaxValue;
    }

    /// <summary>Vanilla's own display rule for the number: a foreign player reads "?" for exactly
    /// the frames <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> does (which is precisely
    /// <see cref="RevealGate.ShowRoundCardFronts"/>), and a monster follows vanilla's numeric rule
    /// (&lt;0 blank, 0 → "?", else the number).</summary>
    private static string InitiativeLabel(CActor a)
    {
        try
        {
            if (a is CPlayerActor pa && !RevealGate.ShowRoundCardFronts(pa))
                return "?";
            int v = a.Initiative();
            if (v < 0)
                return string.Empty;
            return v == 0 ? "?" : v.ToString();
        }
        catch { return "?"; }
    }

    /// <summary>Localized actor name — the identical <c>ActorLocKey()</c> string vanilla's own entry
    /// puts in its (hover-only) name label.</summary>
    private static string Text(CActor a)
    {
        try
        {
            string key = a.ActorLocKey() ?? string.Empty;
            return key.Length == 0 ? "?" : Loc.Game(key, key);
        }
        catch { return "?"; }
    }

    /// <summary>One FALLBACK track entry: a tinted plate, the actor name and the initiative number.
    /// Inert — vanilla's entry is a button (character switch / card overview); this one is three
    /// quads. Only ever visible while the real widget cannot be mirrored.</summary>
    private sealed class Chip
    {
        private readonly Transform _root;
        private readonly MeshRenderer _plate;
        private readonly Material _plateMat;
        private readonly TextMeshPro _name;
        private readonly TextMeshPro _initiative;

        private static readonly Color PlayerTint = new(0.20f, 0.26f, 0.20f, 0.95f);
        private static readonly Color EnemyTint = new(0.28f, 0.16f, 0.15f, 0.95f);

        public Chip(Transform parent)
        {
            _root = new GameObject("Chip").transform;
            _root.SetParent(parent, worldPositionStays: false);

            _plateMat = BoardVisual.Unlit(PlayerTint);
            // MR: alpha-only opacify — Set() keeps retinting the RGB (player/enemy), which the
            // helper deliberately leaves alone.
            WorldUI.MrBacking.Opacify(_plateMat);
            _plate = BoardVisual.Quad(_root, "Plate", new Vector2(MaxChipW * 0.94f, ChipH), _plateMat);
            _plate.transform.localPosition = new Vector3(0f, 0f, 0.001f);

            _initiative = RemoteBoardContent.Label(_root, "Initiative", new Vector3(0f, 0.010f, 0f),
                new Vector2(MaxChipW * 0.8f, 0.024f), 0.06f,
                new Color(1f, 0.93f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold);
            _name = RemoteBoardContent.Label(_root, "Name", new Vector3(0f, -0.014f, 0f),
                new Vector2(MaxChipW * 0.92f, 0.018f), 0.030f,
                new Color(0.86f, 0.83f, 0.75f), TextAlignmentOptions.Center);

            _root.gameObject.SetActive(false);
        }

        public void SetShown(bool shown)
        {
            if (_root.gameObject.activeSelf != shown)
                _root.gameObject.SetActive(shown);
        }

        public void Set(Vector3 localPos, float chipW, string name, string initiative, bool player)
        {
            SetShown(true);
            _root.localPosition = localPos;
            _plate.transform.localScale = new Vector3(chipW * 0.94f, ChipH, 1f);
            _plateMat.color = player ? PlayerTint : EnemyTint;
            RemoteBoardContent.SetText(_name, name);
            RemoteBoardContent.SetText(_initiative, initiative);
        }
    }
}
