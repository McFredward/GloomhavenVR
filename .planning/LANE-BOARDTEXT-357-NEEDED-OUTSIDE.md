# Lane BOARDTEXT (ModBuild 363 base `ee04024e`) — work that belongs OUTSIDE this lane's owned paths

> ### ⚠ ONE ITEM IS STILL OPEN — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> **§2 was never applied.** `Cards/Piles/PileViewer.PileStack.Create` still calls only
> `NativeButtonSkin.ApplyFont` on the three pile captions (`PileViewer.cs:1029` and `:1040`) — it
> does not call `StyleWorldReadableLabel`. Meanwhile the item-use slot caption, whose own doc
> comment says it is built to match those three exactly, **did** get the keyline
> (`Cards/Tray/PlayTray.4.Slots.cs:662`). The deliberate pairing is half-applied and has been since
> ModBuild 363.
>
> §3 (the full bundle rebake for the regenerated `Keycap*` atlases) and §4 (the ModBuild bump) were
> both consumed long ago; §1 asked for nothing. Only §2 remains.

Branch: `lane/boardtext-357`. Everything below is a change the lane could NOT make because the
file belongs to another lane or is out of this lane's scope. Nothing here is a blocker for the
lane's own commit — each item is listed with the exact patch text and the reason.

---

## 1. `Core/Loc/**` — NOTHING NEEDED

This lane added **no new Loc keys and no new config keys**. The two user requests it answers are a
material recipe change and a mesh deletion; neither introduces a string the player reads or a dial
the player sets.

Stated explicitly rather than left blank, because a "needed outside" file with an empty Loc section
and a "needed outside" file that was never checked look identical from the outside.

---

## 2. `Cards/Piles/PileViewer.cs` — the pile captions want the same one-liner

**Owner:** not this lane (`Cards/Piles/**` is not in the owned set).

**Why it matters.** Request 5 was answered by opting four families of floating label into
`WorldUI.NativeButtonSkin.StyleWorldReadableLabel` — the battle goal, the game's objectives rows,
the item-use slot caption and the peer-board readouts. The item-use caption's own doc comment in
`Cards/Tray/PlayTray.4.Slots.cs` states that it deliberately matches the PILE captions
("ABGEWORFEN" / "VERBRANNT" / "GEGENSTÄNDE", `PileViewer.PileStack.Create`) — *"the same muted
parchment colour, the same native HUD font, and the SAME fit box and font ceiling, so at the shipped
pile scale the glyphs come out the same physical size"*. That pairing is now HALF applied: the
item-use caption has a keyline and the three pile captions beside it do not.

They are visible in `text-board.jpg` (right edge, over black) and they hang over the scene exactly
like the item-use caption does, so they have the same defect for the same reason — they just happen
to be over the dark half of it in that particular screenshot.

**The patch.** In `PileViewer.PileStack.Create`, immediately after the existing
`NativeButtonSkin.ApplyFont(...)` call on the caption TMP:

```csharp
        // …AND THE RIM (user request 5, 2026-09-03). These captions hang over the scenario, not
        // over the board, so they cross the same ground the battle goal does — measured off
        // text-board.jpg, that ground runs from L=0.0005 to L=0.6312 within one line. The item-use
        // slot caption is built to match these three exactly (PlayTray.4.Slots), and it now carries
        // the keyline; without this line the deliberate pairing is half applied.
        WorldUI.NativeButtonSkin.StyleWorldReadableLabel(label);
```

(substitute the local variable name for the caption TMP). No colour, size or font change — the
method writes only `_OutlineColor` / `_OutlineWidth` / `_Underlay*` on the label's own per-instance
font material, and is a clean no-op on a TMP shader variant without those passes.

---

## 3. FOR THE INTEGRATOR: this build needs a FULL BUNDLE REBAKE

Not a code change, but it must not be missed.

`unity/GloomhavenVR.Assets/Assets/Bundle/Table/Keycap{Oak,Steel,Bronze}_{albedo,normal}.png` were
**regenerated** (new `FixedPinned` cap symbol — request 6a). The plugin DLL alone will not carry
them: the tester needs a full install with a freshly baked `gloomhavenvr.bundle`.

- Bake with `/home/claw/unity-2021.3.5` (NOT `unity-2021.3`), then `scripts/build-bundles.sh`.
- `scripts/check-bundle-format.sh` PASSES in this lane and that is expected, not reassuring: it
  reads the PREBUILT bundle in `prebuilt/`, which still holds the pre-change atlases. It will only
  see the new ones after the bake.
- Only cell 7 of each atlas changed; cells 0-6 and 8-9 are bit-identical. Nothing in
  `Cards/Caps/CapSymbols.cs` or `CapCellMath` moved, so an OLD bundle with a NEW DLL is not
  mis-indexed — it simply still shows the anchor.

---

## 4. `NetProtocol.ModBuild` — deliberately NOT bumped

Per the lane brief. The integrator bumps it when this lands.
