# Load-Bearing Behaviour Registry — `Net/` and `Rig/`

> Companion to `CHARTER.md`. Written **before** any refactor touches these two subsystems.
>
> Scope: `src/GloomhavenVR/Net/` (29 files, ~9 100 lines) and `src/GloomhavenVR/Rig/`
> (10 files, ~2 900 lines).
>
> **Symbol names only — no line numbers.** Files are being edited concurrently; a line
> number in this document would be a lie within a day.
>
> Reading order: the **WIRE FORMAT** section first. It is frozen. Everything after it is
> ordinary (if hard-won) behaviour; the wire is the one part of this codebase where a
> mistake is invisible to the compiler, invisible in single-player, invisible on the
> sender's own screen, and only shows up as *other people's* clients being subtly wrong.

---

# PART I — THE WIRE FORMAT (FROZEN)

## 0. Why this section exists

Two byte-oriented packets ride the game's own Photon-Bolt "side action" channel. There is
no schema language, no version negotiation beyond a single byte, no checksum on the body,
and **no test**. Both serializers are hand-written index-walkers (`int i = 0; buffer[i++] = …`).

Consequences that must be internalised before editing anything in
`AvatarSerializer` / `PresenceSerializer` / `NetProtocol`:

1. **Field order is the schema.** There are no tags. Moving a `buffer[i++]` one statement
   earlier or later re-interprets every subsequent byte on every peer.
2. **A flag bit's numeric value is the schema.** `FlagCardFx = 1 << 5` is not an
   implementation detail; it determines *where in the byte stream* the card-FX block sits,
   because blocks are ordered by flag-bit index.
3. **Both sides must change together, and old builds exist in the wild.** The
   compatibility contract below is what makes that survivable — it is not decoration.
4. **Nothing here can be validated locally.** A sender never parses its own packet. Every
   bug in this file manifests only on *someone else's* headset.

## 1. Transport envelope

| Item | Value | Symbol |
|---|---|---|
| Channel | Photon-Bolt side action, `GlobalTargets.Others`, **Unreliable** | `FfsNetTransport.Send` |
| Carrier | `FFSNet.CustomDataToken(byte[], compress:false)` | `FfsNetTransport.Send` |
| Action type | `60123` (out of range of the real `GameActionType` enum, max real ≈126) | `NetProtocol.SentinelActionTypeId` |
| TargetPlayerID | `int.MaxValue` | `NetProtocol.SentinelTargetPlayerId` |
| Receive hook | Harmony **prefix** on `FFSNet.ActionProcessor.ProcessSideAction`, returns **false** for our packets only | `FfsNetTransport.ReceivePrefix` |
| Access | 100 % reflection — zero compile-time dependency on bolt.dll / FFSNet | `FfsNetTransport.Resolve` |

**Three independent safety nets** (all three must survive any refactor):

1. The sentinel `TargetPlayerID` makes *vanilla* `ProcessSideAction` take its
   "Ignoring SideAction" branch — it never calls `GameAction.Execute()`, so an unmodded
   peer cannot desync on our bytes.
2. The Harmony prefix consumes the packet on a *modded* peer before the vanilla body runs.
3. Magic + version prefix every payload; the readers bail on mismatch.

Any one of these alone would probably do. All three are kept because the failure mode
(a desynced multiplayer campaign) is unrecoverable for the user.

## 2. Byte layout — common header

Both packets start with the same 6 bytes.

| Offset | Size | Field | Value / notes |
|---|---|---|---|
| 0 | 4 | `magic` | `NetProtocol.Magic = 0x47565231` written **little-endian** by `AvatarSerializer.WriteU32`. On the wire the bytes are therefore `31 52 56 47` = `'1' 'R' 'V' 'G'`. The *constant* reads "GVR1"; the *byte stream* reads "1RVG". Do not "fix" one to match the other. |
| 4 | 1 | `version` | `NetProtocol.Version = 3`. Readers reject any other value outright. |
| 5 | 1 | `type` | `NetProtocol.MsgRig = 0` or `NetProtocol.MsgExtras = 1`. |

`NetPacket.PeekType` validates offsets 0–5 and returns the type byte (or −1) **without
parsing the body**, so `NetAvatarDriver.OnPacketReceived` can route without touching either
serializer. Minimum length for a peek is 6.

## 3. Packet type 0 — RIG (`AvatarSerializer`)

Rate: `NetProtocol.SendRateHz = 15f`. `MaxSize = 132` (real worst case 127).

### 3a. Fixed part (12 bytes, always present)

| Offset | Size | Field | Symbol / notes |
|---|---|---|---|
| 0..5 | 6 | header | type == `MsgRig` |
| 6 | 1 | `flags` | see 3b |
| 7 | 1 | `maskId` | clamped `0 .. HeadMaskLibrary.MaskCount-1` **on write and on read** |
| 8..11 | 4 | `worldScale` | `float32`, sender's rig `lossyScale.x`; ≤0 / NaN / Inf → `1f` on both sides |

### 3b. Rig flag byte (offset 6) — **FULL, all 8 bits spent**

| Bit | Mask | Constant | Meaning | Payload it demands |
|---|---|---|---|---|
| 0 | 0x01 | `FlagHeadValid` | head pose present | 20 B pose |
| 1 | 0x02 | `FlagLeftTracked` | left hand present | 20 B pose (+5 B if bit 3) |
| 2 | 0x04 | `FlagRightTracked` | right hand present | 20 B pose (+5 B if bit 3) |
| 3 | 0x08 | `FlagHasFingers` | finger curls included | 5 B **per tracked hand** |
| 4 | 0x10 | `FlagHeldFigure` | held-figure block follows the hands | 24 B (`int32` actorId + 20 B pose) |
| 5 | 0x20 | `FlagDominantRight` | sender's dominant hand is RIGHT | 0 B (pure flag) |
| 6 | 0x40 | `FlagHandStyle` | trailing hand-style byte present | 1 B |
| 7 | 0x80 | `FlagHeldCard` | trailing held-card pose present | 20 B |

**Bit 5 (`FlagDominantRight`) is deliberately duplicated** into the extras packet at a
*different* bit index (`FlagExtrasDominantRight = 1 << 1`). That is not a mistake — the two
flag bytes are independent namespaces and were laid out at different times.

**Bit 6 is always set on write** (`flags |= NetProtocol.FlagHandStyle;` unconditionally in
`AvatarSerializer.Write`). The style byte is therefore always on the wire from a current
build. Reading still honours the flag, so a pre-style sender parses.

### 3c. Rig body, in exact write order

```
if FlagHeadValid    : pose 20 B                                   (head)
if FlagLeftTracked  : pose 20 B  [+ if FlagHasFingers: 5 B curls]  (left)
if FlagRightTracked : pose 20 B  [+ if FlagHasFingers: 5 B curls]  (right)
if FlagHeldFigure   : int32 actorId 4 B + pose 20 B               = 24 B
                      byte handStyle                              (1 B — ALWAYS written)
if FlagHeldCard     : pose 20 B                                   (held card)
```

Order is load-bearing twice over: fingers come **immediately after their own hand's pose**
(not batched at the end), and the held-card pose comes **after** the style byte so that a
pre-held-card reader still finds the style byte at the offset it expects.

### 3d. Pose encoding (shared by both packets)

| Part | Size | Encoding |
|---|---|---|
| position | 12 B | 3 × `float32` little-endian, raw |
| rotation | 8 B | 4 × `int16` = `round(clamp(q_i/‖q‖, −1, 1) · 32767)`, order **x, y, z, w** |

- Written by `AvatarSerializer.WritePose`, exposed to `PresenceSerializer` as
  `WritePoseShared` / `ReadPoseShared`. **Both packets must keep using the same primitive.**
- A quaternion with magnitude `< 1e-6` degrades to identity on write *and* on read.
- Read re-normalises. Worst-case error ≈ 0.006 rad — deliberately accepted.
- `QuatScale = 32767f` is a wire constant. Changing it to `32768` silently rotates every
  remote hand by a tiny amount on mixed builds.

Finger curls: 5 bytes, order **Thumb, Index, Middle, Ring, Pinky**, `byte = clamp(round(curl·255), 0, 255)`,
decoded as `b / 255f`.

### 3e. Rig read-side length validation

```
need = (head ? 20 : 0)
     + (left  ? 20 + (fingers ? 5 : 0) : 0)
     + (right ? 20 + (fingers ? 5 : 0) : 0)
     + (heldFigure ? 24 : 0)
     + (style ? 1 : 0)
     + (heldCard ? 20 : 0)
```

Validated **once**, before any body read, against `length - 12`. This sum must be edited in
lock-step with the write order. It validates *only what this reader's own known flags
demand* — that is the forward-compatibility contract, not an oversight.

## 4. Packet type 1 — EXTRAS (`PresenceSerializer`)

Rate: `NetProtocol.ExtrasSendRateHz = 5f`, **plus on-change pre-emption** (see §II).
`MaxSize = 44` (real worst case 39).

### 4a. Extras flag byte (offset 6) — **FULL, all 8 bits spent**

| Bit | Mask | Constant | Meaning | Payload it demands |
|---|---|---|---|---|
| 0 | 0x01 | `FlagHasBoard` | control-board pose + scale present | 24 B (20 B pose + 4 B `float32` scale) |
| 1 | 0x02 | `FlagExtrasDominantRight` | sender's dominant hand is RIGHT | 0 B |
| 2 | 0x04 | `FlagExtrasGhostHand` | ghost-hand strength byte follows | 1 B |
| 3 | 0x08 | `FlagItemFan` | item-fan count byte follows | 1 B |
| 4 | 0x10 | `FlagItemFanHeld` | item fan is hand-held, not board-anchored | 0 B |
| 5 | 0x20 | `FlagCardFx` | card-FX block follows | 2 B (`fxSeq` + `fxEndpoints`) |
| 6 | 0x40 | `FlagItemFanLeft` | hand-held item fan is on the LEFT hand | 0 B |
| 7 | 0x80 | `FlagPileBrowse` | **a trailing BLOCK follows** (see 4d) | 2 B + conditional 1 B |

Write-side gating rules that are part of the contract:

- `FlagItemFanHeld` is only set when `HasItemFan` is also set.
- `FlagItemFanLeft` is only set when `HasItemFan && ItemFanHeld`.
- `FlagPileBrowse` is set when **any** of `HasPileBrowse`, `HasMaskSize`, or
  `BoardStyleCode != BoardStyleDefaultCode` is true. It no longer means "a browse fan is open".

### 4b. Extras body, in exact write order

```
if FlagHasBoard          : pose 20 B + float32 boardScale 4 B     = 24 B
                           byte handCardCount                       (1 B — ALWAYS written)
--- ADDITIVE BLOCKS, IN FLAG-BIT ORDER ---
if FlagExtrasGhostHand   : byte ghostStrength                       (1 B)
if FlagItemFan           : byte itemCardCount                       (1 B)
if FlagCardFx            : byte fxSeq + byte fxEndpoints            (2 B)
if FlagPileBrowse        : byte A (kindFlags) + byte B (count)      (2 B)
    if byte A bit 4      : byte C maskSizeCode                      (1 B, INSIDE the block)
```

`handCardCount` sits **before** every additive block and after the optional board block.
That position is the anchor the whole compatibility scheme hangs off: a reader from any
build finds it at the same offset.

### 4c. Payload byte A (the trailing block's first byte) — bit map

| Bits | Mask | Constant | Field |
|---|---|---|---|
| 0..1 | 0x03 | `PileBrowseKindDiscard=0`, `…Burnt=1`, `…Items=2` | pile kind |
| 2 | 0x04 | `PileBrowseHeldBit` | fan is hand-held (else board-anchored) |
| 3 | 0x08 | `PileBrowseLeftBit` | that hand-held fan is on the LEFT hand |
| 4 | 0x10 | `PileBrowseMaskSizeBit` | **a mask-size byte (byte C) follows** |
| 5..6 | 0x60 | `PileBrowseBoardStyleShift = 5`, `PileBrowseBoardStyleMask = 0x60` | control-board style, 0 Oak / 1 Steel / 2 Bronze (2 bits ⇒ 4 boards max) |
| 7 | 0x80 | — | **reserved, must be written 0** — the last free extension slot in the entire protocol |

Byte B = browse card count, `0..255`, clamped. Written as `0` when the block is present for
mask-size/board-style reasons only.

Byte C = `EncodeMaskSize(size)` = `clamp(round(size·100), 25, 255)` ⇒ 0.25× .. 2.55× in
0.01 steps. `DecodeMaskSize` maps any code `< 25` back to the default 1.00× rather than
collapsing a peer's head. `MaskSizeDefaultCode = 100`.

### 4d. The "block header, not a boolean" decision

Flag bit 7 was spent on **"a block follows"** rather than on "a browse fan is open". This
is the single most consequential design decision in the protocol and it is why two later
features (head-mask size, control-board style) shipped with **no wire-version bump**.

Its consequences, all of which are contract:

- A **size-only** or **style-only** packet writes the block with the pile-browse sub-fields
  zeroed and **byte B = 0**.
- **Every reader that has ever shipped bit 7 requires `count > 0` before it renders a fan**
  (`RemoteAvatar.SetExtras` sets `PileBrowseOpen = p.HasPileBrowse && p.PileBrowseCardCount > 0`;
  `RemoteBrowserFan` gates on it too). A zero-count block therefore reads as "no fan"
  everywhere, past and present.
- Board style spends **zero** extra bytes and **zero** presence bits: code 0 == Oak == "field
  absent". The packet length does not change at all, so even a byte-length-sensitive reader
  is unaffected.
- Mask size is sent **only when non-default**, so a default-size player emits bytes
  identical to pre-mask-size builds.

### 4e. Extras read-side length validation — two-level, deliberately

```
need = (hasBoard ? 24 : 0) + 1
     + (ghost ? 1 : 0) + (itemFan ? 1 : 0) + (cardFx ? 2 : 0) + (pileBrowse ? 2 : 0)
```

The **mask-size byte is NOT in this sum**. It cannot be: the bit that demands it lives
inside byte A, which has not been read yet at validation time. It is therefore validated
separately, immediately before it is consumed, inside the `if (pileBrowse)` branch:
`if (length < i + 1) return false;`

This two-level validation is the same contract one level down, and it is what lets a future
sender append yet another field behind byte C without breaking this reader.

## 5. THE BACKWARD/FORWARD COMPATIBILITY CONTRACT (verbatim rules)

These four rules together are the reason the protocol has survived six feature additions on
one version number. **All four must hold simultaneously; each is useless alone.**

1. **Additive blocks are appended after every field a pre-existing reader knows.**
2. **Additive blocks are written AND read in FLAG-BIT ORDER** (ghost → item fan → card FX →
   pile-browse block). This is the rule that lets independently developed extensions share
   one packet without any of them knowing about the others.
3. **A reader validates only the length ITS OWN known flags demand** — never the total
   packet length, never `length == expected`.
4. **An absent flag must decode to the safe default, and the safe default must be
   visually identical to what the sender intends.** Absent ghost ⇒ solid hands. Absent item
   fan ⇒ no fan. Absent mask size ⇒ 1.00×. Absent board style bits ⇒ Oak. Absent card FX ⇒
   no flight. This is why "send only when non-default" is safe for mask size and board style.

Corollary — **there is exactly one free bit left in the entire protocol**: byte A bit 7.
Both flag bytes are full. The next extras extension must go there (as another sub-block
header), or it needs a version bump and a coordinated release.

### 5a. Version history — and why only one field addition ever bumped it

| Version | What changed |
|---|---|
| v1 | Original rig packet: magic, version, flags (head/left/right/fingers), worldScale, poses |
| v2 | Added the 1-byte head-mask id to the **fixed header** — the *only* field addition that ever required a bump, precisely because it went into the fixed header rather than behind a flag |
| v3 | Inserted the **message-type byte** after the version, extended the rig packet with the held-figure block + dominant-hand flag, and added the **second (extras) packet type** |
| v3, unchanged | hand style, held card, ghost strength, item fan (+held/left), card FX, pile-browse block, head-mask size, control-board style — **eight additive features, zero bumps** |

The lesson is in the table: v2's bump is what motivated the additive-flag discipline that has
held for every feature since. Anything added to a *fixed* header costs a version bump and
breaks every existing peer.

### 5b. Known wire cost of features deliberately NOT shipped

Recorded so a future budget discussion starts from evidence rather than a guess:

| Feature | Cost |
|---|---|
| Hand-fan hover split / insertion gap | 1 byte (hovered index) + 1 reserved bit |
| Gaze-bias yaw opt-in | 1 bit (the sender's toggle; the yaw itself is computable from the synced gaze) |
| Empty-fan hint | **not expressible** with any single field — its trigger is the palm-roll gate EDGE, and the hand-card count cannot distinguish "gate opened with zero cards" from "fan closed" |
| A fifth control board | 1 trailing byte behind byte C (the 2-bit field holds four) |

## 6. Card-FX event encoding

| Item | Value | Symbol |
|---|---|---|
| `fxEndpoints` | low nibble = FROM, high nibble = TO | `NetCardFx.Report`, `NetCardFx.From/To` |
| `fxSeq` | wrapping counter, incremented **only** on a real dequeue | `NetCardFx.TryDequeue` |
| Anchor ids | `HandFan=0, Slot0=1, Slot1=2, Discard=3, Burnt=4, Items=5, Board=6` | `CardFxAnchor` |
| Unknown id | clamps to `CardFxAnchor.Board` | `NetCardFx.Clamp` |
| Flight duration | `NetProtocol.CardFxSeconds = 0.4f`, matched to local `CardsDriver.FlyToPileSeconds` | `RemoteCardFx.Tick` |

Anchors are **semantic, never positional**. The receiver resolves each anchor against the
*sender's own* synced hand / board pose. 4 bits instead of 12 bytes, and the flight lands
where that player's board actually is.

## 7. Wire constants that are append-only, never renumber

- `CardFxAnchor` members.
- `PileBrowseKindDiscard/Burnt/Items` — these mirror `Cards.PileKind`'s member order.
  `NetAvatarDriver.TickExtrasSend` casts `(int)browseNow.Kind.Value` straight onto the wire
  with the comment *"PileKind order == PileBrowseKind\* wire order"*. **Reordering
  `Cards.PileKind` — a file outside this subsystem — silently corrupts this field.**
- `Cards.ControlBoard` ids (0 Oak / 1 Steel / 2 Bronze) — same coupling via
  `NetProtocol.EncodeBoardStyle` / `LocalRigSampler.LocalBoardStyle`.

  **Both of these two are now defended in code** (batch B1), because the danger sits in a file
  outside this subsystem where none of this document is visible:
  1. Both enums number themselves explicitly (`= 0, 1, 2`), which makes an alphabetising sort
     harmless — it was previously the most likely way to break either.
  2. Both carry the wire-constant warning **at the enum**, in `Cards/`.
  3. Both are checked at compile time, at the cast site:
     `NetAvatarDriver.PileKindWireOrderGuard` and `LocalRigSampler.ControlBoardWireOrderGuard`.
     A renumber makes the divisor 0 and the build fails with CS0020 instead of shipping a
     desync. Verified by deliberately breaking each enum. **Known gap:** `Net/` names no
     constant for Steel or Bronze, so swapping only those two is caught by (1)+(2), not by (3).
  A legal APPEND (a fourth pile, a fourth board) still compiles — verified.
- `Hands.HandStyle` ids (0 Glove / 1 Plate / 2 Arcane).
- `HeadMaskLibrary` mask ids 0..2.
- `NetProtocol.Magic`, `Version`, `MsgRig`, `MsgExtras`, `SentinelActionTypeId`,
  `SentinelTargetPlayerId`.

## 8. What NEVER goes on the wire (standing rules)

| Rule | Enforcement |
|---|---|
| **No card identity, ever.** Hand fan, item fan, browse fan, held card — all are counts and poses only; peers render card BACKS. | `RemoteHandFan`, `RemoteItemFan`, `RemoteBrowserFan`, `RemoteAvatar.BuildHeldCardSlab` |
| **Reveals happen only through `RevealGate`**, off the game's own authoritative phase — identical to the vanilla client rule. | `RevealGate.ShowRoundCardFronts` |
| **No authoritative game state.** Every packet is cosmetic; nothing mutates game state. | whole subsystem |
| **No world positions for card flights** — semantic anchors only. | `CardFxAnchor` |
| Figure transforms are *not* networked by the game either; the held-figure block is cosmetic mirroring only. | `NetFigures` header comment |

---

# PART II — INVARIANT ENTRIES

## Net — wire and protocol

### Additive blocks in flag-bit order
- **Where:** `PresenceSerializer.Write`, `PresenceSerializer.TryRead`, `AvatarSerializer.Write`, `AvatarSerializer.TryRead`
- **Rule:** Optional blocks are emitted and consumed strictly in ascending flag-bit order, appended after every field a pre-existing reader knows. `handCardCount` (extras) and the hand-style byte (rig) are the fixed anchors.
- **Why:** It is the only thing that lets independently developed extensions share one packet without a version bump. Reordering re-interprets every trailing byte on every peer — a silent, compiler-invisible corruption that only manifests on *other* people's headsets. **The rule was written down because it was violated:** the ghost hand and the card-FX feature were developed in parallel and **both claimed extras flag bit 2** and appended trailing bytes. The merge resolved it by giving them distinct bits and stating the contract — *"each only has to sit behind every field a pre-existing reader knows, and the bit order then fixes the layout without either side knowing about the other."*
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3 (2nd packet type), reveal gate, identity mapping, config, stubs; extended by `da2ee2a`, `54b09b1`, `1c2a0df`, `cc99144`
- **Breaks if:** someone "tidies" the serializer by grouping all the `if` blocks by feature, sorting them alphabetically, extracting them into per-feature methods called in a different order, or writing a new block before an older one because it "logically belongs with the board pose".
- **Confidence:** high

### Readers validate only their own flags' length
- **Where:** `AvatarSerializer.TryRead` (`need` sum), `PresenceSerializer.TryRead` (`need` sum)
- **Rule:** The length check sums **only** the bytes the reader's own known flags demand, and compares `length < i + need`. Never `length != expected`, never a total-size check.
- **Why:** A stricter check would reject every packet from a future build that appended a block — turning a forward-compatible protocol into a hard version wall.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3
- **Breaks if:** someone adds an exact-length assertion "for safety", or refactors the two `need` expressions into a shared helper that sums *all* possible fields.
- **Confidence:** high

### Mask-size length is validated inside the block, not in `need`
- **Where:** `PresenceSerializer.TryRead`, inside the `if (pileBrowse)` branch
- **Rule:** The trailing mask-size byte's length check (`if (length < i + 1) return false;`) lives after byte A has been read, **not** in the `need` sum above.
- **Why:** The bit demanding it (`PileBrowseMaskSizeBit`) is inside byte A, which is unreadable at `need`-computation time. Hoisting it into `need` would require reading byte A twice or guessing.
- **Established by:** `2aba446` wip: mask size setting + MP sync → merged `1c2a0df` feat(net): mask size is tunable and travels to every peer
- **Breaks if:** a refactor "unifies all length validation up front".
- **Confidence:** high

### Flag bit 7 of the extras byte is a BLOCK header, not "browse fan open"
- **Where:** `NetProtocol.FlagPileBrowse`, `PresenceSerializer.Write` (`bool block = HasPileBrowse || HasMaskSize || boardStyle`)
- **Rule:** The block goes out when *any* of pile-browse / non-default mask size / non-default board style is present. A size-or-style-only packet writes the pile-browse sub-fields zeroed and **byte B = 0**.
- **Why:** Both flag bytes are exhausted. Spending the last bit on a block header is what made two later features shippable without a wire-version bump (which would have broken every existing peer).
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too (block created); exploited by `2aba446`/`1c2a0df` (mask size) and `fd79eaa`/`cc99144` (board style)
- **Breaks if:** someone renames it to `FlagBrowseFanOpen` and then "simplifies" the write condition to `if (state.HasPileBrowse)` — mask size and board style silently stop transmitting.
- **Confidence:** high

### Zero card count means "no fan" on every reader that ever shipped
- **Where:** `RemoteAvatar.SetExtras` (`PileBrowseOpen = p.HasPileBrowse && p.PileBrowseCardCount > 0`), `RemoteBrowserFan`
- **Rule:** A browse fan is rendered only when the received count is `> 0`.
- **Why:** It is the mechanism that lets the block carry mask size / board style without inventing a phantom fan on old peers. Also prevents an empty fan emerging and then re-emerging one frame later: the browse arc's content only fills in on the driver's *next* rebuild pass, which is why the sender also gates on `Cards.Count > 0` (see `NetAvatarDriver.TickExtrasSend`).
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too
- **Breaks if:** the receiver is "simplified" to render whenever the flag is set.
- **Confidence:** high

### Non-default-only transmission for mask size and board style
- **Where:** `NetAvatarDriver.TickExtrasSend` (`if (maskSizeCode != NetProtocol.MaskSizeDefaultCode)`), `PresenceSerializer.Write` (`boardStyle = state.BoardStyleCode != NetProtocol.BoardStyleDefaultCode`)
- **Rule:** Send the field only when it differs from the default; absence means the default.
- **Why:** Guarantees a default-configured player emits **byte-identical** packets to pre-feature builds, and communicates a step back to the default by the field disappearing. Also the reason code 0 == Oak == "absent" by construction.
- **Established by:** `1c2a0df` (mask size), `cc99144` (board style)
- **Breaks if:** someone makes transmission unconditional "for symmetry" — harmless on modern peers, but it changes the packet length for every default player and removes the property the compat argument rests on.
- **Confidence:** high

### `FlagHandStyle` is always set on write
- **Where:** `AvatarSerializer.Write` (`flags |= NetProtocol.FlagHandStyle;` unconditional)
- **Rule:** The style flag is set on every outgoing rig packet; the style byte is always present. The **reader** still honours the flag.
- **Why:** Asymmetric on purpose — write unconditionally so the byte is always there for modern peers, read conditionally so pre-style senders still parse. Making the read unconditional breaks old senders; making the write conditional gains one byte and nothing else.
- **Established by:** `f584617` feat(hands): selectable hand styles — Plate gauntlet + Arcane mage glove, MP-synced
- **Breaks if:** a "consistency" pass makes read and write symmetric in either direction.
- **Confidence:** high

### Held-card pose comes after the hand-style byte
- **Where:** `AvatarSerializer.Write` / `TryRead`, `NetProtocol.FlagHeldCard`
- **Rule:** The 20-byte held-card pose is the **last** field of the rig packet, after the style byte.
- **Why:** Pre-held-card readers must still find the style byte at the offset they expect. This is the flag-bit-order rule applied to the rig packet (`FlagHeldCard = 1<<7` > `FlagHandStyle = 1<<6`).
- **Established by:** `19a50e5` fix(mirror+net): style-scaled mirror/remote hands, held card in mirror + wire, held-figure mirror pose
- **Breaks if:** the pose is moved next to the held-figure block because "they're both poses".
- **Confidence:** high

### Fingers follow their own hand's pose, not batched
- **Where:** `AvatarSerializer.Write` / `TryRead`, `WriteFingers` / `ReadFingers`
- **Rule:** Each tracked hand writes `pose(20) [+ curls(5)]` as a unit; the two hands are not written as "both poses then both curl sets".
- **Why:** Pure layout contract. Batching would be a compiler-invisible reinterpretation of 10 bytes.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment (head mask + hands sync)
- **Breaks if:** a loop-extraction refactor writes all poses then all optional data.
- **Confidence:** high

### Magic is little-endian; the byte stream reads "1RVG"
- **Where:** `NetProtocol.Magic`, `AvatarSerializer.WriteU32` / `ReadU32`, `NetPacket.PeekType`
- **Rule:** `PeekType` reconstructs the magic with the **same** little-endian assembly the serializer uses. The comment "ASCII GVR1" describes the constant, not the byte order on the wire.
- **Why:** `PeekType` duplicates the LE reconstruction by hand (it deliberately does not call `ReadU32`, which takes a `ref int`). The two must agree byte-for-byte.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** someone "fixes" the endianness in one place to make the bytes spell GVR1, or replaces one of the two reconstructions with `BitConverter.ToUInt32` on a big-endian-assuming path.
- **Confidence:** high

### Both packets share one pose primitive
- **Where:** `AvatarSerializer.WritePoseShared` / `ReadPoseShared`, used by `PresenceSerializer` for the board pose
- **Rule:** The extras packet's board pose uses the rig packet's exact pose encoding via the `…Shared` wrappers.
- **Why:** Two independently maintained pose encodings would drift; the `internal` wrappers exist precisely so `PresenceSerializer` cannot accidentally invent its own quantization.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3
- **Breaks if:** the wrappers are removed as "pointless indirection" and `PresenceSerializer` grows its own pose writer.
- **Confidence:** high

### `PileKind` member order is a wire constant
- **Where:** `NetAvatarDriver.TickExtrasSend` (`extras.PileBrowseKind = (byte)browseKind;`), `NetProtocol.PileBrowseKindDiscard/Burnt/Items`
- **Rule:** `Cards.PileKind`'s member order must equal the `PileBrowseKind*` wire order. The driver casts the enum straight onto the wire.
- **Why:** Reordering `Cards.PileKind` — a file in a *different* subsystem, with no compiler link to `Net/` — makes every peer render the wrong pile's fan.
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too
- **Breaks if:** anyone alphabetises or inserts into `Cards.PileKind`. **This coupling should be noted in `Cards/` too.**
- **Confidence:** high

### `PileBrowseKindItems` is understood but never emitted
- **Where:** `NetProtocol.PileBrowseKindItems`, `RemoteBrowserFan`
- **Rule:** The value is decoded but no current sender writes it (the item fan rides its own older `FlagItemFan`, which predates the block and carries its own held-hand bits).
- **Why:** Reserved so a later unification of the two fans needs no wire change. Deliberate dead-on-the-send-side code.
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too
- **Breaks if:** a dead-code pass deletes the constant and its receiver branch.
- **Confidence:** high

### Item fan and pile-browse block are mutually exclusive by construction
- **Where:** `NetAvatarDriver.TickExtrasSend`
- **Rule:** At most one pile fan is ever open — the Cards layer enforces it (`PileViewer.ItemsOpening` / `DispatchPoke` close the other fan), so `FlagItemFan` and the browse block can never both describe a fan in the same packet.
- **Why:** The receiver has no arbitration for two simultaneous fans. The invariant is upheld *outside* `Net/`, which makes it fragile.
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too
- **Breaks if:** the Cards layer's mutual exclusion is relaxed without teaching the receiver to arbitrate.
- **Confidence:** medium

### Card-FX endpoints are semantic, never positional
- **Where:** `CardFxAnchor`, `NetCardFx.Report`, `RemoteCardFx.Play`
- **Rule:** A card animation transmits a `(from, to)` pair of 4-bit *anchor ids*, resolved on the receiver against that sender's own synced hand/board pose.
- **Why:** 2 bytes per animation instead of ~20 B × 15 Hz for the flight's duration, and it lands correctly under packet loss. Streaming transforms was rejected explicitly.
- **Established by:** `eaec5f8` wip: item cards in mirror + remote card animations → merged `da2ee2a` feat(net): item cards in the mirror, and every card animation replicated to peers
- **Breaks if:** someone "improves fidelity" by streaming the flying card's world pose.
- **Confidence:** high

### Card-FX plays on sequence CHANGE only, and never on the first packet
- **Where:** `RemoteAvatar.SetExtras` (`_fxSeqInit`, `_lastFxSeq`), `NetCardFx.TryDequeue`
- **Rule:** The last event is deliberately **re-sent unchanged** on every extras packet for redundancy. The receiver plays only on a sequence change, and the very first extras packet from a peer *adopts* the sequence without playing.
- **Why:** Redundancy on an unreliable channel costs 2 bytes and recovers a single lost packet within 200 ms. Without `_fxSeqInit`, joining mid-flight would fire a stray card across the table.
- **Established by:** `da2ee2a` feat(net): item cards in the mirror, and every card animation replicated to peers
- **Breaks if:** the re-send is removed as "duplicate work", or `_fxSeqInit` is dropped as an unnecessary flag.
- **Confidence:** high

### `NetCardFx` queue is bounded and drops the OLDEST
- **Where:** `NetCardFx.MaxQueued`, `NetCardFx.Report`
- **Rule:** Cap 8; on overflow dequeue the oldest and enqueue the new one.
- **Why:** In single-player nothing drains the queue. Dropping the *newest* would make the stale animation the one that eventually goes out.
- **Established by:** `da2ee2a` feat(net): item cards in the mirror, and every card animation replicated to peers
- **Breaks if:** the cap is removed, or the overflow policy flipped to "drop newest".
- **Confidence:** high

### `NetCardFx.Reset` on driver disable
- **Where:** `NetAvatarDriver.OnDisable` → `NetCardFx.Reset()`, `_hasFx = false`
- **Rule:** Queued and sticky card-FX state is cleared when the driver is disabled.
- **Why:** A stale event must never leak into the next session / hot reload and fire a phantom card.
- **Established by:** `da2ee2a` feat(net): item cards in the mirror, and every card animation replicated to peers
- **Breaks if:** `OnDisable` is trimmed to just the event unsubscribe.
- **Confidence:** high

## Net — rate gating and send policy

### Two independent rates, two independent accumulators
- **Where:** `NetAvatarDriver.TickSend` (`_sendAccumulator`, 15 Hz), `NetAvatarDriver.TickExtrasSend` (`_extrasAccumulator`, 5 Hz)
- **Rule:** Rig at `SendRateHz = 15f`, extras at `ExtrasSendRateHz = 5f`, with separate accumulators, both reset to `0f` (not `-= interval`) after a send.
- **Why:** The board moves rarely and the counts change on card play only. Resetting to zero rather than subtracting deliberately **drops excess accumulation so a hitch does not produce a burst**.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3
- **Breaks if:** the accumulators are merged, or `-= interval` is used "to keep the rate exact" — reintroducing post-hitch bursts on an unreliable channel.
- **Confidence:** high

### Both accumulators reset to zero while offline
- **Where:** `NetAvatarDriver.TickSend`, `TickExtrasSend` (early-return branches)
- **Rule:** When not `VRSession.IsRunning`, not online, or `LocalPlayerId <= 0`, the accumulator is zeroed before returning.
- **Why:** Otherwise the accumulator grows all through single-player and the first online frame fires immediately, before the rig has settled.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3
- **Breaks if:** the early return is hoisted above the reset.
- **Confidence:** high

### `LocalPlayerId > 0` gates the join window
- **Where:** `NetAvatarDriver.TickSend`, `TickExtrasSend`
- **Rule:** Do not send while `LocalPlayerId <= 0`, even when `IsOnline` is true.
- **Why:** There is a window where the session is "online" but our `NetworkPlayer` (and thus `PlayerRegistry.MyPlayer`, which `SendSideAction` dereferences) does not exist yet.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3
- **Breaks if:** the condition is simplified to `!_transport.IsOnline`.
- **Confidence:** high

### On-change pre-emption of the 5 Hz extras gate
- **Where:** `NetAvatarDriver.TickExtrasSend` — `fxPending`, `countsChanged`, `browseChanged`, `maskSizeChanged`, `boardStyleChanged`
- **Rule:** Five distinct edges bypass the rate gate and send on the frame they occur:
  card-FX pending; hand/item fan count change; browse open/close/switch; mask-size code change; board-style code change.
- **Why:** Each has a concrete observed failure: a 0.4 s card flight arriving after it finished; a peer's fan appearing ~200 ms after the gesture that raised it; a browse fan's *emerge* animation playing after the owner already finished reading; a settings stepper that "does nothing on their screen". Events are human-paced, so this cannot become a stream.
- **Established by:** `da2ee2a` (fx + counts), `54b09b1` (browse), `1c2a0df` (mask size), `cc99144` (board style)
- **Breaks if:** the five edge tests are collapsed into "send at 5 Hz, it's close enough", or one of them is dropped as redundant.
- **Confidence:** high

### Mask size is quantized BEFORE the change test
- **Where:** `NetAvatarDriver.TickExtrasSend` (`byte maskSizeCode = NetProtocol.EncodeMaskSize(...)` then compare)
- **Rule:** Compare the **wire code**, not the float.
- **Why:** The change test must be the change the receiver can actually observe. A sub-0.01 config wobble must not trigger a packet, and must not spam the change-gated log.
- **Established by:** `1c2a0df` feat(net): mask size is tunable and travels to every peer
- **Breaks if:** the comparison is moved onto the raw `float` config value.
- **Confidence:** high

### Browse send gates on `Cards.Count > 0`
- **Where:** `NetAvatarDriver.TickExtrasSend` (`browseNow.Cards.Count > 0` in the `browseKind` expression)
- **Rule:** Do not report an open browser until its card list is populated.
- **Why:** The arc's content only fills in on the driver's *next* rebuild pass. A zero-card block would make the peer emerge an empty fan and then emerge it **again** one frame later.
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too
- **Breaks if:** the gate is reduced to `IsOpen`.
- **Confidence:** high

### Change-gated logging, one line per edge
- **Where:** `NetAvatarDriver` (`_lastSentMaskSizeCode`, `_lastSentBoardStyleCode`, `_loggedBrowseOpen`), `RemoteAvatar` (`_loggedMaskSize`, `_loggedBoardStyle`, `_heldCardBillboardLogged`), `RemoteBoardGate.LogModeIfChanged`, `LocalRigSampler.s_loggedHeldItem`, `NetCardFx.s_loggedFirst`
- **Rule:** These log lines fire once per **actual change**, never per packet or per frame.
- **Why:** They are the evidence a hardware tester greps for ("Mask size SENT", "Remote board visibility APPLIED", "Pile-browse SENT"). At 5 Hz × N peers they would be unusable, and the latch fields are the only thing keeping them readable. Per §5 of the Charter, a log grep token is a feature.
- **Established by:** `1c2a0df`, `cc99144`, `54b09b1`
- **Breaks if:** the latch fields are removed as "unused private state" — they *are* read, but only by the guard they gate.
- **Confidence:** high

## Net — transport

### The Harmony prefix returns `true` on any exception
- **Where:** `FfsNetTransport.ReceivePrefix` (catch block)
- **Rule:** If the action cannot be classified, defer to vanilla (`return true`).
- **Why:** Safe both ways — a real game action **must** reach the game, and one of our own packets that slipped through still carries the sentinel `TargetPlayerID`, so vanilla ignores it without ever calling `Execute()`. Returning `false` on error would swallow real game actions and desync.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** the catch is changed to `return false` "to be safe", or removed so the exception propagates into Bolt's dispatch loop.
- **Confidence:** high

### The send-args array is pre-boxed once
- **Where:** `FfsNetTransport._sendArgs`, populated in `Install`, only slot `[1]` written in `Send`
- **Rule:** Slots 0 and 2..7 are boxed once at install; the 15 Hz send path writes only the token slot.
- **Why:** Avoids seven boxing allocations per send, 15 times a second, in a GC-sensitive VR frame budget. The positional meaning of each slot is the `SendSideAction` signature and is documented in the comment there.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** `Send` is "clarified" to build a fresh `object[]` per call, or the slot order is changed without re-checking the reflection signature.
- **Confidence:** high

### The payload byte array is an exact-size copy per send
- **Where:** `FfsNetTransport.Send` (`new byte[length]` + `Buffer.BlockCopy`)
- **Rule:** The token gets its own exact-size array, never the caller's reusable buffer.
- **Why:** `CustomDataToken` keeps a **reference** to the array; the driver's `_sendBuffer` is reused on the next frame. Handing over the shared buffer would let Bolt serialize a half-overwritten packet.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** the copy is removed as an "unnecessary allocation".
- **Confidence:** high

### Everything FFSNet is reached by reflection
- **Where:** `FfsNetTransport.Resolve`, `NetPlayerActors`, `NetFigures` (for `NetworkPlayer` access)
- **Rule:** No compile-time reference to bolt.dll / FFSNet types. Failure to resolve any member sets `_degraded` and the transport becomes a logged no-op.
- **Why:** Zero csproj coupling, and an unexpected game build degrades to vanilla instead of crashing. Standing decision: "Never patch ScenarioRuleLibrary/Bolt".
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** a refactor adds a direct type reference "now that we publicize refs anyway".
- **Confidence:** high

### Received packets are parked, not applied, inside the Bolt callback
- **Where:** `NetAvatarDriver.OnPacketReceived` → `_pending` / `_pendingExtras`; applied in `NetAvatarDriver.ApplyPending` from `Update`
- **Rule:** The receive path only decodes, converts to world frame, and stores the newest state per sender. GameObject creation happens in `Update`.
- **Why:** The callback runs on the main thread but **inside Bolt's event iteration**. Creating GameObjects there is the classic mid-iteration mutation hazard. The dictionary also dedups: for an unreliable stream only the newest state matters.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** someone removes the "pointless" indirection and calls `SetTarget` / `GetOrCreate` directly from the prefix.
- **Confidence:** high

### Own-echo rejection
- **Where:** `NetAvatarDriver.OnPacketReceived` (`if (senderId != 0 && senderId == _transport.LocalPlayerId) return;`)
- **Rule:** Reject packets whose sender id equals our own — but only when the id is non-zero.
- **Why:** `0` is the "unknown" sentinel from `LocalPlayerId`; treating unknown-vs-unknown as self would drop legitimate traffic.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** simplified to `senderId == _transport.LocalPlayerId`.
- **Confidence:** high

### Staleness teardown, not player-left
- **Where:** `NetAvatarDriver.TickAvatars` vs `NetProtocol.StaleTimeoutSeconds = 3f`
- **Rule:** A remote avatar is destroyed after 3 s without a rig packet. `NetAvatarDriver.RemovePlayer` exists as an *additional* immediate path but is not currently called.
- **Why:** Covers a peer that stopped sending without a Bolt player-left: switched to flat, minimised, long hitch. More robust than a single callback.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** the timeout is replaced by a player-left hook alone; flat-switching peers then leave ghost avatars forever.
- **Confidence:** high

### Teardown always releases held figures
- **Where:** `NetAvatarDriver.TickAvatars`, `RemovePlayer`, `DestroyAllAvatars` — each calls `NetFigures.ReleaseRemote(id)`
- **Rule:** Every avatar-destruction path also releases that peer's held figure.
- **Why:** A remotely-held figure has the game's own transform writers **suppressed** (`NetHeldFigures.Owns`). Failing to release strands a mini frozen in mid-air with the game unable to move it — a visible, non-cosmetic-looking defect.
- **Established by:** `826c75a` feat(net): full figure-pickup sync (held figure rides the grabber's hand for all peers)
- **Breaks if:** the three teardown paths are deduplicated into one that forgets the release, or a fourth path is added.
- **Confidence:** high

### `HeadMaskLibrary.Reset` on module shutdown
- **Where:** `NetModule.Shutdown`
- **Rule:** The mask-prefab cache is dropped on shutdown.
- **Why:** A hot-reloaded or freshly-shipped bundle must be re-probed; a cached miss would persist across the reload and everyone would keep the placeholder head.
- **Established by:** `5a6e255` chore(net): reset HeadMaskLibrary cache on NetModule shutdown
- **Confidence:** high

## Net — receiver-side rendering

### Exponential smoothing, frame-rate independent
- **Where:** `RemoteAvatar.Tick` and `NetFigures.Tick`, both `k = 1 - exp(-NetProtocol.InterpolationSharpness * dt)` with `InterpolationSharpness = 15f`
- **Rule:** Interpolation uses the exponential form with `dt`, not a bare `Lerp(a, b, 0.2f)`. Remote figures use the **same** sharpness as remote avatars.
- **Why:** A fixed lerp factor makes remote motion frame-rate dependent — a peer looks different at 72 fps and 90 fps. Sharing the constant keeps a held figure visually welded to the hand holding it; two different sharpnesses make the figure swim relative to the hand.
- **Established by:** `c6f3901` (avatars), `826c75a` (figures)
- **Breaks if:** either call site is "simplified" to a constant factor, or the two are given independent tuning constants.
- **Confidence:** high

### First activation snaps; subsequent frames interpolate
- **Where:** `RemoteAvatar.UpdatePart`
- **Rule:** When a holder transitions inactive → active, set the pose directly and return; only interpolate on already-active holders.
- **Why:** Without it, every newly-tracked hand lerps in from the world origin — a visible streak across the table.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** the branch is folded into the general path "since Lerp with k≈1 is nearly a snap".
- **Confidence:** high

### The held-card slab billboards to the SENDER'S synced head, not the wire rotation
- **Where:** `RemoteAvatar.UpdateHeldCard`
- **Rule:** The slab's rotation is re-derived each frame as `LookRotation(slabPos − headPos, headHolder.up)`. The transmitted rotation is kept **only** as the fallback for a peer whose head is not tracked. No extra slerp on top.
- **Why:** `Cards.VRCard.TickHeldPose` re-billboards a held card to its **owner's** head every frame. Trusting the transmitted rotation broke that rule twice: (a) a 16-bit-quantized snapshot at send rate, eased independently of the head and of the card's own position, visibly lags out of the "facing its owner" relationship during motion; (b) after packet loss the last rotation keeps pointing where the peer's head *was*. Re-deriving costs **no wire field, no flag bit, no version bump** — the head is already a mandatory part of the same rig packet.
- **Established by:** `19a50e5` fix(mirror+net): style-scaled mirror/remote hands, held card in mirror + wire, held-figure mirror pose
- **Breaks if:** someone "uses the data we already send" and slerps toward `HeldCardPose.Rotation`; or adds a smoothing pass on top of the billboard (re-introducing exactly the lag it removes).
- **Confidence:** high

### Degenerate-billboard guards
- **Where:** `RemoteAvatar.UpdateHeldCard`
- **Rule:** Skip the billboard when `away.sqrMagnitude < 1e-6`, and when `|dot(away, up)| > 0.9995` (forward ∥ up ⇒ `LookRotation` undefined) — keeping the previous rotation in both cases.
- **Why:** Prevents a card snapping to an arbitrary orientation when the peer holds it directly at eye level / directly overhead.
- **Established by:** `19a50e5`
- **Breaks if:** the guards are removed as unreachable.
- **Confidence:** high

### Card `+Z` points AWAY from the reader
- **Where:** `RemoteAvatar.UpdateHeldCard` (`away = slabPos − headPos`), `RemoteHandFan.BuildBackSlab`, `Cards.CardMesh`
- **Rule:** The look direction is **head → card**, not card → head.
- **Why:** It is the `CardMesh` / `BuildBackSlab` convention: the owner sees the face, everyone else sees the back. Inverting it shows every peer the wrong side.
- **Established by:** `19a50e5`
- **Breaks if:** the subtraction is flipped to "point at the viewer", which reads as the obvious billboard idiom.
- **Confidence:** high

### Sender scale goes on the holder; style scale and mask size go on the CHILD
- **Where:** `RemoteAvatar.SetTarget` (holder `localScale = one * WorldScale`), `RemoteAvatar.Tick` (`HeadMaskLibrary.ApplySize(_headVisual, MaskSize)`, `HandVisuals.ApplyStyleScale(rig.Root, …)`)
- **Rule:** The sender's diorama `WorldScale` is written to the *holder* transforms. The per-style visual scale and the head-mask size live on the **"HandVisual" / "HeadVisual" child roots**, never on the holder.
- **Why:** They are multiplicative and independently sourced (one from the wire, one from local config). Putting both on the holder means whichever writes last stomps the other — that is the exact regression: the scale write "can no longer stomp it" is called out in the code.
- **Established by:** `19a50e5` fix(mirror+net): style-scaled mirror/remote hands, held card in mirror + wire, held-figure mirror pose; mask-size half by `1c2a0df`
- **Breaks if:** a cleanup consolidates "all the scaling" onto one transform.
- **Confidence:** high

### Live re-apply of mask size and style scale happens in `Tick`, not in the build path
- **Where:** `RemoteAvatar.Tick` (`_appliedMaskSize`, `_appliedStyleScale` latches)
- **Rule:** Both are compared per frame against a latch and written only on change.
- **Why:** Mask size arrives on the **extras** stream (`SetExtras`), which does not run the head-build path; style scale is a *receiver-local* config the sender knows nothing about. Both must therefore be polled. The latch keeps the cost to one float compare per frame.
- **Established by:** `1c2a0df` (mask size), `19a50e5` (style scale)
- **Breaks if:** the checks are moved into `BuildHeadMask` / `BuildHands` "where they belong" — a stepper edit then does nothing until the peer changes mask or style.
- **Confidence:** high

### Ghost hand: one ghost, resolved from the dominant-hand flag
- **Where:** `RemoteAvatar._ghost`, `RemoteAvatar.Tick` (`ghostRig = GhostHand ? (DominantRight ? _leftRig : _rightRig) : null`)
- **Rule:** Exactly one `HandGhost` per avatar; the side is derived from `DominantRight`, not transmitted. `HandGhost` restores the previous rig itself when the side flips or `BuildHands` supplies a new rig.
- **Why:** The fan always sits on the non-dominant hand, which the receiver already knows — so the side costs zero wire. The self-restoring behaviour is what makes a mid-session dominant-hand switch not leave a permanently faded hand.
- **Established by:** `c1456f9` wip: ghost hand while the fan is open
- **Breaks if:** a second ghost is added "for symmetry", or the side is given its own wire flag, or the restore is moved to an explicit call that a code path can miss.
- **Confidence:** high

### The sender's ghost STRENGTH is transmitted on purpose
- **Where:** `PresenceState.GhostStrength`, `NetAvatarDriver.TickExtrasSend`, `RemoteAvatar.SetExtras` (`Hands.HandGhosts.AlphaFor(strength / 255f)`)
- **Rule:** The strength byte is the **sender's** chosen value, decoded through the same `AlphaFor` curve the local hands use.
- **Why:** Standing project rule — "what one player sees, every player sees the same way". Same class of agreement as hand style, head mask, mask size and board style. Using the *receiver's* local strength would make a peer's ghost hand look different to everyone.
- **Established by:** `c1456f9` wip: ghost hand while the fan is open
- **Breaks if:** the receiver substitutes its own `HandGhosts.Strength`, which looks like the obvious "use local config" simplification.
- **Confidence:** high

### Ghost materials are private clones, released before the hand objects
- **Where:** `RemoteAvatar.Destroy` (`_ghost.Release()` **first**), `HandGhost`
- **Rule:** The fade runs on private material copies of *this* avatar only; the clones are freed before the hand GameObjects are destroyed.
- **Why:** Cloned materials are **assets** — destroying the renderer first leaks them. And mutating the shared bundle material would fade every player's hands, including the local ones.
- **Established by:** `c1456f9` wip: ghost hand while the fan is open
- **Breaks if:** `Destroy` is reordered "root first, details after", or the ghost is switched to `sharedMaterial` for performance.
- **Confidence:** high

### Palm-anchored content hangs off `PalmAnchorFor`, not the hand holder
- **Where:** `RemoteAvatar.PalmAnchorFor`, consumed by `RemoteHandFan`, `RemoteItemFan`, `RemoteBrowserFan`
- **Rule:** Anything the owner hangs off `HandRig.PalmCenter` must be reproduced off the receiver's **palm anchor**, never off the holder transform.
- **Why:** The holder carries the sender's `Rig.Root` pose verbatim, and by the `HandRig` contract the hand root's `+Y` points out of the **back** of the hand (procedural hand = 180° Z-flip; glove prefab = whatever `Anchor_Palm` was authored as). Using the holder floats the fan out of the wrong face of the peer's hand. Costs no wire — we build the peer's hand from the same `HandVisuals` rig they do, so their palm anchor is already exact.
- **Established by:** `c7bff30` fix(mirror,net): the hand fan is back in the mirror, and a peer's fan finally matches theirs
- **Breaks if:** a "simplification" parents fans directly to `LeftHandHolder` / `RightHandHolder`.
- **Confidence:** high

### A dropped packet is not a close
- **Where:** `RemoteAvatar.SetExtras` (pile-browse and item-fan reset block)
- **Rule:** Open→closed transitions are driven only by an extras packet that **actually arrived** without the flag. A dropped packet produces no `SetExtras` call at all, so it can never be mistaken for a close.
- **Why:** The receiver animates the close as a collapse-into-the-stack. Inferring "closed" from silence (e.g. a timeout on the extras stream) would make every packet loss play a spurious collapse.
- **Established by:** `54b09b1` feat(net): the pile browser fans open and close on remote boards too
- **Breaks if:** someone adds an extras-staleness timeout that clears fan state.
- **Confidence:** high

### `RemoteBoardGate` is THE single decision point
- **Where:** `RemoteBoardGate.ShowBoardSurface`, `RemoteBoardGate.SurfaceVisible`, `RemoteBoardGate.Mode`
- **Rule:** Every board-anchored surface routes its visibility through this one predicate: the board frame and its widgets, a transient fan the sender parked **above their board**, and any card flight that starts or ends on board furniture. Hand-held surfaces (the peer's hand fan; an item/browse fan they are physically holding) are **avatar** content and are deliberately **not** gated.
- **Why:** The setting was written when a peer's board was a frame with two cards, and `RemoteControlBoard` evaluated the mode inline. The board later grew a whole parity layer plus three transient fans rendered by their **own classes that are not children of the board root** — those classes never learned about the mode (`RemoteCardFx` only ever checked `Off`), so with "Aus"/"Aktionsphase" a peer's item fan and browse fan still bloomed in mid-air where their hidden board would have been, and cards still flew to invisible pile stacks.
- **Established by:** `cc99144` fix(ui): audit every VR setting — remote-board mode now gates ALL remote content
- **Breaks if:** a new remote-content class is added without routing through the gate (the failure is silent and only visible with the setting off); or someone "simplifies" by gating the whole avatar, hiding peers' hands, which is a different feature.
- **Confidence:** high

### `RemoteBoardGate.Mode` degrades to `Off` when config is unbound
- **Where:** `RemoteBoardGate.Mode`
- **Rule:** Unbound `[Net]` config ⇒ `RemoteBoardVisibility.Off` — render nothing rather than guess.
- **Why:** The opposite default would show remote boards during the secret selection phase in a partially-initialised session.
- **Established by:** `cc99144`
- **Confidence:** high

### `RevealGate` degrades to SHOW only when clearly local/offline
- **Where:** `RevealGate.ShowRoundCardFronts`, `RevealGate.InScenario`
- **Rule:** The predicate is a single negated conjunction. Every game access is null-guarded; it degrades to "safe to show" (true) only when we are clearly local or offline, and during the secret selection phase **online** it degrades to hiding. Only `SaveData.Instance` is null-guarded; simple static property reads are deliberately **not** wrapped in try/catch.
- **Why:** This is the anti-cheat linchpin and it mirrors the vanilla client's own rule (`AbilityCardUI`) exactly, so the mod opens no new cheat vector. An exception-swallowing wrapper that returned `true` on error would be a cheat.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3 (2nd packet type), reveal gate, identity mapping, config, stubs
- **Breaks if:** the conjunction is refactored into early-return branches with a different default; or a broad `try { … } catch { return true; }` is added "for robustness".
- **Confidence:** high

### The reveal gate applies ON TOP of the visibility mode
- **Where:** `NetModule.RemoteBoards` doc, `RemoteBoardVisibility` doc, `RemoteBoardGate.SurfaceVisible`
- **Rule:** `Always` shows the board **frame** during the secret phase but its round cards render as BACKS. `ActionPhaseOnly` hides the whole board during that phase. The gate is never bypassed by any mode.
- **Why:** The visibility setting is a *rendering* preference; the reveal rule is anti-cheat. Conflating them would let a user setting disable anti-cheat.
- **Established by:** `f25e76a` feat(net): remote control board at synced world pos + Steam owner tag; hardened by `cc99144`
- **Breaks if:** `Always` is short-circuited to skip the reveal check.
- **Confidence:** high

### Card identities are resolved LOCALLY, never from the wire
- **Where:** `RemoteAbilityCardSource`, `RemoteCardArt`, `NetPlayerActors.ActorFor` → `CPlayerActor.CharacterClass`
- **Rule:** A remote board's round cards are read from the **already host-replicated** `CPlayerActor.CharacterClass.RoundAbilityCards` / `.InitiativeAbilityCard` on the local client, and shown face-up only when `RevealGate` opens. Nothing about card identity is ever transmitted by us.
- **Why:** It is what makes the anti-cheat claim true rather than merely policy: there is no channel to exploit. It is also free — the data is already there.
- **Established by:** `aa9216d` feat(net): show real card art on a remote hand when the reveal gate allows; extended by `c5df48e` feat(net): peers' played cards render at full detail once the phase reveals them
- **Breaks if:** anyone adds a card id to a packet "so the remote board doesn't have to look it up", which is the obvious performance-shaped simplification.
- **Confidence:** high

### Remote boards are PURE DISPLAY
- **Where:** `RemoteControlBoard`, `RemoteBoardContent`, `RemoteBoardFurniture`, `BoardVisual.Quad` (collider stripped at creation), the collider sweep in `RemoteControlBoard`
- **Rule:** Nothing on a remote board is interactive. Colliders are stripped at creation by `BoardVisual.Quad` and a sweep catches anything adopted from game prefabs. Furniture is inert visuals only.
- **Why:** A remote board is a *picture of someone else's board*. An interactive element would let the local player poke/laser it, and worse, could route a click into the game's own UI handlers for another player's actor.
- **Established by:** `f25e76a` feat(net): remote control board at synced world pos + Steam owner tag; hardened by `cf38066` feat(net): the peer's board shows ALL its furniture — as inert visuals
- **Breaks if:** a new widget is built with a component that adds a collider by default and the sweep is trusted to catch it (or the sweep is removed as redundant).
- **Confidence:** high

### Non-interactivity is enforced at THREE layers, not one
- **Where:** `BoardVisual.Quad` (construction), the registration discipline in `RemoteBoardFurniture` / `RemoteBoardContent`, and `RemoteBoardFurniture.StripColliders` (runtime sweep) — called from the `RemoteBoardFurniture` ctor, `RemoteControlBoard.EnsureBuilt`, and `RemoteBoardCard.Set`'s real-face branch
- **Rule:**
  1. **Construction** — every piece is a `BoardVisual.Quad` (which destroys the primitive's collider on creation) or a bare `GameObject` + `TextMeshPro`. No `Collider`, no `Rigidbody`, no `GrabbableBehaviour` is ever created.
  2. **Registration** — these files never call `PlayTray.RegisterLaserTarget`, never implement `IPokeable`/`IGrabbable`, never touch `VRInteractables` / `UguiPokeSurfaces` / `LaserTargets`, and have **no click callbacks at all**. `RemoteBoardFurniture.InertCap` is *"a picture of a button"*.
  3. **Belt and braces** — `StripColliders` walks the finished hierarchy, destroys anything that still carries a collider, and logs a loud warning (grep: `"remote board furniture carried"` / `"INERT-GUARD"`).
  Plus: `RemoteCardArt.Neutralize` destroys every `GraphicRaycaster` in a cloned card face and adds a blocking `CanvasGroup`; `VRLayers.Apply` puts the whole subtree on the mod layer.
- **Why:** The user's requirement, verbatim: *"Nichts davon soll man interagieren können auf dem fremden Board, es ist eine reine Darstellung."* Layer 3 is what *"converts 'I reviewed the code' into a runtime guarantee that survives future edits"* — a future `CreatePrimitive` would otherwise silently make a peer's board pokeable.
- **Established by:** `cf38066` feat(net): the peer's board shows ALL its furniture — as inert visuals
- **Breaks if:** the sweep is removed as redundant with the construction discipline (it exists *because* the discipline is not compiler-enforced), or a new widget is added without going through `BoardVisual.Quad`.
- **Confidence:** high

### Hiding the board root is NOT enough — faces must be torn down and the change key invalidated
- **Where:** `RemoteControlBoard.Tick` (`!showBoard` branch) → `RemoteControlBoard.BlankCardFaces` → `RemoteBoardCard.Blank` + `RemoteActiveCards.Blank`
- **Rule:** When the board is hidden, blank the slots **and clear each slot's change key**.
- **Why:** A hosted card face surviving inside a deactivated board would be re-activated by `SetActive(true)` on the frame the board returns — **one statement before the slots re-evaluate the gate**. That is one frame of a real card face behind a shut gate, in exactly the case that matters most (`ActionPhaseOnly` hides the board *because* the gate shut). The change key must go too, for two reasons: with it stale, a slot reappearing while the gate is **shut** renders the stale face; and a slot reappearing while the gate is **open with the same card** would early-return and stay blank forever.
- **Established by:** `c5df48e` feat(net): peers' played cards render at full detail once the phase reveals them
- **Breaks if:** the blanking is "optimised away" because the root is already inactive, or the key clear is dropped as redundant with the hide.
- **Confidence:** high

### The reveal gate is evaluated ONCE per frame and passed down — never re-derived
- **Where:** `RemoteControlBoard.Tick` computes `showFronts` once; `RemoteAbilityCardSource.ShowFullFace`, `RemoteCardArt`, `RemoteBoardCard.Set`, `RemoteActiveCards.Refresh`, `RemoteStatusReadouts.Refresh` all take it as an argument and hold **no gate of their own**
- **Rule:** *"CALLER CONTRACT (anti-cheat): only ever call this when the reveal gate for `actor` is OPEN. Nothing here re-checks it, deliberately — one gate, in one place, evaluated by the caller BEFORE any face object is created, is easier to audit than a gate re-derived in three files."* The face host is created **inside** the `if (front)` branch and `RemoteCardArt` builds its clone under an **inactive** host, so no face object can render a frame ahead of the gate.
- **Why:** A gate copied into three files drifts. The single evaluation point is the audit surface.
- **Established by:** `c5df48e`
- **Breaks if:** a "defensive" re-check is added inside `RemoteCardArt` or `RemoteAbilityCardSource` — that is *worse*, because it makes the real gate ambiguous.
- **Confidence:** high

### Card art is CLONED, never adopted
- **Where:** `RemoteCardArt.ShowFront` (`Object.Instantiate`), `RemoteAbilityCardSource.TryLiveWidget`
- **Rule:** We clone the peer's own `AbilityCardUI.fullAbilityCard`; the live widget is **never** reparented, never mutated, never flag-flipped.
- **Why:** Adopting it *"would rip the face out of the remote actor's own hidden hand UI and corrupt the game's bookkeeping"*. This is also why the parity panels are mod-drawn: the game instantiates exactly **one** objectives container, **one** infusion board and **one** initiative track per client, the local board already docks those single instances, and *"a canvas cannot be in two places at once"*.
- **Established by:** `aa9216d` feat(net): show real card art on a remote hand when the reveal gate allows; `1899421` (parity panels)
- **Breaks if:** someone reparents a live game canvas "instead of duplicating work".
- **Confidence:** high

### The pooled fallback BORROWS for one method call and returns in a `finally`
- **Where:** `RemoteAbilityCardSource.TryPooledClone` — `ObjectPool.SpawnCard(..., activate: false)` → clone → `ObjectPool.RecycleCard` in `finally`
- **Rule:** The widget is spawned **inactive**, under a deactivated mod-owned holder, cloned, and recycled unconditionally — *"including when `ShowFront` threw"*. An unusable spawn is `Object.Destroy`ed rather than left parented under our holder.
- **Why:** Explicitly contrasted with `Cards.ItemsPile`, which must keep its pooled widget alive because item state changes under it. A played round card is *"a static picture"*. Long-lived hosting would cost the whole ItemsPile hazard list — mutation restore, recycle on **every** teardown path (phase change, board destroy, peer leave, scene unload), and a leak that corrupts the flat UI if any path is missed. Borrowing for one call *"removes that class of bug entirely"*, and the game's own `OnReturnedToPool` contract undoes everything the borrow touched.
- **Established by:** `c5df48e`
- **Breaks if:** the widget is cached "to avoid re-spawning", or the `finally` is converted to a normal-path recycle.
- **Confidence:** high

### `CardEffects` is stripped from the clone with `DestroyImmediate`, before activation
- **Where:** `RemoteCardArt.StripFragileEffects`
- **Rule:** Remove the `CardEffects` component while the clone is still **inactive**, using `DestroyImmediate` — not `Destroy`.
- **Why:** `CardEffects.Initialize` (Awake) swaps the card's image materials to a **custom screen-space shader keyed by a `_PosAndBounds` valid only for the card's original hand-canvas position**. On a detached world-space clone that is the known *"card renders DEEP BLACK"* hazard (`CardFace.Maintain` documents the identical burn/lose-popup failure). `DestroyImmediate` is required because a **deferred `Destroy` would still let Awake run this frame**. Safe here precisely because the clone has never been active. Cost: the cosmetic holo/foil sheen — *"an acceptable, deterministic trade for a correct render"*.
- **Established by:** `aa9216d`
- **Breaks if:** `DestroyImmediate` is "modernised" to `Destroy` (a lint rule will suggest exactly this), or the strip is moved after activation.
- **Confidence:** high

### The shared card-BACK material is read, never mutated, never destroyed
- **Where:** `Cards.CardMesh.CreateBackMaterial` (a lazily-created singleton); consumers `RemoteCardFx.Acquire`, `RemoteItemFan.Rebuild`, `RemoteBrowserFan.Rebuild`, `RemoteHandFan.Rebuild`, `RemoteAvatar.BuildHeldCardSlab` (all via `sharedMaterial`, each marked *"SHARED cache — never ours to destroy"*); tinting consumers `RemoteBoardCard` ctor and `RemoteControlBoard.EnsureBuilt` read only `.mainTexture` and wrap it in a fresh `BoardVisual.Unlit`
- **Rule:** Where a tint is needed, extract the **texture** and build a new material. Never colour the shared one, never `Destroy` it, never swap its texture.
- **Why:** It is shared by every card in the mod. Tinting it recolours every card back everywhere, permanently. A related bug is on record: *"Teardown no longer destroys CardMesh's SHARED back material (was turning all card backs pink after closing the mirror)."*
- **Established by:** `19a50e5` fix(mirror+net): style-scaled mirror/remote hands, held card in mirror + wire, held-figure mirror pose
- **Breaks if:** a "reuse the material we already have" pass tints the shared instance, or a teardown adds it to the destroy list.
- **Confidence:** high

### Per-peer materials are per-instance and assigned via `sharedMaterial`
- **Where:** `RemoteControlBoard._frameMat` (one `Material` per remote board), `RemoteElementStrip._mats`, `RemoteInitiativeTrack.Chip._plateMat`, `RemoteBoardFurniture._itemUseGlowMat`, `RemoteGlowPulse._material`, `OwnerTag`'s avatar material, `RemoteBoardCard._backMat/_faceMat/_bodyMat`; all created by `BoardVisual.Unlit` (`new Material` per call)
- **Rule:** Every mutated material is a private instance, and renderers are assigned through **`sharedMaterial`**, never `.material`.
- **Why:** *"one Material instance per remote board — nothing is shared, so tinting one peer's board can never touch another's"*. Assigning `.material` makes Unity silently instantiate a hidden per-renderer copy that then leaks.
- **Established by:** `cc99144` (board style), `cf38066` (furniture)
- **Breaks if:** a dedup pass hoists a colour material to a static shared instance, or `.material` is used for convenience.
- **Confidence:** high

### `HandGhost` clones per renderer and restores the original arrays
- **Where:** `HandGhost.Engage` (captures `r.sharedMaterials` into `_originals` and `shadowCastingMode` into `_shadows`), `HandGhost.MakeTransparent`, `HandGhost.Release`, `HandGhost.IsAttachment`
- **Rule:** Clone each material into a per-renderer instance, hand the clones over via **`sharedMaterials`** (which does *not* trigger Unity's implicit instantiation, unlike `.materials`), destroy the clones on release, and put the **original arrays back verbatim**. Anything under one of the three attachment sockets (`PalmCenter`, `GrabAnchor`, `Wrist`) is **excluded** from the sweep.
- **Why:** Hand materials are shared — the procedural hand puts one material on ~40 primitives, and the glove prefab's materials are **the bundle assets themselves**, used by the mirror and by every remote player. Writing an alpha into them would fade every hand in the room, **permanently**, because bundle assets outlive the object that referenced them. The attachment exclusion exists because the card fan is parented to `Rig.PalmCenter`, which for a glove prefab lives *inside* the hand subtree — a naive subtree sweep fades the very cards being revealed.
- **Established by:** `c1456f9` wip: ghost hand while the fan is open
- **Breaks if:** the sweep is simplified to "all renderers under the hand root", or `.materials` replaces `sharedMaterials`.
- **Confidence:** high

### Gated fans hide INSTANTLY; they do not play their collapse
- **Where:** `RemoteItemFan.Tick`, `RemoteBrowserFan.Tick` (the `RemoteBoardGate` branches)
- **Rule:** When the board gate closes on a board-anchored fan, hide the root immediately rather than running the collapse animation. When the gate re-opens with the fan still up, the normal emerge runs (the root is inactive, so the state machine restarts cleanly).
- **Why:** *"the collapse animation flies the chips into the board's items stack, and that stack is exactly what the gate just hid — an arc gliding into nothing is worse than the fan simply not being there."*
- **Established by:** `cc99144` fix(ui): audit every VR setting — remote-board mode now gates ALL remote content
- **Breaks if:** the hide is routed through the normal close path "for consistency".
- **Confidence:** high

### Card-FX gating is per-anchor: hand-fan flights are avatar content
- **Where:** `RemoteCardFx.Play` — `touchesBoard = from != HandFan || to != HandFan`
- **Rule:** A flight is gated on the board surface only when at least one of its endpoints is board furniture. A pure hand-fan flight is never gated.
- **Why:** The class used to check **only** `Off`, which left `ActionPhaseOnly` broken: during the secret selection phase the peer's whole board is hidden, yet the flights that happen *then* — a card docking into a play slot — still played, as *"a lone card back arcing into empty space"*. `CardFxAnchor.HandFan` is the sole non-board anchor, so the test is exhaustive by construction.
- **Established by:** `cc99144`
- **Breaks if:** a new `CardFxAnchor` value is added that is *not* board furniture without extending this test — the test enumerates the exception, not the rule.
- **Confidence:** high

### Remote hand fronts come from the HAND pile only, and fail safe to backs
- **Where:** `RemoteHandFan.ResolveHandFronts` (`widget.CardType == CardPileType.Hand`), `RemoteHandFan.UpdateFaces` (catch ⇒ `showFronts = false`, `_handBuffer.Clear()`)
- **Rule:** Only HAND-pile widgets are candidates — Round, Discard, Lost and Active piles are excluded. **Any** failure yields backs.
- **Why:** *"so the secret round-selection cards are never even candidates for a front here."* And *"fail-safe = no cheat"* — the failure direction must be "no face", never "a face we could not verify".
- **Established by:** `aa9216d`
- **Breaks if:** the pile filter is widened to "all the actor's cards", or the catch is narrowed / removed.
- **Confidence:** high

### Card widgets are matched on the game's own id pair
- **Where:** `RemoteAbilityCardSource.TryLiveWidget` — reference match on `AbilityCardUI.AbilityCard` first, then `CardID` + `CardInstanceID`
- **Rule:** Both ids, in that fallback order, matching `AbilityCardUI.IsMatchingCard`.
- **Why:** `CardID` alone collides across instances of the same card; matching only by reference misses re-built hands.
- **Established by:** `c5df48e`
- **Breaks if:** the pair is reduced to `CardID`.
- **Confidence:** high

### Content re-read is throttled to 4 Hz; the POSE follows every frame
- **Where:** `RemoteBoardContent.RefreshSeconds = 0.25f`, `RemoteControlBoard._nextRefreshAt`, `RemoteControlBoard.Tick`
- **Rule:** Model reads and TMP repaints run on the shared 4 Hz cadence. The board's transform is written every frame.
- **Why:** *"a table of four peers costs nothing measurable."* Throttling the pose instead would make a peer's board visibly stutter as they move it.
- **Established by:** `1899421` feat(net): a peer's control board now shows what their own board shows
- **Breaks if:** the two cadences are unified in either direction.
- **Confidence:** high

### Every text write is change-gated — the badge-flicker lesson
- **Where:** `RemoteBoardContent.SetText`; change keys `RemoteObjectivesPanel._signature`, `RemoteElementStrip._signature`, `RemoteInitiativeTrack._signature`, `RemoteStatusReadouts._roundShown`/`_langShown`, `RemoteControlBoard.PileCounter._shown`, `RemoteBoardCard`'s quadruple key (`_shownEmpty`/`_shownId`/`_shownFront`/`_shownOwner`), `RemoteBoardFurniture._shownArmed`/`_shownWantedMask`/`_shownSnapMask`/`_shownHalfMask`/`_shownPickField`, `RemoteBoardFurniture.InertCap._shown`, `RemoteCardArt._shownSourceId`
- **Rule:** Never assign `TMP.text` unconditionally.
- **Why:** *"a per-frame `TMP.text` assignment re-triggers auto-size layout — the badge-flicker lesson."* `RemoteBoardCard.Set`'s key additionally makes the expensive part (cloning a card widget) run once per actual change and **never per tick**.
- **Established by:** `1899421`
- **Breaks if:** the guards are removed as premature optimisation, or a new widget writes text directly.
- **Confidence:** high

### `_shownHalfMask` is a bit mask, not a bool
- **Where:** `RemoteBoardFurniture._shownHalfMask`
- **Rule:** Per-slot visibility is stored as a mask, seeded to `-1`.
- **Why:** *"A plain bool would miss the case where the dividers stay shown but the OCCUPANCY moves from one slot to the other."*
- **Established by:** `cf38066`
- **Breaks if:** simplified to a bool.
- **Confidence:** high

### Change keys must be invalidated on scenario exit, not just cleared visually
- **Where:** `RemoteObjectivesPanel.Refresh` (clears the panel **and** drops `_signature`)
- **Rule:** Clearing a display must also drop its repaint signature.
- **Why:** *"re-entering a scenario with a bit-identical objective list must repaint, not stay blank."* This is the same class of bug as `RemoteBoardCard.Blank`'s key clear.
- **Established by:** `1899421`
- **Breaks if:** the clear is reduced to hiding the rows.
- **Confidence:** high

### Slot reuse must drop the hosted face BEFORE the slot moves
- **Where:** `RemoteBoardCard.Set` (empty branch) → `ClearFace`
- **Rule:** Tear the face down before the slot goes away.
- **Why:** The active grid re-packs its cells, so a re-used slot would otherwise *"flash the previous card's face"*.
- **Established by:** `c5df48e`
- **Confidence:** high

### Widgets start hidden and in step with their seeded change key
- **Where:** `RemoteBoardCard` ctor, `RemoteBoardFurniture` ctor (`_use.SetShown(false)`)
- **Rule:** A widget that may never receive content starts hidden **and** its `_shown*` seed must agree with that.
- **Why:** `Set` / `Refresh` early-return while nothing changed, so a mismatched seed leaves an unused active-grid cell showing a card back, or a board that never sees an item fan showing a USE cap forever.
- **Established by:** `1899421`, `cf38066`
- **Breaks if:** a constructor is "simplified" to leave the seed at its type default.
- **Confidence:** high

### Remote figures are not grabbable or highlightable locally
- **Where:** `NetHeldFigures.Owns` consumed by the FigureGrab `CanGrab` / `AllowsHand` gates
- **Rule:** A figure a peer is holding cannot be grabbed or highlighted here, and no local info panel opens for it. Strict no-op offline (`NetHeldFigures` is empty).
- **Why:** Mutual exclusion — two players dragging the same mini would fight over a transform that is already suppressed for the remote holder.
- **Established by:** `bcc9260` feat(figuregrab): animated highlight, home-spot ghost, dual info panels (MP-synced)
- **Breaks if:** the grab gate is moved without the `NetHeldFigures` term.
- **Confidence:** high

### Meshes are destroyed explicitly; they are not freed with the GameObject
- **Where:** `RemoteCardFx.Destroy`, `RemoteItemFan.Destroy`, `RemoteBrowserFan.Destroy`, `RemoteAvatar.Destroy` — each marked `// asset — not freed with the GameObject tree`
- **Rule:** Every per-instance mesh built by the subsystem is destroyed by its owner. `RemoteHandFan.BuildBackSlab`'s doc states *"Caller owns the returned mesh."*
- **Why:** Unity does not free `Mesh` assets with the hierarchy that referenced them.
- **Established by:** `ab20eea` / `19a50e5`
- **Breaks if:** a teardown is trimmed to "just destroy the root".
- **Confidence:** high

### Teardown order: clones and material assets first, roots last
- **Where:** `RemoteAvatar.Destroy` (`_ghost.Release()` **first**, then the add-ons, then the mesh, then the root), `RemoteControlBoard.Destroy` (drop hosted faces first, then null every surface handle, then the frame material handle and `_appliedStyle`), `RemoteAvatar.BuildHands` (release the ghost **before** destroying the old hand objects)
- **Rule:** Free cloned material assets before the renderers that referenced them; drop handles and change latches so a rebuild starts clean.
- **Why:** *"we own the clone, we destroy the clone"* must not depend on Unity's destruction order, nor on the board root still existing when a peer leaves mid-teardown. A surviving `_appliedStyle` makes a rebuilt board trust a stale int instead of re-applying the peer's style.
- **Established by:** `c1456f9`, `c5df48e`, `cc99144`
- **Breaks if:** the destroy sequences are shortened to "destroy the root, Unity handles the rest".
- **Confidence:** high

## Net — content classification (GLOBAL / PER-ACTOR MODEL / VR-ONLY / DELIBERATELY-NOT)

This is the decision rule that keeps a peer's board correct at near-zero bandwidth. Getting a
classification wrong produces either a desync-looking display bug or wasted wire.

**The rule:** before adding *anything* to a remote board, ask in this order —

1. Is it already identical on every client (game world state, host-replicated)? → **GLOBAL**:
   render it locally at the peer's synced board pose. **Zero wire.**
2. Is it derivable from `CPlayerActor` / `CharacterClass` (host-replicated per actor)? →
   **PER-ACTOR MODEL**: look it up from `NetPlayerActors.ActorFor(playerId)`. **Zero wire.**
3. Is it a *VR-only* fact that exists nowhere in the game model — a pose, a VR gesture, a
   cosmetic the user picked in the VR settings? → **VR-ONLY**: it needs wire bytes.
4. Would transmitting it leak information or cost more than it is worth? → **DELIBERATELY-NOT.**

### The classification, item by item

**GLOBAL — scenario-wide, bit-identical on every client, ZERO wire.** *"a peer's board just has
to RENDER it at their pose."*

| Content | Renderer | Source |
|---|---|---|
| Objectives + quest header + progress bars | `RemoteObjectivesPanel` → `RemoteWidgetMirror` over `UIManager.MissionObjectiveContainer` | a live **CLONE** of the game's own container, driven per frame from the original. Fallback (container absent): the mod-drawn rows, from `ScenarioManager.CurrentScenarioState.WinObjectives/LoseObjectives` filtered by `MissionObjectiveContainer.InitialiseObjective`'s rule; text via `LocalizationObjectiveConveter.LocalizeText`; progress via `CObjective.GetObjectiveProgress` |
| Element infusion strip | `RemoteElementStrip` (`Refresh`, `ColorFor`) | `ElementInfusionBoardManager.ElementColumn` |
| Round number | `RemoteStatusReadouts` (`RoundText`) | `CardsGameApi.RoundNumber()` |
| Initiative track — the whole widget (portraits, order, numbers, selection, reorder animation) | `RemoteInitiativeTrack` → `RemoteWidgetMirror` over `InitiativeTrack.Instance` | a live **CLONE** of the game's own track, driven per frame from the original. Fallback (track absent): the mod-drawn chip strip over `InitiativeTrack.actorsUI` in sibling order, then `ScenarioManager.Scenario.AllAliveActors` deduped by `CActor.Class` and sorted by initiative with unknowns last |
| Panel SEATS (where every dock sits on a peer's board) | `RemoteBoardLayout` | `PlayTray.{Objectives,Element,Pile,Active}MountBase` / `ReadoutBase` + the authored per-board offset/scale from `CardsConfig.BoardDefaults`, keyed by the peer's **synced** board style |

**PER-ACTOR MODEL — read off the host-replicated `CPlayerActor` / `CharacterClass`, ZERO wire.**
Reached via `NetPlayerActors.ActorFor(playerId)`.

| Content | Renderer | Gate |
|---|---|---|
| The two round-card slots (identity + order) | `RemoteControlBoard.OrderRoundCards`, `RemoteBoardCard.Set` | `RevealGate` |
| Round-card FACE ART (the real painted card) | `RemoteAbilityCardSource.ShowFullFace` → `TryLiveWidget` / `TryPooledClone` → `RemoteCardArt.ShowFront` | `RevealGate` (caller-side, once) |
| Name+initiative fallback panel | `RemoteBoardCard.Set` (`FacePath.None` branch) | `RevealGate` |
| Initiative NUMBER badge (**fallback only**) | `RemoteStatusReadouts.InitiativeText`, shown/hidden by `RemoteControlBoard.SyncInitiativeBadge` | `RevealGate` — mirrors `InitiativeTrackPlayerAvatar.CalculateInitiative` exactly. Hidden while the real track is mirrored: the owner's own board has no such badge, so drawing it there would be an ADDITION, not parity |
| Per-actor numbers inside the track | `RemoteInitiativeTrack.InitiativeLabel` | `RevealGate`; monsters follow vanilla's own numeric rule (`<0` blank, `0` → "?") |
| Rest state | `RemoteStatusReadouts.RestText` | **SPLIT gate** — `HasShortRested` / `HasLongRested` are past-tense public facts, shown unconditionally; the pending `LongRest` *selection* is secret and goes through `RevealGate` |
| Discard / burnt / item pile COUNTS | `RemoteControlBoard.RefreshContent` → `PileCounter.Set` | **deliberately UNGATED** — vanilla lets anyone open any player's full card overview from the initiative track (`InitiativeTrackPlayerAvatar.OnClick`) |
| The three pile STACK slabs | `RemoteControlBoard.EnsureBuilt` (`_piles[0..2]`) | position from wire, content = counts; exists so a card flight has a **visible destination** — before them, pile-bound flights ended in empty air |
| Active / persistent card column | `RemoteActiveCards.Refresh` | `RevealGate` — deliberately **stricter** than vanilla (an active card is public by definition) |
| Owner tag (Steam avatar + masked name) | `OwnerTag.Tick` / `Rebuild`, `NetPlayerActors.AvatarFor` / `NameFor` | read out of the game's own netcode by reflection, not our side channel |
| Wanted-slot pulse, snap glow, half divider | `RemoteBoardFurniture.SetWanted` / `SetSnap` / `SetHalves` | derived from slot occupancy the board **already draws** |

**VR-ONLY — genuinely needs wire bytes.** Head/hand poses and finger curls; control-board world
pose + scale + style; hand-fan count; item-fan count + held + which hand; the pile-browse block;
card-FX events; held-card pose; held-figure id + pose; dominant hand; ghost-hand flag + strength;
head-mask id + size; hand style. (See the wire tables in Part I.)

**DELIBERATELY-NOT — costs 0 B by decision.**

| Not transmitted | Instead |
|---|---|
| **Card identity, ever** | resolved locally through `RevealGate` from the replicated model |
| Card flight transforms | semantic `CardFxAnchor` pairs |
| Ghost-hand side | derived from the dominant-hand flag |
| Held-card rotation | receiver re-derives the billboard from the synced head |
| Item identity / per-item face size | backs only; the item's real effect syncs authoritatively through `UseItemService` |
| Figure transforms | the game re-derives them from authoritative board state every frame |
| Fan geometry (curvature, toe-in, bow, gaze apex, fan-out timing) | all derived from the synced hand + head; *"frame-for-frame the shape the owner sees, with no new wire field and no version bump"* |
| ~~Character portraits on track chips~~ **(retired)** | no longer needed: the track is now a live clone of the game's own widget (`RemoteWidgetMirror`), and `Object.Instantiate` copies the `RawImage.texture` `CharacterPortraitsProvider` already assigned on this client. The old entry claimed reaching the portrait was "neither cheap nor cheat-relevant"; it turned out not to need reaching at all. The mod-drawn chip strip keeps the plate+name look as its **fallback** |
| Confirm/Undo/Skip enabled state, Confirm's live label, follow/pin toggle state, decision-drawer open state, modal pick-field state | drawn in a documented **NEUTRAL** look; none crosses the wire and none is worth a field |
| The local player's own tuning offsets (`ConfirmUndoOffset`, `VRSettingsOffset`, `PinOffset`, `ClusterOffset`, `ItemUseSlotOffset`, `DecisionOffset`, `BrowseFanOffset`, `GenericButtonSpacing`, tuned `Fan*` config) | peers are drawn at the **authored defaults**, so every remote board looks the same regardless of local tuning |
| Hover split / insertion gap | needs the hovered index — one byte plus a reserved bit; not spent |
| Gaze-bias yaw opt-in | computable from the synced gaze; only the sender's toggle bit is missing |
| Empty-fan hint | its trigger is the palm-roll gate EDGE; the hand-card count cannot distinguish "gate opened with zero cards" from "fan closed" |

- **Where:** `RemoteBoardContent`, `RemoteBoardFurniture`, `RemoteAbilityCardSource`, `NetPlayerActors`, `PresenceState`, `AvatarState`
- **Now greppable in code** (batch B3): every remote-content type carries a `CLASSIFICATION:` tag in a `<remarks>` on its own doc comment, from the closed set `GLOBAL` / `PER-ACTOR MODEL` / `VR-ONLY` / `DELIBERATELY-NOT` / `MIXED (…)`, each citing the source expression it is derived from — `grep -rn "CLASSIFICATION:" src/GloomhavenVR/Net/`. `RemoteBoardContent`'s header, which used to say "TWO DATA CLASSES", now names all four. Marker interfaces were considered and rejected: four of the types are genuinely MIXED and an interface would have to lie about them (recorded as a Tier-3 candidate).
- **Rule:** New remote-board content must be classified before it is built, and the classification decides whether it may touch the wire at all.
- **Why:** Adding a wire field for something already replicated wastes a scarce flag bit (there is exactly one left) and creates a second source of truth that can disagree with the game — which looks exactly like a desync. Conversely, rendering a VR-only fact "locally" shows the *local* player's value on the *peer's* board.
- **Established by:** `1899421` feat(net): a peer's control board now shows what their own board shows; `cf38066` feat(net): the peer's board shows ALL its furniture — as inert visuals; `c5df48e` feat(net): peers' played cards render at full detail once the phase reveals them
- **Breaks if:** a new feature reaches for a wire field by reflex. **The default answer is GLOBAL or PER-ACTOR MODEL; VR-ONLY must be justified.**
- **Confidence:** high

### A remote board may CLONE a game canvas — it may never re-parent one
- **Where:** `RemoteWidgetMirror`, used by `RemoteInitiativeTrack` and `RemoteObjectivesPanel`; the same discipline as `RemoteCardArt` / `RemoteAbilityCardSource`
- **Rule:** GLOBAL panels on a peer's board are **live clones** of the game's own widget, hosted on our own world-space canvas, with every non-presentation component destroyed **before the clone is ever active** and the remaining rects/graphics/texts driven per frame from the original. The source is never moved, re-flagged or mutated.
- **Why:** The pre-2026-08 rule said a remote board must hand-draw every panel, because "a canvas cannot be in two places at once, and re-parenting or duplicating a live game canvas would violate the reversibility rule". Re-parenting would; **duplicating does not** — `Object.Instantiate` reads the source and writes a new tree. The hand-drawn versions were exactly what the user rejected three rounds running: the initiative track rendered as green/red rectangles instead of the real portraits, the objectives as a stand-in box. Cloning also copies **runtime** component state, which is why the portraits (assigned by `CharacterPortraitsProvider` at runtime) come across for free.
- **The two hazards, and how they are closed:**
  1. *The clone's game scripts must never run* — they register with singletons and mutate the state the original is driven from. So the clone is built under an **inactive** host (no `Awake` yet) and everything outside the presentation whitelist (`Graphic`, `CanvasRenderer`, `Mask`/`RectMask2D`, `BaseMeshEffect`, `CanvasGroup`) is `DestroyImmediate`d, including layout groups, `Canvas`, `CanvasScaler` and every `GraphicRaycaster`. A rebuild re-parks the host inactive first, for the same reason.
  2. *A structure change desyncs the pairing* — the source⇄clone node pairing is a flat index-aligned array built once; it is re-validated node-for-node on the content cadence and the clone is rebuilt whole on any mismatch.
- **Anti-cheat:** only for panels that are GLOBAL **and already on the local player's screen**. Cloning what this client already displays reveals nothing, which is why the mirror carries no gate of its own. A per-actor secret must never be mirrored this way.
- **Breaks if:** someone "optimises" the mirror by adopting the live widget instead of cloning it, keeps a game MonoBehaviour "because it drives an animation", or drives the clone from the model instead of from the source (which is what made the old hand-drawn panels wrong in the first place).
- **Confidence:** high — mechanism proven by `RemoteCardArt`, which has shipped the identical clone-not-adopt discipline for round-card faces since `c5df48e`

### A remote panel's SEAT is derived from the owner's own mount, never hand-tuned
- **Where:** `RemoteBoardLayout`; `PlayTray.{Objectives,Element,Pile,Active}MountBase` / `ReadoutBase` (widened to `internal` for this); `CardsConfig.BoardDefaults` (likewise)
- **Rule:** Every dock on a remote board sits at `<the owner's mount base> + <the AUTHORED per-board offset for their SYNCED style>` — the same two terms `PlayTray.BuildMounts` composes. Remote-only position constants are forbidden.
- **Why:** The remote board used to reproduce only the base and drop the per-board term, then compensate with a hand-estimated per-style "content proud lift" whose own comment admitted it was "an approximation of the meshes, not a measurement". On Oak (offsets ≈ 0) that was invisible; on Steel it mis-placed the objectives, the elements, the piles, the active column, the round readout and the initiative track simultaneously — the user's defect (c), "die Buttons sind nicht dort, wo der Besitzer sie hat".
- **Note:** the peer's own debug-menu RE-tuning stays DELIBERATELY-NOT (it never rides the wire), so every client renders a given style at its **shipped** layout — identical for the overwhelmingly common case of nobody having tuned anything.
- **Breaks if:** a new remote panel is given a literal board-local position instead of a `RemoteBoardLayout` member, or the promoted `PlayTray` mount bases are narrowed back to `private` and copied into `Net/` (that copy is exactly what `scripts/check-mirrors.sh` exists to police).
- **Confidence:** high

### Cosmetic choices the user picks are always VR-ONLY and always transmitted
- **Where:** `AvatarState.MaskId`, `AvatarState.HandStyle`, `PresenceState.MaskSizeCode`, `PresenceState.BoardStyleCode`, `PresenceState.GhostStrength`
- **Rule:** Every avatar/board cosmetic that appears in the VR settings panel rides the wire, so peers render *the sender's* choice.
- **Why:** Standing project rule: "everything the user sees, every peer sees the same way". A board that is bronze on its owner's screen and oak on everyone else's is the same disagreement class the ghost-strength byte was added to prevent. This is why the board style got two bits rather than being read from local config.
- **Established by:** `6994bc8` (mask id), `f584617` (hand style), `c1456f9` (ghost strength), `1c2a0df` (mask size), `cc99144` (board style)
- **Breaks if:** a new cosmetic is added and read from local config on the receiver — the cheapest-looking implementation, and wrong.
- **Confidence:** high

### The shared frame is world space, and `IBoardAnchor` is the seam that keeps it changeable
- **Where:** `IBoardAnchor`, `WorldAnchor.Instance`, `LocalRigSampler.TrySample` (`ToAnchor`), `NetAvatarDriver.ToWorld` / `ExtrasToWorld`
- **Rule:** Poses are converted to the shared frame on send and back to world on receive, through the anchor interface — even though the anchor is currently the identity.
- **Why:** Gloomhaven is server-authoritative and top-down, so world space **is** the shared frame; no per-player playspace anchor is needed. The seam exists so a future title build that offsets the board per client can be handled **without touching the wire format or the sampler**. The epic brief originally pointed at `PlayTray._root` as the shared origin — that was **wrong**: it is the per-player cards tray, parented under each player's own rig and re-homed to their head, so it is not identical across clients.
- **Established by:** `c6f3901` feat(net): scaffold multiplayer VR embodiment
- **Breaks if:** the identity anchor is inlined away as "obviously a no-op". It is a no-op *today*; that is the point.
- **Confidence:** high

### Frame conversion happens in the driver, so `RemoteAvatar` is world-only
- **Where:** `NetAvatarDriver.ToWorld`, `NetAvatarDriver.ExtrasToWorld`, called from `OnPacketReceived`
- **Rule:** Received poses are converted to world **before** being parked in `_pending`. Renderers never see shared-frame coordinates.
- **Why:** One conversion point. If renderers converted themselves, a new one would forget and place a peer at the wrong place — silently, since the anchor is identity today and the bug would only appear on the build that changes it.
- **Established by:** `1d362a8` feat(net): MP presence foundation — wire v3
- **Breaks if:** conversion is pushed down into the renderers "so the driver stays dumb".
- **Confidence:** high

### Figure sync is cosmetic because figure transforms are not networked
- **Where:** `NetFigures` header comment, `NetFigures.ApplyRemoteHeld`, `NetHeldFigures`, `Board/FigureGrab/ActorBehaviour_HeldTransform_Patch`
- **Rule:** A remotely-held figure is added to `NetHeldFigures`, which suppresses the game's own transform writers for it; on release it leaves the set and the game's `Update` snaps the mini back to its authoritative cell.
- **Why:** Every client re-derives a figure's position from authoritative board state each frame, so mirroring is purely visual and cannot desync. The suppression is what makes the mirroring visible at all — and its **automatic reversibility** (the game reclaims the transform the moment the set no longer owns it) is what makes it safe.
- **Established by:** `826c75a` feat(net): full figure-pickup sync (held figure rides the grabber's hand for all peers)
- **Breaks if:** the release path is made explicit/imperative instead of set-membership-driven, or `RebuildSet` is replaced by incremental add/remove that can drift.
- **Confidence:** high

### `NetHeldFigures` is rebuilt as a union, never incrementally patched
- **Where:** `NetFigures.RebuildSet`, called from `ApplyRemoteHeld`, `ReleaseRemote`, and the prune in `Tick`
- **Rule:** The suppression set is recomputed as the union of every player's held actor.
- **Why:** Keeps suppression correct when two peers hold different figures, or the same one, or a peer switches figures. Incremental removal would un-suppress a figure another peer still holds.
- **Established by:** `826c75a`
- **Breaks if:** replaced with `set.Remove(actor)` on release.
- **Confidence:** high

### The held-figure ghost is captured BEFORE suppression starts
- **Where:** `NetFigures.ApplyRemoteHeld` (the `!NetHeldFigures.Owns(actor) && !HeldFigures.Owns(actor)` guard around `FigureGhosts.NotifyHeld`)
- **Rule:** The home-spot ghost pose is captured on the **first** frame a figure becomes remotely held, before it is added to `NetHeldFigures` and before it is eased toward the remote hand.
- **Why:** That is the only frame the figure is still sitting at its authoritative board cell. Capture one frame later and the ghost marks a point along the flight path instead of the home spot.
- **Established by:** `bcc9260` feat(figuregrab): animated highlight, home-spot ghost, dual info panels (MP-synced)
- **Breaks if:** the ghost notification is moved below the `RebuildSet()` call, or the guard is dropped as "NotifyHeld is idempotent anyway" (it is idempotent — but not about *which pose* it records).
- **Confidence:** high

### Stable figure id is `CActor.ID`, and it can throw
- **Where:** `NetFigures.TryStableId`
- **Rule:** The cross-client id is `CActor.ID` (player = `CharacterClass.ModelInstanceID`, enemy/summon = `StandeeID`), read inside a `try` because it **throws for unsupported actor types**.
- **Why:** It is the game's own networked identifier — identical on every client, and what the game keys networked actor actions by. The `catch` is not defensive noise; the property genuinely throws.
- **Established by:** `826c75a`
- **Breaks if:** the try/catch is removed as an empty-catch smell.
- **Confidence:** high

### Held-item and held-ability-card share one representation
- **Where:** `LocalRigSampler.TryHeldCard`
- **Rule:** A grip-held `Cards.ItemsPile.ItemChip` **or** a grip-held `Cards.VRCard` both sample onto the same `FlagHeldCard` pose field, and both render as a card-BACK slab.
- **Why:** Before this, a held item was not a `VRCard`, so peers saw the grabbing hand move with an **empty hand** while a held ability card showed its back. Both grab routes — pinch-grab (`ProximityGrabber.BeginGrab`) and the board-laser pluck (`ItemChip.OnPoke` → `ProximityGrabber.ForceGrab`) — set `Grabber.Held` to the chip itself, so **one pattern match covers every way an item card gets into a hand**.
- **Established by:** `554f947` feat(items): polish item card system (item #3b); logged once via `LocalRigSampler.s_loggedHeldItem`
- **Breaks if:** the two cases are split into separate flags, or a third grab route is added that does not set `Grabber.Held`.
- **Confidence:** high

### Left hand wins when both hands hold a card
- **Where:** `LocalRigSampler.TrySampleHeldCard` (`TryHeldCard(Left) || TryHeldCard(Right)`)
- **Rule:** Left is probed first; only one held card is ever transmitted.
- **Why:** Matches the mirror's slab order, so the local preview and the remote view agree about which card is shown. The wire has room for exactly one.
- **Established by:** `19a50e5`
- **Breaks if:** the order is swapped, or a "both hands" extension is added without a wire field.
- **Confidence:** medium

### `LocalRigSampler` is read-only with respect to the rig
- **Where:** `LocalRigSampler.TrySample`, `SampleHand`
- **Rule:** The sampler only reads transforms owned by the Rig/Hands modules and returns `false` when there is neither a head nor a tracked hand — so nothing is sent.
- **Why:** Flat peers stay silent and therefore invisible to others, exactly as intended. A sampler that wrote to the rig would make the send rate observable as jitter in the local view.
- **Established by:** `c6f3901`
- **Breaks if:** a "convenience" write (e.g. caching a computed pose back onto a transform) is added.
- **Confidence:** high

### Local cosmetic config is read LIVE on every send
- **Where:** `LocalRigSampler.LocalMaskId`, `LocalMaskSize`, `LocalBoardStyle`, `HandVisuals.LocalStyle`
- **Rule:** These read the config entry on each sample, each guarded so an unbound config falls back to the default rather than throwing.
- **Why:** A settings change must reach peers on the next packet; caching at init means the stepper appears to do nothing on other players' screens. The unbound guards cover hot reload and the case where a module never inited.
- **Established by:** `33285a8` feat(net): [Net] MaskId + MirrorEnabled config; stamp MaskId on send; extended by `1c2a0df`, `cc99144`
- **Breaks if:** the values are cached in a field "since config reads are slow".
- **Confidence:** high

### `[Net] MaskSize` config range must stay inside the wire window
- **Where:** `NetModule.BindConfig` (`AcceptableValueRange<float>(NetProtocol.MaskSizeMin, NetProtocol.MaskSizeMax)`)
- **Rule:** The config bound is expressed in terms of the wire constants (0.25 .. 2.55), not literals.
- **Why:** A config value can then never be clipped in transit — a user setting 3.0× locally would look different to everyone else.
- **Established by:** `1c2a0df` feat(net): mask size is tunable and travels to every peer
- **Breaks if:** the range is replaced by hard-coded literals that later drift from the byte's range.
- **Confidence:** high

### `[Net] MaskId` / `MirrorEnabled` bind independently of `[Net] Enabled`
- **Where:** `NetModule.BindConfig` (separate from `Init`)
- **Rule:** `BindConfig` is separate and idempotent so the mask picker and the local mirror work even with the networking hook disabled.
- **Why:** The mirror is a **local** preview and has nothing to do with the transport. Folding the binding into `Init` breaks it whenever the kill-switch is off.
- **Established by:** `2c0b4bf` feat(worldui): AvatarMirror local self-preview; share head builder / `33285a8`
- **Breaks if:** `BindConfig` is inlined into `Init` as "only called from one place".
- **Confidence:** high

### The whole subsystem is a strict no-op offline
- **Where:** `NullNetTransport`, `NetModule.Init` (kill-switch + Harmony guard), `NetAvatarDriver` send gates, `NetFigures` (nothing sampled unless locally held)
- **Rule:** Single-player, offline, netcode-absent, non-modded-peer and config-disabled are all strict no-ops, and `FfsNetTransport` degrades to a logged no-op if any reflection member fails to resolve.
- **Why:** The mod must never break the base game. `[Net] Enabled` is an explicit kill-switch (default on) that removes the Harmony hook entirely, so a user with a misbehaving session can recover without a rebuild.
- **Established by:** `cac5474` feat(net): register multiplayer embodiment module behind a default-on kill-switch
- **Breaks if:** `NullNetTransport` is deleted as an unused class (it is the field initialiser default for `NetAvatarDriver._transport` and the `Configure` null fallback).
- **Confidence:** high

---

## Rig — world grab, scale, and the control board

### World grab is bound to the THUMBSTICK CLICK, and that is the whole arbitration
- **Where:** `WorldGrab.UpdateStickOwnership` (`VRHand.ThumbstickClickDown` / `ThumbstickClick`, i.e. `primary2DAxisClick`)
- **Rule:** Ownership is derived **solely** from the thumbstick click. There is deliberately **no** `hand.Grabber.Held` check — neither at engage nor mid-gesture. A non-null `Held` at engage only emits a debug line.
- **Why:** The old `Held == null` guard was purely defensive — world grab reads the stick click while figure/card grabs live on the trigger/grip, so they never contend — but it **froze locomotion whenever the player carried a mini or card**, so the table could not be pulled or rotated with an object in hand. A held object is parented to the hand, the hand rides the rig, so drag/rotate/scale carries it correctly and release stays owned by trigger/grip.
- **Established by:** `8d28b55` feat(figures): grab real board minis into hand + rebind world-grab to thumbstick click (binding); `95ae86f` fix(rig): allow world-grab locomotion while a figure/card is held (guard removal)
- **Breaks if:** a "defensive" `Grabber.Held == null` guard is re-added (it reads as obvious grab-arbitration hygiene), or world grab is "unified" back onto the grip. **The contention-freedom is a binding-level property, not a state check.** Note that `6700316`'s commit body still cites "the existing grip-contention rule (ProximityGrabber Held/Highlighted check at grip-down)" — that check no longer exists.
- **Confidence:** high

### THE CONTROL BOARD SCALES INDEPENDENTLY OF WORLD ZOOM
- **Where:** `PlayTray.ApplyFollowMode`, `PlayTray.SyncPinHolder`, `PlayTray.ComputeBoardScale`, `CardsConfig.ClampedTrayScale`, `CardsConfig.BoardScale(ControlBoard)` — with `WorldGrab.ApplyTwoHand` as the other half
- **Rule:** In FOLLOW mode the board root is parented under the rig anchor, so its *apparent* size is constant under world zoom. In PINNED mode the board is reparented under `GloomhavenVR.TrayPin` — a world-static holder at the **world origin with identity rotation** whose `localScale` is baked **once** from `scaleRef.lossyScale.x` at pin time and **never re-asserted**. `SyncPinHolder` carries **pose only**, on `RigPoseVersion` change; it must never touch scale.
- **Why:** This is the regression the user reported verbatim: *"Zooming the world zooms the control board too when it is PINNED — that must not happen. The board is scaled independently by the player."* The cause was a lost-board watchdog that re-asserted the holder's scale from the **live** rig scale every frame — with the holder tracking the rig, the board's world *size* grows with zoom while its world *position* is held, so it swells on screen exactly as the rest of the world shrinks.
- **Established by:** `f6d9725` fix(board): a pinned control board is no longer resized by world zoom; paired default by `f7c9b88` feat(config): default table scale 2.5x, board default 0.4x to match
- **Breaks if:** someone reasons "the holder should track the rig so it can't drift". The commit refutes that theory **twice**: it cannot drift (origin, identity rotation, scale baked once, not parented under the rig), and the rescale *is* the reported bug. A bare world detach instead of the scale-carrying holder is equally wrong — it bakes the diorama factor into `_root.localScale` and breaks `PlaceAtHead`, `PersistPoseToConfig` and the two-hand resize clamp, all of which assume 0.5×–2× semantics.
- **Confidence:** high

### Board default 0.4× is the arithmetic complement of table default 2.5×
- **Where:** `ComfortSettings.SavedScaleMultiplier` (2.5), `CardsConfig.BoardScale_*` (0.4)
- **Rule:** `0.4 == 1.0 / 2.5`. The two defaults are one decision.
- **Why:** The board is rig-anchored, so its apparent size does not shrink with the table. Changing one default without the other changes the board's apparent size for every new user.
- **Established by:** `f7c9b88`
- **Breaks if:** either default is tuned in isolation.
- **Confidence:** high

### One-time config migrations are marker-guarded and only touch untouched values
- **Where:** `ComfortSettings` (`TableScaleDefault25Applied`), `CardsConfig` (`BoardScaleDefault04Applied`)
- **Rule:** A migration runs **at most once per config file** and adopts the new default **only** when the saved value is *exactly* the old default. User-tuned values and the grab-written `TrayScale` are never changed.
- **Why:** Per Charter §5, an unread config key is still a user's persisted setting. A migration that re-fires, or that cannot tell "default" from "deliberately set to the same number", silently overwrites hardware-tuned values.
- **Established by:** `f7c9b88`
- **Breaks if:** the marker keys are pruned as "unused config entries" — the classic Tier-0 mistake this charter warns about.
- **Confidence:** high

### Hand positions are read in TRACKING space; the rig is re-solved, never integrated
- **Where:** `WorldGrab.TrackingPos` (`hand.transform.localPosition`), `WorldGrab.Anchor`, `ApplyOneHand`, `ApplyTwoHand`
- **Rule:** Anchors are captured in world space at engage; every frame the rig is **re-solved** from the anchor (`rig.position = _midAnchorWorld - rot * (mid * s)`), never advanced by a per-frame delta.
- **Why:** Reading `hand.transform.position` would already contain the previous frame's rig write — a feedback loop that makes the world swim or run away. Mapping is `world = rigPos + rigRot·(s·t)`.
- **Established by:** `dd1d637` feat(rig): Demeo-style world grab, snap turn, recenter & comfort guards (P4)
- **Breaks if:** `localPosition` is "clarified" to `position`, or the solve is converted to delta accumulation.
- **Confidence:** high

### Deadzones latch per gesture, and the drag deadzone scales with the rig
- **Where:** `WorldGrab._dragLive` / `_rotateLive` / `_scaleLive`; `DragDeadzoneMeters = 0.015f` (× live scale), `RotateDeadzoneDegrees = 2.5f`, `ScaleDeadzoneFraction = 0.04f`, `MinHandDistanceMeters = 0.05f`
- **Rule:** Once a deadzone is crossed it stays live for the rest of the gesture. The drag deadzone is multiplied by the live rig scale.
- **Why:** A per-frame threshold test re-triggers mid-drag and stutters; an unscaled deadzone is unusable at either zoom extreme.
- **Established by:** `dd1d637`
- **Breaks if:** the latches are replaced by a stateless threshold test.
- **Confidence:** high

### Scale clamps read the `Effective*` helpers, never the raw settings
- **Where:** `ComfortSettings.EffectiveScaleMin` / `EffectiveScaleMax` / `EffectiveVerticalDrag` / `PositionalClampsDisabled` / `ClampedSavedMultiplier`; consumers `WorldGrab.ApplyOneHand`, `WorldGrab.ApplyTwoHand`, `Comfort.SetScaleMultiplier`, `RigClamp.Apply`, `VRRigDriver.BuildRig`
- **Rule:** Consumers must read the `Effective*` indirection. `EffectiveScaleMin = FreeMovement ? Min(ScaleMin, 0.1) : ScaleMin`; `EffectiveScaleMax = FreeMovement ? Max(ScaleMax, 12) : ScaleMax`.
- **Why:** The user's requirement was *"komplett frei — auch nach unten oder oben — keine Grenzen, nur großzügige Zoom-Limits"*. Existing persisted configs (`VerticalDrag=false`, `ScaleMin=0.5`, `ScaleMax=4` on disk) would otherwise **silently defeat the new default** — the defaults only apply to fresh installs.
- **Established by:** `dfedadb` feat(comfort): fully free diorama movement — [Comfort] FreeMovement (default on)
- **Breaks if:** a caller reads `ComfortSettings.ScaleMin.Value` directly "since that's the setting" — which is exactly what it looks like.
- **Confidence:** high

### Reached scale is persisted on BOTH exit paths
- **Where:** `WorldGrab.Disengage` and the two-hand state-transition exit, both → `ComfortSettings.PersistScaleMultiplier`
- **Rule:** Persist on the clean release path **and** on `Disengage` (hand loss, mode change, disable). `PersistScaleMultiplier` no-ops for changes < 0.01.
- **Why:** Persisting only on the clean path loses the user's zoom on every tracking blip.
- **Established by:** `dd1d637`
- **Breaks if:** the two paths are deduplicated into the release path alone.
- **Confidence:** high

### Mode exclusions: Menu2D out, ModalUI in, dev proxy exempt
- **Where:** `WorldGrab.Update` gate, `SnapTurn.Update` gate
- **Rule:** World grab is disabled when `rig == null || !ComfortSettings.IsBound || !WorldGrabEnabled || (mode == VRMode.Menu2D && !RigTarget.IsDevProxy)`. `VRMode.ModalUI` is deliberately **absent** from the list. Snap turn is hard-disabled in `BoardTargeting` (P3a owns the stick for AoE rotation) and `Menu2D`, active in `ModalUI`.
- **Why:** Menu2D has a rig root (the menu rig) but no table — grabbing air there must not drag the menu view. Excluding `ModalUI` froze the diorama whenever a story/help box floated (test #13); nothing modal reads the stick.
- **Established by:** `6c5a832` feat(rig): menu rig — head-tracked Menu2D camera + Menu2D world-grab exclusion; `6700316` feat(rig): keep world grab + snap turn active in ModalUI (test #13)
- **Breaks if:** the gate is "tidied" into a switch over all non-scenario modes, or `RigTarget.IsDevProxy` is dropped — that flag is what makes the whole grab math testable on flat desktop.
- **Confidence:** high

## Rig — clip planes

### Near and far clip planes track the live diorama scale, every frame
- **Where:** `VRRigDriver.TickClipPlanes`; `BaseNearMeters = 0.05f`, `MinNearClip = 0.01f`, `MaxNearClip = 0.5f`, `MaxFarNearRatio = 50000f`, `_buildScale`, `_baseFarClip`
- **Rule:**
  `near = clamp(BaseNearMeters * rigScale, MinNearClip, MaxNearClip)`;
  `far = min( max(_baseFarClip, _baseFarClip * (scale / _buildScale)), near * MaxFarNearRatio )`.
  Writes are guarded by `Mathf.Approximately` — two float compares per frame. The same values are seeded in `CreateHeadCamera`.
- **Why:** The near plane was set **once** at rig build (0.05 × build scale, world units) while `WorldGrab` rescales the rig live over 0.1×–12×. Zooming in shrinks the rig scale, so the hands' world-unit distance from the eyes shrank below the frozen near plane and **the hands clipped invisible**. The commit verified no other scale-dependent culprit exists (no `LODGroup`, no `layerCullDistances`, no distance-based `SetActive`), so frustum clipping was the only mechanism. The far-plane floor keeps zoom-in from popping the diorama out; the ratio cap protects depth precision.
- **Established by:** `acd5b27` fix(rig): scale-aware clip planes — hands no longer vanish at max zoom-in
- **Breaks if:** the clip planes are set once in `CreateHeadCamera` ("cameras don't need per-frame clip-plane writes"), the `Mathf.Max(_baseFarClip, …)` floor is dropped, or the `MaxFarNearRatio` cap is removed.
- **Confidence:** high

## Rig — world tilt

### The tilt axis is a pure function of the rig pose (Demeo's parenting, expressed algebraically)
- **Where:** `VRRigDriver.TickWorldTilt` — `aimYaw = YawOnly(rig.rotation) * AngleAxis(_tiltAimYawDeg, up)`, `axis = aimYaw * Vector3.right`, `desired = AngleAxis(_tiltApplied, axis) * yawOnly`
- **Rule:** The rig-yaw factor makes `AngleAxis(tilt, yawOnly·right) ∘ yawOnly == yawOnly ∘ AngleAxis(tilt, +X)` — algebraically identical to Demeo's yaw-parent / tilt-child transform chain, where no code ever aims the axis and it co-rotates with world yaw **purely by parenting**.
- **Why:** Because the axis is a pure function of the rig pose, snap turn, smooth turn and world-grab rotation co-rotate it with **zero correction writes**, and under head-only motion `desired == current` so **no transform is written at all** — the world is bit-frozen. Magnitude is driven only by a discrete setting change, never by scale or zoom.
- **Established by:** `e8a9446` feat(tilt): replicate Demeo's exact tilt model — axis is the rig's own yaw-right, magnitude tweens 0.2s linear
- **Breaks if:** the axis is derived from anything head-positional. **Four earlier derivations were tried and reverted**: rig-yaw-only (`854d7b3`, tipped right), `up × flatten(head→FocusPoint)` (`5175921`), untilted-head frame (`0fa046e`), view-right with a 25° deadband + ~1 s ease (`8454e88`). Each looked correct and each produced a documented nausea bug.
- **Confidence:** high

### `YawOnly` is exact swing–twist, not euler extraction and not forward projection
- **Where:** `VRRigDriver.YawOnly` — `normalize(0, q.y, 0, q.w)` with a `mag < 1e-6` identity guard
- **Rule:** Twist about world up, computed exactly.
- **Why:** Forward-projection was only exact while the tilt axis *was* the yaw's own right axis. Under a non-yaw-aligned tilt axis (and under snap turn's world-up compositions) it bleeds per-frame yaw error into the healing loop, so the rig drifts in yaw every frame.
- **Established by:** `5175921` fix: wall fade needs the tiles-occlusion map …; player-relative world tilt axis
- **Breaks if:** "simplified" to `Quaternion.Euler(0, rot.eulerAngles.y, 0)`. Euler extraction is not the twist.
- **Confidence:** high

### Head movement must have ZERO visible effect on the world
- **Where:** `VRRigDriver.TickWorldTilt` — the `Quaternion.Angle(current, desired) > 0.01f` write gate, and the two-channel aim maintenance
- **Rule:** `_tiltAimYawDeg` is written by exactly two channels and nothing else:
  1. **Masked event** — `_axisSnapReason != null || grabActive || seedAimFromHead` ⇒ snap `_tiltAimYawDeg = headYawDeg`, error 0.
  2. **Masked rotation** — only while `|headRate| >= MaskedReaimHeadRateDps (30°/s)` **and** `|aimError| > MaskedReaimDeadbandDeg (5°)`: `step = sign(err) · min(|err|, MaskedReaimGainFrac (15%) · |headRate| · dt)`.
- **Why:** The round-4 hardware log is the evidence: with a 25° deadband and a 3/s ease the axis chased the view (`axisYaw 72.9→16.8→90.5→88.8→57.5→2.2→82.4`) and every re-aim rotated the whole world about the focus pivot **on pure head movement** — and the ease made it worse by gliding for a second after each glance. The stated hard requirement is verbatim: *"head movement must have ZERO visible effect on the world."* The gain sits below the ~20 % rotation-gain perceptual detection threshold, further scaled by `sin(tilt)`, and corrections stop **the same frame** the head slows.
- **Established by:** `7374507` fix(tilt): freeze tilt axis outside locomotion events — zero head-driven world motion (round 5); refined by `6301f60` feat(tilt): view-aimed world tilt via perceptually masked re-aiming (round 6)
- **Breaks if:** an ease or lerp is added "so the residual error converges eventually" — that **is** the catch-up glide round 5 deleted. Or the head-rate gate is evaluated once per burst instead of per frame. Or head yaw is read from `_camera.transform.rotation` (world) instead of `localRotation` — rig writes then fake head rotation and close the feedback loop.
- **Confidence:** high

### Head yaw is read from `localRotation` — device pose only
- **Where:** `VRRigDriver.TickWorldTilt` (`YawOnly(_camera.transform.localRotation).eulerAngles.y`), `_prevHeadYawValid`
- **Rule:** The aim's reference is the head's **local** yaw, invariant under rig writes. `_prevHeadYawValid` is cleared on the 0° fast path and in `TearDownRig`.
- **Why:** World-space head yaw contains the rig rotation we are about to write — a direct feedback loop. Clearing the validity flag prevents a stale-rate spike from firing a phantom re-aim on enable or rebuild.
- **Established by:** `6301f60`
- **Confidence:** high

### `grabActive` bypasses the aim deadband — continuous re-aim DURING a drag
- **Where:** `VRRigDriver.TickWorldTilt` (channel 1 includes `grabActive`), `WorldGrab.Instance.IsGrabbing`
- **Rule:** While a world grab is in progress the tilt re-aims to the live view every frame.
- **Why:** Explicit round-4 request: the tilt must re-aim continuously during a world drag, not only snap on release. The drag itself is the perceptual mask.
- **Established by:** `6b5c38c` fix(cards,rig): roll gate v4 parallel-transport … + continuous tilt re-aim during world-grab
- **Breaks if:** `grabActive` is dropped from channel 1 as redundant with the release-time snap.
- **Confidence:** high

### `NotifyTiltAxisSnap` is called by every locomotion event, synchronously
- **Where:** `VRRigDriver.NotifyTiltAxisSnap(string)` / `_axisSnapReason`; callers `SnapTurn.Turn` ("stick turn"), `WorldGrab.Update` on release ("world-grab release"), `WorldGrab.Disengage` ("world-grab disengage"), `VRRigDriver.Recenter` (sets `_axisSnapReason = "recenter"` directly)
- **Rule:** The snap notification happens **in the same call** as the locomotion write.
- **Why:** Deferring it by a frame reads as "the horizon slowly rolling right after every turn". The string is not decoration — it is the log attribution that tells a hardware tester which writer moved the axis.
- **Established by:** `8454e88` / `7374507`
- **Breaks if:** the notify is moved into a shared post-locomotion hook, or the reason strings are collapsed to a bool.
- **Confidence:** high

### The tilt tween sentinel is `-1f`, not `0f`
- **Where:** `VRRigDriver._lastTiltTarget`, `_tiltTweenFrom`, `TiltTweenSeconds = 0.2f` (linear, on `Time.unscaledTime`)
- **Rule:** `_lastTiltTarget == -1f` means "fresh rig" ⇒ adopt the target instantly (`_tiltTweenFrom = target`), attributed `"rig-build"`. Any other value tweens, attributed `"config-change"`. Reset to `-1f` in `TearDownRig`.
- **Why:** With a `0f` sentinel every rig rebuild would visibly tween the world up from flat. The 0.2 s linear tween replicates Demeo's `tiltTime = 0.2f` LeanTween exactly; `Time.unscaledTime` keeps it correct under pause and time scale.
- **Established by:** `e8a9446`
- **Breaks if:** `-1f` is normalised to `0f` as "cleaner", or `Time.deltaTime`-based smoothing is substituted.
- **Confidence:** high

### Tilt rotates about `FocusPoint`, not the rig origin, with a 0° fast path
- **Where:** `VRRigDriver.TickWorldTilt` — `pivot = CameraController.s_CameraController.FocusPoint`; `delta = desired * Inverse(current); rig.position = pivot + delta * (rig.position - pivot); rig.rotation = desired;` and the `if (target <= 0f && !_tiltActive) return;` fast path
- **Rule:** Rotate the rig **around the board focus point**. At 0° the code path is bit-identical to the pre-feature rig — early return, zero writes.
- **Why:** Rotating about `rig.position` reads as the world snapping rather than the viewpoint orbiting. The 0° fast path is what makes the feature free for users who never enable it.
- **Established by:** `854d7b3` feat: optional wall see-through ([Compat] WallFade) + Demeo-style world tilt ([Rig] WorldTiltDegrees)
- **Breaks if:** the fast path is removed for uniformity, or the mid-frame `controller == null` guard is dropped.
- **Confidence:** high

### Tilt diagnostics are a test oracle, not logging noise
- **Where:** `TiltLogIntervalSeconds = 5f`, `ChangeLogThrottleSeconds = 1f`, `BurstEndGraceSeconds = 0.3f`, `_lastChangeTrigger`, and the trigger fallback chain `_axisSnapReason ?? changeTrigger ?? (grabActive ? "world-grab" : maskedStep ? "masked-reaim" : "rig-pose-heal")`
- **Rule:** `rigYaw` / `aim` / `axis` **must read identical across consecutive periodic lines** unless a `WorldTilt change [trigger]` or a burst-summary line sits between them. `"rig-pose-heal"` appearing means an unattributed foreign writer flattened the rig. Masked-reaim frames are deliberately silent (one summary per burst).
- **Why:** This is the *only* way the freeze invariant can be verified on hardware. Round 4's diagnosis came directly from reading these numbers off a log.
- **Established by:** `7374507`, `e8a9446`, `6301f60`
- **Breaks if:** the logs are deleted as noise, or the trigger fallback chain is shortened — `"rig-pose-heal"` is the alarm value and only exists as the last fallback.
- **Confidence:** high

### `TickWorldTilt` runs in `LateUpdate` and is a full reconstruction
- **Where:** `VRRigDriver.LateUpdate` → `TickGuard.Run("Rig.WorldTilt", …)`
- **Rule:** LateUpdate — after every Update-phase rig writer (`WorldGrab`, `SnapTurn`, `Comfort`, `Recenter`) and before rendering. The tilt is recomputed from scratch each frame, never applied as a delta.
- **Why:** `WorldGrab`'s two-hand solve writes `rig.rotation = Quaternion.Euler(0, yaw, 0)` and `Recenter` flattens explicitly — both destroy the tilt. Running in LateUpdate heals it before the player ever sees an untilted frame. Being a reconstruction is what makes recenter / rebuild / snap-turn / world-grab compose "for free".
- **Established by:** `854d7b3`, hardened by `e8a9446`
- **Breaks if:** the tilt is moved to `Update`, into a coroutine, or converted to delta accumulation.
- **Confidence:** high

## Rig — snap turn and recenter

### Schmitt-trigger hysteresis on the turn stick
- **Where:** `SnapTurn.SnapEngageThreshold = 0.7f`, `SnapTurn.SnapRearmThreshold = 0.3f`, `SnapTurn._armed`, `SnapTurn.WaitingForRearm`
- **Rule:** Fire at `|x| >= 0.7` and disarm; re-arm only at `|x| <= 0.3`. Smooth mode uses a **rescaled** response `(|x| − 0.2) / (1 − 0.2)` so there is no discontinuity at the deadzone edge. Defaults: 45° (15–90), 90 °/s (30–270), `TurnMode.Snap`, `TurnHand.Dominant` resolved via `SnapTurn.ResolveTurnHand` from `[Hands] PrimaryHand`.
- **Why:** A single-threshold flick detector machine-guns the player around.
- **Established by:** `dd1d637`
- **Breaks if:** the two thresholds are unified, or the smooth-mode rescale is dropped for a plain `|x| - deadzone`.
- **Confidence:** high

### Snap turn pivots on the live HMD position
- **Where:** `SnapTurn.Turn` — `rig.RotateAround(head.transform.position, Vector3.up, degrees)`, fallback `rig.position` only when no head camera exists
- **Rule:** The rig root rotates **around the player's actual head**, so the head never translates and the table pivots around the player.
- **Why:** Pivoting at the rig root translates the player through the world on every turn.
- **Established by:** `dd1d637`
- **Breaks if:** the pivot is "simplified" to the rig transform, which is the obvious `transform.Rotate` idiom.
- **Confidence:** high

### Every stick-gate early return re-arms first
- **Where:** `SnapTurn.Update` — the `BoardTargeting` and `Menu2D` branches both set `_armed = true` before returning; per-hand suppression while `WorldGrab.Instance.IsHandGrabbing(hand)`
- **Rule:** Never return from the mode gate without re-arming.
- **Why:** Otherwise the very first frame after leaving AoE targeting fires a **phantom turn** from a stick that was already deflected. The commit calls this out explicitly: "re-armed on exit so no stale flicks".
- **Established by:** `dd1d637`, extended by `6700316`
- **Breaks if:** a guard-clause cleanup hoists the returns above the re-arm.
- **Confidence:** high

### `RigPoseVersion` bumps on build and recenter ONLY
- **Where:** `VRRigDriver.RigPoseVersion`, bumped in `BuildRig` / `BuildMenuRig` / `Recenter` / `RecenterMenu` / `ApplyRingSeat`; consumed by `PanelLayout` and `PlayTray.SyncPinHolder`
- **Rule:** Snap turn and world grab must **not** bump it.
- **Why:** That is the point — world-anchored panels and pinned control boards must stay fixed in the world while the player merely turns or drags. Bumping it "for consistency" makes them chase every turn.
- **Established by:** `66c0839` fix(rig): campaign/world map stays flat — scenario rig requires an actual scenario board
- **Breaks if:** a "complete the pattern" pass adds the bump to `SnapTurn.Turn` or `WorldGrab`.
- **Confidence:** high

### The multiplayer join seat lives in `TickSpawnRingSettle` and NOWHERE else
- **Where:** `VRRigDriver.TickSpawnRingSettle` (the only caller of `SpawnRing.Solve` and of `ApplyRingSeat`), reached from two places that are the same method: the first tracked pose and the 30-frame poll (`CircleReseatIntervalFrames`). `Recenter` knows nothing about the ring.
- **Rule:** `Recenter` is unconditional table-edge behaviour. The ring never edits it, never branches inside it, and never runs from `RequestRecenter`. Every ring outcome — `Placed`, `Offline`, `PeersUnknown`, `BoardPending`, config-off, window-closed, correction — writes an `Info` line naming the reason **and** the evidence (`SpawnRing.Probe`).
- **Why:** Round 1 put the ring inside `Recenter(useSpawnRing:)`, so its only real-world failure was reported inside a line that reads "Recentered", and the give-up path logged only for one of three non-placing outcomes. The 2026-08-02 hardware log therefore contained no line matching "Spawn ring" at all, and the feature looked as if it had never run.
- **Established by:** `83499d7` (round 1), rewritten in the ModBuild-20 post-mortem round.
- **Breaks if:** a "tidy up" folds the seat back into `Recenter`, or an outcome is allowed to return without a log line.
- **Confidence:** high

### The join seat is decided by PEER POSES, never by the FFSNet participant count
- **Where:** `SpawnRing.SolveCore` — `FFSNetwork.IsOnline` gates single-player; `NetAvatarDriver.CollectPeerHeads` decides the azimuth; `NetPlayerActors.LocalStableIndex` feeds ONLY the index fallback and the log.
- **Rule:** `Participants <= 1` must never be treated as "single player".
- **Why:** `PlayerRegistry.Participants` is `AllPlayers.FindAll(x => x.IsParticipant)` and is empty for seconds after a join (the same handshake the mod's own "Broadcast WAITING: … our NetworkPlayer has no id yet" line reports). Round 1 gated the whole feature on it and no-opped on exactly the client that needed it, while that client was already receiving the peer's head packets.
- **Breaks if:** a future refactor "simplifies" the online check back to a participant count.
- **Confidence:** high

### The join seat is armed on ARRIVAL only, and any self-movement ends it
- **Where:** `VRRigDriver.BuildRig` (`_priorKind != RigKind.Scenario`), `CloseRingWindow`, `NotifyPlayerLocomotion` (called from `WorldGrab` drag + two-hand, `SnapTurn.Turn`, `RequestRecenter`), `SpawnRingSettleSeconds = 30` measured from the FIRST TRACKED POSE.
- **Rule:** A scenario→scenario rig rebuild does not re-seat anyone. Exactly one placement plus at most one correction (only when the number of known peer poses grew). The window closes permanently on the first world grab, stick turn or manual recenter.
- **Why:** The ring is a JOIN placement. Re-seating a player who is already standing somewhere — because a camera got re-anchored, or because a peer's first packet arrived 20 s in — is the "fighting the player" failure, and it is worse than not placing at all.
- **Breaks if:** the window is armed in `BuildRig` unconditionally, or the locomotion notifications are dropped as "noise".
- **Confidence:** high

### Recenter flattens the tilt first, and `LateUpdate` restores it the same frame
- **Where:** `VRRigDriver.Recenter` — `if (_tiltActive) rig.rotation = YawOnly(rig.rotation);` then `_axisSnapReason = "recenter"`
- **Rule:** Flatten, do the yaw-only seat math, then let `TickWorldTilt` re-apply the tilt in the same frame's `LateUpdate`.
- **Why:** The seat math is authored for a yaw-only rig; under tilt it lands wrong. Because the re-apply happens the same frame, **no untilted frame is ever rendered**.
- **Established by:** `854d7b3`
- **Breaks if:** the flatten is removed, or the tilt re-apply is deferred a frame.
- **Confidence:** high

### The pending recenter waits for a real tracked pose
- **Where:** `VRRigDriver.Update` — `_pendingRecenter` fires only once `_camera.transform.localPosition.sqrMagnitude > 1e-6f`
- **Rule:** Do not recenter against the zero pose.
- **Why:** A recenter computed from an untracked (0,0,0) head seats the player wrongly, and it is not self-correcting.
- **Established by:** `dd1d637` / `cbc1b52`
- **Breaks if:** the guard is treated as a redundant null check.
- **Confidence:** high

### `RecenterMenu` legitimately puts the rig root ~1.1–1.7 m below the anchor
- **Where:** `VRRigDriver.RecenterMenu` — `rig = anchor − yaw·headLocal`
- **Rule:** `head_world = rig + yaw·headLocal = anchor`, exactly. With floor-origin tracking `headLocal.y ≈ eye height`, so the rig root sits below the anchor by that amount.
- **Why:** This is documented specifically because **it looks like a bug in a log**. Hardware test #3's odd `head y = −1.09` was a *stale pose from the disabled camera*, not this.
- **Established by:** `3468ced` fix(rig): rebuild on head-camera death/disable any frame; own head mask + stereo
- **Breaks if:** someone "fixes" the apparent below-ground rig root.
- **Confidence:** high

### `RigClamp` keeps the eyes above the table, unless FreeMovement says otherwise
- **Where:** `RigClamp.Apply`, `RigClamp.MinEyeAboveTableMeters = 0.10f`, `RigClamp.LastClampActive`; called from `WorldGrab.ApplyOneHand`, `WorldGrab.ApplyTwoHand`, `Comfort.SetScaleMultiplier`, `VRRigDriver.Recenter`
- **Rule:** Fully disabled while `ComfortSettings.PositionalClampsDisabled`; otherwise lifts the rig so `head.y >= FocusPoint.y + 0.10 · rigScale`. Called after **every** rig manipulation. Null-safe for the dev proxy and a torn-down rig.
- **Why:** Applying it at only some manipulation sites lets the player end up inside the table via the uncovered path.
- **Established by:** `dd1d637`, gated by `dfedadb`
- **Breaks if:** a new rig-writing path is added without the clamp call, or the four call sites are consolidated into one that misses a case.
- **Confidence:** high

### The recenter chord is two-handed and latched
- **Where:** `Comfort.UpdateRecenterChord`, `Comfort.ChordProgress`, `_chordFired`, `ComfortSettings.RecenterHoldSeconds` (1.0 s, 0 disables)
- **Rule:** B+Y (`VRHand.SecondaryButton`) on **both** hands, held for the configured duration, latched to fire once per hold, with `HapticPreset.GrabPulse` on both hands.
- **Why:** B/Y are the only spare buttons the frozen P2 hand API exposes (the OpenXR menu/system button is runtime-reserved), and a two-hand chord makes an accidental recenter unlikely. Without `_chordFired` it repeat-fires every frame past the threshold.
- **Established by:** `dd1d637`
- **Breaks if:** it is made a single-hand or single-press binding, or the latch is removed.
- **Confidence:** high

### `Comfort.SetScaleMultiplier` scales around the HMD
- **Where:** `Comfort.SetScaleMultiplier` — `rig.position = pivot + (rig.position − pivot) * (s / current)`
- **Rule:** Scale around the head's world position, then clamp and persist.
- **Why:** Scaling around the rig origin lurches the view whenever a settings slider moves.
- **Established by:** `dd1d637`
- **Confidence:** high

## Rig — comfort settings plumbing

### `ComfortSetting<T>` stores its handler so `Detach` can unsubscribe exactly
- **Where:** `ComfortSetting<T>._handler`, `ComfortSetting<T>.Detach`, `ComfortSetting<T>.OnSettingChanged`, `ComfortSettings.Unbind`
- **Rule:** Subscribe with a **stored delegate field**, never a lambda. `OnSettingChanged` wraps subscriber invocation in try/catch and logs via `VRLog.Error`. `Unbind` unsubscribes the file event, the bridged `WorldScaleBase.Changed`, and calls `Detach()` on all 16 wrappers.
- **Why:** A lambda cannot be unsubscribed, so every hot reload leaks a handler. A throwing settings-panel subscriber must not kill the whole config event chain.
- **Established by:** `df07bb7` feat(rig): ComfortSettings — typed comfort config API + change events
- **Breaks if:** the wrapper is "simplified" to `entry.SettingChanged += (s, e) => …`.
- **Confidence:** high

### `WorldScaleBase` lives in a different config file and needs its own bridge
- **Where:** `ComfortSettings.OnFileSettingChanged` (comfort file) and `ComfortSettings.OnWorldScaleBaseChanged` (main plugin file), both feeding `ComfortSettings.AnyChanged`
- **Rule:** Both handlers must exist; both try/catch.
- **Why:** File-level `SettingChanged` only covers the comfort file. Without the dedicated bridge the settings panel never dirty-marks on a world-scale edit.
- **Established by:** `df07bb7`
- **Breaks if:** `OnWorldScaleBaseChanged` is deleted as duplicate plumbing.
- **Confidence:** high

### The comfort vignette and seated mode were REMOVED — do not restore them
- **Where:** formerly `ComfortVignette` (component, `Pulse` / `NotifyMotion` call sites, `RigModule` registration, `ComfortGizmos` row); formerly `[Comfort] SeatedMode`
- **Rule:** Both are gone. `ComfortSettings` has no `SeatedMode`; `Comfort` always uses the standing preset (`StandingEyeHeightMeters` / `StandingEyeBackMeters`).
- **Why:** The vignette was removed because the user reported it *"does nothing"*; seated mode was dropped in the settings-panel redesign. `ComfortGizmos`'s class doc still *mentions* "vignette status" — that is stale text with no code behind it.
- **Established by:** `8454e88` (vignette component), `5e4942a` chore: remove dead vignette settings row, bindings and Loc label; `7bb5757` / `f89e9fc` (seated mode)
- **Breaks if:** a refactor "re-adds the obviously-missing comfort vignette", or re-introduces a seated preset because `StandingEyeHeightMeters` looks like it should have a sibling. This is a **negative** invariant: the absence is the decision.
- **Confidence:** high

## Rig — render quality

### The head camera renders FORWARD
- **Where:** `VRRigDriver.CreateHeadCamera` — `if (Plugin.ForwardRendering.Value) _camera.renderingPath = RenderingPath.Forward;`
- **Rule:** The owned head camera uses the forward path. `[Rig] ForwardRendering` exists only so forward's per-object light limit can be reverted if dungeon lighting ever regresses.
- **Why:** **This took eight hardware rounds to corner.** The SkyBackdrop `DepthResetRenderer` (Overlay shader, `ZTest Always`, queue 1999) has **no deferred pass**, so on a deferred camera it renders in the forward-opaque *fallback* — after the deferred G-buffer walls — wiping wall depth for the entire transparent pass (queues 3000–4000, even at `ZTest LEqual`). Every transparent effect bled through walls. Opaque figures were unaffected because they are occluded in the G-buffer before the wipe — "exactly the observed split". Forward restores strict per-queue order: reset (1999) before walls (2000). **MSAA also requires forward** — the deferred path ignores it.
- **Established by:** `1aa339a` fix: head camera renders FORWARD — deferred made the sky depth-reset (ZTest Always, no deferred pass) wipe wall depth for the transparent pass, so ALL transparent effects bled through walls (root cause, 8 rounds)
- **Breaks if:** the path is set to `DeferredShading` "to match the game", or `ForwardRendering` is defaulted off.
- **Confidence:** high

### The head camera generates `_CameraDepthTexture`, unconditionally
- **Where:** `VRRigDriver.CreateHeadCamera` — `_camera.depthTextureMode = DepthTextureMode.Depth;`
- **Rule:** Not config-gated.
- **Why:** The game's VFX shaders (torch/candle flame + glow, DFade clouds, distortion) **soft-fade against `_CameraDepthTexture`**. Big glow billboards physically poke through thin walls, and the depth-fade term is what hides those fragments. Our mod-created camera shipped `DepthTextureMode.None`, so the fade sampled nothing and **failed open**. All serialized shader pass states were proven clean (ZTest LEqual, walls ZWrite On) — this was the only missing piece. Cost: one depth prepass per eye.
- **Established by:** `7854669` fix: head camera generates _CameraDepthTexture — restores the game's own soft-particle depth fade
- **Breaks if:** it is removed as a perf win ("we don't use depth-based post effects").
- **Confidence:** high

### `allowMSAA` is FORCED true, never copied from the anchor
- **Where:** `VRRigDriver.CreateHeadCamera` — `_camera.allowMSAA = true;`
- **Rule:** Do not seed this flag from the anchor camera alongside the other copied properties.
- **Why:** A game camera shipping `allowMSAA = false` would silently veto the entire MSAA feature with no error. `allowMSAA` is only a permission; the actual sampling comes from `QualitySettings.antiAliasing`, and only on the forward path.
- **Established by:** `fef87d2` feat(rig): force eye-texture MSAA + global aniso (VR aliasing fix)
- **Breaks if:** a "seed all camera flags from the anchor uniformly" cleanup.
- **Confidence:** high

### `QualitySettings.antiAliasing` is re-asserted EVERY frame
- **Where:** `RenderQuality.ApplyMsaa`, `RenderQuality.Sanitize`, `MsaaSteps = {0,2,4,8}`, `_lastPushedDisplayMsaa`; ticked as `"Rig.RenderQuality"`
- **Rule:** Assert per frame (cheap int compare, log only on transition). `XRDisplaySubsystem.SetMSAALevel(max(wanted,1))` is pushed **once per value change** (the XR API uses 1 = no MSAA), with an early return when `Displays.Count == 0` so it retries next tick.
- **Why:** The HMD rendered with **zero** anti-aliasing: the game's FXAA/SMAA lives in the `PostProcessLayer` the mod kill-switches, and the boot quality level "Fastest" sets `antiAliasing = 0`. **Every game quality-level swap rewrites the value** — this is the same trap as `HandsDriver.EnforceGlobalSkinWeights`, where `QualitySettings.skinWeights` is a hard cap the boot level pins to 1 bone.
- **Established by:** `fef87d2`
- **Breaks if:** it is set once at rig build ("quality settings don't need per-frame writes"). It dies at the next quality-level swap, invisibly.
- **Confidence:** high

### `eyeTextureResolutionScale` is asserted per frame but written only on drift
- **Where:** `RenderQuality.ApplyEyeScale` (`|current − wanted| < 0.0005f` ⇒ return), `EyeResolutionScale` (0.8–2.0, default 1.0), `StepEyeScale` (rounds to the 0.1 grid), `_lastLoggedEyeScale`
- **Rule:** Never write when equal.
- **Why:** The setter **re-allocates the swapchain**. Writing it unconditionally every frame means constant re-allocation. `StepEyeScale`'s grid rounding stops repeated presses from drifting off the 0.1 steps. GPU cost is proportional to scale².
- **Why the lever exists at all:** Under VDXR the MSAA row does nothing (the runtime binds MSAA at swapchain creation and caps it), so supersampling is the *working* AA lever there — and the only one that touches shader/texture shimmer, which geometry-edge MSAA cannot.
- **Established by:** `6f16020` diag(rig): prove eye-target MSAA sample count; add EyeResolutionScale supersampling lever
- **Breaks if:** the drift epsilon is removed for "simplicity".
- **Confidence:** high

### The eye-target diagnostic readback is delayed 30 frames
- **Where:** `RenderQuality.RequestEyeTargetDiagnostics`, `LogEyeTargetDiagnostics`, `DiagDelayFrames = 30`, `_diagCountdown`, `_diagReason`; requested from `VRRigDriver.BuildRig`, `BuildMenuRig`, `ApplyMsaa`, `ApplyEyeScale`
- **Rule:** Wait 30 frames after any MSAA push / eye-scale change / rig build before reading `XRSettings.eyeTextureDesc`, then emit an explicit VERDICT line. `GetRenderPassCount()` is try/catch'd and **0 passes is logged as evidence**, not skipped.
- **Why:** MSAA cycling showed zero visible change on hardware despite the push chain logging success — **the log only ever proved WE set the level, never that the eye texture came back multisampled.** Reading the desc immediately measures the pre-reallocation state and "proves" the wrong thing. This whole mechanism exists to close that measurement gap.
- **Established by:** `6f16020`
- **Breaks if:** the delay is removed as an arbitrary magic number.
- **Confidence:** high

### Aniso forcing captures and restores the original
- **Where:** `RenderQuality.ApplyAniso`, `ForcedMinAniso = 8`, `GlobalMaxAniso = 16`, `_anisoForced`, `_anisoOriginal`
- **Rule:** Capture `QualitySettings.anisotropicFiltering` on the first force; on toggle-off restore it **and** call `Texture.SetGlobalAnisotropicFilteringLimits(-1, -1)` (the engine reset sentinel).
- **Why:** Otherwise the game keeps forced aniso after the user turns the toggle off — a non-reversible mutation of game state.
- **Established by:** `fef87d2` (aniso 8); max raised to 16 by `8454e88`, which also proved the earlier mip/aniso fix only touched a CPU-side silhouette readback that was destroyed and never rendered.
- **Breaks if:** the capture or the `-1/-1` reset is dropped.
- **Confidence:** high

### `RebuildRigOnMsaaChange` skips the boot-time first push
- **Where:** `RenderQuality` (`firstPush = _lastPushedDisplayMsaa < 0`), `VRRigDriver.RequestRebuild` / `_pendingRebuildRequest`
- **Rule:** Default OFF, diagnostic only, and never fires on the first push. The request is a one-shot consumed by the next `Update` and dropped while no rig exists.
- **Why:** Without the `firstPush` guard the rig tears down and rebuilds during boot on every launch. The toggle exists solely to prove/disprove on hardware that the OpenXR swapchain is session-owned rather than camera-owned.
- **Established by:** `6f16020`
- **Breaks if:** the guard is removed as an edge case.
- **Confidence:** high

## Rig — camera ownership and lifecycle

### The rig owns its head camera; game cameras never render stereo, period
- **Where:** `VRRigDriver.CreateHeadCamera` (`_cameraGo` = `"GloomhavenVR.HeadCamera"`), `VRRigDriver.CreateRigRoot` (`"GloomhavenVR.VRRig"`, `DontDestroyOnLoad`, `HideAndDontSave`), `VRCameraPolicy.AllowedHead`
- **Rule:** The anchor game camera is a **reference only** — vantage, yaw, culling-mask source, far plane. It is never reparented, never pose-driven, so **there is nothing to restore on it**. `stereoTargetEye = Both` on ours; `VRCameraPolicy` forces every other camera to `StereoTargetEyeMask.None` **unconditionally while VR runs** (the tracked-head "Reclaim" special case was deleted). Pose: `XRDevice.DisableAutoXRCameraTracking(_camera, true)` **before** adding `TrackedPoseDriver` (GenericXRDevice / Center, RotationAndPosition, `UpdateAndBeforeRender`). `_camera.depth = anchor.depth + 1f`.
- **Why:** Hardware test #4 was a **total HMD image freeze in the menu with zero exceptions, session FOCUSED, game running, our log silent** — because the rig head-tracked the game's menu camera directly, handing the whole pose chain to objects the game owns: menu camera animation writers, component toggles that don't trip `isActiveAndEnabled`, VideoPlayer interactions. The fix removes the interference *class* rather than chasing the trigger.
- **Established by:** `55569fe` fix(rig): rig owns its own head camera — game cameras never render stereo, period
- **Breaks if:** "just head-track the game camera, it's one less camera" — that is precisely the failure class removed. Also: copying `anchor.stereoTargetEye`, or letting `AllowedHead` be anything but our owned camera.
- **Confidence:** high

### The rebuild health check runs every frame, in a specific priority order
- **Where:** `VRRigDriver.Update` teardown-reason chain, `_rebuildTrigger`, `_sceneRecheck` / `_sceneRecheckName` set by `VRRigDriver.OnSceneLoaded`
- **Rule:** Priority order: pending `RequestRebuild` > rig-kind change > **our** camera destroyed > rig root destroyed > anchor destroyed > `!_anchor.isActiveAndEnabled` **(menu only)** > scene load brought a better menu camera via `ResolveMenuCamera()` **(menu only)**. Every teardown logs its trigger.
- **Why:** The MainMenu load **DISABLED (not destroyed)** the camera the menu rig was built around. A `_camera == null` check never fires for a disabled component, so the rig froze around a dead camera and the HMD stayed grey **with no rebuild ever logged**. Unity's fake-null does not cover disabled.
- **Established by:** `3468ced` fix(rig): rebuild on head-camera death/disable any frame; own head mask + stereo
- **Breaks if:** the disabled and destroyed cases are collapsed into one null check; or the two menu-only conditions are applied to the scenario rig as well.
- **Confidence:** high

### `ResolveMenuCamera` must exclude the head camera itself
- **Where:** `VRRigDriver.ResolveMenuCamera`
- **Rule:** `Camera.main` first, else the highest-`depth` enabled backbuffer camera (`targetTexture == null`) that is not tagged `UICamera` and **is not `HeadCamera`**. Uses the shared non-alloc buffer (`VRCameraPolicy.GetAllCamerasNonAlloc`); cold path only.
- **Why:** Without the self-exclusion the rig re-anchors to itself.
- **Established by:** `3468ced`
- **Confidence:** high

### Scenario head mask ORs the mod layer onto the LIVE anchor mask; the menu mask is the mod layer ONLY
- **Where:** `VRRigDriver.ComposeHeadMask`, `VRRigDriver.TickHeadCullingMask`
- **Rule:** Scenario ⇒ `(sourceMask == 0 ? 1 : sourceMask) | VRLayers.ModLayerMask`, re-composed **per frame from the live anchor mask**, falling back to our own mask once the anchor dies. Menu ⇒ `VRLayers.ModLayerMask` only, **never** the anchor mask.
- **Why:** Two separate hardware bugs. MainMenu's `Camera` shipped mask `0x00000000` — nothing rendered at all, hence the `?: 1` Default fallback. And on the campaign map the anchor is `MapCamera` with mask `0xF00FFE37` (the whole 3D world), so copying it made the HMD render **the giant map 1:1 below the player** while the flat screen floated inside it. The stated contract: *"The HMD in Menu2D must contain exactly: void + screen quad + hands + indicator."*
- **Established by:** `3468ced` (zero-mask fallback), `8af03f5` fix(rig): Menu2D head camera culls the MOD LAYER ONLY — never the anchor mask
- **Breaks if:** both rig kinds are unified onto `ComposeHeadMask` "since the menu is just another anchor"; or the mask is set once at build — `CanvasConversion` may OR UI bits onto our camera in scenario, and the game toggles the anchor's layers scene-side.
- **Confidence:** high

### Scenario keeps a Skybox clear; menu is always SolidColor VoidColor
- **Where:** `VRRigDriver.TickHeadClearColor`, and the clear seeding in `CreateHeadCamera`
- **Rule:** Scenario keeps the anchor's **Skybox** clear when it has one (that *is* visible content); otherwise — and **always** in the menu / mod-layer-only rig — `SolidColor` with `Plugin.VoidColor.Value` (default pure black). `TickHeadClearColor` early-returns unless `clearFlags == SolidColor`, then compares colour only.
- **Why:** Forcing SolidColor in scenario kills the sky; dropping the `clearFlags` guard in the tick hijacks a Skybox anchor. The historical dark grey `(0.12, 0.13, 0.15)` is documented as the debug value that distinguishes "renders but empty" from "camera dead".
- **Established by:** `b3fe14a` (dark grey era), `ebfac5d` feat(rig): head-camera void color from [Rig] VoidColor (default pure black), `8af03f5` (menu forced SolidColor)
- **Breaks if:** the `clearFlags` guard is removed, or SolidColor is forced unconditionally.
- **Confidence:** high

### The scenario rig requires an actual scenario BOARD, not just the orbit camera
- **Where:** `VRRigDriver.Update` rig-kind selection — `desired = !VRSession.IsRunning ? None : (scenarioCameraAlive && VRModeStateMachine.ScenarioBoardExists) ? Scenario : Plugin.MenuRig.Value ? Menu : None`
- **Rule:** Both terms are required.
- **Why:** `CameraController.s_CameraController` **exists on campaign/world-map scenes too** (verified against decompiled `ClickTrackerMap.cs:78`). Anchoring on it alone built the giant scenario diorama on the guildmaster map and the flat window lost the map (test #8). The scenario diorama additionally requires the Choreographer to be alive.
- **Established by:** `66c0839` fix(rig): campaign/world map stays flat — scenario rig requires an actual scenario board
- **Breaks if:** someone concludes `ScenarioBoardExists` is redundant because the camera controller "already tells us". It does not. Related: `[Rig] Experimental3DMap` is a **RESERVED, explicitly unimplemented** placeholder — wiring it to the rig-kind selection would silently re-enable the broken orbit-camera anchoring.
- **Confidence:** high

### `BaseWorldScale` stores the UNMULTIPLIED base
- **Where:** `VRRigDriver.ResolveWorldScale` (`FallbackWorldScale = 12f`, `TargetHexSizeMeters = 0.15f`), `VRRigDriver.BuildRig`, `VRRigDriver.BaseWorldScale`
- **Rule:** `[Rig] WorldScale > 0` wins (clamped 1–100); else `s_TileSize.x / 0.15f` clamped 1–100 so a hex reads ~15 cm; else 12 outside a scenario. `BuildRig` then multiplies by `ComfortSettings.ClampedSavedMultiplier`, but `BaseWorldScale` keeps the **unmultiplied** value.
- **Why:** Every scale clamp in `WorldGrab` / `Comfort` and the `CurrentMultiplier` readout are relative to the base. Storing the multiplied scale makes them compound on every session.
- **Established by:** `cbc1b52` feat(rig): VR camera rig — head-tracked scenario camera at diorama scale; multiplier default from `f7c9b88`
- **Breaks if:** `BaseWorldScale` is assigned the post-multiply value "since that's the actual scale".
- **Confidence:** high

## Rig — patches and ordering

### Two CameraController prefix-skips, both required, for FocusPoint parking
- **Where:** `CameraController_LateUpdate_Patch.Prefix` (`!VRSession.IsRunning`), `CameraController_RefreshFocusPosition_Patch.Prefix` (`!VRSession.IsRunning`)
- **Rule:** Both patches must stay. Since the owned-head-camera redesign the scenario camera does not render into the HMD at all — **the skips exist solely to keep the scenario camera and `FocusPoint` PARKED**, because `FocusPoint` is the anchor for the rig, the panels, recenter and the tilt pivot, and virtual-mouse edge-scroll/zoom must not drag it under the diorama.
- **Why:** They are complementary halves, not duplicates. Scripted flows (SmartFocus / MoveToLook / ZoomTo coroutines, message-profile camera moves) write the camera **without going through LateUpdate** and **re-toggle `m_IsCameraCodeControlDisabled` themselves**, so "LateUpdate skip alone is not durable". `BuildRig`'s `m_IsCameraCodeControlDisabled = true` is a third, also non-durable, half. Signatures were verified against the real `GH.Runtime.dll` v1.1.8307 with ilspycmd: global-namespace `CameraController`, `private void LateUpdate()` (single overload, no parameters) and `private void RefreshFocusPosition(float? y = null)` (single overload).
- **Established by:** `cbc1b52` (LateUpdate), `0cd975d` feat(rig): also prefix-skip CameraController.RefreshFocusPosition while VR runs, purpose narrowed by `55569fe`
- **Breaks if:** either is deleted as redundant now that we own the camera. Also: the `!VRSession.IsRunning` return value is what makes the game 100 % vanilla when VR is off — `RigModule.Init` relies on it ("applying them here is safe even if VR later shuts down").
- **Confidence:** high

### The Update tail is a cached, ordered, guarded step array — MixedReality LAST
- **Where:** `VRRigDriver._tailSteps`, built once in `Awake`; order `"Rig.HeadCullingMask"` → `"Rig.HeadClearColor"` → `"Rig.ClipPlanes"` → `"Rig.RenderQuality"` → `"Rig.CameraPolicy"` → `"Rig.MixedReality"`. `_tickSceneLoaded` carries `TickCameraPolicy`'s bool argument.
- **Rule:** The order matches the original inline `Update` tail **exactly**. MixedReality runs **last** so its key-colour clear wins the frame over `TickHeadClearColor`'s VoidColor — documented precedence: *"MR owns the head clear while on."* The bool goes through a field specifically so the lambda stays cached (zero per-frame allocation).
- **Why:** Reordering breaks the chroma key. Replacing the array with inline `TickGuard.Run("...", () => …)` calls allocates a delegate per step per frame — `TickGuard`'s own doc requires cached delegates. Capturing `sceneLoaded` in the lambda would defeat the caching.
- **Established by:** `16f32ee` fix: promote shared Core.TickGuard, isolate+attribute driver tick sequences; MR precedence from `5c881e7` feat(core): mixed reality chroma-key mode
- **Breaks if:** the array is reordered, inlined, or the field-passed argument is "cleaned up" into a closure.
- **Confidence:** high

### `TickGuard` isolates each sub-tick — and is the attribution mechanism
- **Where:** `Core.TickGuard.Run(name, fn, scope?)`
- **Rule:** Each sub-tick is isolated (a throw must not abort the rest of the frame); the **first** throw per name is logged at Error with stack, repeats are throttled to ~1 per 10 s on `Time.unscaledTime`; it **never rethrows** and allocates nothing on the hot path. Scope is the name prefix before the first `'.'`.
- **Why:** A per-frame stackless NullReferenceException flood (~1/frame) came from driver `Update` sequences calling `.Tick()` with no try/catch: **one throwing sub-tick aborted the rest AND logged an anonymous NRE every frame, starving the input pipeline** — this is the pause-menu-reopen bug.
- **Established by:** `16f32ee`
- **Breaks if:** the guard is removed because "the ticks don't throw". Without it the next hardware log names no subsystem at all.
- **Confidence:** high

### Update ordering inside `VRRigDriver.Update`
- **Where:** `VRRigDriver.Update`
- **Rule:** teardown/rebuild check (consuming and clearing `_pendingRebuildRequest` at the top) → pending-recenter check (gated on a real tracked pose; it arms the spawn-ring window, runs the ring's FIRST attempt, and falls back to `Recenter()` only when the ring did not place) → spawn-ring poll (only when `_kind == Scenario && !_pendingRecenter && !_ringSettled`) → the guarded tail.
- **Why:** Rebuilding after recentering would recenter a rig about to be destroyed; polling the ring during a pending recenter would double-seat. The ring runs BEFORE the fallback recenter so that a joining client whose peer is already known never sees the shared table-edge seat at all.
- **Established by:** `cbc1b52`, `68da11e`, `16f32ee`
- **Confidence:** medium

### `WorldGrab.Update` has a mid-frame drag-hand-swap re-anchor
- **Where:** `WorldGrab.Update` — the `_state == OneHand && the drag hand's stick released` branch
- **Rule:** Ownership update → state transition (persist scale / notify tilt / `Anchor`) → drag-hand-swap re-anchor → apply.
- **Why:** If the old hand releases and the new hand engages in the **same frame**, the state does not change but the anchor is stale — the world would jump by the hand separation.
- **Established by:** `dd1d637`
- **Breaks if:** the branch is deleted as an unreachable edge case.
- **Confidence:** medium

## Rig — reversible mutations

### The rig touches exactly ONE piece of game state, and restores it
- **Where:** `VRRigDriver.BuildRig` (`controller.m_IsCameraCodeControlDisabled = true`, `_frozeGameCameraControl = true`) and `VRRigDriver.TearDownRig` (sets it back to `false`, null-guarded on `s_CameraController`)
- **Rule:** This is the only game-side field the rig writes. Everything else the rig destroys is its own (`GloomhavenVR.VRRig`, `GloomhavenVR.HeadCamera`, the `TrackedPoseDriver`).
- **Why:** The class doc states it plainly: *"Everything we destroy here is OURS — the anchor game camera was never reparented or modified, so there is nothing to restore on it."* That property is what makes VR toggling reversible at runtime.
- **Established by:** `55569fe`
- **Breaks if:** the anchor camera is destroyed or reparented "since we're taking over anyway" — which re-creates the whole test-#4 interference class *and* introduces an unrestorable mutation.
- **Confidence:** high

### `MixedReality.RestoreAll` runs BEFORE `VRCameraPolicy.RestoreAll`
- **Where:** `VRRigDriver.OnDestroy`
- **Rule:** Restore keyed cameras first, then release the camera policy. `MixedReality.PruneDead()` on scene load.
- **Why:** The keyed cameras must be put back before the policy that governs them is released; the reverse order leaves cameras in the key-colour state with nothing left to restore them.
- **Established by:** `5c881e7`
- **Breaks if:** the two restore calls are reordered or alphabetised.
- **Confidence:** medium

### `TearDownRig` resets the whole tilt state machine
- **Where:** `VRRigDriver.TearDownRig` — `_tiltActive`, `_axisSnapReason`, `_lastChangeTrigger`, `_lastTiltTarget = -1f`, `_tiltApplied`, `_tiltTweenFrom`, `_tiltAimYawDeg`, `_prevHeadYawValid`, `_burstActive`, the spawn-ring pair `_ringSettled = true` / `_ringPlaced = false`, plus the statics `RigRoot` / `HeadCamera` / `BaseWorldScale`
- **Rule:** All of them, every teardown.
- **Why:** A surviving `_lastTiltTarget` makes the next rig tween up from flat; a surviving `_prevHeadYawValid` fires a stale-rate re-aim on the first frame of the new rig; a surviving `_axisSnapReason` mis-attributes the first log line; a surviving `_ringPlaced` would make the NEXT rig's first-pose recenter think it had already been seated and skip it entirely (the menu rig would never take its vantage).
- **Established by:** `e8a9446`, `6301f60`
- **Breaks if:** the reset block is trimmed to "the ones that matter".
- **Confidence:** high

### The dev rig proxy is auto-destroyed the moment a real rig appears
- **Where:** `RigTarget.Current` (contains `DestroyProxy()`), `GloomhavenVR.DevRigProxy`, `RigModule.Shutdown`
- **Rule:** The proxy's destruction is inside the property getter, not a separate lifecycle call.
- **Why:** It guarantees the flat-desktop test rig can never coexist with a real rig regardless of ordering. This is a deliberate feature (flat-desktop testability of the grab/turn math), not scaffolding.
- **Established by:** `dd1d637`
- **Breaks if:** the proxy is deleted as dev-only code, or `RigTarget.IsDevProxy` checks are pruned from `WorldGrab` / `SnapTurn`.
- **Confidence:** medium

### ~~`Comfort` subscribes and unsubscribes symmetrically~~ — MOOT since 2026-08
- **Where:** it *was* `Comfort.OnEnable` / `Comfort.OnDisable` — `ComfortSettings.TableHeightOffset.Changed`, both `IsBound`-guarded. Both methods are GONE with that setting (user ruling: free locomotion replaced the "Tischhöhe" dial), so `Comfort` subscribes to nothing at all now.
- **Rule:** Symmetric subscribe/unsubscribe — still the rule the moment `Comfort` grows another subscription; there is simply none to keep symmetric today.
- **Why:** Hot-reload cleanliness; that handler re-ran recenter, so a leaked one recentered a dead rig.
- **Established by:** `df07bb7` / `dd1d637`; retired with the setting 2026-08
- **Confidence:** high

---

# PART III — SUSPECTED VESTIGIAL

**Nothing here is deleted.** Each item is flagged with the reason it *looks* dead and the
reason it may not be. Per Charter §5, "no references" is not sufficient evidence in this
codebase.

### `NetModule` file-header registration comment
The block comment at the top of `NetModule.cs` says *"NOT REGISTERED in Plugin.cs yet — see
the registration note at the top of this file. The module is inert until that one line is
added"*, and gives instructions plus a stale line number. **It has been registered since
`cac5474`** — `Plugin.RegisterModules` contains `_modules.Add(new Net.NetModule());`.
The class-level `<summary>` repeats the claim. **Stale documentation, actively misleading.**
Safe to correct (documentation only, Tier 0). Confidence: high.

### `NetProtocol.FlagPileBrowse` doc comment: "bits4..7 reserved (0)" — **FIXED in batch B2**
The XML doc for `FlagPileBrowse` described byte A as *"bits4..7 reserved (0)"*. Bit 4 is the
mask-size bit and bits 5..6 the board style; **only bit 7 is still reserved.** It now names
each bit and points at `PileBrowseReservedBit`. Fixed because it sat directly at one of the
two "FLAG BYTE IS FULL" terminator sites B2 added and would have contradicted it.
**Still outstanding:** the same drift in a `PresenceSerializer.Write` inline comment,
*"Bits 5..7 stay zero for the next extension after this one."* **Doc only — the code is
correct.** Confidence: high.

### `NetProtocol.PileBrowseReservedBit` — added by batch B2, never read BY DESIGN
Extras trailing-block byte A, bit 7: **the last free bit in the entire protocol.** It has no
call site and never will until it is deliberately claimed — it is a reservation, not a field.
It exists as a symbol rather than a comment so it is greppable and appears in IntelliSense
beside the bits it neighbours, which is where somebody hunting for a free bit is standing.
**Keep. Same rule and same reason as `PileBrowseKindItems` and `BoardStyleMaxCode` below.**
Verified that no sender sets it: `PresenceSerializer.Write` builds byte A from bits 0..1
(masked `0x03`), bits 2/3/4 and the 5..6 style field, and never touches bit 7. Confidence: high.

### `NetAvatarDriver.PileKindWireOrderGuard` / `LocalRigSampler.ControlBoardWireOrderGuard`
Added by batch B1. Two `private const int` with no reader, whose initialiser is
`1 / (<wire-order predicate> ? 1 : 0)`. They look exactly like nonsense constants to a
"private, never read" analysis, which is why each carries a comment longer than the code.
**They are the only mechanism in the subsystem that turns a silent multiplayer desync into a
build failure** (CS0020, "Division by constant zero"). Deleting one restores the trap in full.
**Keep.** Confidence: high.

### `NetAvatarDriver.RemovePlayer`
Public, no callers. Its own doc says it is the *"immediate teardown entry point for a future
`PlayerRegistry.OnPlayerLeft` hook (staleness already handles it after
`StaleTimeoutSeconds`)"*. **Deliberate, documented forward hook** — and it is the only path
that also clears `_pending` / `_pendingExtras`. Keep. Confidence: high.

### `NetModule._driver`
Assigned in `Init` (`AddComponent` result, then `Configure`), nulled in `Shutdown`, never
otherwise read. Functionally the `Configure` call is the only use. It *may* be serving as an
intentional strong reference / debugger aid. Low-risk to inline, but it is a field on a
module class — check the debug menu before touching. Confidence: medium.

### `NetProtocol.PileBrowseKindItems`
Decoded by `RemoteBrowserFan` but **never emitted by any sender** — the item fan rides its
own older `FlagItemFan`. Explicitly documented as reserved for a later unification of the two
fans. **Wire constant: never renumber, never delete.** Confidence: high.

### `NetProtocol.BoardStyleMaxCode`
Only used by `EncodeBoardStyle`'s clamp. That clamp is what stops a hypothetical fifth board
from corrupting byte A's neighbouring bits. Keep. Confidence: high.

### `IBoardAnchor` / `WorldAnchor` — the identity anchor
`WorldAnchor` is the only implementation and every conversion is a no-op today. It is an
**explicitly documented seam** (see the invariant above) so a future build that offsets the
board per client needs no wire or sampler change. Inlining it is exactly the kind of
"obviously dead abstraction" removal the Charter warns about. Confidence: high.

### `NullNetTransport`
No `new NullNetTransport()` outside `NetAvatarDriver` — but it is the **field initialiser**
for `_transport` and the null fallback in `Configure`. Removing it makes every offline path a
null-reference risk. Keep. Confidence: high.

### One-shot log latches
`LocalRigSampler.s_loggedHeldItem`, `NetCardFx.s_loggedFirst`,
`RemoteAvatar._heldCardBillboardLogged`, `_loggedMaskSize`, `_loggedBoardStyle`,
`NetAvatarDriver._loggedBrowseOpen`, `RemoteBoardGate._loggedMode`. These look like dead
private state to a "written but never meaningfully read" analysis. They are read by the guard
that gates their own log line, and those lines are **hardware-test grep tokens** documented in
the debug notes (Charter §5). Keep. Confidence: high.

### `NetAvatarDriver.IncludeFingers`
A `private const bool = true` with a comment *"Fingers are cheap (10 B) and improve presence;
on by default. No shared-config edit."* Constant-folded by the compiler, so it is invisible in
the built output — a deliberate documented decision recorded as code rather than as a config
key. Keep as documentation. Confidence: medium.

### `RemoteBoardFurniture.Refresh`'s discarded `actor` parameter
The body contains `_ = actor; // reserved: no per-actor furniture state is knowable beyond the
slots (see notes)`. An **explicitly reserved seam**, not an oversight. Removing the parameter would
be a signature change that the next furniture feature has to undo. Confidence: high.

### `RemoteHandFan.PalmStandoff` — **REMOVED in batch D; it was never on this list**
Found by an independent unreferenced-member scan of `Net/` and `Rig/` at HEAD, not by this
registry: an `internal const float PalmStandoff = PalmOffset;` with **no call site anywhere**.
It was added by `eaec5f8` for `RemoteCardFx` to aim a card flight at the fan, and `9a7f911`
replaced that call with `RemoteHandFan.FanAnchorPoint(...)` **because aiming with the bare
offset was a bug** — the hand ROOT's `+Y` points out of the BACK of the hand, so flights landed
a palm-thickness on the wrong side. The constant survived its own supersession, still `internal`,
still offered by IntelliSense, with a doc that said only "prefer `FanAnchorPoint`".
Checked against Charter §5 before removal: not a Harmony target, not a Unity message, not a
serialized field, no reflection or `nameof` reference, not a config key, not a log grep token,
no debug-menu caller (the only other occurrences in the repo were the guard's own snapshots).
The record is preserved where it belongs — `FanAnchorPoint`'s doc now states why the bare offset
is not exposed. Guard: the single declaration line disappears from
`GloomhavenVR.Net/RemoteHandFan.cs` and nothing else changes anywhere.

**This is the only dead member in either subsystem.** Every other candidate the scan produced
(`PileKindWireOrderGuard`, `ControlBoardWireOrderGuard`, `PileBrowseReservedBit`,
`NetAvatarDriver.RemovePlayer`) is a Part III keep, below.

### `RemoteHandFan._sharedCardMesh`
A `static Mesh?` built once and shared by every ghost card, and — unlike every other mesh in the
subsystem — **never destroyed**. This is the one asymmetry in an otherwise uniform ownership rule
(`BuildBackSlab`'s doc says "Caller owns the returned mesh"). It is a process-lifetime cache by
design, but it is worth an explicit comment rather than leaving the asymmetry to be "fixed" by
someone adding it to a teardown. Confidence: medium.

### `RemoteAbilityCardSource.s_loggedPath`
A per-path one-shot log latch that is **not** reset on module shutdown, unlike `HeadMaskLibrary.Reset`
and `NetCardFx.Reset`. Diagnostics only — after a hot reload the "which face path fired" line does not
re-log. Harmless; noting it so the inconsistency is a known one. Confidence: high.

### The remote board POSE is not interpolated
`RemoteControlBoard.Tick` writes `SetPositionAndRotation` directly from the 5 Hz extras pose, while
the head, hands, held card, held figures and all three fans ease. Whether this is deliberate (a board
is usually stationary, and easing would lag a deliberate re-place) or simply never needed is not
recorded anywhere. **Flagging, not changing** — adding smoothing here is a Tier 3 behavioural change.
Confidence: medium.

### `RemoteHandFan.Tick` carries a live TODO
`// TODO(handedness): if fan is on wrong hand, foundation DominantRight predicate is inverted.` An
open hardware question, not dead code. Keep until a test round settles it. Confidence: high.

### Rig documentation drift (code is correct, prose is stale)
- `WorldGrab` / `SnapTurn` / `ComfortSettings.WorldGrabEnabled` descriptions still say "grip" in
  places (`"Grip-based table manipulation"`, `"one-grip drag"`, `RigClamp`'s "grab, scale, turn").
  The binding has been the **thumbstick click** since `8d28b55`. The `WorldGrab` class doc and
  `UpdateStickOwnership`'s doc are the accurate ones.
- `ComfortGizmos`'s class doc still mentions "vignette status"; there is no vignette row and no
  vignette component.
- `Comfort`'s doc says recenter is bound "in Phase 4" and `ComfortSettings` refers to the "deferred
  P3c panel" — both shipped.
- `6700316`'s commit body cites a grip-contention rule that `95ae86f` later deleted. Historical
  record only; do not restore the rule.
Confidence: high. Doc-only fixes; the code is right.

### `[Rig] Experimental3DMap`
Bound but **explicitly unimplemented** — a reserved placeholder whose stated purpose is that it
*"must never silently re-enable the broken orbit-camera anchoring"*. It reads exactly like a dead
config key. Per Charter §5 it is a user-visible persisted setting **and** a deliberate guard rail.
Keep. Confidence: high.

### `RenderQuality.RebuildRigOnMsaaChange`
Default OFF, diagnostic-only, and its only effect is to prove or disprove a hypothesis about
swapchain ownership on hardware. Looks like dead config. It is a documented debug lever
(Charter §5: "referenced only from the debug menu, which is a deliberate feature"). Confidence: high.

### `VRRigDriver.NotifyTiltAxisSnap`'s reason strings
`e8a9446` states that `NotifyTiltAxisSnap` "remains as log-attribution plumbing only" for the axis
itself — but it still sets `_axisSnapReason`, which channel 1 of the aim maintenance reads as a
*behavioural* input. **The string is diagnostics; the non-null-ness is behaviour.** Collapsing the
parameter to a bool would preserve behaviour and destroy the attribution; deleting the call entirely
would break the masked-event channel. Confidence: high.

### `RenderQuality` global state is not restored on teardown
`QualitySettings.antiAliasing`, `XRSettings.eyeTextureResolutionScale` and the pushed
`SetMSAALevel` are session/global state that — unlike the aniso settings — have **no restore path**
in `TearDownRig` or `OnDestroy`. Reverting requires setting the config back. This is a **deliberate
gap** (`EyeResolutionScale 1.0` is documented as "restores the native allocation"), but it is the one
place in the Rig subsystem where a mutation is not symmetric. Recording it rather than closing it;
closing it is a behavioural change. Confidence: medium.

### `NetProtocol.SendRateHz` / `ExtrasSendRateHz` as `float` Hz
Both are immediately inverted (`1f / rate`) at every use site. A "simplification" to store
intervals directly would change the meaning of the config-adjacent constants and of the
`ExtrasSendRateHz` reference in `PresenceState`'s doc. Cosmetic only — **Tier 3, out of
scope.** Confidence: high.
