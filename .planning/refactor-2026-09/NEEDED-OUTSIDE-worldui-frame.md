# NEEDED-OUTSIDE — lane `worldui-frame` (refactor 2026-09)

> **[verified 2026-09-08 against `49ceab21` (ModBuild 483)] SOME OF THESE HAVE BEEN APPLIED, AND
> THIS FILE DOES NOT SAY WHICH.** A NEEDED-OUTSIDE list is written at the moment the lane closes
> and is never revisited, so its standing claim that "nothing here has been applied" decays into a
> false statement the first time the integrator lands one of them. Per-item status was **spot
> checked, not exhaustively re-derived** — check the item against source before acting on it, and
> read the line numbers as advisory (they are from the lane's base commit, not from `49ceab21`).
>
> Verified in this pass:
> - **§1a `["Hands/GhostHandOnFan"]` German description — DONE.** It reads *"Standardmäßig AN"*,
>   matching `Defaults.Hands.cs:18` (`GhostHandOnFan = true`).
> - **§2 `INVARIANTS-Hands-Board-Core.md`, all three bullets — DONE.** The palm-gate entry (§5),
>   the modal pick-block entries (§1) and the §15 grep-token tier problem are all corrected in that
>   file, with the old approaches kept as rejected approaches.

Changes this lane needs in files it does not own (BRIEF §1.4). Each entry: the file, the exact
diff, the reason, and the lane finding it belongs to. Nothing here has been applied.

## 1. `src/GloomhavenVR/Core/Loc/Loc.ConfigDescriptions.German.cs` (lane core) — two German descriptions carry the same false claims as the English `Bind` text this lane corrects (F-14, F-15)

### 1a. `["Hands/GhostHandOnFan"]` — "Standardmäßig AUS" while `Defaults.Hands.cs:18` ships `true`

```diff
             ["Hands/GhostHandOnFan"] =
                 "Macht die Hand, die gerade den GEÖFFNETEN Kartenfächer hält, halbtransparent "
                 + "(\"Geisterhand\"), damit das Hand-Mesh keine Kartendetails mehr verdeckt. Die Hand bleibt "
-                + "sichtbar — nur ihre Deckkraft sinkt (Stärke: GhostHandStrength). Standardmäßig AUS; an den "
-                + "Händen ändert sich nichts, bis du es einschaltest. Live änderbar, vollständig umkehrbar (die "
+                + "sichtbar — nur ihre Deckkraft sinkt (Stärke: GhostHandStrength). Standardmäßig AN. "
+                + "Live änderbar, vollständig umkehrbar (die "
                 + "Überblendung läuft auf privaten Materialkopien pro Renderer, nie auf den gemeinsamen "
```

Reason: the English `Bind` description (`Hands/HandsConfig.cs:396-403`) said "OFF by default" and
is corrected by this lane to "ON by default."; the shipped default has been `true` since the
toggles "shipped off when the feature was new" (`HandsConfig.cs:140-146`). The tooltip prints the
real `Standard: True` one line below this sentence.

### 1b. `["Hands/*VerticalOffset"]` and `["Hands/*ForwardOffset"]` — "beim ersten Start daraus übernommen" describes a seeding that no longer exists

```diff
             ["Hands/*VerticalOffset"] =
                 "Vertikaler Versatz (Meter, Geräteachse Y; POSITIV = nach oben) der sichtbaren Hand gegenüber "
                 + "der Griffpose, während dieser Handstil getragen wird. PRO STIL absoluter Wert (ersetzt das "
-                + "alte gemeinsame HandVerticalOffset + Trimmung; beim ersten Start daraus übernommen). Live "
+                + "alte gemeinsame HandVerticalOffset + Trimmung). Live "
                 + "änderbar.",
```

and the same one-clause removal for `["Hands/*ForwardOffset"]` (the English twin says "seeded on
first run"). Reason: `HandsConfig.cs:203-207` — the per-style seat tables are "MEASURED, NOT
DERIVED … the old shared [Hands] seat keys … were retired and have since been deleted from
Plugin", so there is nothing left to seed from; a reader who trusts the sentence would look for a
key that does not exist.

## 2. `.planning/refactor/INVARIANTS-Hands-Board-Core.md` (not a lane file) — two entries describe retired mechanisms (F-06, F-09)

- §5 "The gate measures the VISUAL hand frame (`HandRig.Root`)": at HEAD `PalmGate.Tick`
  (`Hands/Interact/PalmGate.cs:164-175`) builds its frame from `_hand.transform.rotation *
  HandsConfig.ShippedSeatRotation(style)` and records that reading `HandRig.Root` "was tried and
  rejected: a cosmetic re-seat then silently re-tuned the gesture". The entry's "Breaks if:
  switched back to `_hand.transform.rotation`" is now literally the shipped mechanism (times the
  shipped seat). The same entry's `UseDevicePalmNormal` aside is settled: the field is gone.
- §1 "Modal pick-block keys on **Blocking**WindowModalActive" / "computed once per frame and
  SHARED by both hands": `RayInteractor.UpdateModalPickBlock` / `_modalPickBlocked` no longer exist;
  the physics pick always runs and only COMMITS are gated (`RayInteractor.cs:888-933`,
  `UpdateCommitSuppression`, user ruling 2026-08 "der Laser ist ausnahmslos da und collidet"). The
  shared-per-frame half survives as `_commitPolicyFrame`.
- §15 "Log lines that are grep tokens": every token listed for Hands/Board is at `Info`/`Warn`/
  `Debug` since the ModBuild 331 tier re-decision, i.e. present only in a `[General] LogLevel =
  Debug` log. The list is still right about WHICH lines matter; it should say which level carries
  them.

## 3. `.planning/redundancy-audit.md` (not a lane file) — SEVEN rows this lane verified CLOSED at HEAD

Nothing to change in `src/`. These are recorded so the next reader does not spend a round
re-opening them; each was checked against HEAD SOURCE, never against the audit's own prose
(`[[an-audit-is-a-snapshot]]`, which this round hit five separate times).

| row | closed by | evidence |
|---|---|---|
| **R7** | partly | the two `ActiveSetSignature` walks are DIFFERENT QUESTIONS and must not be merged; only the party-display tail is one concept — see F-54/F-71 for the contract and the load-bearing call order. |
| **R16** | ModBuild 439 | `PanelSupersample.1.Core.cs:612` is `PerfMonitor.BudgetMilliseconds`, not `1000f / 90f`. The report window is deliberately NOT coupled, and the reason is in the doc — do not "finish" it. |
| **R20** | — | all three `fadeDuration` sites reference `UguiTintFeel.HoverTintFadeSeconds`; no bare literal survives. |
| **R27** | ModBuild 439 | `PanelInkBounds.FaintAlphaFloor = CanvasConversion.FitMinAlpha`, a real reference. The pair R27 did NOT cover was F-52, fixed in this lane. |
| **R41** | `756bda65` | `PanelOrderStep` is `internal const` with a registration-seam bound check. **The remedy's own bound is a defect — see F-46.** |
| **§6.5** | ModBuild 439 | `9d` exports `PreFlashVeilAlpha` and `WindowMaterialiseRunner.cs:366-368` asks all three veils in one nested expression. Verified independently: `.SetAlpha(` has writers in exactly four files. |
| R1, R2, R18, R45, §6.1, §6.3, §6.4/R30(a), R42, R10, R14 | various | all verified closed; the details are in the review's §9 table and in the sub-reader files. |

## 4. `src/GloomhavenVR/WorldUI/WorldUIAssets.cs` or `WorldUI/WorldUIModule.cs` (lane worldui-front) — CONDITIONAL, only if the integrator wants `VariantTiles.ResetVariantTiles` wired (F-85)

`VariantTiles.ResetVariantTiles()` has no caller and its doc claimed it "matches
`WorldUIAssets.Reset`'s contract" — it does not, and nothing calls it. This lane corrected the
sentence and KEPT the method (deleting it would remove the only mechanism). **No change is
required**: the sprites are built over `Core/EmbeddedTexture.cs` textures whose own cache is never
cleared on a module reset, so they survive a hot reload and a stale entry is a small leak, not a
blank tile. If the integrator nonetheless wants the drop wired, the one line belongs in
`WorldUIAssets.Reset` (beside `_bundle`/`_probed`/`_gameFont`) or in `WorldUIModule.Shutdown`, both
of which are lane **worldui-front**.

## 5. `src/GloomhavenVR/Net/Board/BoardVisual.cs` (lane net) — CONDITIONAL, only if the user picks option (a) for F-46

F-46 (the panel ladder's bound check firing an Alert on a by-design `-1`) has three resolutions and
**this lane recommends (c)**, a named `allowBehind` exemption parameter, which is entirely in-lane
and changes no number and no draw order. Option (a) — widening the bound to accept `-1` and
conceding the tie — would move the free-floating identity plates that
`Net.BoardVisual.OrderWithPanels` resolves down to share `slot-1` with the materialise debris'
behind-half, and that file is lane **net**. **Not requested. Recorded so the decision has its cost
attached.**

## 6. `src/GloomhavenVR/Core/Loc/Loc.ConfigNames.cs` (lane core) — the actual remedy behind `OPTION NAME TOO LONG` (F-87)

`VROptionsTab.2.Rows.cs`'s `OPTION NAME TOO LONG` is the only reporter of the 2026-08-03 "ellipsis
is banned" ruling and no hardware log has ever carried one — **and this lane deliberately did NOT
promote it**, because it is deduped per KEY over 362 localized names in two languages and a German
menu browse could emit dozens of lines at once. The right instrument is a per-session SUMMARY line,
which is a new instrument rather than a tier change. The remedy the line exists to trigger —
shortening a name that does not fit — lives in `Core/Loc/Loc.ConfigNames.cs`, lane **core**. Nothing
is requested until a log names a key.
