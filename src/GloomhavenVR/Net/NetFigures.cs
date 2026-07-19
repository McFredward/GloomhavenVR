// FEATURE C — full figure-pickup sync (cosmetic, desync-safe).
//
// Figure transforms are NOT networked by the game: every client re-derives a figure's position
// from authoritative board state each frame (ActorBehaviour.Update → DoTransform / LateUpdate →
// ApplyMotion). So mirroring the figure a peer physically holds is PURELY cosmetic — we never
// send or execute an authoritative action, and on release the game's own Update snaps the mini
// straight back to its cell. This file is the send + receive + interpolation half; the transform
// suppression that lets a remotely-held figure actually track the synced pose lives in
// Board/FigureGrab/ActorBehaviour_HeldTransform_Patch (gated on NetHeldFigures.Owns).

using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Figure-pickup sync (cosmetic). Send side samples the figure the local player physically holds
/// (<see cref="HeldFigures.Current"/>) as a STABLE cross-client actor id + world pose. Receive side
/// maps the id back to the local <c>ActorBehaviour</c>, adds it to <see cref="NetHeldFigures"/> so
/// the game's transform writers are suppressed for it, and eases its transform toward the pose the
/// grabber sends (like <c>RemoteAvatar</c> interpolation). Release drops it from the set and the
/// game resumes control — the mini snaps back to its board cell naturally.
///
/// The stable id is <see cref="CActor.ID"/> — the game's OWN networked actor identifier (player =
/// <c>CharacterClass.ModelInstanceID</c>, enemy / summon = <c>StandeeID</c>), identical on every
/// client for the same figure (it is what the game keys networked card / actor actions by, e.g.
/// <c>CardsHandManager.GetHand(int actorID)</c> / <c>GameAction.ActorID</c>). Works for heroes AND
/// monsters. Everything is a strict no-op offline / single-player (nothing is ever sampled unless a
/// figure is locally held, and no remote figure is driven unless a modded VR peer reports one).
/// </summary>
internal static class NetFigures
{
    private sealed class RemoteHeld
    {
        public ActorBehaviour Actor = null!;
        public int ActorId;
        public Vector3 TargetPos;
        public Quaternion TargetRot = Quaternion.identity;
    }

    // The figure each remote player is currently holding, keyed by playerId (two peers holding
    // different figures both work; a peer switching figures overwrites its own entry).
    private static readonly Dictionary<int, RemoteHeld> _byPlayer = new();

    // actorId → local ActorBehaviour cache. Rebuilt lazily on a miss or a dead cached entry from
    // the same live-figure registry FigureGrabDriver adopts from.
    private static readonly Dictionary<int, ActorBehaviour> _idLookup = new();

    private static readonly List<int> _scratchPlayers = new();
    private static readonly List<ActorBehaviour> _scratchActors = new();

    // ---- send ---------------------------------------------------------------------------

    /// <summary>Send side: the figure the local player is holding this frame (stable id + world
    /// pose). False when nothing is held or no stable id exists.</summary>
    public static bool TrySampleHeld(out int actorId, out Vector3 pos, out Quaternion rot)
    {
        actorId = 0;
        pos = default;
        rot = Quaternion.identity;

        ActorBehaviour? actor = HeldFigures.Current;
        if (actor == null || !TryStableId(actor, out actorId))
            return false;

        Transform? t = RootTransform(actor);
        if (t == null)
            return false;

        pos = t.position;
        rot = t.rotation;
        return true;
    }

    // ---- receive ------------------------------------------------------------------------

    /// <summary>Receive side: remote <paramref name="playerId"/> holds figure
    /// <paramref name="actorId"/> at the given world pose. Maps the id to a local actor, records the
    /// target pose, and suppresses the game's writes for it. If the id can't be resolved locally
    /// (figure not spawned in this view) the player's hold is cleared so nothing goes stale.</summary>
    public static void ApplyRemoteHeld(int playerId, int actorId, Vector3 pos, Quaternion rot)
    {
        ActorBehaviour? actor = Resolve(actorId);
        if (actor == null)
        {
            ReleaseRemote(playerId);
            return;
        }

        if (!_byPlayer.TryGetValue(playerId, out RemoteHeld rec))
        {
            rec = new RemoteHeld();
            _byPlayer[playerId] = rec;
        }
        rec.Actor = actor;
        rec.ActorId = actorId;
        rec.TargetPos = pos;
        rec.TargetRot = rot;

        RebuildSet();
    }

    /// <summary>Receive side: <paramref name="playerId"/> released whatever they held. The figure
    /// leaves <see cref="NetHeldFigures"/> and the game's Update snaps it back to its cell.</summary>
    public static void ReleaseRemote(int playerId)
    {
        if (_byPlayer.Remove(playerId))
            RebuildSet();
    }

    // ---- interpolation ------------------------------------------------------------------

    /// <summary>Per-frame ease of every remotely-held figure toward its target pose (exponential
    /// Lerp/Slerp, same sharpness as RemoteAvatar). Prunes a held record whose actor was destroyed.
    /// No-op when no remote figure is held.</summary>
    public static void Tick()
    {
        if (_byPlayer.Count == 0)
            return;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
        float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);

        _scratchPlayers.Clear();
        foreach (KeyValuePair<int, RemoteHeld> kv in _byPlayer)
        {
            RemoteHeld rec = kv.Value;
            Transform? t = RootTransform(rec.Actor);
            if (t == null)
            {
                _scratchPlayers.Add(kv.Key); // actor gone → drop this hold
                continue;
            }
            t.position = Vector3.Lerp(t.position, rec.TargetPos, k);
            t.rotation = Quaternion.Slerp(t.rotation, rec.TargetRot, k);
        }

        if (_scratchPlayers.Count > 0)
        {
            for (int i = 0; i < _scratchPlayers.Count; i++)
                _byPlayer.Remove(_scratchPlayers[i]);
            RebuildSet();
        }
    }

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>Rebuild <see cref="NetHeldFigures"/> as the union of every player's held actor —
    /// keeps suppression correct when two peers hold different figures (or the same one).</summary>
    private static void RebuildSet()
    {
        _scratchActors.Clear();
        foreach (RemoteHeld rec in _byPlayer.Values)
        {
            if (rec.Actor != null)
                _scratchActors.Add(rec.Actor);
        }
        NetHeldFigures.ReplaceWith(_scratchActors);
    }

    /// <summary>The game's cross-client-stable actor id (<see cref="CActor.ID"/>). False when the
    /// actor has no character or is an unsupported type (ID throws).</summary>
    private static bool TryStableId(ActorBehaviour actor, out int id)
    {
        id = 0;
        CActor? ca = actor != null ? actor.Actor : null;
        if (ca == null)
            return false;
        try
        {
            id = ca.ID;
            return true;
        }
        catch
        {
            return false; // CActor.ID throws for an unsupported actor type
        }
    }

    private static Transform? RootTransform(ActorBehaviour actor)
    {
        if (actor == null)
            return null;
        GameObject root = actor.m_RootGameObject;
        return root != null ? root.transform : null;
    }

    /// <summary>Map a stable actor id to the local <c>ActorBehaviour</c>, rebuilding the cache on a
    /// miss or a dead entry. Null when no live figure matches (different scenario view, not spawned
    /// yet).</summary>
    private static ActorBehaviour? Resolve(int actorId)
    {
        if (_idLookup.TryGetValue(actorId, out ActorBehaviour cached) && cached != null && cached.Actor != null)
            return cached;

        RebuildLookup();
        return _idLookup.TryGetValue(actorId, out ActorBehaviour found) && found != null ? found : null;
    }

    private static void RebuildLookup()
    {
        _idLookup.Clear();

        // Same live-figure registry FigureGrabDriver adopts grabbables from, so the ids line up
        // with what the local player can grab. Fallback to a scene scan only if it's unavailable.
        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools != null && tools._panelUIControllers != null)
        {
            List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
            for (int i = 0; i < controllers.Count; i++)
            {
                WorldspacePanelUIController controller = controllers[i];
                if (controller == null)
                    continue;
                GameObject figure = controller.m_ObjectToTrack;
                if (figure == null)
                    continue;
                Index(ActorBehaviour.GetActorBehaviour(figure));
            }
        }
        else
        {
            foreach (ActorBehaviour ab in Object.FindObjectsOfType<ActorBehaviour>())
                Index(ab);
        }
    }

    private static void Index(ActorBehaviour? ab)
    {
        if (ab != null && TryStableId(ab, out int id))
            _idLookup[id] = ab; // last write wins on a rare id collision (cosmetic only)
    }
}
