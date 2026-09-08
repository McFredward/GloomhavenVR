# Code Review — `Net/` and `Rig/`

> **[verified 2026-09-08 against `49ceab21` (ModBuild 483)] "Nothing here has been applied" is
> no longer true.** This is the 2026-07 Phase-1 review; **`PLAN.md` selected from it and `LOG.md`
> records what was executed** (batches A–F), and two further programmes have run since
> (`PLAN-2026-08.md` / `LOG-2026-08.md`, then `.planning/refactor-2026-09/`). Read this file as a
> **reading of the code at that date**, never as a work list — a proposal here may have been done,
> rejected with a recorded reason, or superseded. `LOG.md`'s "Claims that did NOT reproduce" and
> "Open items" sections are where the verdicts are.
>
> The line/file counts in the header below are 2026-07 measurements; the tree is **621 files /
> 551 166 lines** at `49ceab21`. This audit did **not** re-derive the findings themselves — it
> checked the framing. An individual finding in here is unverified against current source.

> Phase 1 output. Companion to `CHARTER.md` and `INVARIANTS-Net-Rig.md`.
> Scope: `src/GloomhavenVR/Net/` (29 files, ~9 100 lines) and `src/GloomhavenVR/Rig/`
> (10 files, ~2 900 lines).
>
> **Nothing in this document has been applied.** No source file was modified.
>
> Every proposal carries: tier, files, exactly what moves, the expected
> `scripts/refactor-guard.sh` output, and the risk if the proposal is wrong.
> Items are ordered by (value ÷ risk) within each section; the global worklist is §0.

---

## 0. The worklist, ordered by (value ÷ risk)

| # | Item | Tier | Guard | Value | Risk |
|---|---|---|---|---|---|
| 1 | [W1] Explicit numbering on `Cards.PileKind` + `Cards.ControlBoard` | 0 | **empty** | very high | none |
| 2 | [W2] Wire-coupling comment at both stragglers' enum sites | 0 | **empty** | very high | none |
| 3 | [D1] Stale doc: `NetModule` header tells a worker to double-register the module | 0 | **empty** | very high | none |
| 4 | [R1] `_tailSteps` comment omits `Rig.RenderQuality` — the order's only guard is wrong | 0 | **empty** | very high | none |
| 5 | [D2] Drifted byte-A bit maps (`FlagPileBrowse` doc, `PresenceSerializer.Write` comment) | 0 | **empty** | high | none |
| 6 | [D3] `FlagItemFan`'s XML doc is malformed (missing `<summary>` open tag) | 0 | **empty** | high | none |
| 7 | [W3] Name the last free bit: `PileBrowseReservedBit` + "FLAG BYTE FULL" markers | 1 | 1 added const on `NetProtocol` | very high | very low |
| 8 | [W4] Compile-time assertion binding `PileKind` to the wire order | 1 | 1 added const on `NetAvatarDriver` | very high | very low |
| 9 | [D4] Rig prose drift (vignette, "grip", P3c/Phase 4, `MixedReality` → `VRRigDriver.Tick()`) | 0 | **empty** | high | none |
| 10 | [C1] Split `RemoteBoardContent.cs` (7 top-level types) into 7 files | 1 | **empty** | high | very low |
| 11 | [C2] Split `RemoteGlowPulse` out of `RemoteBoardFurniture.cs` | 1 | **empty** | medium | very low |
| 12 | [B1] Structural classification: canonical `CLASSIFICATION:` doc tag on every remote-content type | 0 | **empty** | very high | none |
| 13 | [B2] Correct the "TWO DATA CLASSES" header — there are four | 0 | **empty** | high | none |
| 14 | [W5] Golden-vector wire tests (byte-exact, not round-trip) in a linked test project | 1 | **empty** (source-linked variant) | very high | low |
| 15 | [W6] `WIRE LAYOUT — ORDER IS THE SCHEMA` banner at the four serializer blocks | 0 | **empty** | high | none |
| 16 | [X1] Hoist `MountX`/`Width`, duplicated verbatim in two sibling types | 2 | 2 consts move between 3 types | medium | very low |
| 17 | [X2] Collapse `SetWanted`/`SetSnap`/`SetHalves` (truly identical modulo field identity) | 2 | confined to `RemoteBoardFurniture` | medium | low |
| 18 | [C3] Move `StripColliders` from `RemoteBoardFurniture` to `BoardVisual` | 1 | 4 types | medium | low |
| 19 | [R2] `VRRigDriver.Recenter()` is `internal` with no external caller → `private` | 1 | 1 member on `VRRigDriver` | low | very low |
| 20 | [X3] `BoardLocalToWorld` helper — one formula, five verbatim copies | 2 | 3 types | medium | low |
| 21 | [C4] `VRRigDriver` partial-class split (state + 5 leaf files) | 1 | **empty** | medium | low |
| 22 | [X4] `BuildFramedRecess` parameterised extraction | 2 | confined to `RemoteBoardFurniture` | low | low |
| — | Everything in §8 ("leave this alone") | — | — | — | — |
| — | Everything in §9 ("not now") | 3 | — | — | — |

---

## 1. Method, and what the guard actually proves

Before proposing anything I measured the guard snapshot in
`.planning/refactor/.guard/baseline/` rather than reasoning about it. Four facts
determine every guard expectation below, and two of them are not obvious.

**1.1 — Comments and XML docs are absent from the snapshot.**
`grep -c "<summary>" .guard/baseline/GloomhavenVR.Net/NetProtocol.cs` → `0`.
Therefore **every comment-only change in this review has a provably empty guard
diff.** That is not a hedge; it is a proof of no-op, and it is why the
documentation findings sit at the top of the worklist rather than the bottom.

**1.2 — Sequential enum members render *without* their explicit values.**
`Net/CardFxAnchor.cs` and `Hands/HandStyle.cs` both write `= 0, = 1, = 2` in
source. Both appear in the snapshot identically to `Cards/PileKind.cs`, which
does not:

```
internal enum CardFxAnchor : byte { HandFan, Slot0, Slot1, Discard, Burnt, Items, Board }
internal enum PileKind             { Discard, Burnt, Items }
```

Therefore **adding explicit `= 0, = 1, = 2` to `Cards.PileKind` and
`Cards.ControlBoard` produces an empty guard diff.** The single most valuable
change in this review is provably free.

**1.3 — `const` *declarations* are in the snapshot; `const` *use sites* are inlined.**
`AvatarSerializer` shows `private const float QuatScale = 32767f;` as a
declaration, while its `Write` body reads `WriteU32(buffer, ref i, 1196839473u)`
and `b | 1u`. Consequences:
- Adding a named const adds exactly one line to one type's snapshot file.
- **Moving a const between types leaves every consumer body byte-identical** —
  only the two declaration lines move. That makes const-hoisting (§7 X1) one of
  the cheapest verifiable refactors available here.
- Renaming a wire flag constant would change *nothing* except its declaration
  line, because the use sites are already numeric literals.

**1.4 — String literals ARE in the snapshot. `CENSUS.md` is wrong about this.**
`CENSUS.md` states, of the Group A/B config-description relabelling: *"Guard
expectation either way: description-only edits do not survive into IL, so the
guard diff is empty. That is itself the argument for preferring re-labelling over
removal."*

That is factually incorrect. `grep -rl "Grip-based table manipulation"
.guard/baseline/` hits
`.guard/baseline/GloomhavenVR.Rig/ComfortSettings.cs`. A `ConfigEntry` description
is a `ldstr` operand in the `Bind` call and survives into the decompiled output.
A description edit therefore produces a **real, visible diff confined to the
binding type**. The recommendation in `CENSUS.md` is still right; the stated
guard expectation must be corrected, or Phase 4 will see a diff it was told to
treat as collateral damage. This matters for §6 R3 below, which proposes exactly
such an edit.

**1.5 — File layout is invisible to the snapshot.** `ilspycmd -p` emits one file
per type, named after the type. `RemoteBoardContent.cs` (7 top-level types)
already appears as seven separate snapshot files. Splitting the source file to
match is a literal no-op for the guard.

**Organising principle for this review: prefer changes the guard can prove.**
Where a change is provably a no-op (1.1, 1.2, 1.5) the charter's burden of proof
is discharged by running the guard. Where it is not, the proposal must earn its
place, and several below do not — they are in §9.

---

## 2. The wire

### 2.1 The hazard, restated in the form that matters for a refactor

The registry's Part I is complete and correct; I verified every table in it
against `NetProtocol.cs`, `AvatarSerializer.cs` and `PresenceSerializer.cs` and
found no discrepancy in the *code*. Three drifted *comments* are in §5.

What I want to add is the shape of the hazard, because it determines what a test
can and cannot buy:

- A sender never parses its own packet.
- Both serializers are index-walkers with no tags.
- **Therefore the most likely damaging refactor is one that moves a block in
  `Write` and in `TryRead` *together*.** That is precisely what a "tidy the
  serializer by grouping the ifs per feature" pass would do, and it is exactly
  what the registry's `### Additive blocks in flag-bit order` entry warns about.
- **A round-trip test does not catch that.** Writer and reader stay mutually
  consistent, so `Write(x); TryRead(...) == x` still passes while every peer on
  the old build is corrupted.

This is the single most important consequence in this section and it drives W5.

### W1 — Explicit numbering on `Cards.PileKind` and `Cards.ControlBoard`

- **Tier:** 0 (source form only; IL identical — see §1.2).
- **Files:** `src/GloomhavenVR/Cards/PileBrowser.cs` (the `PileKind` enum),
  `src/GloomhavenVR/Cards/CardsConfig.cs` (the `ControlBoard` enum).
- **What moves:** nothing. `Discard,` → `Discard = 0,` and so on for all six
  members across the two enums.
- **Guard:** **empty.** Verified: the snapshot renders sequential enums without
  values regardless of source form (§1.2).
- **Risk if I am wrong:** none. There is no wrong here — the values are already
  0/1/2 by C# definition; the change makes them textual.

**Why this and not just a comment.** The registry entry `### PileKind member order
is a wire constant` says the danger is "anyone alphabetises or inserts into
`Cards.PileKind`". Explicit numbering does not merely *document* that — it
**neutralises the alphabetise case entirely**. With `Discard = 0, Burnt = 1,
Items = 2`, sorting the members to `Burnt, Discard, Items` changes nothing on the
wire. Only a deliberate renumber or a mid-list insertion remains dangerous, and
W4 catches those.

The same argument applies verbatim to `Cards.ControlBoard` (Oak/Steel/Bronze),
which the registry lists in §7 as a wire constant under the identical coupling
(`NetProtocol.EncodeBoardStyle` / `LocalRigSampler.LocalBoardStyle`) and which
carries no numbering and no comment either.

**The codebase already agrees with this recommendation.** Three of the five
wire-or-config-coupled enums in the mod already number themselves explicitly:

| Enum | Coupling | Explicit values today? |
|---|---|---|
| `Net.CardFxAnchor` | wire (4-bit id) | **yes** — `HandFan = 0` … `Board = 6` |
| `Hands.HandStyle` | wire (`FlagHandStyle` byte) | **yes** — `Glove = 0 / Plate = 1 / Arcane = 2` |
| `Net.RemoteBoardVisibility` | persisted config | **yes** — `Off = 0 / ActionPhaseOnly = 1 / Always = 2` |
| `Cards.PileKind` | **wire** (byte A bits 0..1) | **no** |
| `Cards.ControlBoard` | **wire** (byte A bits 5..6) | **no** |

This is not a new convention. It is finishing an existing one, and the two
stragglers are the two the registry singles out as the most dangerous.

### W2 — The comment at the enum

- **Tier:** 0. **Guard:** empty (§1.1).
- **Files:** same two.

A comment is the floor, and it is still needed after W1, because W1 does not tell
a reader *why* they may not renumber. Recommended wording, at `Cards.PileKind`:

```csharp
/// WIRE CONSTANT — the numeric values of these members are transmitted.
/// Net.NetAvatarDriver.TickExtrasSend casts this enum straight onto the extras
/// packet's byte A (bits 0..1); the receiver decodes it against
/// Net.NetProtocol.PileBrowseKindDiscard/Burnt/Items. Values are APPEND-ONLY:
/// never renumber, never insert in the middle. Nothing in the compiler links
/// these two files — a mistake here is invisible locally and shows up only as
/// every peer rendering the wrong pile's fan.
/// (Enforced at compile time by NetAvatarDriver.PileKindWireOrderGuard — see W4.)
```

and the equivalent at `Cards.ControlBoard`, naming `NetProtocol.EncodeBoardStyle`
and the two-bit field.

The registry's own entry ends with **"This coupling should be noted in `Cards/`
too."** — this is that note. It requires touching a file outside these two
subsystems; per the charter's one-subsystem-per-commit rule it should be its own
commit, coordinated with whoever reviews `Cards/`.

### W3 — Make "one free bit" visible where someone would reach for it

**The finding.** Byte A bit 7 is the last free bit in the entire protocol. Today
that fact is stated in *four* comment blocks (`NetProtocol.FlagPileBrowse`,
`PileBrowseMaskSizeBit`, `PileBrowseBoardStyleShift`, `PresenceSerializer`'s
layout doc) — **and two of the four say the wrong thing** (see D2). More
importantly, none of them is where a person would actually look.

Somebody adding an extras feature reads down the run of `Flag*` constants looking
for a free bit. They find `FlagPileBrowse = 1 << 7`, conclude the byte is full,
and have **nothing at that point in the file** telling them where to go next. The
answer lives 80 lines further down inside a 30-line XML doc on a different
constant.

**Proposal.** Give the bit a name so it exists as a symbol, and put two one-line
signposts at the two places a person actually stands.

1. A named constant next to the other byte-A bit constants:

```csharp
/// <summary>
/// Trailing-block byte A, bit 7 — THE LAST FREE BIT IN THE ENTIRE PROTOCOL.
/// Both flag bytes are exhausted (rig offset 6: bits 0..7 all spent, up to
/// <see cref="FlagHeldCard"/>; extras offset 6: bits 0..7 all spent, up to
/// <see cref="FlagPileBrowse"/>). This is the only extension slot left.
///
/// MUST BE WRITTEN 0 by every sender until it is deliberately claimed. Whoever
/// claims it should spend it the way <see cref="FlagPileBrowse"/> was spent —
/// on "a further sub-block follows", not on a boolean — or accept a wire-version
/// bump and a coordinated release (see the compatibility contract in
/// <see cref="PresenceSerializer"/>). Never read, by design: it is a reservation.
/// </summary>
public const byte PileBrowseReservedBit = 1 << 7;
```

2. A terminator comment closing each of the two flag-constant runs, i.e. directly
   after `FlagHeldCard` and directly after `FlagPileBrowse`:

```csharp
// ---- RIG FLAG BYTE IS FULL (bits 0..7 all spent). There is no free rig flag bit.
//      The only remaining extension slot in the protocol is NetProtocol.PileBrowseReservedBit
//      (extras trailing-block byte A, bit 7). ----
```

- **Tier:** 1 (one added, never-read `public const`).
- **Files:** `Net/NetProtocol.cs` only.
- **Guard:** one added line, `public const byte PileBrowseReservedBit = 128;`,
  in `GloomhavenVR.Net/NetProtocol.cs`. Nothing else anywhere — the constant has
  no use site, so no method body changes.
- **Risk if I am wrong:** the constant is an unreferenced public const and a
  future Tier-0 dead-code pass deletes it. Mitigation: it must be added to
  `INVARIANTS-Net-Rig.md` Part III alongside `PileBrowseKindItems` and
  `BoardStyleMaxCode`, which are kept for the identical reason.
- **Comment-only alternative:** drop the constant, keep only the two terminator
  comments. Guard diff then **empty**. Weaker, because a symbol is greppable and
  shows up in IntelliSense next to the bits it neighbours; a comment does not.
  I recommend the constant.

### W4 — A compile-time assertion for the `PileKind` coupling

W1 kills the alphabetise case; W2 documents the rule; neither stops a deliberate
renumber or a mid-list insertion. This does, and it is the only mechanism in this
review that turns a silent multiplayer corruption into a build failure.

Place it **at the cast**, in `NetAvatarDriver`, not in `NetProtocol` — the file
already has `using GloomhavenVR.Cards;`, and the assertion then sits in the same
file as the line it protects (`extras.PileBrowseKind = (byte)browseKind;`).

```csharp
// COMPILE-TIME WIRE GUARD. Cards.PileKind's member ORDER is a wire constant: the
// cast below puts it straight onto the extras packet's byte A (bits 0..1). Nothing
// in the compiler otherwise links Cards/ to Net/, so a reorder there would corrupt
// every peer's fan with no error, no single-player symptom and nothing wrong on the
// sender's own screen. If the orders ever diverge, THIS LINE STOPS COMPILING
// (CS0020, "Division by constant zero") instead. Values are append-only: adding a
// FOURTH pile at the end is fine and this guard permits it.
private const int PileKindWireOrderGuard = 1 / (
    (int)PileKind.Discard == NetProtocol.PileBrowseKindDiscard &&
    (int)PileKind.Burnt   == NetProtocol.PileBrowseKindBurnt   &&
    (int)PileKind.Items   == NetProtocol.PileBrowseKindItems ? 1 : 0);
```

**I verified this works** rather than assuming it. Building a minimal project
with the correct order succeeds; swapping two enum members produces:

```
A.cs(5,31): error CS0020: Division by constant zero
```

- **Tier:** 1. **Files:** `Net/NetAvatarDriver.cs`.
- **Guard:** one added line, `private const int PileKindWireOrderGuard = 1;`, in
  `GloomhavenVR.Net/NetAvatarDriver.cs`. No method body changes.
- **Risk if I am wrong:** the idiom is obscure and a future reader deletes it as
  a nonsense constant. Mitigated by the eight-line comment, which is longer than
  the code precisely for that reason. Also add it to Part III of the registry.
- **Recommendation on the three together:** do **all** of W1, W2 and W4. They
  defend different failure modes and none is redundant with another:
  W1 neutralises reordering, W4 catches renumbering and insertion, W2 explains
  the rule to the person who wants to add a fourth pile. W1 alone would be my
  answer to "the single cheapest change"; the set is the correct answer to
  "make it impossible to trip over".

  The same assertion should be written for `Cards.ControlBoard` against
  `NetProtocol.BoardStyleDefaultCode`/`BoardStyleMaxCode`, in
  `LocalRigSampler.LocalBoardStyle` — same tier, same guard shape.

### W5 — Golden-vector tests, not round-trip tests

**The requirement is narrower than it looks.** As argued in §2.1, a round-trip
test cannot detect the failure this codebase is actually exposed to: a refactor
that reorders `Write` and `TryRead` in lockstep. The test must assert **exact
bytes** against the tables in `INVARIANTS-Net-Rig.md` Part I §3a–3d and §4a–4e.

Minimum useful suite (each one a hand-computed expected `byte[]`):

1. Header: magic bytes are `31 52 56 47` in that order, version `03`, type byte.
2. Rig, all flags clear except head — asserts the 12-byte fixed part and a
   20-byte pose at offset 12.
3. Rig, both hands + fingers — asserts curls sit **immediately after their own
   hand's pose**, not batched (registry: `### Fingers follow their own hand's pose`).
4. Rig, held figure + held card — asserts the held-card pose is **after** the
   style byte (registry: `### Held-card pose comes after the hand-style byte`).
5. Rig, style flag — asserts bit 6 is set unconditionally on write and that a
   packet built without it still parses (registry: `### FlagHandStyle is always set on write`).
6. Extras, board + all four additive blocks — asserts `handCardCount` sits before
   every additive block, and the four blocks appear in ascending flag-bit order.
7. Extras, mask-size-only — asserts byte B is `0`, and that a reader renders no
   fan (registry: `### Zero card count means "no fan"`).
8. Extras, default board style + default mask size — asserts the emitted bytes
   are **identical** to a packet built without either feature. This is the test
   that protects the non-default-only transmission contract, which is the whole
   backward-compatibility argument.
9. Forward compatibility: append 8 junk bytes to each valid packet and assert
   `TryRead` still returns `true` with identical parsed state. This is the
   `### Readers validate only their own flags' length` invariant, expressed
   executably.
10. Byte A bit 7 is `0` in every packet the current writer can produce.

**How to build it without touching the shipped DLL.** The wire files' only
non-Unity code dependencies are two compile-time constants —
`HeadMaskLibrary.MaskCount` (`public const int = 3`) and `Hands.HandStyles.Count`
(`public const int = 3`). Nothing else in `NetProtocol.cs`, `AvatarState.cs`,
`AvatarSerializer.cs`, `PresenceState.cs` or `NetPacket.cs` reaches outside
`UnityEngine.Mathf` / `Vector3` / `Quaternion`.

Two options:

| | Source-linked test project | `InternalsVisibleTo` + project reference |
|---|---|---|
| Mechanism | `<Compile Include="../../src/GloomhavenVR/Net/AvatarSerializer.cs" />` ×5, plus a 2-const shim for `MaskCount`/`HandStyles.Count` | Add `[assembly: InternalsVisibleTo("GloomhavenVR.Tests")]` to the mod |
| Guard diff | **empty** — the shipped DLL is untouched | one assembly-level attribute; `ilspycmd -p` emits assembly attributes, so it **is** visible |
| Fidelity | the two shimmed consts could drift from the real ones | tests the real assembly |
| Verdict | **recommended** — and pin the shim with its own assertion so drift is a build error | acceptable fallback |

- **Tier:** 1 (new project; no production source changes in the linked variant).
- **Files:** new `tests/GloomhavenVR.WireTests/`; `GloomhavenVR.sln`.
- **Guard:** **empty** in the linked variant. The guard only decompiles
  `src/GloomhavenVR/bin/Release/net472/GloomhavenVR.dll`, which is not rebuilt by
  adding a sibling project. Add the test project to the solution *after*
  confirming `refactor-guard.sh baseline` still resolves the same DLL path.
- **Risk if I am wrong:** the shimmed constants diverge from the real ones and
  the clamp tests assert the wrong bound. Mitigation: give the shim the same
  CS0020 assertion idiom as W4 against a literal `3`, and cite the real
  declaration in a comment. Secondary risk: the test project is treated as
  shippable and ends up in the release payload — `scripts/package-release.sh`
  must be checked.
- **What this does not buy:** it does not test any receiver-side *rendering*, and
  it must not try to. Everything downstream of `TryRead` is a negotiation with
  Unity and a headset, which is exactly the class the charter says cannot be
  tested. The value is confined to the byte layout, and that is where it is high.

### W6 — Naming that makes the ordering contract obvious at the point of edit

Four blocks in two files carry the ordering contract:
`AvatarSerializer.Write` body, `AvatarSerializer.TryRead`'s `need` sum + body,
`PresenceSerializer.Write` body, `PresenceSerializer.TryRead`'s `need` sum + body.

Today the contract is explained at the top of each file, in prose, 150 lines away
from the statements it governs. `PresenceSerializer` does better than
`AvatarSerializer` — it has an inline banner
(`// ---- ADDITIVE trailing blocks (MUST stay after handCardCount and in FLAG-BIT ORDER) ----`)
in both `Write` and `TryRead`. `AvatarSerializer` has no equivalent banner in
either direction.

- **Proposal:** put the same style of banner immediately above all four blocks,
  with a fixed, greppable token and a pointer to the registry section that owns
  the layout:

```csharp
// ==== WIRE LAYOUT — STATEMENT ORDER IS THE SCHEMA ====================================
// This block's write order and TryRead's read order are one contract. Moving a
// statement re-interprets every trailing byte on every PEER — invisible to the
// compiler, invisible in single-player, invisible on this machine's own screen.
// Byte-by-byte spec: .planning/refactor/INVARIANTS-Net-Rig.md, Part I §3c.
// Any edit here must be paired with the `need` sum in TryRead AND a golden-vector test.
// ====================================================================================
```

- **Tier:** 0. **Files:** `Net/AvatarSerializer.cs`, `Net/PresenceSerializer.cs`
  (the latter is inside `PresenceState.cs`).
- **Guard:** **empty** (§1.1).
- **Risk if I am wrong:** none.

**Considered and rejected:** naming the `need`-sum magic numbers
(`PoseBytes = 20`, `FingerBytes = 5`, `HeldFigureBytes = 24`). It reads better and
the guard would confirm the use sites are unchanged (§1.3) — but it converts two
self-evidently-arithmetic expressions into indirections through constants that a
future editor could get wrong in a way the guard *cannot* catch (a wrong constant
value is a real behaviour change, and the guard would faithfully report it as
one, which is not the same as preventing it). The expressions are 2 lines each
and are already annotated. Value does not clear the bar. **Leave alone.**

**Also considered and rejected:** renaming `FlagPileBrowse`. The registry's own
"Breaks if" for that constant warns about a rename to `FlagBrowseFanOpen`
followed by a "simplified" write condition — a name that encoded the truth
(`FlagTrailingBlock`) would prevent that failure. The guard cost is trivial
(§1.3: one declaration line; use sites are already inlined literals). But the
name appears in ~15 doc comments, in `PresenceState`'s layout table, and by name
throughout `INVARIANTS-Net-Rig.md` Part I — so the change is 90 % documentation
churn, including churn in the registry that is supposed to be the stable
reference. **Not now**; the W3 signposts plus the D2 doc fix address the same
confusion at a fraction of the blast radius.

---

## 3. Is the GLOBAL / PER-ACTOR MODEL / VR-ONLY / DELIBERATELY-NOT classification in the code?

### The answer: no. It is prose everywhere, and the prose is incomplete.

Findings, all verified against source:

**3.1 — No structural expression exists.** All six remote-content files sit in
the single flat `namespace GloomhavenVR.Net;`. There is no marker interface, no
base class, no attribute, no enum and no naming convention that encodes the
classification. The only two interfaces in `Net/` are `INetTransport` and
`IBoardAnchor`, neither of which is a data-source marker. The only enum in the
content files is `RemoteAbilityCardSource.FacePath`
(`None`/`LiveWidget`/`PooledBorrow`), which is a *fidelity* axis, not a
data-source axis.

**3.2 — There is one accidental, machine-checkable signal, and it is not named.**
The `Refresh` signatures encode the classification almost perfectly:

| Widget | Signature | Class |
|---|---|---|
| `RemoteObjectivesPanel` | `Refresh()` | GLOBAL |
| `RemoteElementStrip` | `Refresh()` | GLOBAL |
| `RemoteInitiativeTrack` | `Refresh()` | GLOBAL list + per-actor numbers |
| `RemoteStatusReadouts` | `Refresh(CPlayerActor, bool showFronts)` | GLOBAL **+** PER-ACTOR (mixed) |
| `RemoteActiveCards` | `Refresh(CPlayerActor, bool showFronts)` | PER-ACTOR |
| `RemoteBoardFurniture` | `Refresh(CPlayerActor, RemoteAvatar owner, bool, bool, bool)` | VR-ONLY + GLOBAL + derived |

The load-bearing distinction is real: **`RemoteAvatar` — the wire-sourced type —
appears in exactly one widget signature**, and `CPlayerActor` — the
model-sourced type — appears in the others. That *is* the taxonomy, expressed in
the type system by accident. Nothing names it, nothing enforces it, and
`RemoteBoardFurniture` weakens it by taking a `CPlayerActor` it discards:

```csharp
_ = actor; // reserved: no per-actor furniture state is knowable beyond the slots (see notes)
```

**3.3 — The code's own taxonomy header says there are TWO classes. There are
four.** `RemoteBoardContent.cs` opens with:

> `/// TWO DATA CLASSES, both zero-wire:`
> `///   • GLOBAL — …`
> `///   • PER-ACTOR MODEL — …`

VR-ONLY is never named as a class anywhere in `Net/`. The literal phrase
"VR-only" occurs **exactly once in the entire directory**, in
`RemoteControlBoard.cs`, referring to four classes that live in other files and
share no base type:

> `/// The transient reading fans (hand fan, item fan, pile browse, card flights) are handled by the`
> `/// dedicated VR-only wire fields (RemoteHandFan / RemoteItemFan / RemoteBrowserFan / RemoteCardFx).`

And `RemoteBoardFurniture` implements a **fourth** flavour the header does not
admit exists — state knowable from neither wire nor model, drawn in a fixed
neutral look. It is documented four separate times, each in a different member's
comment ("NEUTRAL LOOKS declared here, once", "LOCAL-ONLY STATE" ×2,
"drawn at the AUTHORED defaults"). It maps onto the registry's
**DELIBERATELY-NOT** class, but nothing says so.

**3.4 — The closest thing to a declaration is a field comment column.**
`RemoteControlBoard.cs` is the only place the taxonomy is written next to the
declarations:

```csharp
private RemoteObjectivesPanel? _objectives;   // GLOBAL
private RemoteElementStrip? _elements;        // GLOBAL
private RemoteStatusReadouts? _status;        // GLOBAL round + per-actor initiative/rest
private RemoteActiveCards? _active;           // per-actor active/persistent cards
private RemoteInitiativeTrack? _track;        // GLOBAL actor list + per-actor initiative (gated)
private RemoteBoardFurniture? _furniture;     // INERT copies of the board's interactive controls
private readonly PileCounter?[] _piles = new PileCounter?[3]; // discard / burnt / items
```

Note that `_furniture` and `_piles` get **no data-class tag at all** — and
`_furniture` is the one that reads the wire.

### B1 — Make it a canonical, greppable doc tag on every remote-content type

- **Tier:** 0 (documentation).
- **Files:** `Net/RemoteBoardContent.cs`, `RemoteBoardFurniture.cs`,
  `RemoteControlBoard.cs`, `RemoteHandFan.cs`, `RemoteItemFan.cs`,
  `RemoteBrowserFan.cs`, `RemoteCardFx.cs`, `RemoteAvatar.cs`,
  `RemoteActiveCards`/`RemoteObjectivesPanel`/`RemoteElementStrip`/
  `RemoteStatusReadouts`/`RemoteInitiativeTrack`/`RemoteBoardCard` (all inside
  `RemoteBoardContent.cs` today; see C1).
- **What moves:** nothing. One `<remarks>` line is added to each type's
  `<summary>`, using one fixed token from a closed set:

```csharp
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide game state, bit-identical on every
/// client, ZERO wire. Source: ScenarioManager.CurrentScenarioState. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
```

  Closed set, matching the registry exactly:
  `CLASSIFICATION: GLOBAL` / `CLASSIFICATION: PER-ACTOR MODEL` /
  `CLASSIFICATION: VR-ONLY` / `CLASSIFICATION: DELIBERATELY-NOT` /
  `CLASSIFICATION: MIXED (…)` for the three types that genuinely span classes.

- **Guard:** **empty** (§1.1).
- **Risk if I am wrong:** a type is tagged with the wrong class and a future
  feature trusts the tag. Mitigation: every tag in the proposal is derived from
  the registry's per-widget table, which was itself derived from the code; the
  tag must cite its source expression (`ScenarioManager.…`, `CPlayerActor.…`,
  `owner.…`) so it is checkable in one glance.

**Why a doc tag rather than a marker interface.** I considered
`interface IGlobalBoardWidget { void Refresh(); }` /
`IPerActorBoardWidget { void Refresh(CPlayerActor, bool); }`. It would make the
classification compiler-visible and would force a new widget to pick a side —
which is exactly the property §3 is asking for. I am **not** recommending it, for
three reasons:

1. It cannot express the truth. Three of the six types are genuinely MIXED
   (`RemoteStatusReadouts`, `RemoteInitiativeTrack`, `RemoteBoardFurniture`), and
   forcing them onto one interface would either be a lie or would require the
   cohesion split in §9 N2 first — which is Tier 3.
2. Implementing an interface changes the type's metadata: the guard diff would
   show `internal sealed class X : IGlobalBoardWidget` on six types plus a new
   type. That is a wide, non-empty blast radius for a documentation benefit.
3. It buys enforcement only at the *call* site, and there is exactly one call
   site (`RemoteControlBoard.RefreshContent`), which is eleven lines long and
   already reads as a manifest.

The doc tag gets ~90 % of the benefit at zero guard cost. If the user later wants
the compiler to enforce it, the interface becomes a clean follow-on — and it will
be much easier after C1 has put each type in its own file. **Record it as a
Tier-3 candidate, not as this refactor's work.**

### B2 — Correct the "TWO DATA CLASSES" header

- **Tier:** 0. **Files:** `Net/RemoteBoardContent.cs` (header), and a matching
  pointer in `RemoteBoardFurniture.cs`.
- **What changes:** "TWO DATA CLASSES, both zero-wire" → four named classes
  matching the registry, with the note that `RemoteBoardFurniture`'s "NEUTRAL
  LOOKS" and "LOCAL-ONLY STATE" blocks *are* the DELIBERATELY-NOT class and are
  not a fifth thing.
- **Guard:** **empty**.
- **Risk if I am wrong:** none.

**Why this matters more than it looks.** The registry's own rationale for the
classification is: *"Adding a wire field for something already replicated wastes a
scarce flag bit (there is exactly one left)."* A worker who reads only
`RemoteBoardContent.cs`'s header learns that remote-board content is
"both zero-wire" — and therefore has no framework at all for the question
"should this new thing go on the wire?". The header is the first thing they read
and it currently forecloses the very decision the registry says must be made
first.

---

## 4. God files and cohesion

### C1 — Split `RemoteBoardContent.cs` (1 202 lines, 7 top-level types) into 7 files

- **Tier:** 1, pure motion.
- **Files:** `Net/RemoteBoardContent.cs` →
  `RemoteBoardContent.cs` (the static helper: `Label`, `SetText`, `RefreshSeconds`),
  `RemoteObjectivesPanel.cs`, `RemoteElementStrip.cs`, `RemoteStatusReadouts.cs`,
  `RemoteInitiativeTrack.cs`, `RemoteActiveCards.cs`, `RemoteBoardCard.cs`.
- **What moves:** each top-level type, its banner comment and its nested type,
  verbatim, statement order untouched. The file is already sectioned by six
  `// ====` banners — it is a seven-file directory that was never split. Its
  seven types already appear as seven separate files in the guard snapshot.
- **Accessibility changes required: none.** Every inter-type reference in the
  file goes through an `internal` or `public` member — `RemoteBoardContent.Label`
  (12 call sites) and `.SetText` (11), `RemoteBoardCard`'s public ctor/`Set`/
  `Move`/`Blank`/`Destroy`/`Path` from `RemoteActiveCards`, and
  `RemoteControlBoard.BoardHalfW`/`ProudZLocal`, which are already `internal const`.
  The **only** private cross-type access is nested-into-outer
  (`Row` reads `RemoteObjectivesPanel.BarHeight`; `Chip` reads
  `RemoteInitiativeTrack.MaxChipW`/`ChipH`), which is unaffected as long as the
  nested types travel with their outer type. **They must.** Do not promote `Row`
  or `Chip` to top level in this commit — that *would* require making five
  private consts internal, which is a different tier.
- **Guard:** **empty.** Verified mechanism (§1.5).
- **Risk if I am wrong:** a `using` is dropped during the split and the build
  breaks — caught immediately by the compiler, not by the user. There is no
  runtime failure mode for this change.

### C2 — Split `RemoteGlowPulse` out of `RemoteBoardFurniture.cs`

- **Tier:** 1, pure motion.
- **Files:** `Net/RemoteBoardFurniture.cs` → + `Net/RemoteGlowPulse.cs`.
- **What moves:** `RemoteGlowPulse` (a 20-line `MonoBehaviour`) verbatim. It
  references `RemoteBoardFurniture` **not at all**; `RemoteBoardFurniture` reaches
  it only through `Init`, which is already `internal`.
- **Guard:** **empty.**
- **Risk if I am wrong:** as C1 — compile-time only.
- **Not proposed:** extracting `RemoteBoardFurniture.InertCap`. It is also
  self-contained (it references no outer member), so it would be pure motion too
  — but `RemoteBoardFurniture` is *the* inertness file and `InertCap` is the
  literal "picture of a button". Keeping them together is the cohesive choice.
  Leave alone.

### C3 — Move `StripColliders` from `RemoteBoardFurniture` to `BoardVisual`

- **Tier:** 1.
- **Files:** `Net/RemoteBoardFurniture.cs`, `Net/BoardVisual.cs`, plus the three
  call sites (`RemoteBoardFurniture` ctor, `RemoteBoardContent.cs`'s
  `RemoteBoardCard.Set`, `RemoteControlBoard.EnsureBuilt`).
- **What moves:** the `internal static void StripColliders(GameObject, string)`
  method body, verbatim — **including its log strings**, which are hardware-test
  grep tokens (`"remote board furniture carried"`, `"INERT-GUARD"`) and must not
  be reworded.
- **Why:** the registry's `### Non-interactivity is enforced at THREE layers`
  entry names layer 1 as `BoardVisual.Quad` (destroys the collider at creation)
  and layer 3 as `StripColliders` (the runtime sweep). Layer 1 lives on
  `BoardVisual`; layer 3 lives on a *widget* class, so two unrelated files must
  name the furniture type to assert their own inertness. Co-locating layers 1 and
  3 on `BoardVisual` makes the file that owns the guarantee the file that
  contains it.
- **Guard:** the method disappears from `GloomhavenVR.Net/RemoteBoardFurniture.cs`
  and appears in `GloomhavenVR.Net/BoardVisual.cs`; three `call` targets change in
  three bodies. Diff confined to **4 types** and consisting only of a moved method
  plus three changed call targets.
- **Risk if I am wrong:** a call site is missed and a widget subtree is no longer
  swept — silent, and only visible with the remote-board setting off. Mitigation:
  the compiler catches every call site, because the old name goes away entirely.
  Do **not** leave a forwarding wrapper; that would defeat the compiler check.
- **Explicitly not proposed:** removing the apparent double sweep.
  `RemoteControlBoard.EnsureBuilt` re-sweeps the whole board including the
  furniture subtree already swept by the `RemoteBoardFurniture` ctor. That is
  belt-and-braces by design (registry: layer 3 exists *because* the construction
  discipline is not compiler-enforced) and it runs at build, not per frame.
  **Leave alone.**

### C4 — `VRRigDriver` (1 253 lines) partial-class split

I mapped every member and every field read/write. The responsibility groups are
clean; the **fields are not**:

| Field | Groups that touch it |
|---|---|
| `_camera` | **7** (lifecycle, camera-create, culling mask, clear colour, clip planes, recenter, tilt) |
| `_rigRoot` | **6** (and both recenter and tilt *write its transform*) |
| `_kind` | **5** |
| `_axisSnapReason` | **4 writers** (`NotifyTiltAxisSnap`, `Recenter`, `TickWorldTilt`, `TearDownRig`) |
| the nine tilt-state fields | owned by tilt, but reset **en bloc** by `TearDownRig` |

**Therefore:** extracting these groups into separate *types* is not pure motion
and is not proposed. A **partial class** split is, because fields stay class-scoped.

- **Tier:** 1, pure motion.
- **Files:** `Rig/VRRigDriver.cs` → `VRRigDriver.cs` (all fields/consts/statics +
  `Awake`/`Update`/`UpdateBody`/`LateUpdate`/`OnDestroy` + build/teardown),
  `VRRigDriver.WorldTilt.cs` (~330 lines: the round-5/6 provenance comment block,
  `TargetTiltDegrees`, `YawOnly`, `TickWorldTilt`, `NotifyTiltAxisSnap`),
  `VRRigDriver.HeadCamera.cs` (`CreateHeadCamera`, `ComposeHeadMask`,
  `TickHeadCullingMask`, `TickHeadClearColor`, `TickClipPlanes` + the 4 clip consts),
  `VRRigDriver.Recenter.cs` (`Recenter`, `RecenterMenu`, `RequestRecenter`,
  `ResolveWorldScale`).
- **What moves:** whole members with their doc comments. Zero statement
  reordering. **All instance fields stay in the primary file** — that is what
  makes it mechanical.
- **Guard:** **empty** (§1.5).
- **Risk if I am wrong:** the tilt group's fields end up separated from the
  `TearDownRig` block that resets them, and a future edit adds a tilt field
  without adding it to the reset. That reset is a documented invariant
  (`### TearDownRig resets the whole tilt state machine`). **Mitigation, and it
  is a condition of this proposal:** the reset block in `TearDownRig` must carry
  a comment naming `VRRigDriver.WorldTilt.cs` and stating that every field
  declared for the tilt must appear here.
- **Ranked low** deliberately. The file is long but it is *navigable* — it is
  sectioned, the sections match the responsibility groups, and every group has a
  provenance comment. Per the charter, "a file being long is not, by itself, a
  reason to touch it". The strongest argument for doing it at all is that the
  tilt group carries ~80 lines of hard-won round-5/6 derivation that currently
  sits in the middle of unrelated camera code, and that derivation is the thing
  future readers most need to find. If the user wants only one Rig change, take
  R1 instead.

### R2 — `VRRigDriver.Recenter()` is `internal` with no external caller

- **Tier:** 1. **Files:** `Rig/VRRigDriver.cs`.
- **What changes:** `internal void Recenter()` → `private void Recenter()`.
  Verified: the only external path is `VRRigDriver.RequestRecenter()` (static),
  whose sole caller is `Comfort.RequestRecenter`. No file outside
  `VRRigDriver.cs` names `Recenter()` in code.
- **Guard:** one member's accessibility changes in `GloomhavenVR.Rig/VRRigDriver.cs`.
  Nothing else.
- **Risk if I am wrong:** I missed a reflection or debug-menu caller. Checked
  against `docs/PATCH-INVENTORY.md` scope and grepped the repo; the only
  non-code mentions are prose. Low, but non-zero — and the value is small. Rank
  accordingly.

### Files that are the right size and should not be touched

`RemoteAvatar.cs` (673) — long, but it is one coherent thing (the per-peer proxy)
and every section is a documented invariant. `RemoteControlBoard.cs` (569) — one
type plus one nested; nothing to split. `SnapTurn.cs` (113), `WorldGrab.cs` (361),
`Comfort.cs` (190), `RigModule.cs` (76), `CameraControllerPatches.cs` (52),
`BoardVisual.cs` (48), `IBoardAnchor.cs` (43), `NetPacket.cs` (28) — all
cohesive. **Leave alone.**

---

## 5. Stale documentation — the complete list

All verified against source. All are Tier 0 with an **empty** guard diff (§1.1)
unless noted.

### D1 — `NetModule.cs`: instructions that would double-register the module

The first ten lines of the file are a block comment headed `// REGISTRATION` that
instructs a future worker to add `_modules.Add(new Net.NetModule());` to
`Plugin.RegisterModules()`, quoting a stale line number (359). The class
`<summary>` repeats the claim:

> `/// NOT REGISTERED in Plugin.cs yet — see the registration note at the top of this file. The`
> `/// module is inert until that one line is added, so it cannot affect the rest of the mod.`

**Verified:** `Plugin.cs:537` contains `_modules.Add(new Net.NetModule());`, and
`RegisterModules` is at line 528, not 359.

This is the worst item in the review. It is not merely wrong — it is an
instruction, addressed to exactly the audience this refactor consists of, whose
execution would double-register the module. **Fix: delete the block, and replace
the `<summary>` sentence with a statement that the module is registered in
`Plugin.RegisterModules` behind the `[Net] Enabled` kill-switch.**

### D2 — Drifted byte-A bit maps (two sites, code is correct)

1. `NetProtocol.FlagPileBrowse`'s XML doc, in the LAYOUT block:
   > `///           bit2 hand-held (else board-anchored), bit3 held in the LEFT hand, bits4..7 reserved (0)`

   Bit 4 is `PileBrowseMaskSizeBit`, bits 5..6 are the board style. **Only bit 7
   is reserved.** The correct map is documented on `PileBrowseMaskSizeBit` and
   `PileBrowseBoardStyleShift`, three screens away.

2. `PresenceSerializer.Write`, inside the `if (block)` branch:
   > `// Bits 5..7 stay zero for the next extension after this one.`

   Bits 5..6 are written by the very next statement in the same block
   (`kindFlags |= (byte)((NetProtocol.EncodeBoardStyle(...) << ...) & ...)`).
   This comment is contradicted four lines below itself.

**Correct in both cases:** `PresenceState`'s layout doc (`bit7 reserved (0)`) and
`NetProtocol.PileBrowseBoardStyleShift`'s doc (`Bit 7 stays reserved for whatever
comes after this`). Fix the two drifted ones to match, and fold in the W3 pointer.

### D3 — `NetProtocol.FlagItemFan`'s XML doc is malformed — new finding

Not in the registry. `NetProtocol.cs` lines 133–140:

```csharp
    public const byte FlagExtrasGhostHand = 1 << 2;
    /// Extras packet: a 1-byte ITEM-fan card count trails the packet (the sender's equipped-item fan
    ...
    /// </summary>
    public const byte FlagItemFan = 1 << 3;
```

The doc block has a closing `</summary>` and **no opening `<summary>`**, and there
is no blank line separating it from the preceding constant. The project does not
set `GenerateDocumentationFile`, so no CS1570 warning fires and the defect is
silent. The effect is that the item-fan flag — one of eight wire bits — has no
tooltip and no rendered documentation, on a file where the documentation *is* the
specification. Fix: insert the blank line and the `<summary>` tag.

### D4 — Rig prose drift

| Where | Says | Reality |
|---|---|---|
| `Rig/VRRigDriver.cs`, the `_tailSteps` comment | order is `HeadCullingMask → HeadClearColor → ClipPlanes → CameraPolicy → MixedReality` | **`Rig.RenderQuality` is missing.** See R1 — this is the most consequential drift in `Rig/`. |
| `Rig/ComfortGizmos.cs:11` | class doc lists "clamp and vignette status" | The vignette component, its settings row and its Loc label were all removed (`8454e88`, `5e4942a`). There is no vignette row in `OnGUI`. Registry marks this as a **negative invariant**: the absence is the decision. |
| `Rig/RigModule.cs:10-11` | "(Phase 4, feat/comfort — the in-VR settings *panel* is deferred to the P3c panel framework)" | The panel shipped. `SettingsPanel` exists and calls `Comfort.RequestRecenter`. |
| `Rig/Comfort.cs:45`, `Rig/ComfortSettings.cs:33,100,120` | "the future P3c in-VR settings panel", "deferred P3c panel" | same |
| `Rig/VRRigDriver.cs:1081` | recenter is bound "in Phase 4" to the B+Y chord | shipped |
| `Rig/VRRigDriver.cs:1080` | "standing/seated presets" | Seated mode was removed (`7bb5757`/`f89e9fc`). `Comfort` uses `StandingEyeHeightMeters`/`StandingEyeBackMeters` only, and `ComfortSettings.cs:108` already notes "The old seated preset is GONE". |
| `Core/MixedReality.cs:28,200` | reference `VRRigDriver.Tick()` | **No such member exists.** MR runs as the `"Rig.MixedReality"` entry in `_tailSteps`. New finding, outside `Rig/` but caused by it. |

### R3 — The "grip" drift is a *user-visible* defect, not a comment

Distinguished from D4 because the fix has a non-empty guard diff (§1.4).

`ComfortSettings.BindConfig` ships these strings into `dev.gloomhavenvr.comfort.cfg`,
where the user reads them:

```
"Grip-based table manipulation: one grip (away from grabbable objects) drags the table,
 two grips rotate and pinch-scale it. …"
"Fully free diorama movement: the one-grip drag moves the table in ANY direction …"
"Allow the one-grip drag to also move the table vertically. …"
"Two-grip gesture rotates the table around the point between your hands (yaw only)."
"Two-grip pinch scales the table (spread hands = board grows)."
"… Written automatically after each two-grip gesture."
```

The binding has been the **thumbstick click** since `8d28b55`. A user following
these descriptions presses the grip and nothing happens — and the grip is now the
figure-grab button, so they will pick up a mini instead. `ComfortGizmos` already
labels this state honestly at runtime (`"grip(unused)"`), which is evidence the
mismatch is known at the code level but was never carried into the config text.

- **Tier:** 1 (string literal change; no behaviour).
- **Files:** `Rig/ComfortSettings.cs` (six descriptions) and the four XML `<summary>`
  lines that say "grip" (`WorldGrabEnabled`, `VerticalDrag`, `Rotate`, `Scale`,
  and `RigClamp`'s "grab, scale, turn").
- **Guard:** **NOT empty** — a diff confined to `GloomhavenVR.Rig/ComfortSettings.cs`,
  string literals only. Record this expectation in `LOG.md`; do not treat it as
  collateral damage. (This is the case that motivated §1.4.)
- **Risk if I am wrong:** none behaviourally. The only cost is that a user's
  existing `.cfg` keeps the old description until BepInEx rewrites the file, which
  it does on next launch.
- **Do not touch:** `WorldGrab`'s class doc and `UpdateStickOwnership`'s doc — the
  registry names these as the *accurate* ones. And `6700316`'s commit body cites a
  grip-contention rule that `95ae86f` deleted; that is historical record and must
  not be "restored".

### D5 — Additional stale-doc items found, for the registry

- `RemoteControlBoard.cs`'s field-comment column tags five widgets and leaves
  `_furniture` and `_piles` untagged (§3.4). Fold into B1.
- `RemoteBoardFurniture.cs:469-470` explains that `0.15f` is hard-coded because it
  must match the remote board's slot width — see X5 in §7 for why it cannot
  reference it.
- `RemoteHandFan._sharedCardMesh` is the one mesh in the subsystem that is
  deliberately never destroyed, and the asymmetry carries no comment. The registry
  Part III already flags this; it is worth the one-line comment it asks for, at
  the field.

---

## 6. Rig — order, and what would let a reorder slip in unnoticed

### R1 — The `_tailSteps` comment omits `Rig.RenderQuality` (highest-value Rig finding)

`Rig/VRRigDriver.cs`, `Awake`:

```csharp
        // Build the guarded tick list once — order matches the original Update() tail
        // exactly (HeadCullingMask → HeadClearColor → ClipPlanes → CameraPolicy →
        // MixedReality). Cached delegates → zero per-frame allocation in the loop.
        _tailSteps = new (string, System.Action)[]
        {
            ("Rig.HeadCullingMask", TickHeadCullingMask),
            ("Rig.HeadClearColor", TickHeadClearColor),
            ("Rig.ClipPlanes", TickClipPlanes),
            ("Rig.RenderQuality", RenderQuality.Tick),      // <-- absent from the comment above
            ("Rig.CameraPolicy", () => TickCameraPolicy(_tickSceneLoaded)),
            ("Rig.MixedReality", MixedReality.Tick),
        };
```

**Provenance, confirmed by `git log -L`:** the comment was written correctly by
`16f32ee` for a five-step array. `fef87d2` ("feat(rig): force eye-texture MSAA +
global aniso") inserted `("Rig.RenderQuality", RenderQuality.Tick)` **and did not
touch the comment.** It has been wrong ever since.

**Why this is the most consequential drift in `Rig/`.** The registry entry
`### The Update tail is a cached, ordered, guarded step array — MixedReality LAST`
states the order is load-bearing and that MR must run last so its key-colour clear
wins over `TickHeadClearColor`'s VoidColor. This comment is the **only** guard on
that order that lives at the code. It is now a five-item list next to a six-item
array — so it no longer verifies anything, and a reader who trusts it has an
incorrect model of what runs when. It also claims the order "matches the original
`Update()` tail exactly", which stopped being true the moment `RenderQuality` was
added (it was never in the original inline tail).

- **Tier:** 0. **Files:** `Rig/VRRigDriver.cs`.
- **Fix:** restate all six steps and, more importantly, restate the *reason* for
  the constraint rather than just the list — a list drifts, a reason does not:

```csharp
        // Build the guarded tick list once. THE ORDER IS LOAD-BEARING and this array is the
        // only place it exists:
        //   HeadCullingMask → HeadClearColor → ClipPlanes → RenderQuality → CameraPolicy → MixedReality
        // MixedReality MUST stay LAST: its chroma-key clear has to win the frame over
        // TickHeadClearColor's VoidColor ("MR owns the head clear while on").
        // Cached delegates → zero per-frame allocation in the loop; the CameraPolicy step reads
        // its bool argument from the _tickSceneLoaded FIELD so the lambda stays cached — do not
        // "clean it up" into a closure over a local.
        // If you add a step here, add it to this comment. (RenderQuality was added by fef87d2
        // and the comment was not updated for months; that is why this note exists.)
```

- **Guard:** **empty.**
- **Risk if I am wrong:** none.

### R4 — What else could let a reordering slip in unnoticed

I checked the whole `Rig/` surface for ordering constraints and where they are
guarded:

| Constraint | Where it lives | Guarded at the code? |
|---|---|---|
| `_tailSteps` order, MR last | array in `Awake` | **comment, and it is wrong** → R1 |
| `LateUpdate` for tilt (after every Update-phase rig writer) | `LateUpdate` doc | **yes**, accurate and explicit |
| `UpdateBody` order: teardown → recenter → circle poll → tail | inline comments | partially — the *reasons* ("rebuilding after recentering would recenter a rig about to be destroyed") are in the registry, not the code. Registry confidence: **medium**. Worth a two-line comment. |
| `WorldGrab.Update`: ownership → transition → drag-hand-swap re-anchor → apply | inline comment at the swap branch | **yes** |
| `OnDestroy`: `MixedReality.RestoreAll` **before** `VRCameraPolicy.RestoreAll` | nothing at the call site | **no** → R5 |
| `RemoteAvatar.Destroy`: `_ghost.Release()` first | inline comment | **yes** ("Cloned ghost materials are ASSETS — free them before the hand objects go away") |
| Component `AddComponent` order in `RigModule.Init` | nothing | **not load-bearing** → see below |

**R5 — the unguarded one.** `VRRigDriver.OnDestroy` calls
`MixedReality.RestoreAll()` then `VRCameraPolicy.RestoreAll()`. The registry
records this as an invariant ("the reverse order leaves cameras in the key-colour
state with nothing left to restore them", confidence **medium**) and there is
**nothing at the call site** saying so. An alphabetising or "group the restores"
pass would silently swap them. Fix: one comment at the two calls. Tier 0, guard
empty. This is the cheapest remaining order-hardening in `Rig/`.

**A non-finding worth recording.** `RigModule.Init` adds `WorldGrab`, `SnapTurn`,
`Comfort`, `ComfortGizmos` to one GameObject in that order. Unity does not
guarantee `Update` order between components on the same object, so this *looks*
like a latent ordering hazard. It is not: all three rig writers run in `Update`
and the tilt reconstructs the rig from scratch in `LateUpdate`, so their relative
order genuinely does not matter. **That is a property worth stating in a comment**
— otherwise a future worker will eventually "fix" it with a
`DefaultExecutionOrder` attribute, which would be a real behaviour change for no
reason. Tier 0, guard empty.

### The four named Rig invariants — verified, nothing proposed

I checked each of the four items named in the review brief and have **no changes**
to propose for any of them. Recording them here as deliberate "leave alone"
findings, with the reason:

**Pinned board vs world zoom.** The mechanism lives in `Cards/PlayTray`
(`ApplyFollowMode`, `SyncPinHolder`, `ComputeBoardScale`), not in `Rig/` —
`WorldGrab.ApplyTwoHand` is only the other half. `SyncPinHolder` is called from
`TickLostWatchdog`, and the surrounding comment already states the housekeeping is
"pose-PRESERVING". The registry's warning is that a future worker will reason "the
holder should track the rig so it can't drift" and re-add the scale write. Nothing
in `Rig/` can prevent that; the guard would not catch it either (it is a real
behaviour change and would show as one). **This is a `Cards/` finding.** The one
thing I would add from this side: the registry's counter-argument — that the
holder *cannot* drift (world origin, identity rotation, scale baked once, not
parented under the rig) — is the refutation, and it belongs as a comment at
`SyncPinHolder`, because the next person to have the idea will be standing there.

**Scale-aware clip planes.** `TickClipPlanes` is 24 lines, correct, and carries
its full test-#17 provenance in an adjacent comment block. It touches four fields,
two of which (`_buildScale`, `_baseFarClip`) are written only by
`CreateHeadCamera` — a clean one-way dependency. It is the single cleanest group
in the file and the best candidate in C4. **No change.**

**The four reverted tilt-axis derivations.** The provenance comment block
(`VRRigDriver.cs:188-262`, ~80 lines) is the most valuable documentation in
`Rig/` and the registry records all four reverted attempts with their commits. The
only risk here is *losing* it, which is why C4 makes moving that block with its
group a condition rather than an option. **No change to the code, and the comment
block must never be trimmed as "history".**

**`_tailSteps` / MixedReality last.** → R1, R5.

---

## 7. Duplication census

Applying the charter's Tier-2 rule strictly: **near-identical is a documentation
finding, not a merge.** Most of what follows lands on the documentation side, and
that is the correct outcome — the fan and board files are the six-file cluster
where genuine duplication was most likely, and the exhaustive diff shows that
almost every apparent duplicate carries a hard-won difference.

### Truly identical — merge candidates

**X1 — `MountX` / `Width`, duplicated verbatim in two sibling types.**
`RemoteObjectivesPanel` and `RemoteElementStrip` each declare:

```csharp
    private const float MountX = -RemoteControlBoard.BoardHalfW - 0.012f;
    private const float Width = 0.26f;
```

Byte-for-byte identical, in the same file, ~200 lines apart, and both are then
used in the identical `MountX - Width * 0.5f` centring expression. One carries a
provenance comment; the other has none. These are two widgets in the same left
column — changing the mount of one silently detaches the other.

- **Tier:** 2 (genuinely identical; the charter's evidence bar is met).
- **What moves:** both consts to `RemoteBoardContent` as
  `internal const float LeftColumnMountX` / `LeftColumnWidth`, with the
  provenance comment.
- **Guard:** two const declarations removed from two types, two added to a third.
  **Consumer method bodies are byte-identical** — consts are inlined (§1.3). Diff
  confined to 3 types, declarations only.
- **Risk if I am wrong:** none at this tier; the guard proves the bodies did not
  change.

**X2 — `SetWanted` / `SetSnap` / `SetHalves` in `RemoteBoardFurniture`.**
Three 11-line methods, statement-for-statement and token-for-token identical
except for the method name, the latch field and the array field. Collapses to
`SetMask(ref int latch, GameObject?[] items, int mask)`; 38 lines → ~11.

- **Tier:** 2. **Files:** `Net/RemoteBoardFurniture.cs` only.
- **Guard:** confined to `GloomhavenVR.Net/RemoteBoardFurniture.cs` — three
  methods removed, one added, three call sites changed.
- **Risk if I am wrong:** the `_shownHalfMask` latch is seeded to `-1` and the
  registry has a specific invariant about it being a **mask, not a bool**. A
  `ref int` parameter preserves that exactly; the risk is if someone later
  "simplifies" the shared helper's parameter to `bool`. Mitigate by carrying the
  `_shownHalfMask` rationale comment onto the helper.

**X3 — `BoardLocalToWorld`: one formula, five verbatim copies.**
`BoardPosition + BoardRotation * (local * boardScale)`, with the identical
`HasBoard` guard and the identical `BoardScale > 0f ? BoardScale : 1f` clamp,
appears in `RemoteItemFan.TryItemStackWorld`,
`RemoteBrowserFan.TryPileStackWorld`, `RemoteCardFx.TryResolve`'s board tail, and
both fans' board-anchored branches. The only differences across the three named
methods are the anchor argument and the local variable name (`bs` vs `scale`).

- **Tier:** 2. **What moves:** a
  `internal static bool TryBoardLocalToWorld(RemoteAvatar owner, Vector3 local, out Vector3 world)`
  onto `RemoteControlBoard` (which already owns `AnchorLocal`), and three call
  sites collapse onto it.
- **Guard:** one added static on `RemoteControlBoard`; three bodies shortened in
  `RemoteItemFan`, `RemoteBrowserFan`, `RemoteCardFx`. Confined to 4 types.
- **Risk if I am wrong:** a caller's scale semantics differ. **They do, in one
  place** — see X6 below, which is why the fans' *anchor* methods are excluded
  from this merge and only the three `Try*StackWorld` bodies are in scope.
- **Ranked below X1/X2** because it crosses four files.

**X4 — `BuildFramedRecess`.** `BuildItemUseRecess` and `BuildPickField` in
`RemoteBoardFurniture` each build a Frame + FrameInner pair with identical object
names, identical `1.12f`/`1.04f` multipliers and identical `0.001f`/`0.0005f` z
offsets. The three differences (size source, frame colour, inner alpha) become
parameters, so nothing is lost. Tier 2, guard confined to one type, low value.

### Near-identical — documentation findings, DO NOT MERGE

Each of the following was diffed in full. In every case a real difference exists,
so per Charter §4 the correct output is a comment explaining *why* they differ.

**X5 — the fans' `EnsureRoot` (3 copies).** `RemoteItemFan` /
`RemoteBrowserFan` / `RemoteCardFx` share 8 of 9 lines. Differences:
`RemoteCardFx` is **missing `_root.SetActive(false)`** (deliberate — its root
stays active and per-flight children toggle instead); `RemoteItemFan` seeds
`localScale` to `Vector3.one * _owner.AppliedScale` where the others use
`Vector3.one`. **Do not merge.** Add a comment at `RemoteCardFx.EnsureRoot`
saying the missing `SetActive(false)` is intentional — it currently reads as an
omission, and the next person to "align the three" will add it.

**X6 — `TryResolvePose` vs `TryResolveAnchor`.** 30 near-identical lines, and
the 9-line billboard tail is **truly identical**. But the board-anchored branch
carries a genuine behavioural divergence: the item fan's root is scaled by the
sender's **rig** scale even when board-anchored, while the browse fan returns the
**board** scale as `rootScale` and the caller applies it. **Do not merge** — and
this difference is currently undocumented anywhere, including the registry. It is
the highest-value comment in this section: someone unifying these two methods
would change what a peer sees, with no compiler signal.

**X7 — `Layout` in `RemoteItemFan` vs `RemoteBrowserFan`.** The 12-line preamble
is verbatim identical. The loop body is not: the browse fan applies `ArchFactor`
(0.55) and `TiltFactor` (0.85) that the item fan does not have at all; the item
fan lerps `localScale` during the ease and the browse fan does not; settled scale
is `Vector3.one` vs `Vector3.one * CardScale` (1.3); `Radius` differs
(0.30464 vs 0.272). **Do not merge.** Note the five constants that are declared
separately in both classes with identical values (`MaxArcDegrees = 110f`,
`MaxStepDegrees = 10f`, `ZStagger = 0.004f`, `EmergeSharpness = 14f`,
`EmergeSettleSeconds = 0.7f`) — hoisting *those* would be a legitimate X1-shaped
Tier 2, but only after confirming with the user that they are one decision rather
than two that happen to agree.

**X8 — `Rebuild` in `RemoteItemFan` vs `RemoteBrowserFan`.** Differences: the GO
name, and the browse fan's extra `card.transform.localScale = Vector3.one * CardScale`.
The 11-statement slab-construction core is verbatim identical across all *three*
fans plus `RemoteCardFx.Acquire`. **Do not merge the methods.** A
`internal static GameObject BackSlab(Transform parent, string name, Mesh, Material)`
factory is defensible — it is the exact block, four times — but `RemoteHandFan`'s
copy additionally attaches a `RemoteCardArt` overlay and `RemoteCardFx`'s calls
`CreateBackMaterial()` inline per slab and applies the layer per slab. Rank it
**not now**: four callers each needing a different hook is how a helper acquires
three boolean parameters.

**X9 — `Destroy` in the three fans.** `RemoteItemFan` and `RemoteBrowserFan`
share 10 of 12 lines including the comment
(`// asset — not freed with the GameObject tree`); the browse fan adds
`_open = false; _shownKind = -1;`. `RemoteCardFx`'s tail is identical again.
`RemoteHandFan` is genuinely different — it destroys **no** mesh, because its mesh
is the process-lifetime static. **Do not merge:** the registry's
`### Meshes are destroyed explicitly` and `### Teardown order` invariants both
turn on each owner destroying its own assets, and a shared teardown helper is
precisely the "trimmed to just destroy the root" failure the registry warns about.
Document instead — and take the registry's suggestion of a comment at
`RemoteHandFan._sharedCardMesh` explaining the asymmetry (D5).

**X10 — `Hide` in `RemoteItemFan` vs `RemoteBrowserFan`.** Same statements,
**inverted order** (item fan deactivates the root before clearing the timers,
browse fan clears first). Behaviourally equivalent today because nothing reads
them in between, but it is a real textual difference and it is exactly the kind of
thing a merge would silently normalise. Also `_loggedCount = -1` (hand fan) vs
`= 0` (item fan). **Do not merge. Do not "normalise" the order either** — there is
no evidence either order was chosen, and changing one to match the other is a
Tier-3 edit with no benefit.

**X11 — the arc-height formula and its duplicated constants.**
`RemoteBrowserFan.BeginCollapse` and `RemoteCardFx.Play` compute
`Mathf.Max(cardHeight * MinArcCardHeights * boardScale, distance * ArcFraction)`
with `CollapseMinArcCardHeights = 1.5f` / `MinArcCardHeights = 1.5f` and
`CollapseArcFraction = 0.28f` / `ArcFraction = 0.28f` — four constants, two
values, declared twice. The formula is numerically identical. This is a
**documentation finding**: state in both places that the two pairs are one
decision (a browse collapse and a card flight are meant to arc alike), or hoist
them, which is a Tier-2 the user should sanction because it asserts they are one
decision rather than two.

**X12 — change-gated writes: eleven hand-rolled implementations.**
`RemoteBoardContent.SetText` (gates on the TMP's own `.text`),
`InertCap.SetLabel` (private `_shown` mirror, early return),
`PileCounter.Set` (int mirror + a colour write), plus `_shownRows`,
`_signature` ×3, `_langShown` ×2, `_shownArmed`, `_shownPickField`,
`_appliedStyle`, `_loggedContent`, `RemoteBoardCard`'s quadruple key,
`_shownSourceId`. Three *different mechanisms* for one invariant.

**Do not merge.** The registry's `### Every text write is change-gated — the
badge-flicker lesson` names every one of these change keys individually, and
several of them carry extra semantics that a shared helper cannot express
(`RemoteBoardCard.Set`'s key additionally makes the expensive part — cloning a
card widget — run once per actual change; `RemoteObjectivesPanel.Refresh` must
*drop* its signature on scenario exit). The finding here is not "merge these" but
**"there are eleven of these and the registry is the only place that knows it"**.
The right output is B1-style: a canonical one-line marker at each latch field
(`// CHANGE KEY — see INVARIANTS-Net-Rig.md "Every text write is change-gated"`)
so the pattern is greppable and a new widget's author finds the rule.

**X13 — `SetActiveIfChanged`.** The two-line
`if (go.activeSelf != v) go.SetActive(v);` idiom appears at 19 sites across the
three board files, and `InertCap.SetShown` / `Chip.SetShown` are truly identical
apart from the receiver. An extension method would delete ~30 lines. **Not
proposed:** the guard diff would touch ~8 types for a purely cosmetic saving, and
two of the 19 sites carry extra clauses (`RemoteActiveCards.SetActive`'s
`&& (Count > 0 || !active)`; `Row.Place`'s trailing position write) that would
have to be left behind, so the pattern would end up half-unified — the worst
outcome. **Leave alone.**

**X14 — `RemoteStatusReadouts`' three-badge constructor.** The root+plate
preamble is identical in shape three times, and the plate colour is
`(0.12, 0.11, 0.10)` twice and `(0.14, 0.12, 0.10)` once. That third value is
**probably unintentional drift**, but it is a tuning value and the charter puts
tuning values out of scope. **Record it as a question for the user, change
nothing.** (§9 N4.)

**X15 — the "root under boardRoot at `ProudZLocal`" preamble, 11 sites.**
Three lines, identical except for the name string and the position expression.
A `Sub(Transform parent, string name, Vector3 pos)` helper would save ~22 lines
across 8 types. **Not proposed at this tier** — 8 types' constructors changed for
22 lines is the wrong ratio, and constructors are where the "widgets start hidden
and in step with their seeded change key" invariant lives. **Leave alone.**

### Corrections to claims that did not survive verification

Two findings surfaced during the census that I checked and **rejected**; recording
them so they are not re-discovered and acted on:

1. **"`CreateBackMaterial()` leaks ~10 materials per board."** False.
   `Cards.CardMesh.CreateBackMaterial` is a lazily-created **singleton**
   (`if (_backMaterial == null) { … } return _backMaterial;`) — it returns the
   same instance every call. The board code wraps its `.mainTexture` in a fresh
   `BoardVisual.Unlit`, which *is* a new material per widget, and that is
   **required** by the registry's
   `### The shared card-BACK material is read, never mutated` invariant. There is
   nothing to fix here, and "fixing" it would tint every card back in the mod.

2. **"`RemoteInitiativeTrack` violates the one-gate caller contract."** False.
   `InitiativeLabel` does re-derive `RevealGate.ShowRoundCardFronts(pa)` internally
   — but it must: the track renders **every actor in the scenario**, while the
   `showFronts` computed once in `RemoteControlBoard.Tick` is the gate for the
   **board owner only**. A per-actor gate is unavoidable and correct, and the
   registry already records it (`Per-actor numbers inside the track | RevealGate;
   monsters follow vanilla's own numeric rule`). The finding is real but it is a
   **documentation** one: `RemoteAbilityCardSource`'s caller contract says
   *"Nothing here re-checks it, deliberately"*, and `RemoteInitiativeTrack` is the
   documented exception with no note saying so. **Add the note.** Tier 0, guard
   empty, and it prevents a future worker from "fixing" the track to take
   `showFronts` — which would show every monster's initiative as `?`.

---

## 8. Leave this alone, because —

Findings whose value is that they were examined and rejected.

- **`AvatarSerializer` / `PresenceSerializer` statement order, flag bit values,
  byte layout, the two `need` sums, `QuatScale`, the LE `magic` reconstruction
  duplicated in `NetPacket.PeekType`.** Not one change proposed. The duplicated LE
  reconstruction in `PeekType` in particular *looks* like an obvious dedup
  (call `ReadU32`) and is not: `ReadU32` takes a `ref int` and `PeekType`'s whole
  purpose is to validate without a cursor. The registry flags this exact trap.
- **`IBoardAnchor` / `WorldAnchor`.** Identity today, one implementation, every
  conversion a no-op. It is the documented seam that lets a future title build
  offset the board per client without touching the wire or the sampler. Inlining
  it is the archetypal "obviously dead abstraction" removal the charter warns
  about.
- **`NullNetTransport`.** Field initialiser for `NetAvatarDriver._transport` and
  the `Configure` null fallback. Deleting it makes every offline path a null-ref.
- **`NetAvatarDriver.RemovePlayer`.** Public, uncalled, documented as the forward
  hook for `PlayerRegistry.OnPlayerLeft`, and the only path that clears `_pending`
  / `_pendingExtras`.
- **`NetProtocol.PileBrowseKindItems`, `BoardStyleMaxCode`.** Wire constants.
  Never renumber, never delete.
- **All one-shot log latches** (`s_loggedHeldItem`, `s_loggedFirst`,
  `_heldCardBillboardLogged`, `_loggedMaskSize`, `_loggedBoardStyle`,
  `_loggedBrowseOpen`, `_loggedMode`, `_gateHiddenLogged`). Hardware-test grep
  tokens; Charter §5.
- **`NetAvatarDriver.IncludeFingers`** (`private const bool = true`). A decision
  recorded as code rather than as a config key. Constant-folded, so invisible in
  the built output anyway.
- **`RemoteBoardFurniture.Refresh`'s discarded `actor` parameter.** An explicitly
  reserved seam with a comment saying so.
- **`[Rig] Experimental3DMap`, `RenderQuality.RebuildRigOnMsaaChange`.** Bound,
  unimplemented / diagnostic-only, both explicitly guard rails. Charter §5.
- **`RemoteControlBoard`'s double collider sweep**, **`RemoteHandFan`'s never-freed
  static mesh**, **the un-interpolated remote board pose**, **the two
  `CameraController` prefix-skips**, **`TickGuard`**, **`RigTarget`'s dev proxy**.
  All examined; all deliberate; all documented in the registry.
- **`NetProtocol.SendRateHz` / `ExtrasSendRateHz` as `float` Hz.** Inverted at
  every use site. Storing intervals instead would change the meaning of
  config-adjacent constants. Tier 3, out of scope.
- **`X13`, `X15`** (§7) — cosmetic dedups whose blast radius exceeds their value.

---

## 9. Not now — proposals that are not pure motion or not Tier ≤ 2

These are recorded so the user can decide, per Charter §4. **None is part of this
refactor.**

**N1 — Marker interfaces for the content classification** (§3, B1 discussion).
Tier 3-adjacent: changes type metadata, cannot express the three MIXED types
without splitting them first. Revisit after C1.

**N2 — Splitting the three MIXED content types.** `RemoteStatusReadouts` holds
three unrelated badges spanning two data classes (round = GLOBAL,
initiative + rest = PER-ACTOR). `RemoteInitiativeTrack` mixes a GLOBAL actor list
with per-actor gated numbers. `RemoteBoardFurniture` (64 members) spans four
classes plus a module-wide static utility. Splitting them would make the
classification structural for free — but it changes constructors, mount positions
and refresh call order, which is behavioural. **Tier 3.**

**N3 — `RemoteBoardFurniture`'s hard-coded `0.15f`.** It must track
`RemoteControlBoard.CardW`, which is `private`, so the coupling is expressed as a
literal in four places plus a comment admitting it. Making `CardW` `internal` and
referencing it would be a genuine improvement (guard: one accessibility change +
four inlined-constant sites that produce identical IL). I did not rank it because
I have not verified that all four `0.15f` sites are meant to track `CardW` rather
than coincide with it — the comment says "match THOSE rather than the local card
metrics", which is suggestive but not conclusive. **Needs a one-line answer from
the user, then it becomes a clean Tier 2.**

**N4 — `RemoteStatusReadouts`' third plate colour** `(0.14, 0.12, 0.10)` where
its two siblings use `(0.12, 0.11, 0.10)`. Probably drift; possibly deliberate.
Tuning value → out of scope. **Question for the user.**

**N5 — `RemoteBrowserFan.Hide` vs `RemoteItemFan.Hide` statement order** (X10).
No evidence either order was chosen. Normalising is a Tier-3 edit with no benefit.

**N6 — Restoring `RenderQuality`'s global state on teardown.**
`QualitySettings.antiAliasing`, `XRSettings.eyeTextureResolutionScale` and the
pushed `SetMSAALevel` have no restore path, unlike the aniso settings which do.
The registry calls this a deliberate gap. Closing it is a behaviour change.

**N7 — Renaming `FlagPileBrowse`** (§2, W6 discussion). Low guard cost, high
documentation churn including churn in the registry itself.

**N8 — A shared `BackSlab` GameObject factory** (X8). Four callers each needing a
different hook.

---

## 10. Summary of what this review proposes

**23 items**, of which:

- **11 are Tier 0** with a *provably empty* guard diff (all documentation, plus
  the enum numbering which is provably no-op in IL). These are the whole top of
  the worklist and they carry no regression risk of any kind.
- **7 are Tier 1** — five file splits / member moves with an empty guard diff, and
  two added constants (W3, W4) each costing exactly one declaration line.
- **4 are Tier 2** merges, every one of which was diffed line-by-line first; X1
  and X2 are truly identical, X3 and X4 become parameterised with nothing lost.
- **1 (R3) has a deliberately non-empty guard diff** — string literals in
  `ComfortSettings` — and it is the item that exposed the `CENSUS.md` error in
  §1.4.

**11 near-duplicate pairs were examined and NOT merged**, each because a real
difference exists; those became documentation findings, which is what the charter
asks for.

The two findings I would most want a future worker to see, if they read nothing
else: **`NetModule.cs`'s header instructs them to double-register the module**,
and **`VRRigDriver`'s `_tailSteps` comment is missing a step, so the only guard on
the mod's most order-sensitive array has been wrong since `fef87d2`.**
