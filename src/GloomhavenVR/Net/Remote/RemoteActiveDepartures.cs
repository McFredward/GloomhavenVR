using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Original active-cell locations retained across a model departure. This cache never
/// starts an animation: an owner's semantic flight is the only departure trigger.</summary>
internal static class RemoteActiveDepartures
{
    private sealed class Cell
    {
        internal int Player;
        internal CPlayerActor Actor = null!;
        internal CAbilityCard Card = null!;
        internal Vector3 Local;
        internal float Seen;
        internal bool Claimed;
        internal byte Seat, Count;
    }

    private static readonly List<Cell> Cells = new();
    private const float Lifetime = 10f;

    internal static void Remember(int player, CPlayerActor actor, CAbilityCard card, Vector3 local, byte seat, byte count)
    {
        Prune();
        Cell? cell = Cells.Find(c => c.Player == player && ReferenceEquals(c.Card, card));
        if (cell == null)
        {
            if (Cells.Count >= 128) Cells.RemoveAt(0);
            cell = new Cell { Player = player, Actor = actor, Card = card };
            Cells.Add(cell);
        }
        cell.Local = local;
        cell.Seat = seat; cell.Count = count;
        cell.Seen = Time.unscaledTime;
        cell.Claimed = false;
    }

    internal static void Clear(int player) => Cells.RemoveAll(c => c.Player == player);

    internal static bool TryPose(int player, CAbilityCard card, out Vector3 local)
    {
        Prune();
        Cell? cell = Cells.Find(c => c.Player == player && ReferenceEquals(c.Card, card));
        local = cell != null ? cell.Local : default;
        return cell != null;
    }

    internal static CardFlightSource? SourceOf(int player, CAbilityCard card)
    {
        Prune();
        Cell? cell = Cells.Find(c => c.Player == player && ReferenceEquals(c.Card, card));
        return cell != null ? new CardFlightSource(NetFigures.StableActorId(cell.Actor), cell.Seat, cell.Count) : null;
    }

    internal static bool TryTake(int player, CPlayerActor? actor, CardFxAnchor destination, CardFlightSource? source,
                                 out CAbilityCard? card, out Vector3 local)
    {
        Prune();
        Cell? found = null;
        foreach (Cell c in Cells)
        {
            if (c.Player != player || c.Claimed || !ReferenceEquals(c.Actor, actor)) continue;
            if (source.HasValue && (source.Value.ActorId != NetFigures.StableActorId(c.Actor)
                || source.Value.Count != c.Count || source.Value.Seat != c.Seat)) continue;
            CCharacterClass cc = c.Actor.CharacterClass;
            bool arrived = destination == CardFxAnchor.Discard ? cc.DiscardedAbilityCards.Contains(c.Card)
                : destination == CardFxAnchor.Burnt
                  && (cc.LostAbilityCards.Contains(c.Card) || cc.PermanentlyLostAbilityCards.Contains(c.Card));
            if (!arrived || cc.ActivatedCards.Contains(c.Card)) continue;
            if (found != null) { card = null; local = default; return false; }
            found = c;
        }
        card = found?.Card;
        local = found != null ? found.Local : default;
        if (found != null) found.Claimed = true;
        return found != null;
    }

    // A previous staged snapshot is not guaranteed to arrive: presence coalescing can replace
    // it with the accepted flight. Its independently validated immutable pool address supplies
    // the original here. Native destination membership, never the cosmetic message, commits it.
    internal static bool HasCommittedOriginal(CPlayerActor? actor, CAbilityCard? card,
        CardFxAnchor destination, CardFlightSource? source)
    {
        if (actor == null || card == null || !source.HasValue || !source.Value.Validate() || source.Value.Count == 0
            || source.Value.ActorId != NetFigures.StableActorId(actor)) return false;
        CCharacterClass cc = actor.CharacterClass;
        return !cc.ActivatedCards.Contains(card)
            && (destination == CardFxAnchor.Discard ? cc.DiscardedAbilityCards.Contains(card)
                : destination == CardFxAnchor.Burnt
                  && (cc.LostAbilityCards.Contains(card) || cc.PermanentlyLostAbilityCards.Contains(card)));
    }

    internal static bool TryDismissPose(RemoteAvatar owner, CAbilityCard card,
        CardDismissFlightOrigin? origin, out Vector3 world, out Quaternion rotation, out float width)
    {
        if (owner.TryDismissPose(card, out world, out rotation, out width)) return true;
        Transform? berth = owner.ItemUseRecess;
        if (berth == null || origin == null) return false;
        // Exact original pose is relative to the owner's physical item berth, not its rig
        // zoom or the obsolete active-grid seat. It survives a missed staging snapshot.
        world = berth.TransformPoint(origin.BerthPose.Position);
        rotation = berth.rotation * origin.BerthPose.Rotation;
        width = origin.Width * Mathf.Abs(berth.lossyScale.x);
        return width > 0f;
    }

    private static void Prune() => Cells.RemoveAll(c => Time.unscaledTime - c.Seen > Lifetime
        || (!c.Actor.CharacterClass.ActivatedCards.Contains(c.Card)
            && (c.Actor.CharacterClass.HandAbilityCards.Contains(c.Card)
                || c.Actor.CharacterClass.RoundAbilityCards.Contains(c.Card)
                || c.Actor.CharacterClass.ExtraTurnCards.Contains(c.Card))));
}
