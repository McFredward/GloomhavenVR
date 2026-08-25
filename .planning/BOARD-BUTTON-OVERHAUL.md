# ROUND 1 (2026-08-25): WHAT WAS BUILT, AND THE TWO PREMISES THAT WERE WRONG

The design below was written before the work started and is kept verbatim. This section is the
RECORD: what shipped, what the design got wrong, and what is still open.

## THE PREMISE THAT WAS FALSIFIED — "a TMP mesh using BoardLit"

The design's TECHNICAL CRUX specifies engraved text as *"a TMP mesh laid flat ON the board surface
… using the board's own lit material family (`BoardLit`) rather than a UI shader"*. **That cannot
be built, and the reason is not a difficulty, it is an impossibility.**

* A TMP glyph is not a shape. It is a **signed distance field** in the font atlas, decoded by the
  TMP shader's own smoothstep against a per-glyph gradient scale. Any shader that samples that
  atlas as an ordinary texture — `BoardLit` included — draws a grey blur, not a letter.
* `BoardLit` is strictly **opaque**: `Tags { Queue=Geometry }`, `return fixed4(col, 1.0)`, no blend
  state and no cutout. It has no way to leave the board showing between the strokes.

The design's own fallback (shallow recesses in `gen_board.py`) was closed this round by the lane
split — the board-texture lane owns that file and the three FBXes.

**What was built instead**, `src/GloomhavenVR/Cards/BoardEngraving.cs`: the carve is done by the TMP
distance-field material itself, with the recipe **inverted** from the one the keycap labels wear.
A cap label sits PROUD on a key, so it is lit on top and drops its shadow down-right — bright
parchment fill, dark keyline, dark underlay offset down-RIGHT. A board label is CUT IN, so the
inside of the stroke is in shadow and the far lip catches the light — dark fill the colour of that
board's own material in shadow, a darker keyline for the shaded wall, and a LIGHT underlay offset
down-LEFT for the lit lip. Same machinery, opposite physics.

**The light direction was read off the shader, not assumed.** `BoardLit`'s baked key is
`normalize(0.35, 0.85, -0.45)` in world space: above, toward the viewer, slightly right. On a board
facing the player that puts the lit lips of an incision on the BOTTOM and LEFT of every stroke,
which is why the underlay is offset down and left and the cap labels' shadow is offset down and
right. They are visibly consistent with the one light the board is actually under.

## THE OTHER CORRECTION — the plate was removed from BOTH boards, and the first fix was right

`Net/RemoteStatusReadouts.cs` records defect (c) of the 1:1 round: *"die Runden-Anzeige sitzt auf
einem grauen Kasten, den der Besitzer nicht hat"*. The answer THEN was correct — the owner did have
a plate, and parity meant building the owner's plate rather than deleting the peer's. The user has
now ruled on the plate itself (*"Das gilt übrigens auch für den Rundentext"*), so it is deleted from
the owner's board and from the mirror in one change. There is nothing drawn behind the glyphs on
either board any more, so the two can no longer disagree about what that something looks like.

## THE DECISIONS AS SHIPPED

| control | cap carries | board carries |
|---|---|---|
| Confirm | bold check mark **+ the live game text** | — |
| Undo | counter-clockwise return arrow **+ the live game text** | — |
| Skip | two triangles against an upright bar **+ the live game text** | — |
| Item use | open hand with rays **+ the live game text** | — |
| Short rest | the SHORT-REST PAD's own crescent-and-embers stencil | engraved "KURZE RAST" above the pad |
| Long rest | the LONG-REST PAD's own spoked wheel | engraved "LANGE RAST" below the pad |
| Fixed / Folgen | an anchor / two footprints, swapped live | engraved "FIXIERT" / "FOLGEN" above the toggle |
| Round readout | — | engraved into the board, no plate |

The generic four keep their text because the caption is the only thing that says what THIS press
will do — it changes every pick and no symbol can carry it. The rest pads and the toggle mean the
same thing for ever, so their caption is a one-time teaching aid, and a teaching aid belongs on the
board where it teaches once and then stops competing with the cap for attention. A player who knows
neither the symbol nor the game still learns it: the word is engraved directly beside the pad.

The two rest symbols are **not merely similar to the pads beside them, they are the pads' own
stencils**, taken from the sheet that style's board took them from — so the pairing is exact rather
than evocative, and it cost no generated image.

## THE MECHANISM — one atlas per board, one cell per role

`unity/board-prep/buttons/` builds `Keycap{Oak,Steel,Bronze}_{albedo,normal}.png`: a 4x4 grid of
256-texel cells, one per `CapRole`. `BoardLit` already runs `o.uv = TRANSFORM_TEX(v.uv, _MainTex)`
and samples `_BumpMap`/`_MRSMap` with that same `i.uv`, so a material's `mainTextureScale/Offset`
picks the cell for every map at once. A role is therefore two floats, not a texture — which is what
makes the follow/pin toggle's live symbol swap free, and what keeps three styles times seven roles
down to six files.

**The symbol is CARVED, not raised, and that is the round-2 finding cashed.** On this shader the
specular is a bevel term, not a surface term: `_SpecStrength` 0 vs 0.85 moves the flat-on mean by
0.001. A raised symbol reads by a highlight this surface cannot deliver; a recessed one reads by
ambient occlusion baked into the albedo, which is view-independent. See
`unity/board-prep/buttons/README.md`.

## THE MIRROR, AND WHY IT CANNOT DRIFT

Every cap material on both boards is minted by ONE call — `PlayTray.NewKeycapMaterial(shader,
colour, role, style)` — which the peer mirror already called before this round. The peer's board
STYLE is `RemoteBoardTuning.Style`, already on record 28 because the board prefab is chosen from it,
so a bronze player is drawn with bronze keys carrying the bronze board's own plaited crescent with
**no new wire field**. The engraved captions are cut, placed and lettered by the owner's own
`BoardEngraving`, whose caption offsets live in that one class rather than as a mirrored pair.

**A mirror group was DELETED, not added.** The press spring used to be an inline `Time.deltaTime *
6f` on the owner's cap and a named `RemoteCapFx.PressDecayPerSecond = 6f` on the peer's. Both sides
now advance a phase in seconds and call `WorldUI.ButtonStroke.Depth01`, so the mirrored press IS the
owner's press. (`check-mirrors.sh` still reports 19 — that pair was described in its prose and was
never one of the nineteen machine-checked groups, because the local half was not a named constant.)

## THE PRESS STROKE

`WorldUI/ButtonStroke.cs`: attack 35 ms (ease-out onto the bottom), detent 30 ms, spring-back 120 ms
with a 10 % overshoot past rest. It replaced an instantaneous drop with a linear 6/s decay — a cap
that teleported to the bottom in one frame and rose back at constant speed.

**Its first draft could not overshoot at all.** The release leg was a decaying sine added to an
ease-out, and the arithmetic says such a sum never crosses rest: the ease dominates while the
oscillation is largest, and the oscillation has died by the time the ease has not. The comment would
have described motion that was not there. It is two explicit smoothsteps now, and
`tests/GloomhavenVR.WireTests/BoardCapSymbolVectors.cs` asserts the crossing count, the peak and the
endpoint.

**It runs on the UNSCALED clock now, on both sides.** The old spring used `Time.deltaTime` and the
mirror's comment defended that explicitly. The game stops simulation time behind menus and dialogs
and during card phases — which is exactly when a player presses board buttons — so a scaled stroke
froze the cap at whatever depth it had reached. Same defect the surface-fade watchdog exists for,
one animation over.

## THE DEFECT THE RENDER CAUGHT — a keycap texture is a MODULATOR, not a colour

Worth recording because nothing in the build would have said so, and because the guard that
exists for exactly this shape could not see it.

`BoardLit` computes `alb = tex2D(_MainTex, uv) * _Color`. A keycap texture therefore MODULATES the
state colour rather than being a colour in its own right, and the one it replaces —
`KeycapGrain_albedo.png` — is a near-white greyscale grain with mean **0.837**, i.e. a modulator
that passes the state colour through almost untouched. The generated material plates are
photographs of materials, means **0.27–0.42**. Dropped in unchanged, the rendered cap face went
from **1.75×** the luminance of the mod-drawn WELL it sits in to **0.95× (oak), 0.85× (bronze) and
0.57× (STEEL)** — from clearly proud of its own recess to level with it or darker.

That is precisely the "invisible button, only the text still visible" shape
`WorldUI.ButtonTuning.SeatedCapColor` was written for after a hardware report — **and the seat
floor cannot see it.** It floors the material's `_Color`, and `_Color` had not moved: the
darkening arrived in the TEXTURE, a term that did not exist when that guard was written.

**What caught it was the render sheet**, which showed the steel caps reading nearly black; the
measurement is what turned "looks dark" into a cause. `cap_atlas.normalise_plate` now re-bases each
plate to the shipped grain's mean before anything is carved into it, with a uniform RGB gain (hue
ratios preserved exactly) and a soft knee at 0.80 so bright grain does not clip flat. The caps come
back at **1.74–1.81×** their well — the shipped ratio — with their own colour casts and structures
intact (oak 1.21:1.14:0.65, steel 1.00:0.98:1.01, bronze 1.13:1.11:0.76).

### AND THE LIMITATION THAT LEAVES, measured rather than glossed

With the level correct, the three IDLE cap faces render at CIELAB **ΔE 13.8 (oak↔steel), 10.3
(steel↔bronze) and only 3.7 (oak↔bronze)**. Steel is clearly a different material; oak and bronze
are within the distance at which two colours read as the same colour under different light, and are
told apart by their GRAIN STRUCTURE rather than by their hue.

The cause is not the re-basing (a uniform gain cannot change a hue ratio) but the STATE COLOUR: the
idle face is the parchment `(0.60, 0.51, 0.35)` × the shipped `[ButtonColors] BoardCapTint` of 0.5,
a strong warm tint applied identically on all three boards, and a material whose own cast is ±20 %
cannot survive being multiplied by it. **A chroma boost was tried on paper and rejected on
measurement**, not on taste: it does not separate oak from bronze at any gain (ΔE 3.7 → 3.9 → 3.1
from k = 1.0 to 2.6, because the separation lives in the blue channel, which is already crushed) and
it clips 99.99 % of oak's texels at k = 1.6.

The lever that WOULD separate them is the idle face colour itself, which is `[ButtonColors]` and is
the user's tuning. It was not touched.

## THREE MORE DEFECTS, ALL FOUND BY THE RENDER STATION AFTER THE FIRST REPORT

**1. THE KEYCAP MESHES WRITE NO TANGENTS, AND `BoardLit` BUILDS ITS WHOLE BASIS FROM THEM.**
Measured: `mesh.tangents.Length == 0` on both `CardMesh.BuildBeveledKeycap` and `BuildRoundCap`,
while the shader does `o.wt = UnityObjectToWorldDir(v.tangent.xyz)` and
`o.wb = cross(o.wn, o.wt) * v.tangent.w`. With no TANGENT stream bound that is whatever the
graphics API supplies for a missing vertex attribute — undefined, and not guaranteed to agree
between the editor's GL and the rig's D3D11. It predates this round and survived because the only
map bound was a low-contrast shared grain; this round binds a per-board map whose carved symbol is
the entire point. `CardMesh.KeycapTangent` now writes an explicit `(1, 0, 0, -1)` on every vertex,
which is exact rather than approximate: both meshes map UV as a pure function of object XY, so the
direction of increasing u is `+X` everywhere and `w = -1` is what makes `cross(n, t) * w` come out
`+Y` on the front-facing plateau. `RecalculateTangents` was the obvious alternative and is worse —
it solves from the UV gradient, which is degenerate on eight of the twenty triangles.
**The picture does not change in the editor**, which is the point: it was working by luck in one
API and had no guarantee in the other.

**2. THE ENGRAVING PALETTE WAS DERIVED FROM THE WRONG SURFACE.** The three colours were taken from
the KEYCAP material plate, on the reasoning that a board and its keys are the same material family.
For oak and bronze that is very nearly true (the cap plate is 0.96x and 1.06x the board's own face
band). **For steel it is wrong by 0.68**: the cap is dark blued iron (0.275) and the board's face
is bright brushed silver (0.418). Cut into the real steel board the plate-derived fill would have
landed a 70 % drop where 55 % was intended — black text, not a groove — and the "lit lip" at 0.372
would have been DARKER than the 0.418 board around it, **inverting the one cue that says the mark
is cut in rather than raised.** The palette is now derived from each board's own albedo face band
(`cap_check.py --palette` reads `ref_face_<style>.png`), and `engrave_preview.py` composites onto
that same surface rather than onto a keycap: 49-53 % drop with the lip at 0.97-1.13x on all three.

**3. THE @32 px CONTRAST NUMBER DOES NOT MEAN WHAT THE FIRST REPORT SAID IT MEANT.**
`cap_check.py` reports 25-44 % luminance drop at the across-the-table size, and that was written up
as the symbols surviving. The render says otherwise: at 32 px the square caps' symbols are a 3-4 px
dark smudge — *present, not identifiable*. Both are true, and the instrument is measuring one term
of what the eye needs. A mean-luminance drop over a symbol's footprint says "there is a mark here";
it cannot say "you can tell which mark". The round rest DISCS survive better (bigger symbol, bigger
cap). At 96 px — the board pulled in to read — everything reads clearly, and the MSAA-off column
shows the far-view loss is not the sample count hiding it.

**AND ONE CORRECTION TO THE PRESS STROKE'S CLAIM.** Measured off the rendered frames, the full
4 mm press moves the cap silhouette 6 px in a 360 px frame; the 0.4 mm rebound moves it 1 px.
Scaled to the real viewing sizes that is 3.5 px of press and 0.35 px of rebound at 0.5 m. **The
rebound is sub-pixel at every distance a player will use.** What the stroke actually buys is the
attack and the detent — 35 ms of ramp instead of a one-frame teleport, i.e. two or three frames of
visible motion at 72-90 Hz. The overshoot is a feel detail carried by timing, not something anyone
will see, and it should not be described as though it were.

## WHAT IS STILL OPEN

* **The engraved text has not been seen on hardware.** TextMeshPro is not in the companion Unity
  project's package manifest, so no station in this repository can render the shipped TMP material.
  What was rendered is a three-layer STAND-IN (legacy `TextMesh`), and every such file is named
  `standin_*` for that reason. It is evidence for LAYOUT, COLOUR and DEPTH BEHAVIOUR, not for glyph
  rendering. The depth-honesty claim rests on arithmetic and on the shader, not on that picture.
* **No new config dial was added, deliberately.** The engraving palette, the caption boxes and the
  stroke timings are authored constants. Adding dials would have meant `Defaults` entries, DE+EN
  `Loc` name and description pairs, `ConfigSteps` entries and wire coverage for each — and a second
  lane was appending to `Loc.Config*.cs` and `ConfigSteps.cs` in the same round. If the user wants
  to tune the engraving, that is the next round's work and it is cheap: every value is already a
  named constant in one class.
* **No new `Loc` key was added either.** The engravings reuse `Loc.Mod("short_rest")`,
  `Loc.Game("GUI_LONG_REST")`, `Loc.Mod("follow")` and `Loc.Mod("pinned")` — the exact strings the
  caps used to carry — upper-cased in the presenter. `Loc.Mod` has no fallback, so every new key is
  a chance to render a raw key on the board; there was no wording that needed one.
* The `[ButtonColors] Label*` dials still style the CAP labels only. The board engraving has its own
  palette (measured from the generated plates) and deliberately does not read them: a keyline tuned
  to read on a bright brass key is the wrong keyline for a cut in dark wood.
* Two spare symbol cells were generated and are unused (`C1` a check inside a ring, `C2` a plain arc
  arrow, `B1` a phial as an alternate item-use device). They are insurance, already paid for.

---

# THE BOARD BUTTON OVERHAUL — THE DESIGN AS IT WAS DECIDED (kept below as written, corrections above)

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
