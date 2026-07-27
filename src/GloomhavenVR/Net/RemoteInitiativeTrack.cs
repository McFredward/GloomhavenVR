using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Initiative TRACK — GLOBAL actor list + PER-ACTOR initiative (vanilla's own gate)
// =================================================================================================

/// <summary>
/// The scenario's INITIATIVE TRACK, drawn above a peer's board — the mirror of the local board's
/// docked <c>InitiativeTrack</c> canvas (<c>PlayTray.InitiativeMount</c>).
///
/// WHY IT IS HERE NOW. The previous parity pass rendered only the peer's own initiative NUMBER and
/// left the track out, arguing that its portraits are CLICKABLE (they switch the local player's
/// selected character / open the card overview). As a purely NON-INTERACTIVE picture that objection
/// disappears — nothing drawn here has a collider, so there is nothing to click — and the user's
/// standing requirement is that every element of the control board is represented on a peer's board.
///
/// SOURCE (global list + per-actor numbers, zero wire): <c>ScenarioManager.Scenario.AllAliveActors</c>
/// filtered and DEDUPED BY <c>CActor.Class</c>, which is exactly what
/// <c>InitiativeTrack.UpdateInitiativeTrack</c> does when it builds its entries (it skips hero
/// summons and prop/object actors and keeps one entry per <c>CClass</c>, so a monster GROUP is one
/// row). Order mirrors <c>InitiativeTrackActorBehaviour.CompareTo</c>'s effect: ascending
/// <c>CActor.Initiative()</c>, with every entry whose initiative is not (yet) knowable pushed to the
/// end — the same place vanilla's negative order-priorities put them.
///
/// ANTI-CHEAT — vanilla's rule, verbatim. A foreign player's initiative reads "?" while
/// <c>FFSNetwork.IsOnline &amp;&amp; phase == SelectAbilityCardsOrLongRest &amp;&amp;
/// !actor.IsUnderMyControl</c> (<c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> /
/// <c>InitiativeTrackActorAvatar</c>) — which is precisely <see cref="RevealGate.ShowRoundCardFronts"/>.
/// Monsters follow vanilla's own numeric rule (&lt;0 blank, 0 → "?", else the number), i.e. their
/// initiative appears only once their ability card has been revealed to everyone. So this shows a
/// number in exactly the frames the shared 2D track already shows it, and "?" in exactly the frames
/// it shows "?". No new information exists anywhere in this panel.
///
/// NOT REPRODUCED — the PORTRAIT. Vanilla's entry paints a character portrait fetched through
/// <c>CharacterPortraitsProvider</c> from the <c>misc_characterportraits</c> asset bundle; reaching
/// that per-actor texture from the mod is neither cheap nor cheat-relevant, and vanilla itself only
/// reveals the actor's NAME on hover. Each chip therefore carries the localized actor name
/// (<c>CActor.ActorLocKey()</c>, the identical string vanilla's own <c>nameText</c> uses) over a
/// class-tinted plate, which is the readable equivalent at VR distance.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (GLOBAL + PER-ACTOR MODEL) — ZERO wire either way. The actor LIST
/// and its order are GLOBAL (<c>ScenarioManager.Scenario.AllAliveActors</c>, deduped by
/// <c>CActor.Class</c>); the per-actor initiative NUMBERS are PER-ACTOR MODEL, gated by
/// <see cref="RevealGate"/> with monsters following vanilla's own numeric rule. Portraits are
/// DELIBERATELY-NOT reproduced (see NOT REPRODUCED above). One of the three genuinely MIXED types.
/// See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteInitiativeTrack
{
    /// <summary>Bottom-centre origin above the board's top edge. The local dock sits at
    /// <c>PlayTray.InitiativeMountY</c> (0.10) and grows up; this one is pushed to 0.165 so it
    /// clears the mod's own INI badge (<see cref="RemoteStatusReadouts"/>, y 0.132 ± 0.021) which
    /// already occupies the lower half of that mount on a remote board.</summary>
    private const float MountY = 0.165f;

    /// <summary>Track width — <c>PlayTray.InitiativeMountWidth</c> (= the board width).</summary>
    private const float Width = 0.64f;

    private const float ChipH = 0.052f;
    private const float MaxChipW = 0.082f;
    private const int MaxChips = 8;

    private readonly Transform _root;
    private readonly Chip[] _chips = new Chip[MaxChips];
    private readonly List<CActor> _entries = new(MaxChips);
    private readonly List<CClass> _seen = new(MaxChips);

    private string _signature = string.Empty;

    /// <summary>How many chips the track currently draws (diagnostics).</summary>
    public int Count { get; private set; }

    public RemoteInitiativeTrack(Transform boardRoot)
    {
        _root = new GameObject("InitiativeTrack").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(0f, MountY + ChipH * 0.5f, RemoteControlBoard.ProudZLocal);

        for (int i = 0; i < MaxChips; i++)
            _chips[i] = new Chip(_root);

        _root.gameObject.SetActive(false);
    }

    /// <summary>Re-read the scenario's actor list + initiatives and repaint on an actual change.
    /// Wrapped whole: a half-initialised scenario must degrade to an empty track, never throw.</summary>
    public void Refresh()
    {
        _entries.Clear();
        _seen.Clear();
        try { Collect(); }
        catch { _entries.Clear(); }

        // Ascending initiative; everything not (yet) knowable to the end — the effect of vanilla's
        // own negative order priorities for actors whose initiative is unknown.
        _entries.Sort(static (a, b) => SortKey(a).CompareTo(SortKey(b)));

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

    /// <summary>Filter + dedupe exactly like <c>InitiativeTrack.UpdateInitiativeTrack</c>: alive
    /// actors, one entry per <c>CClass</c>, no hero summons and no prop/object actors.</summary>
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

    /// <summary>Vanilla's own display rule for the number (see the class note).</summary>
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

    /// <summary>One track entry: a tinted plate, the actor name and the initiative number. Inert —
    /// vanilla's entry is a button (character switch / card overview); this one is three quads.</summary>
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
