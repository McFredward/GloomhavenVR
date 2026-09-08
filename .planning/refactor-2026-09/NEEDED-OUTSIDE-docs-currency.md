# NEEDED-OUTSIDE — audit lane `docs-currency`

Findings that are **not** in `docs/*.md` and therefore not this lane's to fix. Base
`49ceab21` (ModBuild 483), audited 2026-09-08. Ranked by cost of leaving them.

---

## 1. `release.yml` runs EIGHT fewer gates than `ci.yml` — a CODE defect

**File:** `.github/workflows/release.yml`

The 2026-09 tooling review wired eight checkers into `ci.yml` and did not carry them
across. Diffing the two workflows, `release.yml` is missing:

```
scripts/check-partial-order.py
scripts/check-instrument-writes.py
scripts/check-remote-defaults.py
scripts/check-tune-fields.py
scripts/check-desync-surface.py
scripts/check-hw-verify.py
scripts/check-options-coverage.py
scripts/check-docs-i18n.py
```

So **the release path is the weaker of the two**, which is the wrong way round: a push to
`main` publishes to players with no undo. In practice the released commit has already been
through `ci.yml` on `dev`, which is what covers the gap today — but that is a property of
how the maintainer works, not of the pipeline.

`docs/CI-CD.md` §4 said "**The same gates as CI**, plus `fetch-natives.sh`". I corrected the
document (commit `5e89a192`) to describe what the workflow does. **The better fix is to make
the document's old sentence true again** by adding the eight steps to `release.yml`, after
`Enum-keyed array sizes` and before `Config defaults rebase`, in `ci.yml`'s order:

```yaml
      - name: Static initialiser order across partial types
        run: python3 scripts/check-partial-order.py

      - name: Load-bearing writes inside diagnostics
        run: python3 scripts/check-instrument-writes.py

      - name: Remote constants resolve to the local defaults
        run: python3 scripts/check-remote-defaults.py

      - name: Board-tuning field ids in range and in order
        run: python3 scripts/check-tune-fields.py

      - name: Patches on network-action receivers are classified
        run: python3 scripts/check-desync-surface.py

      - name: HW-VERIFY lines print at the default log level
        run: python3 scripts/check-hw-verify.py

      - name: Every option is reachable in the menu
        run: python3 scripts/check-options-coverage.py

      - name: User-facing docs ship in English and German
        run: python3 scripts/check-docs-i18n.py
```

All eight read only `src/`, `docs/` and tracked files under `.planning/refactor/`, which is
exactly the argument `ci.yml`'s own comment block makes for them. If they run there they run
here. Then revert `docs/CI-CD.md` §4 step 3 to the one-line form.

---

## 2. `scripts/worktree-setup.sh` does not link the surface baseline

**File:** `scripts/worktree-setup.sh:87-88`

In a fresh agent worktree, `bash scripts/refactor-guard.sh check --summary` runs all sixteen
text checkers, passes every one, and then dies at the last step:

```
error: no surface baseline — run 'refactor-guard.sh baseline' first
```

because `.planning/refactor/.guard/surface.json` is gitignored and is not linked. The script
links `baseline` and `baseline.rev` and stops there; its own comment (lines 81-86) reasons
carefully about exactly this failure mode and misses the third entry.

The cost is that the gate a worker is told to run cannot go green in a worktree, so a worker
either re-baselines (which makes the check compare against ITSELF and pass vacuously — worse
than failing) or learns to ignore the tail of the output.

```diff
 link .planning/refactor/.guard/baseline
 link .planning/refactor/.guard/baseline.rev
+link .planning/refactor/.guard/surface.json
```

Note the same caveat the existing comment gives: link the ENTRY, never the `.guard`
directory, or cleanup takes the main checkout's baseline with it.

---

## 3. Two stale log lines promise a false alarm that was retired 260 builds ago

**File:** `src/GloomhavenVR/WorldUI/Patches/CharacterClickSelectsOnly.cs:232` and `:241`

Both suppression lines still tell a log reader to expect a known false alarm:

```
"EXPECT A KNOWN FALSE ALARM: GuildmasterDestinations' CHARACTER SHEET OUTCOME "
+ "watcher still assumes a slot click should open the sheet and will report DID NOT "
+ "OPEN after every character click. That is this change, by design — it is not a "
+ "broken character screen, and the watcher needs re-keying onto the person icon."
```

and

```
"A CHARACTER SHEET OUTCOME 'DID NOT OPEN' warning "
+ "following this line is expected.)"
```

The watcher **was** re-keyed onto the person icon's rising edge at ModBuild 220
(`WorldUI/MapRoom/GuildmasterDestinations.cs`, whose own comment at :1922 describes the old
keying in the past tense). So a `DID NOT OPEN` today is a **real finding**, and these two
lines instruct the reader to dismiss it.

`docs/PATCH-NOTES.md` already carries a caveat about this — verified still accurate and
deliberately kept (commit `c62de42c`). The doc is right; the code is stale. Delete the
"EXPECT A KNOWN FALSE ALARM" sentence from the first line and the parenthetical from the
second, and drop the PATCH-NOTES caveat in the same commit.

---

## 4. Stale counts in gate comments — cheap, but they are what the next reader trusts

Each of these is a comment that a reader consults *instead of* running the gate.

| File / line | Says | Gate prints |
|---|---|---|
| `scripts/check-desync-surface.py:15` | "**Twelve** of the mod's patch classes sit on such a type today … never to add a **thirteenth** without somebody looking" | `17 patch classes on 37 receiver types` |
| `scripts/check-desync-surface.py:16` | "the mod patches the **five heaviest** receivers in the table" | seven receivers; and `MapChoreographer` (11 actions) outweighs four of them and is unpatched |
| `scripts/refactor-guard.sh:294` | "The mod patches the five heaviest receivers in that table" | same as above |
| `scripts/refactor-guard.sh:273` | "The **66** that exist today are accepted in a baseline" | `61 load-bearing … baseline 61` |
| `.github/workflows/ci.yml:221` | "prebuilt/gloomhavenvr.bundle, **70,218,494** bytes" | `74943763` (and 483 rebuilt it) |

The `docs/NET-ACTION-SURFACE.md` half of the first three is fixed (commit `cedac64a`).

---

## 5. `.planning/menu-audit/03-core-rig-perf.md:30` — the `Experimental3DMap` error, third copy

**Another lane's file. Recorded here because `Plugin.cs` names it explicitly.**

`Plugin.cs`'s doc on `Vanilla2DMap` says two derived documents inherited a false claim that
`[Rig] Experimental3DMap` is a "RESERVED placeholder, currently unimplemented" with "zero
readers", and names both:

- `.planning/menu-audit/03-core-rig-perf.md:30` — "DEAD … zero readers"
- `docs/INTERFACES-P2.md:339` — "reserved, unimplemented placeholder"

The second is fixed (commit `2d3a6217`). The first is still standing. The key was renamed and
INVERTED to `[Rig] Vanilla2DMap` at ModBuild 230 and the 3D map room has been the mod's
default presentation since ModBuild 158.

This one is not merely editorial. `Plugin.cs` spells out the mechanism: `"RESERVED —"` is one
of `ConfigCatalog.RetiredMarkers`, so anyone who rewrote the config entry's *description* to
agree with either document would have deleted the row from every menu page in the mod
(`ConfigCatalog.Describe` → `IsRetired` → `return null`).

---

## 6. A source comment cites two doc line numbers that have drifted

**File:** `src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs:876`

```
// The 'Ray-uGUI canvas …: world rect …' prefix is a documented grep token
// (docs/TESTING-P3B.md:265, docs/TESTING-P3C.md:342) and is kept verbatim.
```

The token is at **`TESTING-P3B.md:263`** and **`TESTING-P3C.md:360`**. Line numbers in a
doc citation drift on every edit above them — including this lane's. Cite the section, not
the line:

```diff
-            // (docs/TESTING-P3B.md:265, docs/TESTING-P3C.md:342) and is kept verbatim. What it
+            // (docs/TESTING-P3B.md §"Ray-uGUI", docs/TESTING-P3C.md §14) and is kept verbatim. What it
```

The claim the comment exists to protect — that shipping code pins these tokens, so the docs
must not be retired — is otherwise **correct and verified**. Exhaustive `grep -rn "TESTING-P" src/`:

| Doc | Cited from |
|---|---|
| `TESTING-P1.md` | `Core/Startup/OpenXRBootstrap.cs:100`, `:446` (both inside **user-visible error strings**), `GloomhavenVR.Preload/Patcher.cs:211`, `Compat/CompatModule.cs:28` |
| `TESTING-P2.md` | `WorldUI/FlatScreen/VirtualMouseBridge.cs:135` |
| `TESTING-P3B.md` | `Hands/Interact/RayUguiDriver.cs:876` |
| `TESTING-P3C.md` | `RayUguiDriver.cs:876`, `WorldUI/Modal/ModalFallback.1.Core.cs:93` |
| `TESTING-P3A.md`, `TESTING-P4.md`, `TESTING-FULL-LOOP.md` | **nothing in `src/`** |

`docs/DEVELOPING.md`'s "shipping code cites P1, P2, P3B and P3C by path" is exactly right.

---

## 7. Two diagnostics a test script tells a tester to grep for are `VRLog.Debug`

**Files:** `src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs:874`,
`src/GloomhavenVR/WorldUI/Grab/InputModeGuard.cs:80`

`TESTING-P3B.md` and `TESTING-P3C.md` instruct a tester to confirm a step by finding
`Ray-uGUI canvas …` and `blocked switch to gamepad mode` in `LogOutput.log`. Both are emitted
at the **Debug** tier, and `WorldUI/Surfaces/TablePanelSurfaces.cs:350-351` records that
"BepInEx's default disk config drops Debug entirely (test #19: zero 'Ray-uGUI canvas' lines
in LogOutput.log while laser clicks worked)". So both steps read as FAILED on a default
install.

This is the `check-hw-verify.py` failure class — "a line a hardware round is waiting on must
survive the DEFAULT level" — but that gate only inspects `// HW-VERIFY`-marked lines, and
neither of these carries the marker. **Either mark both `// HW-VERIFY` and promote them to
`VRLog.Info`, or leave them at Debug.** I have taken the second reading in the docs (commit
below adds "raise the log level first" to both steps), because promoting a per-canvas
per-registration line is a log-volume decision that is not this lane's to take.
