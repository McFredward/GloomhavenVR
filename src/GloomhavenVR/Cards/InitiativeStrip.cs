using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Test #14: compact initiative order ON the tray — one chip per actor ("12 Brute")
/// along the tray's top edge, in acting order (acts-first on the left), the active
/// actor highlighted gold. STRICTLY read-only from the game's own initiative track
/// (see <see cref="CardsGameApi.GetInitiativeOrder"/> for the verified sources:
/// InitiativeTrack.Instance/actorsUI + InitiativeTrackActorBehaviour.CompareTo,
/// Choreographer.CurrentActor).
///
/// Update model (allocation-free per frame):
/// - CardsDriver marks the strip dirty from VREvents (ChoreographerMessage /
///   ChoreographerStateChanged / CardSelectionChanged);
/// - <see cref="Tick"/> re-reads the order ONLY while dirty, compares against the
///   cached snapshot (actor refs + initiatives) and rebuilds chip texts only on a
///   real change;
/// - the gold active-actor highlight is a per-frame reference compare (no alloc)
///   recolored only on change.
/// </summary>
internal sealed class InitiativeStrip
{
    private const int MaxChips = 16;

    private static readonly Color ActiveColor = new(1f, 0.84f, 0.3f);
    private static readonly Color PlayerColor = new(0.93f, 0.9f, 0.84f);
    private static readonly Color EnemyColor = new(0.72f, 0.7f, 0.68f);
    private static readonly Color DeadColor = new(0.42f, 0.4f, 0.38f);

    private readonly Transform _root;
    private readonly float _width;
    private readonly TextMeshPro[] _chips = new TextMeshPro[MaxChips];

    // Cached snapshot (reference/int compares only).
    private readonly List<CActor> _buffer = new(MaxChips);
    private readonly CActor?[] _actors = new CActor?[MaxChips];
    private readonly int[] _initiatives = new int[MaxChips];
    private int _count;
    private CActor? _active;
    private bool _dirty = true;

    private InitiativeStrip(Transform root, float width)
    {
        _root = root;
        _width = width;
    }

    /// <summary>Build the (initially empty) strip as a child of the tray root.</summary>
    internal static InitiativeStrip Build(Transform parent, Vector3 localPos, float width)
    {
        var rootGo = new GameObject("InitiativeStrip");
        rootGo.transform.SetParent(parent, worldPositionStays: false);
        rootGo.transform.localPosition = localPos;
        var strip = new InitiativeStrip(rootGo.transform, width);

        for (int i = 0; i < MaxChips; i++)
        {
            var chipGo = new GameObject("Chip");
            chipGo.transform.SetParent(rootGo.transform, worldPositionStays: false);
            var tmp = chipGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = EnemyColor;
            chipGo.SetActive(false);
            strip._chips[i] = tmp;
        }
        return strip;
    }

    /// <summary>Event-driven invalidation (wired by CardsDriver to VREvents).</summary>
    internal void MarkDirty() => _dirty = true;

    /// <summary>Called each frame while the tray is visible (PlayTray.TickStatus). Cheap.</summary>
    internal void Tick()
    {
        if (_dirty)
        {
            _dirty = false;
            Refresh();
        }

        // Active-actor highlight: reference compare per frame, recolor on change only.
        CActor? active = CardsGameApi.CurrentTurnActor();
        if (!ReferenceEquals(active, _active))
        {
            _active = active;
            for (int i = 0; i < _count; i++)
                ApplyColor(i);
        }
    }

    private void Refresh()
    {
        CardsGameApi.GetInitiativeOrder(_buffer);
        int count = Mathf.Min(_buffer.Count, MaxChips);

        // Unchanged order (refs + initiatives) → nothing to rebuild.
        bool same = count == _count;
        for (int i = 0; same && i < count; i++)
        {
            same = ReferenceEquals(_buffer[i], _actors[i])
                   && CardsGameApi.ActorInitiative(_buffer[i]) == _initiatives[i];
        }
        if (same)
            return;

        _count = count;
        bool any = count > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);
        if (!any)
        {
            for (int i = 0; i < MaxChips; i++)
                _actors[i] = null;
            return;
        }

        float pitch = _width / count;
        float x0 = -_width * 0.5f + pitch * 0.5f;
        for (int i = 0; i < MaxChips; i++)
        {
            TextMeshPro chip = _chips[i];
            if (chip == null)
                continue;
            bool used = i < count;
            if (chip.gameObject.activeSelf != used)
                chip.gameObject.SetActive(used);
            if (!used)
            {
                _actors[i] = null;
                continue;
            }

            CActor actor = _buffer[i];
            _actors[i] = actor;
            int initiative = CardsGameApi.ActorInitiative(actor);
            _initiatives[i] = initiative;

            chip.transform.localPosition = new Vector3(x0 + pitch * i, 0f, 0f);
            // Rebuild-only string work (order changes a few times per round).
            string name = CardsGameApi.ActorLabel(actor);
            chip.text = initiative > 0 ? $"{initiative} {name}" : name;
            TmpFit.Fit(chip, pitch * 0.94f, 0.024f, maxFontSize: 0.22f, wrap: false);
            ApplyColor(i);
        }
        VRLog.Debug("Cards", $"Initiative strip rebuilt: {count} actors.");
    }

    private void ApplyColor(int i)
    {
        TextMeshPro chip = _chips[i];
        CActor? actor = _actors[i];
        if (chip == null || actor == null)
            return;
        Color color =
            ReferenceEquals(actor, _active) ? ActiveColor :
            actor.IsDeadPlayer ? DeadColor :
            CardsGameApi.IsPlayer(actor) ? PlayerColor : EnemyColor;
        bool active = ReferenceEquals(actor, _active);
        if (chip.color != color)
            chip.color = color;
        FontStyles style = active ? FontStyles.Bold : FontStyles.Normal;
        if (chip.fontStyle != style)
            chip.fontStyle = style;
    }
}
