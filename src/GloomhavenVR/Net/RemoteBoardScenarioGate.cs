using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>
/// THE OUTER GATE on every peer control board: a remote board may not exist AT ALL unless this
/// client is inside a scenario. It sits strictly OUTSIDE <see cref="RemoteBoardGate"/>'s
/// <see cref="NetModule.RemoteBoards"/> dial — the dial keeps its full, unchanged meaning inside a
/// scenario ("Immer" still means always), and this class only removes the places where the question
/// may be asked at all.
///
/// <para><b>THE USER REPORT (2026-08-22, item 1).</b> "Mein Spielpartner hat das remote board von
/// mir in klein über der 3D Map schweben gesehen. In der 3D-Map-Umgebung ist kein board sichtbar von
/// keinem Mitspieler und darf für niemanden sichtbar sein." Screenshot:
/// <c>.planning/debug/remote_board_in_der_3d_map.png</c> — a two-slot wooden control board hovering
/// over the campaign-map parchment. The peer's log proves it was built there, long before any
/// scenario: <c>remote/Player.log:2966</c> "Remote board [1] built on the REAL 3D 'Steel' board
/// asset …" sits 1,466 lines ABOVE <c>:4432</c> "[MapRoom] MAP ROOM PARTY TRAVEL installed" and
/// <c>:4534</c> "MAP SCENE REPORT (3D map room)".</para>
///
/// <para><b>WHY IT WAS ONE-SIDED, AND WHY THAT IS NOT A HOST RULE.</b> The host's log carries NO
/// "Remote board [n] built" line, so he could not see his partner's board. That asymmetry has
/// nothing to do with hosting: a remote board is built only when the owner's extras carry
/// <c>FlagHasBoard</c>, i.e. only while that player's OWN local control board exists and has a
/// synced pose. The host had one standing and PINNED (host <c>Player.log:3314</c> "[Cards] Board
/// pose [initial]", <c>:3843</c> "pin carried through a tracking-origin change", peer
/// <c>Player.log:2946</c> "Tray anchor mode RECEIVED from player 1: PINNED"), so his board block
/// went on the wire in the map room and his partner rendered it. The partner's client had no local
/// board pose at all in that phase — its log contains not a single "[Cards] Board pose" line — so
/// nothing came back the other way. Either player could have been on either side of it, which is
/// exactly why the suppression here is symmetric and unconditional rather than host-aware.</para>
///
/// <para><b>THE AUTHORITY: <see cref="VRModeStateMachine.ScenarioBoardExists"/>.</b> Chosen for
/// three reasons, and NOT invented here:
/// <list type="number">
///   <item>It is the repo's own documented canonical "an actual scenario board exists right now"
///     signal (its doc block, and <c>docs/INTERFACES-P2.md</c> §325) — the scenario
///     <c>Choreographer</c> scene object, alive only in the Game/Game_gamepad scenario scenes
///     (decompiled <c>GH.Runtime/Choreographer.cs:659,715</c>). It is false in the 3D map room
///     (which runs a <c>MapChoreographer</c>, not a <c>Choreographer</c>), false on the vanilla 2D
///     campaign map, false in the main menu and false on the loadout screen.</item>
///   <item>It is byte-identical to <c>Cards.CardsGameApi.InScenario</c>, which is how the LOCAL
///     control board decides it exists at all: <c>CardsDriver.6.Flows.cs:1940-1955</c> —
///     <c>if (CardsGameApi.InScenario) { _tray.EnsureBuilt(…); _tray.SetVisible(true); } else
///     { _tray.SetVisible(false); … }</c>. So after this gate a peer's board exists on this client
///     under EXACTLY the condition our own board exists on it. That is the strongest possible form
///     of "darf für niemanden sichtbar sein": there is no seat at the table from which the answer
///     differs.</item>
///   <item>It is deliberately NOT <c>RevealGate.InScenario</c> (the save's
///     <c>GlobalData.CurrentGameState == EGameState.Scenario</c>), even though that lives in this
///     very namespace. <see cref="VRModeStateMachine.ScenarioBoardExists"/>'s own doc rejects it by
///     name: the save state "flips during loading/travel before any board exists", which would put
///     a peer's board up over a loading screen. Board presence is the tighter answer and it is the
///     one every other subsystem in this mod already reads.</item>
/// </list></para>
///
/// <para><b>IT REFUSES THE BUILD, it does not build-then-hide.</b> The gate is folded into
/// <see cref="RemoteBoardGate.SurfaceVisible"/>, which <c>RemoteControlBoard.Tick</c> evaluates at
/// its line 449 — three statements BEFORE <c>EnsureBuilt</c> (line 513) and behind an early
/// <c>return</c>. So while the gate is shut no prefab is instantiated, no 122-renderer board asset
/// is cloned and no collider strip runs (peer <c>Player.log:2963,2976</c>): the cost of a board
/// nobody may see is zero, not "hidden".</para>
///
/// <para><b>NOTHING IS DROPPED WHILE IT IS SHUT.</b> The gate is a RENDER decision only. Every wire
/// record a peer sends while suppressed — board pose/scale/style, the layout tuning (record 28),
/// slot-card sizes (record 11), pile counts (record 15), slot occupancy, half hover/select — is
/// applied into <c>RemoteAvatar</c> by the receive path exactly as before, because no receiver
/// consults this class. <c>RemoteControlBoard.EnsureBuilt</c> reads all of them at BUILD time
/// (<c>_builtTuningRevision</c>, <c>_layout</c>, <c>SlotCardW/SlotFrameW</c>, and it arms
/// <c>_nextRefreshAt = 0f</c> so the first tick after the build repaints), and the pose applies with
/// <c>_poseInit == false</c>, i.e. it SNAPS rather than easing in from the origin. A board therefore
/// comes back complete and correct on its FIRST drawn frame, at whatever layout/scale/tuning the
/// peer has meanwhile moved to — there is nothing to re-apply on un-suppress.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY rendering, zero wire — a local decision about what this client
/// draws, taken from a purely local game-state read. It transmits nothing and changes no game
/// state. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class RemoteBoardScenarioGate
{
    /// <summary>Name prefix <c>RemoteControlBoard.EnsureBuilt</c> gives its root object. The edge
    /// census below identifies live boards by it; keep the two in step.
    ///
    /// <para>THE PLAYER-INDEX BRACKET IS DELIBERATELY NOT PART OF THIS STRING, and that is not a
    /// style choice: <c>scripts/patch-inventory.py</c>'s declaration scanner does not skip string
    /// literals, so a lone <c>[</c> inside one makes its bracket matcher walk to the end of the file
    /// and fail a build gate with "unbalanced [ at offset …". Nothing is lost by dropping it — the
    /// full name is <c>GloomhavenVR.RemoteControlBoard[n]</c> and no other object in the
    /// DontDestroyOnLoad scene begins with this prefix — but anyone who "completes" the literal
    /// breaks the gate, not this class.</para></summary>
    private const string BoardRootPrefix = "GloomhavenVR.RemoteControlBoard";

    // ---- the predicate, memoised per frame ------------------------------------------------------
    private static int _evaluatedFrame = -1;
    private static bool _open;

    /// <summary>The state the edge line has already been emitted for, as a TRI-state: -1 = never
    /// stated, 0 = closed, 1 = open. The unknown start is deliberate. The reported defect is a board
    /// standing in the 3D map room on a client that has NOT yet been in a scenario this session, so
    /// the run that has to be verifiable is the one with no transition in it at all; a gate that
    /// only spoke on flips would leave that run with nothing but an absence to read. The first tick
    /// that serves any peer board therefore states the gate's position once, and after that it is
    /// strictly one line per edge.</summary>
    private static int _loggedOpen = -1;

    // ---- per-frame census of how many peer boards the gate is answering for ---------------------
    private static int _tickFrame = -1;
    private static int _ticksThisFrame;
    private static int _ticksServedLastFrame;

    /// <summary>
    /// May a peer's control board exist on this client at all? True only inside a scenario.
    /// Memoised per frame: a four-peer table asks this several times per frame through
    /// <see cref="RemoteBoardGate.SurfaceVisible"/> and the three fan classes, and the answer cannot
    /// change within a frame. Emits the ONE edge line (see <see cref="LogEdge"/>) on a real flip.
    /// </summary>
    internal static bool Open
    {
        get
        {
            int frame = Time.frameCount;
            if (frame == _evaluatedFrame)
                return _open;
            _evaluatedFrame = frame;
            _open = VRModeStateMachine.ScenarioBoardExists;
            int state = _open ? 1 : 0;
            if (state != _loggedOpen)
            {
                bool first = _loggedOpen < 0;
                _loggedOpen = state;
                LogEdge(_open, first);
            }
            return _open;
        }
    }

    /// <summary>
    /// Called once per peer control-board tick, from <see cref="RemoteBoardGate.LogModeIfChanged"/>
    /// — the one seam <c>RemoteControlBoard.Tick</c> reaches unconditionally, before it can early-
    /// return on <c>HasBoard</c> or on the dial. Two jobs: keep the count the edge line reports
    /// honest, and make sure the predicate is EVALUATED (and therefore its edge stated) even in the
    /// frames where nothing downstream would have asked — e.g. with the dial at "Aus", where
    /// <c>Tick</c> returns before it ever reaches <see cref="RemoteBoardGate.SurfaceVisible"/>.
    /// With no remote avatars at all nothing calls this and the gate stays silent, which is correct:
    /// there is no board on either side of that edge.
    /// </summary>
    internal static void NoteBoardTick()
    {
        int frame = Time.frameCount;
        if (frame != _tickFrame)
        {
            _tickFrame = frame;
            _ticksServedLastFrame = _ticksThisFrame;
            _ticksThisFrame = 0;
        }
        _ticksThisFrame++;
        _ = Open;
    }

    /// <summary>
    /// ONE Info line per real transition of this gate — never per frame, never per peer (grep:
    /// "Remote board scenario gate"). It names the predicate that decided, the mode machine's view
    /// of where the player is standing, the untouched dial, how many peer boards were being served
    /// in the last frame that served any, and a census of what is actually built right now.
    /// </summary>
    private static void LogEdge(bool open, bool first)
    {
        string census = Census();
        string edge = first ? "(first statement this session — no transition, this is where it starts)"
                            : "(transition)";
        // On the very first statement there IS no previous frame, so report what THIS frame has
        // served so far (at least 1 — NoteBoardTick increments before it evaluates the predicate).
        // Never a max() of the two: on a real transition the previous frame is the honest number,
        // because the frame the edge lands in is still being served.
        int served = first ? _ticksThisFrame : _ticksServedLastFrame;
        if (open)
        {
            VRLog.Info("Net", $"Remote board scenario gate OPEN {edge} — peer control boards may be built " +
                              "and drawn again. Predicate: VRModeStateMachine.ScenarioBoardExists = " +
                              $"true (the scenario Choreographer is alive; mode={VRModeStateMachine.CurrentMode}, " +
                              $"modRoom={VRModeStateMachine.ModRoomStands}). [Net] RemoteBoards = " +
                              $"{RemoteBoardGate.Mode} decides from here, with its full ordinary meaning. " +
                              $"Restored for {served} peer board tick(s) served in the frame this line " +
                              $"was decided from; built right now: {census} (a board is rebuilt on its " +
                              "next tick, at the peer's current pose, style, tuning and pile counts).");
        }
        else
        {
            VRLog.Info("Net", $"Remote board scenario gate CLOSED {edge} — NO peer's control board may be " +
                              "built or drawn, for anybody, whatever the dial says. Predicate: " +
                              "VRModeStateMachine.ScenarioBoardExists = false (Choreographer.s_Choreographer " +
                              "is null — this client is outside a scenario: 3D map room, vanilla 2D map, " +
                              $"loadout or main menu; mode={VRModeStateMachine.CurrentMode}, " +
                              $"modRoom={VRModeStateMachine.ModRoomStands}). [Net] RemoteBoards = " +
                              $"{RemoteBoardGate.Mode} is UNCHANGED and resumes its full meaning when the " +
                              $"gate reopens. Suppressed for {served} peer board tick(s) served in the " +
                              $"frame this line was decided from; standing at the edge: {census} " +
                              "(any of those roots is deactivated on its next tick; nothing new is built).");
        }
    }

    /// <summary>
    /// What is actually built at this instant: the live <c>RemoteControlBoard</c> roots and their
    /// object/renderer cost. Taken ONLY on an edge (twice per scenario transition at most).
    ///
    /// <para>Deliberately NOT <c>FindObjectsOfType</c> — this project has lost a whole frame budget
    /// to scene sweeps more than once. <c>RemoteControlBoard.EnsureBuilt</c> calls
    /// <c>Object.DontDestroyOnLoad</c> on its root, so every board lives in the DontDestroyOnLoad
    /// scene, whose root list is a handful of objects. A throwaway probe object is the only
    /// documented way to obtain a handle on that scene; it is filtered out of its own census by the
    /// name prefix and destroyed immediately.</para>
    ///
    /// <para>It reports what the scan FOUND and claims nothing beyond that: if the roots cannot be
    /// enumerated the line says so rather than asserting a zero.</para>
    /// </summary>
    private static string Census()
    {
        GameObject? probe = null;
        try
        {
            probe = new GameObject("GloomhavenVR.RemoteBoardScenarioGate.SceneProbe");
            Object.DontDestroyOnLoad(probe);
            Scene ddol = probe.scene;
            if (!ddol.IsValid())
                return "census unavailable (no DontDestroyOnLoad scene handle)";

            int roots = 0, objects = 0, renderers = 0;
            foreach (GameObject go in ddol.GetRootGameObjects())
            {
                if (go == null || !go.name.StartsWith(BoardRootPrefix, StringComparison.Ordinal))
                    continue;
                roots++;
                objects += go.GetComponentsInChildren<Transform>(true).Length;
                renderers += go.GetComponentsInChildren<Renderer>(true).Length;
            }
            return $"{roots} remote board root(s), {objects} object(s), {renderers} renderer(s)";
        }
        catch (Exception e)
        {
            return $"census unavailable ({e.GetType().Name}: {e.Message})";
        }
        finally
        {
            if (probe != null)
                Object.Destroy(probe);
        }
    }
}
