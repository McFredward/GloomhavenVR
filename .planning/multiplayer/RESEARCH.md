# VR Embodiment Sync over Host Netcode — Research (RepoXR / LCVR / Demeo)

> Salvaged from a research sub-agent. Gloomhaven Digital netcode = **Photon Bolt**, custom
> `FFSNet` layer at `decompiled/GH.Runtime/FFSNet/` (`FFSNetwork.cs`, `NetworkManager.cs`,
> `NetworkBehaviour.cs`, `BitCompressionTable.cs`). RepoXR is the closest blueprint (Photon).
> LCVR contributes playspace-local coords + finger sync.

## Reusable architecture (both DaXcess mods share this)

```
LOCAL VR CLIENT                                  REMOTE CLIENT (VR or flat, modded)
sample local rig (head + 2 hands + fingers)      patched receive hook
  in PLAYSPACE-LOCAL coords                        magic-number check → is this ours?
  → serialize (compact binary struct)               no → ignore (vanilla-safe)
  → piggyback an EXISTING per-player stream          yes → deserialize → per-remote
    the host already relays                          "NetworkPlayer" avatar → Lerp/Slerp → IK
```

Five invariants:
1. Never add a new networked object/prefab — ride an existing per-player stream (host stays vanilla).
2. Magic number + protocol version prefixes every payload (graceful degradation).
3. Only the local owner writes; identity = host's existing player/actor IDs.
4. The game already syncs the player's world position — you only add the limbs, relative to that anchor.
5. Interpolate on the receiver (fixed Lerp/Slerp), never snap raw values onto transforms.

## RepoXR (R.E.P.O., Photon PUN) — closest analog
- Repo: https://github.com/DaXcess/RepoXR — `Source/Networking/{NetworkSystem,NetworkPlayer}.cs`,
  `Source/Networking/Frames/RPC.cs`, `Source/Player/VRRig.cs`, `Source/Patches/Player/PlayerNetworkPatches.cs`.
- Transport: Harmony-postfix `PlayerLocalCamera.OnPhotonSerializeView`; when `stream.IsWriting`
  append own bytes to the SAME `PhotonStream` (`WriteAdditionalData` / `ReadAdditionalData`). No custom
  RaiseEvent — reuse the existing owner→others channel. Rate = Photon serialization rate (~10 Hz).
- Custom RPC layer on top: `[XRRpc]` methods hashed (type+method), enqueued as `RPCFrame` flushed in next
  WriteAdditionalData; dedup replaces same-type/method frames (calling UpdateRigRPC every frame → one packet).
  Wire: `long MAGIC ("REPOXR")`, `int PROTOCOL_VERSION`, `int rpcCount`, per rpc `long typeHash, long methodHash, int argCount, args…`.
- Rig schema (hands only, WORLD space): `UpdateRigRPC(Vector3 leftPos, Quaternion leftRot, Vector3 rightPos, Quaternion rightRot)`
  sent from `VRRig.UpdateArms()` each LateUpdate. Head NOT sent (game already replicates the camera/avatar).
  Low-rate: UpdateDominantHandRPC(bool), UpdateHeadlampRPC(bool), UpdateMapRPC, UpdateEyeTrackingRPC(Vector3).
- Discovery: announcement frame (typeHash==0 && methodHash==0) = "I am VR"; remote lazily creates
  `GameObject("VR Player Rig - <name>").AddComponent<NetworkPlayer>()`. Re-announced on any player join.
- Remote avatar: `NetworkPlayer` each frame `transform.position = playerAvatarVisuals.transform.position`
  (anchors to game-replicated body), places hand targets at received world pos; cheap arm solver `armRoot.LookAt(target)`.
- Interpolation: `Vector3.Lerp(cur, target, 15*Time.deltaTime)` / `Quaternion.Slerp(..., 15*dt)`.
- Degradation: `if ((long)stream.PeekNext() != MAGIC) return;`. `view.IsMine` gates sending.

## LCVR (Lethal Company, Unity Netcode) — no-shared-channel workaround
- Repo: https://github.com/DaXcess/LCVR — `Source/Networking/{NetworkSystem,VRNetPlayer,Serialization,Channel}.cs`, `Source/Player/VRPlayer.cs`.
- Transport: (ab)uses Dissonance voice relay — postfix `BaseClient<…>.ProcessReceivedPacket`, magic `ushort 51083`,
  require `messageType >= 16` (voice types <16 pass untouched). Send `network.SendReliableP2P`.
- Discovery: periodic `HandshakeRequest{protocolVersion}` → `HandshakeResponse{bool inVR}` → `subscribers` set;
  inVR → add `VRNetPlayer`. Vanilla peers never answer.
- Channel pub/sub (`ChannelType {Rig, SpectatorRig, …}` + optional instanceId, length-prefixed, PACKET_MAX_SIZE guard).
- Rig schema (fingers + PLAYSPACE-LOCAL coords):
  `Rig { Vector3 R/LHandPosition, R/LHandEulers; Fingers R/L; Vector3 CameraEulers; Vector3 CameraPosAccounted, ModelOffset, SpecialAnimationPositionOffset; CrouchState; Vector3 RotationOffset; float CameraFloorOffset; }`
  `Fingers { byte Thumb, Index, Middle, Ring, Pinky }` (curl quantized to byte). Hand pos = `controller.localPosition`
  (local to XR Origin, NOT world). Send every frame at tail of VRPlayer.Update().
- Remote avatar: `VRNetPlayer` rebuilds a fake XR Origin hierarchy scaled by SCALE_FACTOR, parented under the game
  player, offset each frame by received CameraFloorOffset/ModelOffset/crouch; real `TwoBoneIKConstraint` drives arm bones;
  `FingerCurler.SetCurls(Fingers)`. Playspace-local → differing room-scale origins never matter.

## Demeo / Photon baseline (native)
- Only three positions sent: head + 2 hands, interpolated on remotes. Validates "head + 2 hands, interpolate" as MVP.
- https://doc.photonengine.com/fusion/v1/industries-samples/fusion-vr-shared-meta-avatar-integration

## Recommendation for Gloomhaven VR (Photon Bolt / FFSNet)
- Transport, preferred order:
  1. Piggyback an existing Bolt entity's per-tick state serialization (Bolt per-entity serialize callbacks /
     `IProtocolToken` on state/events) — append rig token, no host mod. RepoXR "append to OnPhotonSerializeView" move.
  2. Else Bolt events: `FFSNetwork.SendGameAction(..., byte[] customBinaryData, IProtocolToken supplementaryDataToken)`
     and `SendSideAction(..., bool canBeUnreliable, ..., IProtocolToken)`. Use `SendSideAction canBeUnreliable:true`
     at fixed rate for rig updates. Prefer unreliable + dedup over reliable-every-frame.
- Schema: head rot AND pos (Gloomhaven is top-down/table, NOT first-person-locked, so send head position too) +
  2 hand pos/rot, optional 5 quantized finger bytes. Use LCVR-style PLAYSPACE-LOCAL coords anchored to a SHARED
  table origin — the board `_root` frame (memory notes it exists) — so each player's different physical playspace is fine.
- Discovery/degradation: magic number + protocol version + announcement frame; readers peek+bail if absent
  → vanilla/flat clients untouched; flat modded clients render VR hands without sending.
- Send rate: ~10–20 Hz unreliable (or once per Bolt serialization tick with dedup); interpolate on receiver (~15·dt).
- Remote avatar: per-player rig object anchored to the shared board frame (mirror NetworkPlayer/VRNetPlayer);
  floating hands + head "mask" (Demeo style — no humanoid IK needed for top-down). Tear down on Bolt player-left
  (`NetworkCallbacks`/`HostCallbacks` in FFSNet).

## Local reference clones
- LCVR: /tmp/claude-1000/-home-claw-gloomhaven-vr/fa97d5b8-4ff0-4338-ace7-e1f738988a35/scratchpad/LCVR
- RepoXR: .../scratchpad/research/repoxr
- Gloomhaven netcode: /home/claw/gloomhaven_vr/decompiled/GH.Runtime/FFSNet/

---

## Verified injection point (against the ACTUAL decompiled source)

> Paths below are in the main repo (`decompiled/` is gitignored, present only there):
> `/home/claw/gloomhaven_vr/decompiled/GH.Runtime/...`. The research draft named
> `FFSNetwork.cs` in `FFSNet/`; the real layout differs (see below).

### Netcode shape
- **Photon Bolt**, wrapped by the `FFSNet` layer. `FFSNet.NetworkCallbacks : GlobalEventListener`
  (`[BoltGlobalBehaviour]`) is the global Bolt event sink (`FFSNet/NetworkCallbacks.cs:10-11`).
- `FFSNetwork` (the static facade) is at `GH.Runtime/FFSNetwork.cs` (NOT under `FFSNet/`).
  `FFSNetwork.IsOnline` (`:25`), `IsHost`/`IsClient` (`:37-39`).
- The **send** API is `FFSNet.Synchronizer` (NOT `FFSNetwork`), at `FFSNet/Synchronizer.cs`.

### SEND — carrier confirmed
`FFSNet.Synchronizer.SendSideAction(GameActionType actionType, IProtocolToken supplementaryDataToken = null, bool canBeUnreliable = false, bool sendToHostOnly = false, int targetPlayerID = 0, int dataInt = 0, int dataInt2 = 0, bool dataBool = false)`
— `FFSNet/Synchronizer.cs:23-32`. Internally:
```
NetworkActionEvent evt = NetworkActionEvent.Create(sendToHostOnly ? OnlyServer : Others,
                             canBeUnreliable ? Unreliable : ReliableOrdered);
evt.Token = new NetworkAction(actionType, PlayerRegistry.MyPlayer, supplementaryDataToken,
                              targetPlayerID, dataInt, dataInt2, dataBool);
evt.Send();
```
- `NetworkAction : IProtocolToken` carries `ActionTypeID:int`, `PlayerID:int` (sender),
  `DataToken:IProtocolToken`, `TargetPlayerID:int`, `DataInt/DataInt2:int`, `DataBoolean:bool`
  (`FFSNet/NetworkAction.cs`). Its `DataToken` is our payload slot.
- Payload carrier = **`FFSNet.CustomDataToken(byte[] customData, bool compressData=false)`**
  (`FFSNet/CustomDataToken.cs`), holding a `byte[] CustomData`. It is **already registered with
  Bolt on every client** (`BoltNetwork.RegisterTokenClass<CustomDataToken>()`,
  `NetworkCallbacks.cs:22`), so **vanilla peers deserialize it without error**. Using an
  UNregistered custom token would corrupt the packet stream on vanilla peers → this is why we
  reuse CustomDataToken.
- Sender identity requires `PlayerRegistry.MyPlayer != null` (NetworkAction dereferences it) —
  gate sends on that.

### RECEIVE — hook confirmed
Incoming side actions land at `NetworkCallbacks.OnEvent(NetworkActionEvent evnt)`
(`FFSNet/NetworkCallbacks.cs:130-156`), which does
`ActionProcessor.ProcessSideAction(new GameAction(evnt))`. We **Harmony-prefix
`FFSNet.ActionProcessor.ProcessSideAction(GameAction action)`** (`FFSNet/ActionProcessor.cs:175`):
`GameAction` is a `GH.Runtime` type (referenceable), and this is the single chokepoint every
inbound side action passes. The `GameAction(NetworkActionEvent)` ctor copies
`ActionTypeID`, `PlayerID`, `SupplementaryDataToken (= DataToken)`, `TargetPlayerID`
(`GameAction.cs:1022-1032`).

### Why vanilla peers are SAFE (no desync) — the critical proof
`ProcessSideAction` (`ActionProcessor.cs:175-201`) only calls `action.Execute()` when
`action.TargetPlayerID == 0 || action.TargetPlayerID == PlayerRegistry.MyPlayer.PlayerID`
(`:188`); otherwise it logs *"Ignoring SideAction …"* and returns. And `GameAction.Execute()`
**throws** on an action type not in its delegate dictionary (`GameAction.cs:1047-1056`), which
`ProcessSideAction` would turn into `FFSNetwork.HandleDesync`. Therefore, stamping every rig
packet with **`TargetPlayerID = int.MaxValue`** (no real player owns it) makes **vanilla peers
skip `Execute()` entirely → no desync**. Modded peers additionally intercept via the Harmony
prefix before the vanilla body runs. Two independent safety nets, plus the magic/version prefix
in the payload.

### Player enumeration / identity
- `FFSNet.PlayerRegistry` (static, `FFSNet/PlayerRegistry.cs`):
  `AllPlayers : List<NetworkPlayer>` (`:13`), `MyPlayer` (local, `:86`), `HostPlayer` (`:108`),
  `HostPlayerID = 1` (`:37`), `GetPlayer(int playerID)` (`:261`).
- `NetworkPlayer` (`FFSNet/NetworkPlayer.cs`): `PlayerID` (`:29`), `IsClient => PlayerID > 1`
  (`:31`). Local vs remote = compare against `PlayerRegistry.MyPlayer` (avoids touching the
  Bolt `entity`). PlayerID = the Bolt connectionId (host = 1).

### Player join / left callbacks
- **Join**: `NetworkPlayer.OnInitialized()` → `PlayerRegistry.AllPlayers.Add(this)` and
  `PlayerRegistry.OnPlayerJoined?.Invoke(this)` (`NetworkPlayer.cs:382-385`).
- **Left**: `NetworkPlayer.Detached()` → `PlayerRegistry.AllPlayers.Remove(this)` then
  `PlayerRegistry.OnPlayerLeft?.Invoke(this)` (`NetworkPlayer.cs:230-232`).
- `PlayerRegistry.OnPlayerJoined/OnPlayerLeft` are settable `PlayersChangedEvent`
  (`delegate void(NetworkPlayer)`) statics (`PlayerRegistry.cs:126-128`) — subscribable WITHOUT
  Harmony (combine, don't clobber; they are nulled on `PlayerRegistry.Reset()` at shutdown).
- Server-only Bolt conn callbacks: `HostCallbacks.Connected/Disconnected(BoltConnection)`
  (`FFSNet/HostCallbacks.cs:9,25`), overridden by `GHHostCallbacks` (`FFSNet/GHHostCallbacks.cs:27`).
- **Scaffold choice**: teardown is driven by a **staleness timeout** (packets stop → avatar
  removed) rather than the `OnPlayerLeft` delegate, because subscribing to a `NetworkPlayer`-typed
  delegate would pull the Bolt `EntityBehaviour<IPlayerState>` base into the mod's compile graph.
  `NetAvatarDriver.RemovePlayer(int)` is the ready entry point if instant teardown is later wired
  via a reflection subscription to `OnPlayerLeft`.

### Bolt assemblies
`bolt.dll` (`Photon.Bolt`), `bolt.user.dll`, `PhotonBolt.dll` ship in the game's Managed dir.
The scaffold deliberately does **NOT** reference them: the transport reaches
`Synchronizer.SendSideAction` / `CustomDataToken` / `GameAction` / `FFSNetwork` /
`PlayerRegistry` **through reflection** (`FfsNetTransport.Resolve()`), so no csproj change is
needed and the build has zero Bolt coupling. (Adding a `bolt.dll` reference and calling these
directly is a valid future simplification if desired.)

### Native `Avatar` field — NOT what we need
`NetworkPlayer.Avatar : Sprite` + `AvatarUpdated : bool` (`NetworkPlayer.cs:83-85`) is the 2D
**profile picture** (`PlatformLayer.UserData.GetAvatarForNetworkPlayer`), unrelated to VR
embodiment. Do not repurpose it.
