# THE BOARD BUTTON OVERHAUL — the design, decided before the work starts

User request, 2026-08-25. Not yet implemented; two lanes were in flight on the same files when it
was written (the button-group unification, and the board-texture side/back round). **Read this
before starting, and read `BOARD-REBUILD-HANDOVER.md` for the board pipeline itself.**

## WHAT HE ASKED FOR

> "Statt einfach nur Text, möchte ich ein Symbol (und Text dazu), aber der Text soll sich in den
> button nativ einfinden. Pro Board soll es auch ein anderes passendes Aussehen der buttons sein,
> das zu dem board und seinem Aussehen passt. Nutze hierbei auch die Hilfe von gpt-image-2 …
> Die Rast buttons sollen weiterhin Rund sein (auf der linken Seite des boards) und die generischen
> buttons auf der rechten Seite (viereckig). Aber auch Buttons wie 'Fixed' soll zum board passen und
> immersiv und gut aussehen. Auch Drück-Animation wenn der button nach unten gedrückt wird soll gut
> funktionieren. Die 'Verschwinden' und 'Auftauchen' Animation kannst du beibehalten."

And, when asked whether every cap needs a caption:

> "Entscheide das selber pro Fall. Versetze dich in einen Spieler der die Symbolik und eventuell auch
> das Spiel noch nicht kennt. Es soll klar sein was die buttons bedeuten. Eventuell kannst du auch
> Text dynamisch in das board mit einarbeiten? Zb über dem long-rest button. Wichtig ist hierbei nur
> das a) es lokalisiert sein kann (deutsch, englisch) und b) es nativ und immersiv in dem board
> verarbeitet ist, nicht einfach als schwebender Text darüber. Das gilt übrigens auch für den
> Rundentext … Die generischen Buttons haben immer unterschiedlichen Text darauf, d.h. auf denen
> sollte der Text auch erhalten bleiben, da es sich immer ändert."

## THE CONSTRAINT THAT SHAPES EVERYTHING — measured, not assumed

**The generic caps' labels are LIVE GAME TEXT, not a fixed set.** `CardsGameApi.PickDialogOptionLabel`
(`CardsGameApi.cs:533-555`) reads the caption straight off the option button a 2D player would
click — `InputButton.ExtendedButton.buttonText` — so it is whatever the game chose for this pick,
in the player's language: *"Karten abwerfen"*, *"Wähle eine andere Karte"*. It is also mirrored to
peers (`NetProtocol.ExtIdCapLabels`) so a teammate reads the same wording.

Therefore:
* **A SYMBOL may be a generated texture.** Symbols belong to the ROLE, and the roles are fixed.
* **TEXT MAY NEVER BE BAKED INTO AN ATLAS.** It changes at runtime and with the language. Any
  design that paints a caption into a texture is wrong the moment the dialog changes or the user
  switches to English.

## THE DECISIONS — per control, decided for a player who knows neither the symbols nor the game

| control | shape / side | cap carries | board carries |
|---|---|---|---|
| Confirm | square, right | **symbol + the live game text** | — |
| Undo | square, right | **symbol + the live game text** | — |
| Skip | square, right | **symbol + the live game text** | — |
| Item use | square, right | **symbol + the live game text** | — |
| Short rest | round, left | **symbol only** | engraved caption beside/above the pad |
| Long rest | round, left | **symbol only** | engraved caption beside/above the pad |
| Fixed / FIXIERT | its own | **symbol only** | engraved caption |
| Round readout | — | — | **engraved into the board frame**, no backing plate |

**Why the generic four keep their text:** the caption is the only thing that says what THIS press
will do, it changes every pick, and a symbol cannot carry it. His instruction is explicit.

**Why the rest pads and Fixed do not:** their meaning never changes, so the caption is a one-time
teaching aid — which is exactly what an engraved board label is for. It teaches a new player once
and then stops competing with the cap for attention. The board already carries the motifs: the
crescent-and-flames pad and the wheel pad are on all three boards. The cap symbol should echo the
pad it sits beside, so the pairing is self-explanatory.

**The round readout** is a floating plate today (`RemoteStatusReadouts.cs:76` records that its grey
backing plate was itself a reported defect). It becomes carved frame text.

## THE TECHNICAL CRUX — engraved text that is still localized

Carved-looking text that must change at runtime cannot come from the atlas. Preferred route, and it
needs no mesh change:

* A TMP mesh laid flat ON the board surface, inset slightly along the board normal, using the
  board's own lit material family (`BoardLit`) rather than a UI shader, so it takes the same light
  and the same per-board material response.
* The carved read comes from a generated **gutter/AO strip** under the glyphs — a small generated
  texture, per board style — plus the inset. That is the part gpt-image-2 is for.
* **Verify it is depth-honest and does not z-fight** at the shipped board tilt and at the zoom
  range the player actually uses. A label that shimmers is worse than a floating one.

Fallback if the inset cannot be made to read: cut shallow recesses for the fixed-position labels in
`gen_board.py` and inset the TMP into them. This changes the mesh, so it must keep every seat, pad
and slot anchor bit-identical — they are on the wire-test harness
(`tests/GloomhavenVR.WireTests/BoardSeatVectors.cs`).

**Localization:** every engraved caption goes through `Loc` like any other string. `Loc.Mod` has NO
fallback — a missing key renders as the raw key — so each caption needs its DE/EN pair added.
Layout must survive the longer of the two: "Kurze Rast" against "Short Rest", "Lange Rast" against
"Long Rest".

## PER-BOARD LOOK
The caps today share ONE material for all three boards — `KeycapGrain_albedo.png` /
`KeycapGrain_normal.png` with `BoardLit` (`PlayTray.6.Build.cs:820-821`). That is why they look
identical everywhere. Split it per style: carved oak, forged steel, cast bronze with verdigris,
matching each board's own atlas. Same treatment for the symbol set and the engraving gutters.

## WHAT ALREADY EXISTS AND MUST NOT BE REINVENTED
* **The press animation exists**: `PlayTray.BoardButton._press` decays at 6/s, ~170 ms, and it is
  MIRRORED TO PEERS — `BoardCapPress` publishes it in five bits of `ExtIdHalfHover` because at the
  5 Hz extras cadence an event that short falls between packets. Improve the feel; do not rebuild
  the mechanism, and do not break the wire contract.
* **The appear/disappear dust dissolve stays** — his words, explicitly.
* Two press commit points report: `PlayTray.BoardButton.Press` and
  `WorldUI.ButtonCluster.PhysicalButton.Fire`. If the button-unification round removes the second,
  the surviving one must still report.

## SEQUENCING — why this was not started immediately
The button-group unification lane owns `PlayTray.*`, `ButtonCluster`, `CardsConfig` and `Defaults`;
the board-texture lane owns `unity/board-prep/` and REBUILDS THE BUNDLE. Two lanes rebuilding
`prebuilt/gloomhavenvr.bundle` conflict unconditionally — it is a binary file and there is no merge.
Start this only after both have landed, and inherit their result: the unification moves the caps
into the three mesh seats and collapses the per-board offsets onto the mesh anchors, which is the
structure these visuals attach to.

## IMAGE BUDGET
gpt-image-2 is REQUIRED for the symbol set and the material/gutter textures, not optional — the user
has said twice that handling it responsibly does not mean avoiding it. Prepare on paper first, then
generate what the job needs, and report the count.
