# Multiplayer Presence Expansion — PLAN2 (card hands, control boards, figure grab, spawn circle)

Builds ON TOP of the shipped VR embodiment sync (`src/GloomhavenVR/Net/`, see PLAN.md). Same
FFSNet side-channel, same desync-safety (sentinel TargetPlayerID + magic/version), same
zero-Bolt-coupling (NetworkPlayer reached ONLY by reflection). Everything here is COSMETIC and
never mutates authoritative game state.

## Goals (user, verbatim intent)
1. A player's spawned **card hand** is visible to others — but ONLY card BACKS (never fronts),
   both sides. Cards a remote player physically holds are synced too. → ghost fan, always backs.
2. Other players' **control boards** optionally synced at their REAL world position (owner can
   move it, others see it move). Visibility setting (VR menu): Off / ActionPhaseOnly / Always.
3. **Ownership** must be visually clear + WHO: Steam avatar picture + name on the board.
4. **No cheating**: during the secret selection phase only card BACKS; real faces only AFTER all
   committed (reveal). Enforced by the game's OWN authoritative phase, not our channel.
5. New players **spawn in a circle** around the map (distinct azimuth per player), not stacked.
6. **Figure pickup fully synced**: grab+move a figure → others see it move in the grabber's hand;
   release → authoritative game logic resumes.
7. Mod-wide MP-compatibility (audit done — see scratchpad/audit_mp_compat.md).

## THE anti-cheat invariant (linchpin)
Reveal timing is one authoritative signal, identical to the vanilla client
(`AbilityCardUI.cs:980/1024/1100/1188`):
```
HIDE round-card fronts of playerActor  ==
    FFSNetwork.IsOnline
 && SaveData.Instance.Global.CurrentGameState == EGameState.Scenario
 && !playerActor.IsUnderMyControl
 && PhaseManager.CurrentPhase.Type == CPhase.PhaseType.SelectAbilityCardsOrLongRest
```
We NEVER transmit card identities over our side channel. Remote round cards are READ locally from
`playerActor.CharacterClass.RoundAbilityCards` / `.InitiativeAbilityCard` (already host-replicated)
and shown face-up ONLY when the predicate above is false. The ghost HAND fan is ALWAYS backs (you
never see another player's hand in Gloomhaven). ⇒ the mod opens no new cheat vector.

## Verified game API (against decompiled GH.Runtime v1.1.8307)
- Phase: `PhaseManager.PhaseType` / `PhaseManager.CurrentPhase.Type` (== `CPhase.PhaseType`; namespace `ScenarioRuleLibrary`). Secret phase = `SelectAbilityCardsOrLongRest`. Also `.Action`, `.ActionSelection`.
- Played cards: `CPlayerActor.CharacterClass.RoundAbilityCards` (`List<CAbilityCard>`, the 2 selected), `.InitiativeAbilityCard` (set at reveal).
- Hand widget list: `CardsHandManager.Instance.GetHand(CPlayerActor)` / `GetHand(int actorID)` → `CardsHandUI`.
- Locality: `CActor.IsUnderMyControl` (bool); `FFSNetwork.IsOnline`; `SaveData.Instance.Global.CurrentGameState == EGameState.Scenario`.
- Identity (FFSNet ns, NetworkPlayer is Bolt-derived → REFLECTION ONLY):
  - `FFSNet.PlayerRegistry.AllPlayers` (`List<NetworkPlayer>`), `.GetPlayer(int)`, `.MyPlayer`, `.Participants`.
  - `NetworkPlayer.PlayerID:int`, `.Username:string` (bad-word-masked for remotes), `.Avatar:Sprite`, `.MyControllables` (`ObservableCollection<NetworkControllable>`).
  - `NetworkControllable.ControllableObject:IControllable` (NOT Bolt-derived — but reach via reflection too to avoid loading NetworkPlayer metadata). When it `is CharacterManager cm`, `cm.CharacterActor` (`CActor`, global) → cast `as CPlayerActor`.
  - Reverse: `ControllableRegistry.GetController(int controllableID) → NetworkPlayer`.
- Figures: `ActorBehaviour.Actor` (`CActor`) / `ActorBehaviour.GetActorBehaviour(GameObject)`; stable cross-client id = `CActor.Class.ID` (ModelInstanceID) — worker C confirms exact field.
- Local rig seat: `Rig/VRRigDriver.cs` — `BuildRig` (:466-483) + `Recenter` (:571-597, bumps RigPoseVersion at :593). Circle azimuth MUST live inside Recenter/BuildRig (else overwritten by first-pose `_pendingRecenter` :253-258,483,523). Seat hardwired `Vector3.back` today.
- Local board: `PlayTray.Current` (static, nullable) `.Root` (Transform). Local fan: `CardFan` (add `static Current` + `int Count`).
- Held figures: `Board/FigureGrab/HeldFigures` (HashSet<ActorBehaviour>, `Owns/Add/Remove/Count`).
- Handedness: `WorldUI/NonDominantHold.Hand` (VRHand?, has `.Side`). dominantRight = NonDominant is Left.

## Wire format v3 (bump Version 2→3; both peers modded+versioned)
Insert a **message-type byte** right after version. Two types on the same sentinel side-action:

RIG (type 0) — 15 Hz, extends the existing avatar packet:
```
magic u32 | version u8 | TYPE=0 u8 | flags u8 | maskId u8 | worldScale f32
  flags: bit0 head, bit1 left, bit2 right, bit3 fingers, bit4 heldFigure, bit5 dominantRight
  [head pose 20B] [left pose 20B (+5 fingers)] [right pose 20B (+5 fingers)]
  if heldFigure: actorId i32 (4B) + pose 20B         ← the figure the sender holds, world frame
```
EXTRAS (type 1) — ~5 Hz + on-change, board + hand-count:
```
magic u32 | version u8 | TYPE=1 u8 | flags u8
  flags: bit0 hasBoard, bit1 dominantRight (mirror)
  if hasBoard: pos 12B + rot 8B (quantized int16) + scale f32 (4B)
  handCardCount u8
```
Pose encoding identical to today (pos 3×f32, rot 4×int16·32767). Anchor frame = world (identity),
via `IBoardAnchor.ToAnchor/ToWorld` exactly like the rig poses.

## File plan
### FOUNDATION (one worker, merged FIRST; owns all of these):
- `Net/NetProtocol.cs` EDIT: Version=3; consts `MsgRig=0`,`MsgExtras=1`; flags `FlagHeldFigure=1<<4`,`FlagDominantRight=1<<5`; `ExtrasSendRateHz=5f`.
- `Net/AvatarState.cs` EDIT: add `bool HasHeldFigure; int HeldFigureActorId; RigPose HeldFigurePose; bool DominantRight;`.
- `Net/AvatarSerializer.cs` EDIT: write/read TYPE byte (assert type 0 on read), held-figure block (flag4), dominantRight (flag5). Recompute `MaxSize` (=12+70+24=106 → use 112).
- `Net/PresenceState.cs` NEW: `struct PresenceState { bool HasBoard; RigPose Board; float BoardScale; byte HandCardCount; bool DominantRight; }` + `static class PresenceSerializer { int Write(in,byte[]); bool TryRead(byte[],int,out); const MaxSize; }` (type 1). Reuse the LE/quat primitives (duplicate the small helpers or make AvatarSerializer's internal).
- `Net/NetPacket.cs` NEW: `static int PeekType(byte[] buf,int len)` → reads magic+version, returns the type byte or -1 on mismatch/short.
- `Net/RemoteBoardVisibility.cs` NEW (DONE — enum Off/ActionPhaseOnly/Always).
- `Net/RevealGate.cs` NEW: `bool IsSecretSelectionPhase`, `bool InScenario`, `bool ShowRoundCardFronts(CPlayerActor)` (the predicate above, guarded). `using ScenarioRuleLibrary;`.
- `Net/NetPlayerActors.cs` NEW: reflection wrapper. `CPlayerActor? ActorFor(int playerId)`, `Sprite? AvatarFor(int playerId)`, `string? NameFor(int playerId)`, `int LocalStableIndex(out int total)` (index of MyPlayer within Participants sorted by PlayerID → for spawn circle). Cache MethodInfo/PropertyInfo statically; ALL NetworkPlayer access by reflection (never reference the type). Degrade to null/(-1,0) when netcode absent.
- `Net/NetFigures.cs` NEW STUB (worker C fills body; keep signatures): `bool TrySampleHeld(out int actorId, out Vector3 pos, out Quaternion rot)` (send side; stub returns false); `void ApplyRemoteHeld(int playerId,int actorId,Vector3 pos,Quaternion rot)`; `void ReleaseRemote(int playerId)`; `void Tick()`. Stub bodies = no-op.
- `Net/RemoteHandFan.cs` NEW STUB (worker A fills): `RemoteHandFan(RemoteAvatar owner)`, `void Tick(float dt)`, `void Destroy()`. Stub = empty.
- `Net/RemoteControlBoard.cs` NEW STUB (worker B fills): `RemoteControlBoard(RemoteAvatar owner)`, `void Tick(float dt)`, `void Destroy()`. Stub = empty.
- `Net/RemoteAvatar.cs` EDIT: PUBLIC SEAM — expose `Transform Root/HeadHolder/LeftHandHolder/RightHandHolder`, `Color Tint`, `float AppliedScale`, and received data: `bool HasBoard; Vector3 BoardPosition; Quaternion BoardRotation; float BoardScale; int HandCardCount; bool DominantRight; bool HasHeldFigure; int HeldFigureActorId; Vector3 HeldFigurePosition; Quaternion HeldFigureRotation;`. Add `SetExtras(in PresenceState)`. In ctor create `RemoteHandFan`, `RemoteControlBoard` (own+Tick+Destroy them). `SetTarget` also stores held-figure + dominantRight. Non-dominant holder helper: `Transform NonDominantHandHolder => DominantRight ? LeftHandHolder : RightHandHolder`.
- `Net/NetAvatarDriver.cs` EDIT: (a) send EXTRAS at ExtrasSendRateHz — sample board (`PlayTray.Current?.Root` → ToAnchor), hand count (`CardFan.Current?.Count`), dominantRight; (b) rig send also samples held-figure via `NetFigures.TrySampleHeld` + dominantRight; (c) receive: `NetPacket.PeekType` → type0 AvatarSerializer→SetTarget (+ `NetFigures.ApplyRemoteHeld`/`ReleaseRemote`), type1 PresenceSerializer→`_pendingExtras`→SetExtras; (d) `NetFigures.Tick()` each frame; on avatar teardown call `NetFigures.ReleaseRemote(id)`.
- `Net/LocalRigSampler.cs` EDIT: stamp `state.DominantRight` (NonDominantHold.Hand?.Side==Left ⇒ true, default true) + `state.HasHeldFigure = NetFigures.TrySampleHeld(...)`.
- `Net/NetModule.cs` EDIT: bind `RemoteBoards` (`ConfigEntry<RemoteBoardVisibility>`, default ActionPhaseOnly) in BindConfig.
- `Cards/CardFan.cs` EDIT: add `internal static CardFan? Current` (set in Open/ctor, cleared on close/dispose) + `internal int Count => _cards.Count`.

### FEATURE WORKERS (parallel, worktree-isolated, off merged foundation):
- A `Net/RemoteHandFan.cs`: fan of card BACKS on `owner.NonDominantHandHolder`, count `owner.HandCardCount` (clamp 0..~12, default a few). Reuse CardFan arc math (palm standoff + head-face toward... use owner head holder) + `CardMesh.GetBackTexture()`/`CreateBackMaterial()`. NO game data. Scale by owner.AppliedScale. Always visible while the hand holder is active.
- B `Net/RemoteControlBoard.cs` + `Net/OwnerTag.cs`: read-only board visual at `owner.BoardPosition/Rotation/BoardScale` (world). Reuse a STRIPPED PlayTray visual (2 round-card slots + frame) — do NOT call PlaceAtHead. Cards via `NetPlayerActors.ActorFor(owner.PlayerId)` → `RoundAbilityCards`/`InitiativeAbilityCard`; face-up iff `RevealGate.ShowRoundCardFronts(actor)`, else backs. Gate whole board on `NetModule.RemoteBoards` (Off/ActionPhaseOnly/Always × phase). `OwnerTag` = unlit quad with `NetPlayerActors.AvatarFor` sprite + `NameFor` text, pinned to a board corner facing the local head.
- C figure sync: fill `Net/NetFigures.cs` (send: current HeldFigures actor→id+world pose; receive: id→ActorBehaviour, add to a `NetHeldFigures` set + drive its transform toward the synced pose, release restores) + extend `Board/FigureGrab/ActorBehaviour_HeldTransform_Patch.cs` to also suppress writes when `NetHeldFigures.Owns(actor)` + R2 hardening (grab guard/auto-release on authoritative cell change, `_origParent` null-guard). Owns `Board/FigureGrab/*`.
- D spawn circle: `Rig/VRRigDriver.cs` — in Recenter/BuildRig apply azimuth `360°·index/total` (from `NetPlayerActors.LocalStableIndex`) around the focus point; keep single-player (total<=1) at the current `Vector3.back` seat. Owns VRRigDriver.cs.
- E compat: `WorldUI/WristHud.cs` R1 gate on `IsUnderMyControl` (only show local player's HP/XP/gold). Owns WristHud.cs.
- Wiring done by orchestrator at merge (shared files): `WorldUI/SettingsPanel.cs` (RemoteBoards stepper in the Avatar/“Mitspieler” section), `Core/Loc.cs` (ids), `WorldUI/WorldUIConfig.cs` if needed.

## Desync-safety notes (from audit)
- Figure transforms are NOT networked (locally re-derived, snap back on release) → figure-grab sync is pure cosmetic; safe. Suppress local re-derivation for a remotely-held figure exactly like HeldFigures does locally.
- Spawn circle: re-seating the local rig broadcasts the new head pose for free (world-frame) → remote avatars separate without extra mapping (R3).
- Known live-test risks: client→client relay behind a flat host (R4); vanilla mixed-session sentinel ignore (R5). Unchanged from PLAN.md.
