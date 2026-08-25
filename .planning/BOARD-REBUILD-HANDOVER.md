# CONTROL-BOARD REBUILD — owner handover (2026-08-25)

You own this task end to end. The integrator has handed it over so the main session can work on
other topics in parallel. Read this file first; it is the state of the world, not a summary.

**Branch:** work on `boards-rebuild`, push there as often as you like, and never touch `dev` — the
integrator is committing to `dev` in parallel and will merge your branch when the task is finished.
Creating and pushing that branch is additive and allowed; force-pushing anything is not, ever.

**You may spawn your own workers.** Give each one explicit file ownership so two never edit the same
file, and pass on the rules below — they are the ones this project has already paid for.

## THE USER'S TASK, verbatim

> "Ich möchte, dass du das Mesh und die Textur von allen 3 Controllboards überarbeitest. Aktuell hat
> das Mesh viele Lücken, die Symbole darauf sehen nicht clean sondern KI-generiert aus (was es ja
> auch ist). Vergleiche einmal das alte Mesh der Hände mit den neuen. Die neuen haben ein deutlich
> cleaneres Mesh. In der Liga möchte ich, dass du die Controllbaords auch hinbekommst. Auch die
> Texturen können nachmal cleaner sein. Nutze dafür gpt-image-2. … Jedes generierte Bild kostet Geld,
> daher mache das eher selten wenn du viel Vorbereitung getroffen hast. … Kümmere dich aber auch
> darum, dass die Texturübergänge passen. Render dir die Ergebnisse und begutachte sie. Weiterhin
> noch eine größere Teilaufgabe: Wie du mir selbst gesagt hast, ist 3 das Maximum an gleichzeitigen
> Knöpfen. Daher möchte alle 3 Boards so umgebaut haben, dass sie auf der rechten Seite wo die Knöpfe
> hinkommen 3 statt 2 Slots für die buttons habe. Die slots sollen zum Styl des boards passen und
> sich natlos einfügen. Mach selbständig so lange weiter bis du dein Ziel bei allen drei boards
> erreicht hast - parallelisier deine tasks wie immer."

He answers in German; you write code, comments and log strings in English. Any report that reaches
him must be in German.

## DONE AND PUSHED (do not redo)

| commit | what |
|---|---|
| `17c6488b` | `unity/board-prep/BOARD-CONTRACT.md` + `gen_stats.py` + `gen_render.py` |
| `28ce64c0` | three button seats in code: `BoardAnchors`, alias table, top-anchored stack |
| `252b9e66` | procedural texture pipeline `tex_*.py` |
| `3682a037` | **all three boards re-authored** — watertight, three seats |
| `bde2f72a` | keycaps fitted to the measured recess; `LogSeatOccupancy` |
| `18fddc60` | seat-pose clamp (a cap may not leave its seat) |

Mesh result, verified independently at the shipped files:

| board | tris | holes | loose | dims (m) |
|---|---|---|---|---|
| Oak | 20000 → 11896 | 1288 → **0** | 0 | 0.640 × 0.0356 × 0.320 |
| Steel | 20000 → 9968 | 1639 → **0** | 4098 → **0** | 0.640 × 0.0343 × 0.320 |
| Bronze | 20000 → 19580 | 1628 → **0** | 4559 → **0** | 0.640 × 0.0354 × 0.320 |

Reference: the hands are 20 654 tris, 0 boundary edges. UV islands went 1046 → 121/156/107.

## OPEN — this is your work

1. **Finish the texture atlases.** A lane is running (or has just finished) fixing
   `tex_symbols.py --process`, which was destroying the motifs two ways (threshold landing at 0.994
   instead of ~0.5, and a cell mapping that did not match the sheet). Collect its diff, verify its
   claims yourself, and carry it to finished atlases for all three styles.
2. **Bronze needs another pass.** Verdigris reads as cyan blobs sitting ON the surface instead of
   corrosion in the low spots, and does not follow the relief. Drive the patina from the
   height/cavity term.
3. **BUILD THE PREFABS AND THE BUNDLE.** `unity/GloomhavenVR.Assets/Assets/Editor/BuildBoard.cs` has
   been edited three times and **has never been compiled or executed by any gate.** Unity is at
   `/home/claw/unity-2021.3.5` (NOT `unity-2021.3`). Its expected measurements are recorded in the
   file: seat floors `74.6×64.3 / 81.0×70.1 / 61.2×51.9` mm, rest pads `81.6 / 81.7 / 68.1` mm. **If
   a rebuild logs anything else, the code is wrong, not the boards.**
4. **Verify in a render that the three seats and the ornament land where they should**, on the real
   meshes with the real atlases, and say what you see.
5. **Report to the user in German** what he must install and what changed. The bundle has been
   byte-identical since ModBuild 250 — every test since was a DLL-only install. **This time he must
   copy `gloomhavenvr.bundle` as well**, and he must be told so explicitly.

## THINGS THAT WILL BITE YOU — all measured, none hypothetical

- **The user's tuned offsets are mirror compensation.** `ConfirmUndoOffset_Steel` is **+0.462 m** and
  `RestButtonOffset_Steel` is **−0.44 m**, because shipped Steel and Bronze had their zones mirrored.
  On the re-authored canonical boards the same dials would put the clusters ~350–380 mm off the
  board. `18fddc60` clamps the in-plane displacement to the slack inside the recess, keyed on whether
  the board carries a measured recess — old bundle, no measurement, no bound, bit-identical. Do not
  "fix" his config: those values are correct for the bundle installed on his machine, and changing
  shipped defaults cannot reach him anyway because his own cfg wins.
- **A clamp fallback is not a default.** `ButtonTuning.DefaultBoardWidth = 0.073` is the pre-Bind
  fallback; the bound value is `Defaults.BoardButtons_Width/Height = 0.063/0.065`. This trap has cost
  two rounds in this session alone, on two unrelated subsystems.
- **`gen_render.py` is ~2 stops overexposed** and makes every material look like white plastic. Use
  `tex_render.py` for material judgement — it has an 18% grey card in shot and iterates exposure
  until the card measures correctly. Use `gen_render.py` only for FORM.
- **A self-test that builds its own input proves nothing.** `tex_symbols.py --selftest` passed 9/9
  while the real sheets came out shattered. Point acceptance at real inputs.
- **Census/log lists in this repo truncate with an ellipsis and no marker.** "X does not appear" has
  been wrong three times this session.
- `unity/board-prep/out/` is **gitignored** — the UV JSONs, the generated sheets and every render
  live there and are NOT captured by a diff. Collect them by hand.

## THE GENERATED SHEETS — the money is spent, do not spend more

`unity/board-prep/out/sheet_a.png` (medieval guild woodcut) and `sheet_b.png` (Norse interlace),
1024² each, nine motifs per sheet. Both are good. Sheet A's `bracket_alt` cell came back as a hammer;
sheet B's covers it. **Do not use the `rest_alt` cell from either sheet** — both are a crescent
enclosing a six-pointed star, i.e. two real-world religious symbols combined. It is a spare cell.

### You MAY generate more images — and you carry the same responsibility for them

The user has granted this explicitly (2026-08-25): *"Er DARF und muss auch Bilder erstellen können.
Er muss damit aber genauso verantwortungsvoll umgehen wie du."* So you do not need permission. You
do need the discipline that came with it:

- **Every image costs real money.** His standing instruction is *"mache das eher selten wenn du viel
  Vorbereitung getroffen hast"* — generate only after the surrounding pipeline is finished and
  proven, never to explore.
- **Never generate what a procedure can produce better.** Material bases, anything that must tile,
  and anything built from exact geometry (rings, evenly spaced rivets, right angles) stay
  procedural. Diffusion cannot close a seam.
- **Batch.** One sheet carrying nine motifs beat nine calls, and two sheets in two different
  ornamental hands beat one sheet plus a re-roll. Think in sheets, not in motifs.
- **Prove the consumer before you feed it.** The last round spent two calls into a processor whose
  self-test passed 9/9 and which then shattered every motif. Point the acceptance at a REAL input
  before spending.
- **A generated image never becomes albedo.** It is a binary stencil; the bevel is rebuilt from an
  exact distance transform and the motif is carved into a procedural material. This is the whole
  reason the result stops reading as AI, and it is not negotiable.
- **Look at what came back** and say what you see, per cell, before using it.

How to reach the tool: it is an MCP tool, so load its schema first with
`ToolSearch` for `create_asset`, then call it with `model: "gpt-image-2"`. Output lands in
`unity/board-prep/out/` (gitignored). If it returns a 401 the plugin's key is stale — that costs
nothing, but stop and tell the integrator rather than retrying.

## RULES YOU INHERIT (non-negotiable)

- Presentation only; never write game state from presentation code. Never patch
  `ScenarioRuleLibrary`, Photon Bolt or `FFSNet.NetworkManager`. `ressources/` and `libs/` are
  read-only symlinks; `decompiled/` is read-only reference.
- Every feature must be multiplayer-compatible; peers derive board layout locally from the same
  prefab, so anything you change must be derivable on every client or it is a picture desync.
- Never destructive git or `gh`: no delete, rename, archive, transfer or force-push, ever.
- Figures are never touched; lights are never written to; doorway segments never fade.
- Bump `NetProtocol.ModBuild` +1 on every build handed to the user.

## GATES — every one, every time
```
./scripts/build.sh          # 0 errors, EXACTLY 6 warnings
./scripts/wire-tests.sh     # 146857 assertions
./scripts/check-mirrors.sh ; ./scripts/check-frame-order.sh ; ./scripts/check-bundle-format.sh
./scripts/patch-inventory.sh check   # 78 classes / 130 methods
python3 ./scripts/check-wire-coverage.py ; python3 ./scripts/rebase-defaults.py check ; python3 ./scripts/check-refasm.py
```
The 6 expected warnings: CS8602 `ButtonCluster.cs:459`/`:1638`, `RemotePickBanner.cs:113`,
`RemoteHandFan.cs:1810`; CS8604 `StatPanelSurface.cs:360`/`:362`.
`check-bundle-format.sh` currently asserts 72 966 925 bytes — **that number changes when you rebuild
the bundle**, which is expected and must be updated deliberately, not silenced.
