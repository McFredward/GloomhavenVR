# Phase 2 — Dead code and duplication census

> **Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** A **2026-07 census** of a
> 197-file tree; the tree is now 621 files. **Its candidate lists are spent** — Phase 4 acted on
> them (`LOG.md` batches C/D/E) and three programmes have run since. Do not re-open a candidate
> from here without re-measuring it.
>
> **What is still worth reading, and is why this file was not deleted:** the *method note* below.
> "Which `ConfigEntry<T>` is never read as `<name>.Value`?" returned **54 of 257** entries and was
> almost entirely wrong, because per-board settings live in an array behind an accessor and the
> consumer never names the field. Narrowing to "mentioned in only one file" took 54 down to **3**.
> That is the charter's burden-of-proof rule in miniature and it is a trap the next sweep will
> walk into again. The config surface is **625 keys** now, not 257.

> Findings are recorded here as they are verified. Nothing in this file has been deleted yet;
> deletions happen in Phase 4 against `PLAN.md`, one commit per group, guard-checked.
> Every candidate is tested against `CHARTER.md` §5 before it may be removed.

## Method note: why the naive census is wrong

The first pass asked "which `ConfigEntry<T>` field is never read as `<name>.Value`?" and
returned **54 of 257** entries. That answer is almost entirely false.

The reason is a pattern used throughout `CardsConfig`: per-board settings are stored in an
**array** and reached through an accessor, so the consumer never mentions the field name at all:

```csharp
private static readonly ConfigEntry<float>[] _objectivesWidth = new ConfigEntry<float>[BoardCount];
internal static ConfigEntry<float> ObjectivesWidth(ControlBoard b) => _objectivesWidth[(int)b];
// consumer:
PlayTray.ObjectivesMountWidth * CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard).Value
```

`ObjectivesWidth` is demonstrably live (it is the objectives-width feature the user tested in
round 25), yet a `.Value`-based census reports it dead. Narrowing to entries mentioned in **only
one file** — their own config file, i.e. never referenced by any consumer, accessor or the
settings panel — takes 54 candidates down to **3 real ones**.

This is the charter's rule in miniature: the burden of proof is on the deletion.

## Config entries with no reader — NOT a Tier 0 deletion (corrected)

My single-file census found **3**; the Cards history mining found **8**, by the better test
"is `CardsConfig.<name>` referenced anywhere outside `CardsConfig.cs`". The extra five are
mentioned in other files only from *comments*, so they pass a mention-based filter while still
having no consumer. The eight:

**Corrected a second time — the list itself was half fictional.** `REVIEW-Cards.md` §5.0
checked each name against `src/` rather than against the history, and found that
**`InspectForward`, `InspectUp`, `RevealPreset` and `RevealDemeo` have zero occurrences in the
source tree.** They exist only in `.planning/research/DEMEO-HANDS-CARDS.md`, in
`INVARIANTS-Cards.md`, and — because I copied it from there — in this document. I verified
independently: 0 mentions, 0 `Bind` calls, for all four.

Two real ones were missing instead: `TrayTilt` (superseded by `BoardTilt_{board}`, and
`PlayTray` says so) and `RoundButtonThickness` (the live value is `ButtonTuning.RestCapDepth`).

The **actual** set of bound-but-unread `[Cards]` entries is seven:
`HeldTiltDegrees`, `RoundButtonDiameter`, `RoundButtonThickness`, `RestButtonInsetX`,
`ConfirmUndoInsetX`, `TrayTilt`, `FanArcDegrees`.

Worth recording why this happened, because it is the same failure the registry warns about:
the entries were mined from **commit history**, where all four once existed, and nobody asked
whether they still do. A history-derived fact needs a HEAD check before it becomes a plan.

**And "dead" is the wrong word for most of them.** They split into two groups that need
opposite treatment:

**Group A — deliberately retained legacy.** The commit that superseded them says so outright:
`HeldTiltDegrees` is "now legacy, kept bound so existing cfg files load" (`6db51a2`), the inset
and diameter globals "remain bound for back-compat" (`17862bb`). Removing these *reverses a
deliberate decision*. They are not leftovers; they are a compatibility promise. The only defect
is that their help text does not say so.

**Group B — knobs that lie.** `RestButtonInsetX`'s description opens with "**LIVE FIT KNOB**
(dial in dev.gloomhavenvr.cards.cfg without a rebuild)" and then walks the user through tuning
the sign by watching the discs move. Nothing reads it. `FanArcDegrees` presents itself as
"Maximum total fan arc in degrees". `ConfirmUndoInsetX` ends with "PER-BOARD." These do not just
sit there unused — they **invite the user to tune something that cannot respond**, which is
precisely the complaint that triggered the settings audit in `cc99144`.

This is a **user decision, not a cleanup**, and it must be taken for all of them at once —
unbinding a key drops it from the user's `.cfg` on the next write, which `CardsConfig`'s own
`DebugMenu` note already documents as a consequence. Recommendation to put to the user:

- Group A: keep bound, prefix each description with `LEGACY — no effect, superseded by <X>.`
- Group B: same treatment, not deletion. It costs one line each, keeps every existing `.cfg`
  loading unchanged, and converts a misleading knob into an honest one. Deletion buys a slightly
  shorter file and risks nothing except the back-compat promise — not worth it.

**Guard expectation — corrected.** I first wrote here that description-only edits give an empty
guard diff. **That is wrong**, and the Net/Rig review caught it by *measuring* the snapshot
instead of reasoning about it. A config description is a **string literal argument** to `Bind`,
so it is compiled into the assembly and appears in the decompiled snapshot verbatim — I
confirmed by finding "LIVE FIT KNOB" in `baseline/GloomhavenVR.Cards/CardsConfig.cs`.

The correct rule, now measured rather than assumed:

| Edit | In the snapshot? | Guard diff |
|---|---|---|
| XML doc comment (`/// <summary>`) | no — 0 `<summary>` tags in the whole snapshot | **empty** |
| `//` comment | no | **empty** |
| Config `Bind` description | **yes** — it is a string literal | one line per edited description |
| Sequential enum member values | not rendered when they are the implicit 0,1,2… | **empty** — so making them explicit is provably free |

So re-labelling the legacy config entries WILL show a guard diff — one line per description,
inside `CardsConfig`, and nothing else. That is still the expected, reviewable outcome; it just
is not the "provably nothing changed" case I claimed. The argument for re-labelling over removal
stands on the back-compat promise, not on the guard.

## Confirmed dead — stale documentation (Tier 0)

| Where | What | Evidence |
|---|---|---|
| `Net/NetModule.cs` header | A 10-line comment block instructing a future worker to register the module in `Plugin.cs`, "in a SEPARATE change to avoid conflicting with parallel workers" | It has been registered since `cac5474`; `Plugin.cs` contains `_modules.Add(new Net.NetModule());` |
| `Net/NetProtocol.cs` (`FlagPileBrowse` doc) | Says "bits4..7 reserved (0)" | Bits 4, 5 and 6 are all in use; only bit 7 is still free |
| `Net/PresenceSerializer.Write` comment | Says "Bits 5..7 stay zero" | Same — the code is correct, the prose drifted |

These are worse than harmless: the first one actively instructs a future agent to make a change
that would double-register the module.

## Undocumented cross-subsystem coupling (Tier 0 — add a comment, change nothing)

`Cards.PileKind`'s **member order is a wire constant**. `NetAvatarDriver.TickExtrasSend` casts
the enum straight into the extras payload. The constraint is documented on the *Net* side
(`NetProtocol`: "they mirror `Cards.PileKind`'s member order; append only, never renumber") but
**nothing at the enum itself says so**. A tidy-up in `Cards/` — alphabetising the members,
inserting a fourth pile — would corrupt every peer's view with no compiler error, no
single-player symptom, and no error on the sender's own screen.

This is the single most dangerous refactor trap found so far. Fix: a comment at the enum. No
behaviour change, guard diff empty (comments do not survive into IL).

## Compiler warnings (Tier 0 candidates, not yet investigated)

| Warning | Where |
|---|---|
| CS0414 — field assigned but never used | `Cards/ItemsPile.cs` — `ItemChip._hasClip` |
| CS0162 — unreachable code (×3) | `WorldUI/FlatScreenStereo.cs` |

The three unreachable-code sites need care rather than deletion: unreachable code behind a
`const bool` switch is often a deliberately preserved alternative path. To be checked against
`INVARIANTS-WorldUI.md` before anything is removed.
