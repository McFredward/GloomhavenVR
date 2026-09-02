# Wire records 35 and 36 — the 2026-09-02 report's items 5 and 6

Base: `origin/dev` at ModBuild 351 (`219fadc5`). Both records are additive TLV entries in the
extras packet's extension tail; **the wire version byte is unchanged (still v3)** and no flag bit
was spent.

---

## Record 35 — `ExtIdItemUsable`, 2 bytes, `ushort` LE

> *"Die Gegenstände, die benutzbar sind, haben eine highlighting Animation, diese ist aber nicht
> beim remote board beim Mitspieler sichtbar bei deren Gegenstandsfächer (verletzt 1:1 Regel)."*

A bitmask over `CInventory.AllItems` **RAW** index (bit *i* ⇔ `AllItems[i]` is usable). Written only
while non-zero.

* **Sender**: `Cards/Piles/PileViewer.ItemsUsableMask`, read verbatim in
  `NetAvatarDriver.TickExtrasSend` — the value the owner's own board framed its chips from in that
  frame, not a second evaluation of the predicate.
* **Receiver**: `RemoteAvatar.ItemUsableMask` → `RemoteItemFan.TickUsableFrames` →
  `RemoteUsableFrame.Build` (`WorldUI.SoftCueArt.FrameSprite` + `WorldUI.SoftFramePulse`, the
  owner's own machinery, driven off the shared `Time.unscaledTime` clock so every framed card in
  the room breathes in phase).

### Why the receiver cannot re-derive it

`ItemsPile.UsableMask` has two arms and both end in state that exists only on the owner's machine:

1. `CardsGameApi.IsActionTurn`, whose last term is `!FFSNetwork.IsOnline || cur.IsUnderMyControl` —
   the **viewer's** dial, false for every character the viewer does not control, i.e. identically
   false on exactly the board that needs the answer;
2. `Singleton<UIActiveBonusBar>.Instance`, a **local** UI singleton holding the local decider's rows.

Re-deriving would AND the owner's state with the viewer's copy of the same key — the mirrored
card-dust defect that was inert for seven builds.

### The index-space hazard (found while building it, not assumed)

`ItemsPile.Populate` and `RemotePileFronts.Resolve` both **skip null** inventory entries, while
`ItemsPile.UsableMask` sets bit *i* from the **raw** index. One null anywhere in `AllItems`
therefore puts every later chip one arc position below its mask bit.
`RemoteUsableFrame.ResolveSlots` re-walks `AllItems` and counts non-nulls to map bit → arc slot;
`ItemUsableVectors` pins both halves of that pair against the shipped source.

### Not gated by `RevealGate`

Same reason `RemotePileFronts.TryResolveItemSpentFlags` states: the gate governs card **fronts** in
the secret selection window, and a frame around a position in an inventory the flat game shows in
full is neither a front nor a secret. Gating it would make the mirrored arc disagree with its owner
in the one phase the 1:1 ruling grants no exception to.

### The beat period is deliberately the VIEWER's dial

`[Cards] ItemCueBeatSeconds` is **not** on the wire for this cue and that is not a per-sub-feature
sync carve-out. The dial is not a property of the owner's board: it is the rhythm this client
already beats every one of its own item cues on, and `SoftFramePulse` reads `Time.unscaledTime`
precisely so that every framed card in the room is in phase. Following the owner's period would put
a peer's frames out of phase with the viewer's own — one cue rendered as two. **Which** cards are
framed is the owner's answer and rides the wire; how fast the room breathes is the room's.

---

## Record 36 — `ExtIdHeldCardFace`, 2 bytes per pose slot (record length 2 or 4)

> *"Die Vorderseite SOLL man sehen auch von Karten die ein Spieler gerade in der Hand hat. Nur die
> Rückseite angezeigt werden soll nur in der Auswahlphase, in allen anderen Phasen sollen die Karten
> immer sichtbar sein, egal ob auf dem Fächer oder in der Hand eines Mitspielers."*

| byte | field |
|---|---|
| 0 | bits 0..4 **index** (31 = `HeldFaceIndexUnknown`), bits 5..7 **source list** (0 none / 1 hand / 2 discard / 3 burnt / 4 items; 5..7 reserved) |
| 1 | **length** of the list the sender indexed into, clamped to 255 |

Slot 1 is the rig packet's `FlagHeldCard` card, slot 2 is record 10's — the same two pose slots
record 34's grip bits name, filled by the same left-first rule in `LocalRigSampler`. The record is
read by LENGTH (2 = slot 1 only, 4 = both), like record 20's two forms.

* **Sender**: `LocalRigSampler.SampleHeldCardFaces` → `NameHeldCard`, which builds each list with
  the *exact* expression the receiver uses.
* **Receiver**: `RemoteHeldCardFace.Tick` → `Resolve`, one instance per pose slot, driven from
  `RemoteAvatar`.

### The paragraph that said this was impossible

`RemoteAvatar.UpdateHeldCard`'s doc argued that naming a held card would require putting a **card
id** in a packet, because "which of my cards is pinched between my fingers" exists nowhere in the
game model. Every clause of that is true except the conclusion — and the paragraph refutes itself
one sentence earlier, where it notes the model still holds the card *in whatever pile it came from*.
The receiver never needed an identity. It needed a **position** in a list it is already reading in
full to draw the fan the card was plucked out of. That paragraph has been **replaced**, not amended,
so the next reader cannot re-derive the wrong conclusion.

### Why the length byte is not padding

ModBuild 351 shipped a stopgap in `RemoteHandFan` because this client's copy of a peer's hand can
lag a whole choreographer turn behind theirs. A bare index would inherit that: a shifted **front** is
wrong in a way the player cannot read and would act on. The receiver refuses the front unless its own
copy of the named list is exactly as long as the sender said, so the record can fail to draw a card
but cannot draw the wrong one.

### The secret window

`RemoteHeldCardFace` asks `RevealGate.InScenario && RevealGate.ShowRoundCardFronts(actor)` for the
character the peer's board is displaying — the identical pair `RemotePileFronts.Tick` asks,
evaluated **every frame** for the reason that file states. During
`CPhase.PhaseType.SelectAbilityCardsOrLongRest` the slab is a back and **no clone exists at all**, so
the borrow path has nothing to borrow. Nothing was widened and no second copy of the rule was added.

The gate is on the **receiver** rather than the sender on purpose: an index leaks nothing a
determined local reader does not already have (the hand list is host-replicated onto every client),
so gating the write would buy no secrecy and would make the record's absence ambiguous between "not
holding" and "holding, in the secret phase".

---

## Every `{ back, back }` held-card surface, and which ones these records serve

| # | Site | Draws | Served? |
|---|---|---|---|
| 1 | `Net/Remote/RemoteAvatar.BuildCardSlab` | a peer's HELD card (both pose slots) | **yes — record 36** |
| 2 | `Net/Remote/RemoteCardFx.Acquire` | a peer's card in FLIGHT | no — see below |
| 3 | `Net/Remote/RemoteHandFan.Rebuild` | a peer's ability hand fan | not a defect: the back is the slab BODY; fronts are an overlay already routed through `RevealGate` |
| 4 | `Net/Remote/RemoteBrowserFan.Rebuild` | a peer's discard/lost browse arc | not a defect, same construction |
| 5 | `Net/Remote/RemoteItemFan.Rebuild` | a peer's item fan | not a defect, same construction |
| 6 | `WorldUI/AvatarMirror.GetOrCreateSlab` | the LOCAL player's own cards in the in-world mirror | not a defect and not this lane's path: a mirror shows the back of a card whose face points at you |

**Why the in-flight slab (2) is not served by record 36**, contradicting the old paragraph's claim
that it "would be fixed by exactly the same field": a flight is an **event** (the extras packet's
card-FX block is a wrapping seq plus two anchor ids), not a state, and the card is in transit
*between* two lists — the model has usually already moved it by the time the receiver replays the
arc, so there is no list position that names it for the duration of the flight. It needs a different
record.

---

## The `RemoteHandFan` stopgap is NOT retired

Record 36 names **one position**. The stopgap needs the whole **membership** — N entries, to know
that slab *i* is card *i*. A record that answers "which one is in the fist" cannot answer "which N
are in the fan", and its length byte is a consistency *test*, not a membership list (the same test
the stopgap already performs, from the other side). Retiring it needs a per-card membership record,
which rides on every packet of every player with an open fan rather than only while somebody is
physically holding a card — a decision about the wire's steady-state size, and a different round.
The reasoning is written at the stopgap itself so it is not re-derived.

---

## Cost

| | bytes | when |
|---|---|---|
| record 35 | 4 (`[id][len]` + 2) | only while ≥ 1 item is playable on the owner's own board (0 off-turn by construction) |
| record 36 | 4 (one held card) / 6 (both hands) | only while a slot really names a card |

`PresenceSerializer` worst case 1439 → **1449**; `MaxSize` unchanged at 1800, margin 351 > the
largest single record (257).

**Per-frame.** Send: both are sampled in `TickExtrasSend` (5 Hz + on-change), not per frame — record
35 is one static property read, record 36 is two hand checks plus one walk of a bounded list.
Receive: record 35 is one `ushort` compare per peer per frame in the steady state, with the mapping
walk behind that gate; record 36 is the `RevealGate` triple-read per slot per frame (what every
other remote card surface already pays) plus a `RemoteCardArt.ShowFront` that dedups on its own key,
with the list walk on a change edge and the `RemoteBoardContent.RefreshSeconds` cadence.

---

## Default-off proof

Both records are gated on their **payload**, not on their `Has*` flag, and the extension tail's
open-gate uses the same tests — so neither can open the tail on its own:

* `state.HasItemUsable && state.ItemUsableMask != 0`
* `state.HasHeldCardFace && HeldFacePayload(in state) > 0`

Wire vectors assert the bytes directly: a packet with `HandCardCount` set and both features "on but
empty" is `31 52 56 47 03 01 00 05` — 8 bytes, no flag bit, no block, no tail: byte-identical to
ModBuild 351's. On the read side an all-zero mask and an all-zero code pair both decode to "record
absent", so the two states stay one state in both directions.
