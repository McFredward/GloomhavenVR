# NEEDED-OUTSIDE — lane core (refactor 2026-09)

> **[verified 2026-09-08 against `49ceab21` (ModBuild 483)] SOME OF THESE HAVE BEEN APPLIED, AND
> THIS FILE DOES NOT SAY WHICH.** A NEEDED-OUTSIDE list is written at the moment the lane closes
> and is never revisited, so its standing claim that "nothing here has been applied" decays into a
> false statement the first time the integrator lands one of them. Per-item status was **spot
> checked, not exhaustively re-derived** — check the item against source before acting on it, and
> read the line numbers as advisory (they are from the lane's base commit, not from `49ceab21`).
>
> Verified in this pass:
> - **§0 `docs/PATCH-INVENTORY.md` regeneration — DONE.** `bash scripts/patch-inventory.sh check`
>   prints `patch surface: 107 classes, 165 methods, all registered exactly once` with **no**
>   line-shift warning.
> - **§3 `INVARIANTS-Net-Rig.md` "one sentence wider than the code" — DONE, and §3's own diagnosis
>   was too narrow.** The entry is rewritten (see that file, "The MODE gate re-arms on the way
>   out"). §3 names **two** returns that do not re-arm; there are **six**. `SnapTurn.Update` has
>   seven early returns and exactly one — the mode gate — touches `_armed`. §3's proposed narrowing
>   ("every MODE-gate early return re-arms first") is nevertheless the right rule and is what the
>   entry now says; only its count of the exceptions was wrong.

## 0. `docs/PATCH-INVENTORY.md` — one regeneration, ONCE, after all five lanes merge

The final guard run ends with:

```
warn: docs/PATCH-INVENTORY.md line references have shifted (no patch added, removed or unregistered).
  Run `scripts/patch-inventory.sh generate` when convenient.
```

Nothing is wrong: `patch surface: 107 classes, 165 methods, all registered exactly once` still
passes. The line NUMBERS in that generated doc moved because this lane's comment edits changed line
counts in files that carry patch classes. It is deliberately NOT regenerated here — `docs/` is
outside this lane's file set, the file is generated rather than authored, and five lanes each
regenerating it is five guaranteed conflicts on a file whose content is a pure function of the
merged tree. **The integrator should run it once after the last lane lands.**

Findings from `REVIEW-core.md` whose fix lives outside this lane's file set. Nothing here was
changed by the lane; each item states the file, the exact change, and the evidence line.

## 1. WorldUI — `ConfigCatalog` withholds four migration markers by omission (LC-3)

File: `src/GloomhavenVR/WorldUI/Options/ConfigCatalog.cs:470-490` (the explicit per-key withheld
table, beside `Rig/MapPresentationMigrated230`).

Bound keys (per `surface.json`) that are one-shot migration markers and are NOT in that table:

- `PeerBoardFade/DwellsMigrated312`
- `Perf/ProfileDefaultsMigrated227`
- `WallFade/WallFadeBarsMigrated252`
- `WallFade/WallFadeBarsMigrated256`

If no other rule hides them, they sit in the settings browser with exactly the harm the table's own
comment describes for `MapPresentationMigrated230` ("false re-arms the migration"). Verify first,
then add the four to the table. They also have no `Loc.ConfigNames` entry and no German description
(LC-1/LC-2), which is moot once withheld.

## 2. `GloomhavenVR.csproj` — two compile-time references (SU-5)

File: `src/GloomhavenVR/GloomhavenVR.csproj`. Add, verbatim from
`Core/SelfUpdate/SelfUpdateZip.cs:19-22`:

```xml
<Reference Include="System.IO.Compression" Private="false" />
<Reference Include="System.IO.Compression.FileSystem" Private="false" />
```

Both assemblies ship with the game (`Gloomhaven_Data/Managed/`); only the compile-time reference is
missing. Once they land, `SelfUpdateZip.cs`'s reflection layer collapses to ~30 lines of direct
calls with `Verify`/`Extract` unchanged. The lane did NOT touch `SelfUpdateZip.cs`'s mechanism.

## 3. `.planning/refactor/INVARIANTS-Net-Rig.md` — one sentence wider than the code (RV-3)

Registry sentence: "Every stick-gate early return re-arms first." `Rig/SnapTurn.cs:126` (no pose)
and `:144` (world-grab) return WITHOUT re-arming, and both are right — a clicked stick is a deflected
stick, and re-arming there would fire a snap on the frame the click releases. Optional narrowing:
"every MODE-gate early return re-arms first". The code carries its own reason; no src change.

## 4. `.planning/envsound-replica/room.py` — port `MakeFly` before it is deleted (SD-7)

`Core/Sound/EnvSound.Bank.cs:387-399` states the clip's own deletion condition ("if a round goes by
with the catalogue STABLE and nothing claiming this clip, delete `MakeFly`, this property and
`EnvSoundClip.Fly` together"); it has been met since ModBuild 149 and no consumer exists outside the
bank. The bank's rule for deleted generators is preservation sample-for-sample in `room.py`
(`make_stone`, `make_night_air`, `make_chirr` are the precedents); `room.py` has no `make_fly`. Port
`MakeFly` (`:3576-3948`) first, then delete the generator, the `Fly` property, the enum member, the
`Bank` arm and the `Release` null (about 370 lines).

## 5. WorldUI — two `MixedReality.cs` line citations, stale before this lane and 14 lines staler now

File: `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.9b.SeeThrough.cs`.

- `:28` cites `MixedReality.cs:403-413` for the "0/1 FORWARD | 4 = LEqual | On/Off" hazard. That
  block is at `MixedReality.cs:2013-2018` today (`stateProps = { "_ZWrite", "_ZTest", "_SrcBlend",
  "_DstBlend", "_Cull" }` at `:2018`).
- `:45` cites `MixedReality.cs:2129-2140` for "the properties _ZWrite/_ZTest/_SrcBlend/_DstBlend/
  _Cull". Same destination: `:2013-2018`.

BOTH WERE ALREADY WRONG at the lane's base commit `b40f8564` — checked against that revision, line
403 was a `<summary>` about renderQueue and 2129 was `name = "GloomhavenVR.MrUnseenSkip"`. This lane
then deleted 14 lines of dead doc from `MixedReality.cs` (finding MR-1, two orphaned summaries for
constants that had moved to `Defaults`), so every citation into that file below line 368 is
unaffected and every one above it moved up by 14. The lane corrected the eight citations inside its
own files; these two are the WorldUI lane's.

## 6. `.planning/perf/WALL-FADE-CLOSEOUT.md` — two statements the source no longer supports

- **§2.2 / DF-1.** The closeout quotes `ExitDwellStationarySeconds` as 7.00; `Defaults.Core.cs:203`
  ships 3.6 (re-based from the user's cfg). The closeout is the stale side; the Defaults value is a
  tuning value and was not moved.
- **§5.3.** "`CollectWallMountedProps` drains `_mountedTouched` / `_mountedAnchorLedger` /
  `_mountedMobile` at the top of the pass" — at HEAD (`WallSegmentFade.Mounted.cs:1853ff`) the top
  of the pass clears `_mountedOwned`, `_attachmentOwned`, the census lists and `_mountedReleased`;
  the three ledgers are cleared only in `RestoreAllMountedProps` (teardown / toggle-off) and are
  consumed by the orphan guard at the end of the pass.
- **§5.4.** Cites `PropUnit.cs:1212 IsActuallyDrawing … five lines before the figure refusal at
  :1267`; at HEAD they are `:1458` and `:1544` — 86 lines apart, not five — and since ModBuild 291
  the refusal is conditional on `!IsWallBuiltUnitMember`. The substance (a non-drawing figure member
  is skipped rather than refusing the unit) still holds. The same citation is in
  `WallSegmentFade.cs:783` and `:5360`, which this lane did not change either: the closeout is the
  authority the two source sites quote, so it should move first.
- **§9.0 IS FALSIFIED TWICE and needs dating.** It states "a whole-subsystem sweep of 506 private
  fields and ~400 private methods across all 22 files found ZERO unused fields, ZERO uncalled
  private methods". `Core/WallFade/` holds 33 files today (ModBuild 406-441 added the free-standing,
  blockade, hanging, held, water, sole-occluder, occluder-verdict, commit-diff, commit-gate,
  tick-phase and floor-tile files) and the sweep has not been re-run against them. Two counter-
  examples, both in the new files: `MountedProp.ShownAtFade` (`Mounted.cs:250`) is written three
  times and read nowhere, and `IsDoorwayAssembly(Renderer, Bounds, string)`
  (`FreeStanding.cs:834-835`) has zero callers — with the ModBuild-410 "DOORWAYS NEVER FADE" record
  attached to the dead overload rather than to the live one.
- **§3's commit cost.** Three source sites quoted the pre-ModBuild-280 "94.8 ms"; this lane DATED
  all three against §3's 72.84 ms rather than restating the number, so §3 stays the single
  authority.

(Items under §5 are from the WallFade reading; `STALE-DOC-REFS.md` entries naming this lane's own
files are handled in-lane.)
