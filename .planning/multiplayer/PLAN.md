# Multiplayer VR Embodiment — PLAN

Epic: run GloomhavenVR cleanly in the game's multiplayer. (1) VR players play with flat
(non-VR) players with **no desync of authoritative game state**; (2) two VR players see each
other's **head "mask" + hands**, Demeo-style. Built on the game's own Photon-Bolt / FFSNet
netcode. Inspiration: RepoXR / LCVR (DaXcess).

See `RESEARCH.md` (§"Verified injection point") for the cited decompiled evidence. This plan is
the architecture + phased task breakdown + risks, and documents what the SCAFFOLD under
`src/GloomhavenVR/Net/` already implements.

---

## 1. Architecture

```
LOCAL VR CLIENT                                     REMOTE MODDED PEER (VR or flat)
NetAvatarDriver.Update (15 Hz)                      Harmony prefix on
  LocalRigSampler.TrySample                           ActionProcessor.ProcessSideAction(GameAction)
    head cam (VRRigDriver.HeadCamera)                  ActionTypeID == Sentinel?
    + 2 hands (VRHands.Left/Right, Rig.Root)            no  → return true  (vanilla runs it)
    + finger curls, in the SHARED world frame          yes → read CustomDataToken bytes,
  AvatarSerializer.Write → byte[]                            raise PacketReceived, return FALSE
  FfsNetTransport.Send                               NetAvatarDriver.OnPacketReceived
    Synchronizer.SendSideAction(                       AvatarSerializer.TryRead (magic/ver gate)
      (GameActionType)Sentinel,                        anchor → world
      new CustomDataToken(bytes),                      _pending[senderId] = state
      canBeUnreliable:true, sendToHostOnly:false,    NetAvatarDriver.Update
      targetPlayerID:int.MaxValue)                     GetOrCreate RemoteAvatar(senderId)
        → NetworkActionEvent(Others, Unreliable)       RemoteAvatar.SetTarget + Tick (Lerp/Slerp)
                                                       staleness > 3 s → RemoteAvatar.Destroy

VANILLA / NON-MODDED PEER: receives the side action, but TargetPlayerID == int.MaxValue makes
ProcessSideAction IGNORE it (never calls Execute) → zero effect, zero desync.
```

Five invariants (RepoXR/LCVR), all honoured:
1. **No new networked object/prefab** — ride the existing `SendSideAction` event; host stays vanilla.
2. **Magic + version** prefix every payload; readers bail on mismatch.
3. **Only the local owner writes**; identity = FFSNet `PlayerID` (Bolt connectionId).
4. **Shared anchor** = game world space (see §3); avatars are cosmetic world objects like minis.
5. **Interpolate on the receiver** (fixed Lerp/Slerp), never snap.

---

## 2. Message schema (`NetProtocol` + `AvatarSerializer`)

Little-endian, allocation-free, ≤ ~80 bytes; carried as `CustomDataToken.CustomData`:

| Bytes | Field | Notes |
|------|-------|-------|
| 0–3  | `uint32` magic | `0x47565231` ("GVR1") |
| 4    | `byte` version | `1` |
| 5    | `byte` flags | bit0 head, bit1 leftHand, bit2 rightHand, bit3 hasFingers |
| 6–9  | `float32` worldScale | sender rig `lossyScale` — receiver sizes the floating hands to match |
| …    | per present part (head, left, right): **pose** | |
| pose = 20 B | pos `3×float32` (12) + rot `4×int16` quantized `q·32767` (8) | game-unit world coords |
| +5 B | per hand, if hasFingers | thumb/index/middle/ring/pinky curl `0..1 → byte` |

Rotation quantization error ≈ 0.006 rad — imperceptible for a floating hand. Position is full
float32 (game-unit range is large; not quantized). Rate 15 Hz **unreliable**; the receiver
interpolates. Future compaction (not needed for MVP): smallest-three quaternion (8→7 B), int16
board-relative positions.

---

## 3. Coordinate frame (IMPORTANT correction to the brief)

The shared cross-client frame is **game world space (game units)** — NOT `PlayTray._root`.
- The brief/research assumed `PlayTray._root` is a shared table origin. **It is not**:
  `PlayTray._root` is the per-player cards tray, parented under each player's own rig and
  re-homed to their head (`TrayFollow` / `PlaceAtHead`, `Cards/PlayTray.cs`), so it differs per
  client and per frame.
- Gloomhaven is **server-authoritative and top-down**: the board, tiles and minis sit at
  identical world coordinates on every client (that is exactly what makes flat multiplayer show
  everyone the same board). So world space **is** the shared frame. Unlike LCVR there is no
  world-locked player body to offset from — each VR player is a free head above a shared table.
- `worldScale` is sent so a peer's 15 cm real hand reads the same physical size above the board
  regardless of the sender's diorama zoom; positions are absolute world.
- Seam for the future: `IBoardAnchor` (default `WorldAnchor` = identity). If a title build ever
  offsets the board per client, bind a deterministic board transform there — **the wire format
  and sampler do not change.**

---

## 4. Transport (exact FFSNet calls)

- **Send**: `FFSNet.Synchronizer.SendSideAction((GameActionType)60123, new CustomDataToken(bytes,false),
  canBeUnreliable:true, sendToHostOnly:false, targetPlayerID:int.MaxValue, 0,0,false)`
  → `NetworkActionEvent` to `GlobalTargets.Others`, `ReliabilityModes.Unreliable`.
- **Receive**: Harmony **prefix** on `FFSNet.ActionProcessor.ProcessSideAction(GameAction)`;
  match `action.ActionTypeID == 60123`, pull `((CustomDataToken)action.SupplementaryDataToken).CustomData`,
  `senderId = action.PlayerID`; return **false** to consume (modded peers).
- **Zero Bolt compile dependency**: everything above is reached by **reflection**
  (`FfsNetTransport.Resolve()` / `AccessTools`), so no `bolt.dll` reference and no csproj edit.
  If resolution fails on an unexpected build, the transport degrades to a safe no-op and logs once.
- **Gate sends** on `VRSession.IsRunning && FFSNetwork.IsOnline && LocalPlayerId > 0` (the last
  covers the join window where `MyPlayer` — which `SendSideAction` dereferences — is still null).

---

## 5. Discovery / announcement / graceful degradation

- **Lazy discovery (RepoXR)**: the first rig packet from a peer *is* the announcement — a
  `RemoteAvatar` is created on demand. No separate handshake needed for the MVP (a periodic
  15 Hz stream is its own keep-alive).
- **Magic + version gate**: `AvatarSerializer.TryRead` rejects anything else — never throws.
- **Degradation matrix**:
  - *Vanilla / non-modded peer* → receives the side action, ignores it (sentinel TargetPlayerID),
    never sends → no avatar, no desync.
  - *Flat modded peer* → does not send (not VR) so it is invisible to others, but MAY render other
    VR players' avatars (receive path is VR-independent; visuals only matter when it has a head
    camera).
  - *Offline / single-player* → `IsOnline` false → no send; no packets → no avatars.
- **Teardown**: staleness timeout (`StaleTimeoutSeconds = 3`). Covers Bolt player-left, a peer
  switching to flat, or a long stall. `NetAvatarDriver.RemovePlayer(int)` is the entry point for a
  future instant `PlayerRegistry.OnPlayerLeft` hook.

---

## 6. Remote avatar (`RemoteAvatar`)

- Per remote VR player: floating **head mask** + **two hands** under a DontDestroyOnLoad root,
  on the mod layer (`VRLayers.Apply`) so the owned head camera renders them.
- **Hands reuse `HandVisuals.Build`** (bundle glove or procedural fallback) + a `FingerCurler`
  per hand → identical to local hands. Part holders scaled by the sender's `worldScale`.
- **Head mask**: loads `Assets/Bundle/Head/VRHeadMask.prefab` from the ALREADY-LOADED mod bundle
  (`AssetBundle.GetAllLoadedAssetBundles` — never re-opens the file `HandVisuals` owns); until the
  user ships one, a low-poly placeholder head (cranium sphere + +Z visor plate) stands in.
- Interpolation: `Vector3.Lerp` / `Quaternion.Slerp` with `1-exp(-15·dt)`; first activation snaps
  to avoid an origin streak. Unlit material (the void/menu have no lights).

---

## 7. Files delivered (`src/GloomhavenVR/Net/`, namespace `GloomhavenVR.Net`)

| File | Role |
|------|------|
| `NetProtocol.cs` | magic/version/sentinels/rates/flags + the safety rationale |
| `AvatarState.cs` | `RigPose` / `HandStateSample` / `AvatarState` structs |
| `AvatarSerializer.cs` | compact allocation-free (de)serialize; shared-frame contract |
| `IBoardAnchor.cs` | shared-frame seam; `WorldAnchor` (identity) default |
| `INetTransport.cs` | transport seam + `NullNetTransport` no-op |
| `FfsNetTransport.cs` | reflection piggyback of SendSideAction + Harmony receive prefix |
| `LocalRigSampler.cs` | samples head + hands + fingers into `AvatarState` |
| `RemoteAvatar.cs` | head-mask + hands renderer with interpolation |
| `NetAvatarDriver.cs` | 15 Hz send, receive dispatch, avatar registry, staleness teardown |
| `NetModule.cs` | `IVRModule`; installs transport + driver (NOT yet registered) |

**Build status**: `dotnet build -c Release` → **0 warnings / 0 errors**.

**Registration (ONE line, do in a separate change to avoid conflicting with parallel workers):**
`src/GloomhavenVR/Plugin.cs`, `RegisterModules()` — after `Plugin.cs:359`
(`_modules.Add(new WorldUI.WorldUIModule());`):
```csharp
_modules.Add(new Net.NetModule());
```
The module is inert until this line exists.

---

## 8. Phased task breakdown

- **P0 — SCAFFOLD (done):** message schema, sampler, transport (real, reflection), receive +
  RemoteAvatar, discovery/degradation, staleness teardown, module. Builds 0/0.
- **P1 — Bring-up:** register `NetModule`; host+join a 2-player session (both VR, one is host);
  confirm host↔client hands/head render; confirm a flat player sees no change and no desync log.
- **P2 — Client↔client relay:** verify two VR CLIENTS behind a flat host see each other (see
  Risk R1). If Bolt does not relay `Others` client→client, add host-relay (send `sendToHostOnly`
  + a modded-host re-broadcast, or route via a per-connection `NetworkActionEvent.Create(conn,…)`).
- **P3 — Head "mask" asset:** user generates + ships the mask; swap placeholder for the bundle
  prefab (path already probed). Tune scale/pivot/forward per the asset contract (§10).
- **P4 — Polish:** optional handshake frame for instant capability discovery + instant
  `OnPlayerLeft` teardown; smallest-three quaternion; per-remote name label; dead-reckoning.

---

## 9. Risks / netcode unknowns

- **R1 (client→client relay) — MEDIUM.** `GlobalTargets.Others` from a client definitely reaches
  the host; whether Bolt relays it on to *other* clients (two VR clients, flat host) is unverified
  in this title. The common 2-player case (one VR player hosts) is host↔client = direct and fine.
  Mitigation in P2 above. Needs a live 3-machine test to confirm.
- **R2 (world-space sharedness) — LOW.** We assume board world coords are identical across
  clients (true for gameplay to work). If a build offsets them, bind a real board transform in
  `IBoardAnchor` — no wire change.
- **R3 (unreliable side-action volume) — LOW.** 15 Hz unreliable × players is well within Bolt's
  budget (packetSize 1200, our payload ~80 B). If event throttling appears, drop to 10 Hz or
  coalesce. Each packet is independent (no reliability/ordering dependence).
- **R4 (reflection boxing) — LOW.** `ProcessSideAction` prefix boxes one int per side action
  (~30–45/s incl. our stream); send boxes only the token byte[] copy (constants pre-boxed). No
  per-frame allocation. Could be delegate-cached later.
- **R5 (join-window coroutines on vanilla) — LOW/COSMETIC.** A vanilla peer that receives our
  packet before its own `MyPlayer` initializes spawns a harmless `ProcessSideActionCoroutine`
  that resolves to "ignore" once `MyPlayer` exists. Brief, only at join, no state effect.
- **R6 (crossplay / platform) — UNKNOWN.** Untested across platforms; the side-channel is
  platform-agnostic (plain bytes) but real hardware testing is required (Quest 3 + Virtual Desktop
  per project memory).

---

## 10. Head "mask" asset contract (mirrors `unity/hand-prep/rig_hand.py` + the 100× armature note)

Deliver `Assets/Bundle/Head/VRHeadMask.prefab` inside `gloomhavenvr.bundle`
(`BepInEx/plugins/GloomhavenVR/`). Contract:
- **Format**: FBX authored in Unity's companion project, exported into the AssetBundle at
  `Assets/Bundle/Head/VRHeadMask.prefab` (the exact path `RemoteAvatar` probes).
- **Poly budget**: ≤ ~1.5k triangles (a floating head seen at table distance; keep it cheap —
  up to `maxPlayers-1` render simultaneously).
- **Pivot / origin**: at the **eye midpoint** (between the eyes). The prefab is placed directly at
  the received head-camera world pose, so pivot == the tracked eye point.
- **Orientation / forward / eyes**: **+Z is forward (the gaze/where the eyes look)**, **+Y is up**,
  matching Unity camera convention (`HeadCamera.forward == +Z`). Eyes/visor on the +Z face.
- **Scale**: modelled at **real-world metres, ~0.18–0.22 m tall at scale 1** (like the hands). The
  receiver multiplies by the sender's `worldScale`; do not pre-bake the diorama scale.
- **Materials**: **unlit** (e.g. an unlit/emissive shader) — the VR void and menu scenes have no
  lights, so a lit shader renders black (same rule as the hands, `HandVisuals.CreateHandMaterial`).
- **Armature (only if animated)**: follow the memory note / `rig_hand.py` — author at **100×**,
  export FBX with the bundle-build pipeline (`unity-bundle-build`), so the runtime armature scale
  matches the hands. A static mask needs no armature.
- No colliders (cosmetic; the mod strips colliders on procedural hand parts for the same reason).
```
