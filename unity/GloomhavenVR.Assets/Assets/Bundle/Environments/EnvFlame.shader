// GloomhavenVR — candle/lantern flame shader (shader-animated, script-free).
//
// Drawn on small static CROSS-QUAD meshes, NOT camera-facing billboards — the
// permanent VR rule forbids sprites that can visibly re-orient with the head
// (BuildEnvironments.cs header). A cross-quad flame is world-anchored geometry;
// _Time drives a gentle sway + UV wobble + brightness flicker matched to the
// EnvRoom light flicker (same sine family, so the flame and the light it casts
// breathe together). Additive, no depth write.
//
// A CANDLE is two quads at 90°. A BONFIRE is THREE at 60° whose energy fades
// as the view approaches their plane — see the STRICHE block below, which is
// where the tension between "no billboards, ever" and "no visible quads,
// ever" is resolved and costed. Neither kind of card can turn.
//
// ============================ REAL FIRE — USER VERDICT, ModBuild 142 =========
// "Das Feuer im Keller ist eher ein rötlicher Schein - ich möchte lieber das
//  Teile des Kellers wirklich brennen."
//
// The cellar's Fire was a warm RIM on the stone (EnvRoom's elemAdd) plus an
// ember ring at the periphery: light the colour of fire with nothing burning in
// it. This shader is now also the burning half, because a fire and a candle are
// the same drawing problem twice: alpha-blended tongues on world-anchored
// crossed cards, animated from the shared clock.
//
// THE GATE (_FireGate). Everything this shader draws for the burning cellar
// exists ONLY under the Fire infusion, and it has to cost nothing when Fire is
// down — so a gated flame COLLAPSES to a point in the vertex shader exactly as
// the gated particle emitters do (EnvParticleAdd's header): zero-area triangles,
// no fragment ever shaded, and the zero state is the same instructions on the
// same data rather than "close enough". _FireGate = 0 is the candles: they burn
// whatever the elements are doing, because they are the room.
// ============================================================================
//
// ==================== FIRE REAL — USER VERDICT, ModBuild 144 ================
// "Das Feuer im Keller sieht eher aus wie viele Kerzenflammen statt wirklich ein
//  bedrohliches Brennen der Möbel! Überarbeite das Feuer nochmal komplett, es
//  soll realistisch und bedrohlich wirken und auch die Lichtverhältnisse
//  entsprechend anpassen."
//
// He is describing the previous _Bonfire mode exactly, and the renders agree:
// the shipped fire is a ROW OF TALL AMBER SPIKES with black between them, each
// one a closed smooth teardrop, standing on a crate. Every one of those words is
// a candle. The three faults, and where each is now fixed:
//
//   1. THE SPRITE WAS A CANDLE FLAME. `candle_flame_alb` is one laminar
//      teardrop with a hard silhouette; enlarging it enlarges a candle. It is
//      replaced for bonfire materials by a four-cell procedural fire atlas
//      (BuildEnvironments.MakeFireAtlas) whose cells are a BED, two torn tongues
//      and a detached PUFF — shapes with holes and ragged tops, which is what
//      makes two overlapping cards read as one mass instead of as two objects.
//      The candles keep their own sprite and are untouched.
//   2. THE MOTION WAS A SWAY, NOT TURBULENCE. Measured off the shipped
//      material: surge = sin(lt*2.9) + sin(lt*4.7) with lt = t*_Rate*_LickRate,
//      _Rate 1.19, _LickRate 1.15 — i.e. 0.63 Hz and 1.03 Hz. A one-hertz
//      undulation IS a candle in a draught. The bonfire branch now runs on
//      GhvrFireFlicker's bands at _FireHz (shipped 4.6 Hz), so the tongues turn
//      over four to eight times a second.
//   3. NOTHING DETACHED. A candle flame is attached at all times; a fire throws
//      pieces of itself up that separate, cool, redden and go out. GHVR_FKIND_PUFF
//      cards do exactly that, on their own cycle, as a pure function of the
//      clock.
// Plus the light, which was the loudest omission: see EnvFire.cginc.
//
// A BURNING CRATE IS NOT A BIG CANDLE, and _Bonfire is the whole of that
// difference. A bonfire material is fed a mesh of many cards
// (EnvRoomBuilder.FireMesh) whose VERTEX COLOUR carries the per-card variation:
//     COLOR.r  the card's own phase, 0..1 of a turn
//     COLOR.g  how hard it surges (the bed barely; the tallest tongues most)
//     COLOR.b  how far it stands from the seat, 0..1 — the lean and the tear
//     COLOR.a  its share of the fire's brightness
// ...and UV1 the things that are not amounts:
//     UV1.x    which atlas cell it is drawn with
//     UV1.y    its KIND (GHVR_FKIND_* in EnvFire.cginc)
//     UV1.z    a puff's rise, in metres
//     UV1.w    a puff's place in its own cycle, 0..1
// None of that is read while _Bonfire is 0, which is what lets the room's three
// candle materials go on using a mesh that has neither stream.
// ============================================================================
//
// ==================== SHELF RIDERS — USER VERDICT, ModBuild 144 =============
// "Die Kerzen und das Feuer, die auf dem Bücherregal stehen, kippen nicht mit -
//  das musst du beheben das ist ein echter Bug."
//
// Three of this shader's materials stand ON the cellar's tipping bookshelf: the
// shelf candle's flame and the two fires seated on its boards. They take the
// shelf's pose from EnvShelfTip.cginc — the same call the shelf itself makes —
// and then do the two things a FLAME does that a rigid body does not:
//   * it does not turn with the object. Hot gas goes up whatever the wax is
//     doing, so the card is rigidly rotated (which keeps the wick welded to the
//     candle) and then bent BACK about its own base, weighted by height. At 88
//     degrees of shelf the plume is still within twenty of vertical.
//   * it lags, gutters and goes out. See GhvrTipFlameLife: the flame dies about
//     a quarter turn into the fall, is out for the whole of the ten seconds the
//     shelf lies on the floor, and lights itself again late in the recovery with
//     one flare. The LIGHT it casts takes the same number in the same frame
//     (EnvRoom's GhvrTipSlot), which is the only way a candle going out is not
//     also a pool of light that outlives it.
// The two SEATED FIRES ride rigidly and do not gutter: a burning bookshelf that
// falls over is still a burning bookshelf.
// ============================================================================
//
// ============== THE FIVE PAIRINGS THAT INVOLVE FIRE — ModBuild 145 ==========
// USER: "Schau dir auch jede mögliche Kombination der Elemente an ... So zB das
//  das Feuer der brennenden Bäume noch mehr Glut wirft und flackert wenn Wind
//  an ist etc."
//
// The composition rule, the reason there is one, and what each pair means are
// in EnvFire.cginc's PAIRINGS block. What this shader contributes:
//   FIRE+AIR   the rate (GhvrFireHz) and the depth (GhvrFireDepth) — the same
//              two functions the wash takes, so the flame and its light cannot
//              come apart under wind; a harder outward tear; the WHOLE fire
//              leaning, bed included; and detached pieces thrown three times as
//              far downwind and alive for three quarters of their cycle instead
//              of a third, which is "noch mehr Glut" made of one number.
//   FIRE+DARK  the bonfire stops taking the CANDLE's Dark response (which would
//              have dimmed it to 55% — the exact opposite of the pairing) and
//              gains a third instead.
//   FIRE+LIGHT a detached piece becomes SMOKE: pale, climbing far past where an
//              ember dies, spreading as it goes. Only here, because smoke is
//              only visible when something lights it.
//   FIRE+ICE   a detached piece becomes STEAM: white, low, slow, thickest at
//              the seat; and the flame's own ramp is entered further along,
//              because something is taking heat out of it.
//   FIRE+EARTH it SMOULDERS: shorter tongues, the ramp entered further along
//              still, dimmer, and pieces that barely lift before they sink.
// Every one of them is a product of two element strengths and is exactly zero
// unless both are up.
// ============================================================================
//
// ============== IT ZAPPELT — USER VERDICT, ModBuild 145 =====================
// "Das Feuer zappelt viel zu schnell und ist damit nicht sehr immersiv."
//
// The full diagnosis, the frequency/structure-size table it rests on and the
// three fixes that were REJECTED are in EnvFire.cginc's own ModBuild 145 block;
// read that first, because it is the argument and this is only the half of it
// that moves vertices. In one line: the clock is right and always was, but
// every band was pointed at the wrong SIZE of thing. The frequency of a
// structure in a fire is set by its size (f = 1.5/sqrt(D)), so
//
//     w.w 1.08 Hz -> 1.9 m      w.y 2.81 Hz -> 29 cm
//     w.x 4.60 Hz -> 11 cm      w.z 7.96 Hz -> 3.6 cm
//
// ...and this shader was moving a 40 cm TONGUE with w.x and w.z, and moving the
// 3 cm texture detail at 1-2 Hz. Exactly inverted. What changed here:
//
//   1. THE SURGE IS NOW TWO SURGES, split by height. A tongue's LENGTH is a
//      30-60 cm structure and now rides the slow trio (1.1/2.8/4.6 weighted
//      0.34/0.44/0.22); the fast pair survives only as a TIP FLUTTER, entering
//      on h*h so it is nothing at the tongue's foot and 30 % at its tip — which
//      is not a compromise but the actual shape of a flame, whose base is
//      pinned where it is fed and whose last hand's breadth whips. Measured at
//      the tip: mean frequency 6.0 -> 3.8 Hz, rms tip speed 3.33 -> 1.61 m/s,
//      rms acceleration 144 -> 61 m/s^2, and the excursion is UNCHANGED at
//      +-17.8 cm. It moves exactly as far as it did, in half the hurry.
//   2. THE LATERAL WANDER STOPS BEING AN 8 Hz SHAKE. `p.x += w.z * wob` moved
//      the whole card 10.4 cm sideways at 8 Hz — 3.6 m/s, 183 m/s^2, sixteen
//      direction reversals a second, on a card 37 cm long. It is the single
//      loudest jitter in the shader and the most likely literal referent of the
//      word "zappelt". The body of the tongue now wanders on the 2.8/1.1 pair
//      (and on both axes, out of phase, so it circles instead of shivering
//      along one axis), and the fast band stays as a h^4 wrinkle worth 2.5 cm
//      at the very tip.
//   3. THE FAST CONTENT IS NOT DELETED, IT IS MOVED TO WHERE IT LIVES. The
//      fragment's UV wobble is the one term in this shader that displaces
//      TEXTURE detail — holes and torn edges a few centimetres across, i.e.
//      exactly the 3.6 cm structure w.z names — and it was running at 2.1 Hz.
//      The bonfire path now adds a small ripple on the fire's own w.z band,
//      travelling up the card. So the fire keeps its 8 Hz life; it spends it on
//      the scale where 8 Hz is what a fire does, and where the amplitude is
//      millimetres and cannot read as a twitch.
//   4. A CARD'S SHAPE AND ITS BRIGHTNESS ARE NOW THE SAME EVENT. The flicker
//      call was phased with `v.color.r * 6.2831853` while the surge was phased
//      with `v.color.r` — a leftover from a radians formulation, and since the
//      wave argument is in CYCLES it meant every card's glow was uncorrelated
//      with its own leap. Two independent random signals per card is twice the
//      visual noise of one and says nothing more. Same phase now.
// Nothing here changes a rate, a bake constant, or one instruction of the
// CANDLE path: every edit is inside `if (_Bonfire > 0.5)`.
// ============================================================================
//
// ============ THE STRICHE, AND THE RULING — USER VERDICT, ModBuild 147 ======
// "a) Es sitzt nicht direkt auf den assets, schwebt daneben oder darüber.
//  b) Es sind mehrere sichtbare 'Striche' auf den assets drauf.
//  c) Es flackert überhaupt nicht natürlich."
//
// (b) is this shader's, and feuer1.jpg names the fault exactly: the flames are
// visible as X SHAPES — two crossed quads per card, each seen so nearly EDGE-ON
// that its projection is a narrow bright spindle. Sixty centimetres of torn
// flame sprite squeezed into a two-centimetre-wide stroke is a stroke, whatever
// is painted on it, and once the eye has found one it finds all of them.
//
// ---------------------------------------------------------------------------
// THE TENSION, STATED, BECAUSE IT IS THE REASON THIS WAS NEVER FIXED.
//
// STANDING USER RULING: nothing may re-orient with head movement — "ich will
// zB keine Nebeleffekte die sich mit dem Kopf mitbewegen wenn man ihn
// schüttelt". That is why this fire is world-anchored cross-quads and not
// camera-facing billboards; and a world-anchored quad seen along its own plane
// IS a stroke. Every stock fire package solves this by billboarding, including
// the Vefects one the art comes from, which is precisely why theirs has no
// Striche and ours does. The two constraints are in direct conflict and one of
// them has to give.
//
// WHAT WAS CHOSEN, and it gives up neither: THE GEOMETRY NEVER MOVES; A CARD'S
// OPACITY FALLS TO ZERO AS THE VIEW APPROACHES ITS PLANE.
//
//     face = |dot(cardNormal, viewDir)|            (a card's own normal is
//                                                   horizontal and fixed)
//     energy *= smoothstep(_FaceFade.x, _FaceFade.y, face)
//
// _FaceFade is (cos 75 deg, cos 38 deg) = (0.259, 0.788): a card facing within
// 38 degrees of you is at full strength, one past 75 is gone, and in between it
// is a smooth ramp. Nothing rotates, nothing translates, the fire's silhouette,
// position and extent are identical from every direction — only WHICH cards
// carry it changes, and it changes continuously.
//
// WHY THAT IS NOT THE THING THE RULING FORBIDS. What he objected to is an
// object that visibly TURNS as you turn: a shape that keeps its face to you is
// a shape that is following you, and the eye reads that as the world moving.
// A card whose opacity is a function of angle has no orientation to give away —
// it does not turn, it dims, and it dims over a 37-degree band, so a head shake
// of a few degrees changes it by a few per cent. The one experiment this
// project has that speaks to it is the parked wall-fade stereo rivalry, and the
// difference is the same difference: that was a DITHERED DISSOLVE with a hard
// per-pixel threshold, and a hard threshold at slightly different angles in two
// eyes is two different pictures. This is a continuous alpha over a wide band.
// At a 63 mm IPD and one metre the two eyes differ by 3.6 degrees of view
// angle, which across a 37-degree ramp is at most 10 % of alpha on the steepest
// part of the smoothstep and under 4 % over most of it — well below the
// threshold at which the two eyes disagree about anything.
//
// AND THE ROSETTE, which is the half that makes it work. With two quads at 90
// degrees, an azimuth exists (45 deg to both) at which BOTH are 45 degrees off
// face — both survive the fade, both are 71 % wide, and you are looking at an X
// again. So the mesh now builds THREE quads at 60 degrees per card, each two
// thirds as wide (EnvRoomBuilder.FireMesh, QuadYaws). Then:
//   * whatever the azimuth, at least one quad is within 30 degrees of facing
//     you and is at FULL strength;
//   * the worst-placed quad is within 30 degrees of edge-on and is DARK;
//   * the sum of (fade x projected width) over the three varies by 12 % over a
//     full turn, against 41 % for the two-quad cross with no fade — i.e. the
//     fire does not pulse as the player walks round it, which is the failure
//     mode a fade alone would have introduced;
//   * three quads at two thirds the width paint the same screen area as two at
//     full width (mean of |cos| over the rosette: 1.86 x 0.667 = 1.24 against
//     1.27), so the fill budget is unchanged and only the vertex count moves.
// Every quad that is visible at all is now seen at 30-49 degrees off face-on.
// There is no viewing direction from which a spindle exists.
//
// REJECTED: yaw-only billboarding (a flame that spins about its own axis as you
// turn your head is the ruling's own example, in the one direction the eye is
// most sensitive to rotation); and simply adding more, smaller cross-quads,
// which raises the fill budget by exactly the factor it lowers the legibility.
// ============================================================================
//
// ============ IT DOES NOT FLICKER — USER VERDICT, ModBuild 147 ==============
// "c) Es flackert überhaupt nicht natürlich."
//
// Two rounds have now worked on the MOTION and he says the same thing, and the
// reason is that both rounds moved GEOMETRY. A fire's life is not in where its
// tongues are, it is in its mask dissolving: Vefects' eighteen prefabs — the
// pack he supplied — have startSpeed 0, no velocity module and no flipbook, so
// NOTHING IN THEIR FIRE MOVES AT ALL, and all of it is a noise field scrolling
// through the alpha in their fragment shader. Our atlas took their art and
// baked that field in as one frozen instant, which gives their silhouette with
// none of their life. Every card was a rigid stencil being stretched.
//
// So the field is animated now, out of the RGB CHANNELS OF THE SAME ATLAS
// (fire_atlas_pipeline.py packs it there: R one tile over the image, G four,
// both seamless, both rank-uniform, both inverted so the noise's thin ridges
// are dark rifts and not bright scratches). One extra tex2D, no second
// sampler, no second texture, and two octaves — coarse and fine, and because a
// scroll in uv moves the 4x octave through four times as many of its own
// periods, the fine one boils four times faster as well.
//
//     c.a = saturate(saturate(c.a * boost) - field * amount * heightWeight)
//
// The height weight is (0.22 + 0.78 * uv.y): a flame is anchored where it is
// fed and its seat may not dissolve. The boost stays NEAR 1 on purpose — at
// 1.72 the mask saturates over its whole body, the card becomes a picture of
// the noise, and the outline stops moving; the whole value of the technique is
// that the SILHOUETTE tears. Measured over a scroll cycle the outline's
// frame-to-frame change goes from nil to plainly visible, and the drawn energy
// per cell is held at the ModBuild 147 level by the ArtE table, which
// fire_atlas_pipeline.py now computes from a simulation of this exact
// expression rather than from the mask alone.
//
// WHAT IT REPLACES: the ModBuild 145 `rip` UV wobble and the candle-family
// `wob`, both of which were the previous round's attempt at the same idea with
// no texture to do it with. They displaced the whole sprite by a couple of
// centimetres; this dissolves it. Both are gone from the bonfire path (the
// candles keep `wob`, which is theirs and is correct for a 2 cm teardrop), so
// the fragment is one tap heavier and four transcendentals lighter.
//
// AND THE PER-CARD TEMPERATURE TIER. COLOR.b is how far out of the seat a card
// stands, and it now enters the ramp's `cool` argument: a tongue at the rim of
// the fire is entered further along the three-stop ramp than one in the middle,
// so the fire is white-hot at its core and dark red at its edges IN PLAN as
// well as in height. One MAD, and it is most of what stops thirty cards at the
// same height reading as one flat orange sheet.
// ============================================================================
//
// ============ ES ZIEHT FÄDEN — USER VERDICT, ModBuild 149 ===================
// "Feuer zieht nun solche Fäden bis ganz weit nach oben, das sieht nicht
//  realistisch aus."  (feuer3.jpg, feuer4.jpg)
// "Wenn Wind an ist sind die Strahlen vom Feuer extrem lang."  (feuer5.jpg)
//
// One fault, four multiplies, and none of them was authored as a thread. What
// makes a thread is that every one of them acts on the SAME AXIS:
//
//   1. THE CARD. A tongue's drawn width is tw x ArtW x QuadWidthK, which put it
//      at 0.31-0.49 of its own height — 2:1 to 3.2:1 before anything moved.
//   2. THE LICK. `p.y *= tall * lick` scales the HEIGHT and not the width, so
//      up to another 1.48x on the tall axis only: 4.8:1 at the worst card.
//   3. THE EROSION, and this is the one nobody had seen. The field is sampled
//      in CARD UV at (0.85, 0.72), i.e. near-square in UV — but UV is not
//      square in METRES on a card 0.39 as wide as it is tall, so a field cell
//      measured 0.46 x 1.39 card-heights. The holes it cut were themselves 3:1
//      vertical. 3.2 x 1.48 x 3 is a filament twelve times longer than it is
//      wide, and the temperature ramp paints its top dark red, which is
//      exactly the thing in feuer3.jpg.
//   4. AND NOTHING KNEW HOW HIGH THE WHOLE THING WENT. Every term is a
//      fraction of the fire's height and they compose multiplicatively; a puff
//      could be at 2.9 fire-heights with no element up and past 4 with two.
//
// All four are fixed at their own scale and none of them by a clamp on the
// symptom: the card is shorter and broader (EnvRoomBuilder.FireMesh), the lick
// is 0.34, the field is re-tiled to be square IN METRES (Erode* there too), and
// there is now ONE soft ceiling in the fire's own frame (GHVR_FIRE_SOFT/CAP in
// EnvFire.cginc, applied once, below). The fill budget goes DOWN 8 %.
//
// THE WIND HALF IS THE SAME STORY WITH FOUR MORE TERMS, and the arithmetic is
// worse: a detached piece drifted `rise x age^2 x (0.35 + 3.10 x air)`, which
// on the burning snag is 3.4 m downwind while climbing 2.3 — a four-metre
// trajectory shed by a fire one metre tall, with the age^2 piling the
// population up at the far end so that what is left behind is a thin evenly
// spaced line. That is a ray, and it is feuer5.jpg. The drift is now linear in
// age at 1.15x rise, the climb 1.45x instead of 2.30x, the flame gains 6 % of
// height under Air instead of 25 %, and the lean goes 0.55 -> 0.85 so the wind
// is spent laying the fire over rather than stretching it.
//
// ...AND THE RATE TERM IS GONE ENTIRELY, which is a rule and not a taste: an
// element strength multiplies an AMPLITUDE, never a frequency. GhvrFireHz was
// taking the clock the user accepted in ModBuild 145 (mean 3.13 Hz) back up to
// 4.85 Hz the moment Air came up — i.e. a windy fire was faster than the fire
// he had already rejected as "zappelt viel zu schnell". The whole argument is
// in EnvFire.cginc at GhvrFireHz.
// ============================================================================
Shader "GloomhavenVR/EnvFlame"
{
    Properties
    {
        _MainTex ("Flame sprite", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Sway ("Sway amount", Range(0,0.2)) = 0.05
        _Flicker ("Brightness flicker", Range(0,1)) = 0.35
        _Phase ("Phase offset", Float) = 0
        // MUST equal the rate of the EnvRoom light slot this flame belongs to
        // (EnvRoom: slot0 1.00, slot1 0.83, slot2 1.19) — otherwise the flame
        // you see and the light it casts drift apart. A gated FIRE does not use
        // this at all: its rate is _FireHz and its light is the fire wash, which
        // is written with that same number (see _FireHz).
        _Rate ("Flicker rate (match the light slot)", Float) = 1
        // The DRAFT. Deliberately UNPHASED and slow, so every flame in the room
        // leans the same way at the same moment: that is what reads as one
        // draught through the cellar rather than three independent candles.
        _Gust ("Draft amount", Range(0,0.3)) = 0
        _GustDir ("Draft direction (OBJECT space XZ)", Vector) = (1,0,0,0)

        // ---- ELEMENT ART: AIR, and why this is a per-material number --------
        // USER VERDICT, ModBuild 142: "Im Keller sollte es noch mehr wie ein
        // Windzug wirken der insbesondere aus dem Fenster kommt." Air used to
        // multiply every flame's draught by the same 3.4, which is a room that
        // is uniformly windy — the opposite of a draught with a SOURCE. The
        // builder now writes this per flame from its distance to the window
        // opening, so under Air the flame nearest the aperture is laid almost
        // flat and the one in the far corner barely stirs, and the eye can find
        // the window by watching the fire. 3.4 is the old constant, so a
        // material that does not set it is unchanged.
        _AirGust ("Air: draught multiplier at this flame", Float) = 3.4

        // ---- REAL FIRE ------------------------------------------------------
        _Bonfire ("Bonfire mode (0 = candle, 1 = burning object)", Range(0,1)) = 0
        _FireGate ("Exists only under the Fire infusion", Range(0,1)) = 0
        _Lick ("Bonfire: tongue surge amount", Range(0,1.5)) = 0.55
        _Flare ("Bonfire: outward tear of the outer tongues", Range(0,0.5)) = 0.10
        // THE ONE RATE. In HERTZ, and it is the number the wash on the wall is
        // written with as well (EnvFire.cginc / _FireRate), so the standing rule
        // — a flame and the light it casts share a rate — is now one constant in
        // two properties rather than two families of sines that happen to agree.
        _FireHz ("Bonfire: turbulence rate (Hz) — SHARED with the fire wash", Float) = 4.6
        _PuffHz ("Bonfire: detachment rate (puffs per second per card)", Float) = 0.55
        // THE HEIGHT OF THE WHOLE FIRE, in the mesh's own units. The temperature
        // ramp is a property of the FIRE and not of a card: a 12 cm bed card
        // whose own uv.y ran the full ramp would be dark red at its top, which
        // is exactly the wrong end of the gradient for the hottest thing in the
        // frame. Everything is measured against this instead.
        _FireH ("Bonfire: total fire height (object units)", Float) = 1
        _BaseCol ("Bonfire: colour in the seat (white-blue)", Color) = (1.45,1.28,1.10,1)
        _CoreCol ("Bonfire: colour through the body", Color) = (1.30,0.72,0.26,1)
        _TipCol ("Bonfire: colour where the tongues tear off", Color) = (0.62,0.13,0.03,1)
        // What a detached piece BECOMES under the two pairings that change what
        // it is (see the PAIRINGS block below). Neither is ever reached unless
        // both of its elements are up, so both are inert by construction.
        _SmokeCol ("Fire+Light: moonlit smoke", Color) = (0.60,0.66,0.78,1)
        _SteamCol ("Fire+Ice: steam", Color) = (0.66,0.74,0.86,1)

        // ---- THE STRICHE FIX (see the block above) --------------------------
        // (cos of the angle at which a card is fully GONE, cos of the angle at
        // which it is at FULL strength). The default is (-1, 0) and NOT (0, 0):
        // it has to be a pair that makes smoothstep return exactly 1 for every
        // face in [0,1], so a material that sets no fade is the shipped shader
        // — and smoothstep(0,0,x) is (x-0)/(0-0), i.e. a NaN, not a 1. That is
        // the whole reason this default is a strange-looking number.
        _FaceFade ("Bonfire: card face fade (cos out, cos in)", Vector) = (-1,0,0,0)
        // ...and how much a card's DISTANCE OUT OF THE SEAT (COLOR.b) cools it.
        // 0 = the shipped ramp, which was a function of height alone.
        _Tier ("Bonfire: outer tongues are cooler", Range(0,1)) = 0

        // ---- THE ANIMATED EROSION (see the block above) ---------------------
        // The field lives in _MainTex's own RGB — R the coarse octave, G the
        // fine one — so there is no second sampler and no second texture.
        // (tile across a card, tile up a card, field periods per FIRE cycle,
        //  the pre-boost). All zero is a hard off: with .w at 0 the mask is
        // multiplied by nothing and the branch below is skipped outright.
        _ErodeParams ("Bonfire: erosion (tileU, tileV, scroll/cycle, boost)", Vector) = (0,0,0,0)
        // (how deep it cuts at the tip, its share at the FOOT, the fine
        //  octave's share, unused). See fire_atlas_pipeline.py, whose
        //  ERODE_* constants are these four and which derives the energy
        //  compensation from a simulation of exactly this expression.
        _ErodeMix ("Bonfire: erosion (amount, base share, fine share, -)", Vector) = (0,0,0,0)

        // ---- SHELF RIDERS (EnvShelfTip.cginc owns all five; see the block
        // above for what a flame does with them that a rigid body does not).
        // All zero = "this flame is not standing on the bookshelf", which is
        // every flame in both rooms except three.
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, how much the flame refuses", Vector) = (0,-1,0,0)
    }
    // ELEMENT ART (EnvElement.cginc). The flame is the cellar's Fire, Air, Light
    // and Dark all at once, and it is the one object in the room that can show
    // all four without a single new triangle:
    //   FIRE  the flame flares — taller, brighter, whiter at the core.
    //   AIR   the AUTHORED draught strengthens: the same _GustDir it already
    //         leans along, several times over, so the room's one draught becomes
    //         a wind. Nothing new to explain; the player has already seen it.
    //   LIGHT it is a SOURCE, so Light drives it (see the split).
    //   DARK  the candles duck: shorter and dimmer, but NOT out. Under Light+Dark
    //         both apply and Light wins on the flame while Dark wins on the room
    //         around it — which is the split, told on one candle.
    // Ice deliberately does NOTHING here. The design's Fire+Ice mixture is
    // "embers rising through falling snow"; an Ice that shrank the flame would
    // cancel Fire's flare and turn a mixture into an average, which the brief
    // forbids.
    SubShader
    {
        Tags { "Queue"="Transparent+15" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"
            // The fire's shared numbers (the waveform, the ramp, the card kinds)
            // and — through it — the tipping shelf's pose.
            #include "EnvFire.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint, _BaseCol, _CoreCol, _TipCol, _SmokeCol, _SteamCol;
            float _Sway, _Flicker, _Phase, _Rate, _Gust, _AirGust;
            float _Bonfire, _FireGate, _Lick, _Flare;
            float _FireHz, _PuffHz, _FireH, _Tier;
            float4 _GustDir, _FaceFade, _ErodeParams, _ErodeMix;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            // ATLAS GEOMETRY. 2x2 cells; mirrored in BuildEnvironments
            // (FireAtlasCols/Rows, FireTile) — change one, change both. The inset
            // is what stops a card's own bilinear tap reaching into its
            // neighbour's cell at the mip levels a fire is seen at across a room.
            #define GHVR_FIRE_COLS  2.0
            #define GHVR_FIRE_CELL  0.5
            #define GHVR_FIRE_INSET 0.013

            // COLOR and UV1 are the per-card streams and are read ONLY inside the
            // `_Bonfire` branch (a uniform, so the candles never touch them). The
            // candle mesh has neither channel; Unity supplies white for a missing
            // colour stream and zero for a missing texcoord, and neither is used.
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;   // 0..1 up and across THIS card, always
                float4 fp : TEXCOORD1;   // bonfire: cell, kind, puff rise, puff cycle
                fixed4 color : COLOR;
                // THE CARD'S OWN PLANE NORMAL, and it is load-bearing rather
                // than decorative: the face fade multiplies a card's energy by
                // |dot(N, view)|, so a normal that pointed the wrong way would
                // hide exactly the cards that face the player and keep exactly
                // the spindles this round exists to remove. EnvRoomBuilder
                // writes it explicitly (never RecalculateNormals) and asserts
                // it — see FireMesh's WINDING AND NORMAL GATE. Read only inside
                // `_Bonfire > 0.5`; the candle meshes' own normals are ignored.
                float3 normal : NORMAL;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed fl : TEXCOORD1;
                // x = the Fire channel (the candle path's whitening),
                // y = bonfire: COLOR.b, how far out of the seat this card
                //     stands — the per-card temperature tier,
                // z = bonfire: this card's own OFFSET INTO THE EROSION FIELD
                //     across u, so two cards at the same height do not
                //     dissolve in step. A float3 in a register that was
                //     carrying one fixed.
                float3 fire : TEXCOORD2;
                // bonfire: x = height in the FIRE's frame (0 at the seat),
                //          y = a detached puff's age 0..1, z = its cell index,
                //          w = its alpha
                float4 fx : TEXCOORD3;
                // ...and WHAT a detached piece has turned into, which is the one
                // thing the pairings change about a card rather than about a
                // number: x = smoke (Fire+Light), y = steam (Fire+Ice),
                // z = smoulder (Fire+Earth). All exactly 0 unless both of the
                // pair's elements are up. See the PAIRINGS block.
                //
                // ...and w = THIS CARD'S PLACE IN THE EROSION SCROLL, 0..1 —
                // ModBuild 148. It was the fast band's phase, for the `rip` UV
                // wobble that the erosion replaces; it is the same quantity in
                // the same register doing the same job one scale down, and
                // every word of the note below still applies to it:
                //   * it is FRAC'D in the vertex shader. A raw clock through an
                //     interpolator grows without bound and a mobile GPU is free
                //     to carry a varying at half precision, where t = 500 s
                //     quantises to steps of 4; GhvrWave4 takes frac() of its
                //     argument anyway, so pre-wrapping is exact, not a
                //     rounding. (Same reason the puff's `age` is a frac.)
                //   * it is CONSTANT over the card — all eight vertices of a
                //     cross carry one COLOR.r — so it interpolates exactly and
                //     the wrap can never land inside a triangle.
                //   * it costs nothing: TEXCOORD4 was a float3 in a register
                //     four floats wide.
                float4 pr : TEXCOORD4;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float t = _Time.y + _GhvrTimeOfs;
                float ft = t * _Rate;

                // ---- ELEMENT ART: the four modifiers, identity when nothing is up
                GhvrElem e = GhvrElems();

                // REAL FIRE — the gate. A burning crate exists because the Fire
                // infusion is up and for no other reason, so with Fire down the
                // whole object is collapsed to a point: two zero-area triangles
                // per card, nothing rasterised, and the room is bit-for-bit the
                // room that was tuned without it. This is the FIRST thing the
                // shader does, before any of the arithmetic below.
                float gate = 1.0;
                if (_FireGate > 0.5)
                {
                    gate = saturate(e.fire);
                    if (gate <= 0.0)
                    {
                        o.pos = float4(0, 0, 0, 1);
                        o.uv = float2(0, 0); o.fl = 0;
                        o.fire = float3(0, 0, 0);
                        o.fx = float4(0, 0, 0, 0);
                        o.pr = float4(0, 0, 0, 0);
                        return o;
                    }
                }

                // THE FIVE PAIRINGS THAT INVOLVE FIRE — see the block in
                // EnvFire.cginc for the composition rule. Every field is a
                // product of two element strengths and is therefore exactly 0
                // unless both are up, which is what makes the six single-element
                // states below the ones that were tuned.
                GhvrFirePair pair = GhvrFirePairs(e);
                float gustMul = 1.0, swayMul = 1.0, tall = 1.0, bright = 1.0;
                if (e.live > 0.0)
                {
                    // _AirGust is per material and comes from the flame's own
                    // distance to the window (see the property). The draught has
                    // a source, so its strength has a gradient.
                    gustMul = 1.0 + _AirGust * e.air;
                    swayMul = 1.0 + 1.3 * e.air;
                    // a flame laid over by a draught is also LONGER, and a
                    // ducking one is shorter: one number carries both
                    // ...and the DARK term is switched off INDOORS. See the block on
                    // `bright` below: the user's candle ruling covers the whole candle,
                    // and a flame that ducks to 60% of its height under Dark is Dark
                    // influencing the candle just as plainly as a dimmer one would be.
                    // The FIRE and AIR terms stay in both rooms — the flare under a Fire
                    // infusion is the ModBuild 142 design he has never objected to, and
                    // the lean under Air is the draught from the window he asked FOR.
                    // ...and the AIR term is 0.06 and not 0.25 — ModBuild 149,
                    // "Wenn Wind an ist sind die Strahlen vom Feuer extrem
                    // lang". A flame in a draught is LAID OVER, not grown: its
                    // plume bends downwind and its tip is torn off sooner, and
                    // the one thing it does not do is stand a quarter taller.
                    // 0.25 was a quarter of a metre of extra reach on the snag,
                    // added on top of a lick of up to 1.34 and a card that is
                    // already the tallest thing this shader draws, and it was
                    // the cheapest of the four terms that made feuer5's rays
                    // long. The energy is in the LEAN instead (see the gust
                    // term below, 0.55 -> 0.85) and in the depth (EnvFire).
                    tall = max(1.0 + 0.55 * e.fire + 0.06 * e.air
                                   - 0.40 * e.dark * (1.0 - GhvrIndoor()), 0.05);
                    // THE FIRE TERM IS FOR CANDLES ONLY. `1.05 * e.fire` is "the
                    // candles flare while the room is infused", and on a bonfire —
                    // which exists ONLY under that infusion — it is not a response
                    // to anything, it is a hidden constant multiplier of 2.05 on
                    // top of every energy number in the mesh. It cost this round a
                    // bake: with it, thirty overlapping additive cards clipped to
                    // white over their whole area and the fire's own card edges
                    // showed as hard white parallelograms.
                    // ...AND THE CANDLE FLAME ITSELF IS UNTOUCHABLE INDOORS. Two
                    // standing rulings, both verbatim and both about the cellar:
                    //   "anstatt die Kerzenscheine, die sollte identisch beiben."
                    //   "Auch bei Dunkelheit sollte es keinen Einfluss auf den
                    //    Kerzenschein haben."
                    // ModBuild 146's shading pass enforced that centrally for every
                    // reader of the SOURCE gain (GhvrSrcGain/GhvrSrcHard are the exact
                    // identity indoors now), which fixed the candle's POOL, its halo,
                    // its reflection in the puddle and the shelf it stands on — and left
                    // this line, the flame SPRITE itself, still brightening 1.55x under
                    // Light and dimming to 0.55 under Dark. The one part of a candle you
                    // actually look at was the one part still moving. Multiplying by
                    // (1 - GhvrIndoor()) is exact at both ends: outdoors it is the
                    // shipped line unchanged, indoors the two terms vanish rather than
                    // being scaled small.
                    // NOT gated: the fire term. "Die Kerzen flackern auf, wenn der Raum
                    // infundiert ist" is the ModBuild 142 design and is a response to
                    // FIRE, which no ruling touches.
                    bright = max(1.0 + (_Bonfire > 0.5 ? 0.0 : 1.05 * e.fire)
                                     + (0.55 * e.light - 0.45 * e.dark) * (1.0 - GhvrIndoor()),
                                 0.0);
                    if (_Bonfire > 0.5)
                    {
                        // A BONFIRE IS NOT A CANDLE UNDER LIGHT AND DARK. The
                        // candle line above is the ModBuild 142 design ("the
                        // candles duck under Dark, they lift under Light"), and
                        // applied to a burning crate it says the exact opposite
                        // of the two pairings: FIRE+DARK is "the fire is the only
                        // light left" and would have dimmed it to 55%, FIRE+LIGHT
                        // is "it gains a plume, not a boost". So the bonfire path
                        // replaces both with its own pair terms.
                        bright = max(1.0 + 0.34 * pair.dark - 0.30 * pair.earth
                                         - 0.10 * pair.light, 0.0);
                    }
                }
                // .y and .z are filled in the bonfire branch below; the candle
                // path reads neither.
                o.fire = float3(e.fire, 0, 0);

                // sway grows with height (uv.y=0 at flame base) — the tip dances
                float h = v.uv.y;
                float sway = (sin(ft * 5.7 + _Phase) * 0.6 + sin(ft * 9.3 + 1.3 + _Phase) * 0.4)
                             * _Sway * swayMul * h * h;
                // the draft: slow, shared, unphased (see _Gust)
                float g = sin(t * 0.37) * 0.62 + sin(t * 0.83 + 1.1) * 0.38;
                float4 p = v.vertex;

                // ---- FIRE REAL: what a crowd of tongues does that one does not
                float lick = 1.0, lickE = 1.0, age = 0.0, cardA = 1.0, cell = 0.0;
                float3 pr = float3(0, 0, 0);
                float texPh = 0.0;
                if (_Bonfire > 0.5)
                {
                    cell = v.fp.x;
                    float kind = v.fp.y;
                    float tp = v.color.r;                 // this card's own turn
                    // FIRE+AIR — the rate, through the SAME function the wash
                    // takes it through (EnvFire.cginc). A fire in a draught turns
                    // over faster; the pool it throws turns over with it.
                    float bt = t * GhvrFireHz(pair, _FireHz);   // CYCLES, not radians

                    // FOUR BANDS OFF ONE CALL, and the numbers are the point: at
                    // the shipped 4.6 Hz these are 4.6 / 2.8 / 8.0 / 1.1 Hz. The
                    // ratios live in EnvFire.cginc next to the table that says
                    // which SIZE of structure each of them is the frequency of;
                    // this used to be a hand-kept copy of those four arguments.
                    float4 w = GhvrFireBands(bt, tp);
                    // the cards do not surge TOGETHER — each one is on its own
                    // phase, so the silhouette of the fire is never the same
                    // twice. That, and not brightness, is what says "burning" at
                    // two to five metres.
                    //
                    // ---- ModBuild 145, "das Feuer zappelt viel zu schnell" ----
                    // The surge is the tongue's LENGTH, i.e. it moves the whole
                    // 30-60 cm structure, and it used to be 0.58*w.x + 0.42*w.z —
                    // the 11 cm band and the 3.6 cm band, both several times too
                    // fast for the thing they were moving, with nothing at all in
                    // the 1.4-2.8 Hz where a fire this size actually puffs.
                    //
                    // SLOW is that structure's own spectrum: peaked at the
                    // puffing band (w.y, 2.8 Hz), with the swell under it (w.w)
                    // and a roll-off above (w.x). Weights sum to 1.
                    float slow = 0.34 * w.w + 0.44 * w.y + 0.22 * w.x;
                    // FAST is what is left of the old pair, and it is not thrown
                    // away — a flame's last hand's breadth really does whip at
                    // several hertz. It sums to 1 as well.
                    float fast = 0.45 * w.x + 0.55 * w.z;
                    // ...and it enters BY HEIGHT UP THE CARD. h*h is zero at the
                    // foot, which is where the tongue is anchored in the bed and
                    // physically cannot flick, and 30 % at the tip, which is the
                    // part that has left the fuel behind. This is the whole of
                    // the fix: not less fast motion, fast motion CONFINED TO THE
                    // SMALL END of a big structure.
                    //
                    // The pair is MIXED and not summed, so |surge| <= 1 at every
                    // height and _Lick 0.48 keeps meaning exactly what it says on
                    // the bake — the excursion is unchanged at +-17.8 cm on a
                    // 37 cm tongue and only its spectrum has moved. A sum would
                    // have doubled the tongue's reach and cost the round a bake.
                    float mix = 0.30 * h * h;
                    float surge = (1.0 - mix) * slow + mix * fast;
                    lick = max(1.0 + _Lick * v.color.g * surge, 0.10);
                    // THE ENERGY TAKES THE SLOW HALF ONLY, and per CARD rather
                    // than per vertex. `lick` is now a function of height, and
                    // feeding that to o.fl would paint a brightness gradient up
                    // every card that fights the temperature ramp already there.
                    // Physically it is also the right half: a tongue that is
                    // stretching as a whole is being fed harder and glows; a tip
                    // that flutters is not burning any harder for it.
                    lickE = max(1.0 + _Lick * v.color.g * slow, 0.10);
                    // THE EROSION SCROLL, handed to the fragment so that the
                    // 8 Hz life the geometry gave up in ModBuild 145 comes back
                    // on the scale it belongs to — the dissolving of the mask
                    // itself (see the FLICKER block and the note on v2f.pr.w for
                    // why it is frac'd here rather than reconstructed there).
                    // It is in FIRE CYCLES, so it rides GhvrFireHz and a
                    // windblown fire boils faster along with everything else —
                    // the same one number reaching the flame, the wash and now
                    // the dissolve.
                    texPh = frac(bt * _ErodeParams.z + tp * 0.61);
                    // ...and the card's own place ACROSS the field. Without it
                    // every card at the same height dissolves in step, which is
                    // thirty cards sharing one animation and is the loudest way
                    // to make a stack of quads legible as a stack of quads.
                    o.fire.z = frac(tp * 7.31 + 0.17);
                    // ...and how far out of the seat it stands, for the ramp.
                    o.fire.y = v.color.b;
                    // a tongue standing at the RIM of the fire is torn outward and
                    // dies sooner; COLOR.b is how far out it stands. Without this
                    // the tongues all point straight up and the fire reads as a
                    // bush.
                    // FIRE+AIR: a tongue in a draught is torn outward harder and
                    // dies back sooner. Same term, more of it.
                    float tear = _Flare * (1.0 + 1.9 * pair.air) * v.color.b * (0.55 + 0.45 * w.y);
                    p.x += p.x * tear * h;
                    p.z += p.z * tear * h;
                    // FIRE+AIR: ...and the WHOLE fire leans, bed included. The
                    // bed is the one part that does not wander on its own (see
                    // COLOR.g), so without this a windblown fire is tongues
                    // leaning off a seat that is standing still — which is a fire
                    // in a room with a draught, not a fire being blown.
                    // ...and it leans on the SLOW pair. The whole fire is the
                    // largest structure in the picture (0.5-1.2 m across, i.e.
                    // 1.35-2.17 Hz by f = 1.5/sqrt(D)), so of the four bands it
                    // must take the two slowest; on w.x it was a metre of fire
                    // being shoved about at the rate of an 11 cm eddy.
                    // ============ AND IT LEANS BY HEIGHT IN THE **FIRE** ======
                    // ModBuild 149, and this is the fourth ray-maker — the one
                    // that is not a length at all but a SHEAR, which is why two
                    // rounds of shortening things did not remove it.
                    //
                    // `h` is uv.y, the height up THIS CARD, 0 at its own base.
                    // So every card was sheared over its OWN height: a detached
                    // puff 40 cm tall, sitting a metre and a half up, had its
                    // top displaced the full 0.55 m while its bottom did not
                    // move — a 54-degree skew on a card 30 cm wide, which
                    // rasterises as a thin diagonal shard with two straight
                    // edges. Every card in the fire got the same skew whatever
                    // its size, so the picture under wind was a fan of shards
                    // all leaning the same way. That is feuer5.jpg's other
                    // half, and the first ModBuild 149 bake (which raised this
                    // to 0.85 while fixing the drift) made it worse and showed
                    // it plainly: env_swamp_FireSnag_efair, the parallelograms
                    // over the trunk.
                    //
                    // A plume does not shear each parcel of gas about its own
                    // base — it displaces the WHOLE upper fire downwind. The
                    // gradient belongs to the FIRE and not to the card, so the
                    // weight is now height in the fire's own frame. Every card
                    // is then skewed by exactly as much as the fire is (35 deg
                    // at full Air, whatever the card's size or where it sits),
                    // a high puff translates almost rigidly instead of being
                    // sheared to a sliver, and the fire still bends over.
                    //
                    // 0.15 + 0.85 x is the bed keeping a little of it: "the
                    // WHOLE fire leans, bed included" is the ModBuild 145 note
                    // below and it is right — a bed at hFire = 0 would
                    // otherwise be the one part standing still under a gale.
                    float hFire = saturate(p.y / max(_FireH, 1e-3));
                    p.xz += _GustDir.xz * (0.85 * pair.air * (0.15 + 0.85 * hFire)
                                           * (0.6 + 0.4 * (0.6 * w.w + 0.4 * w.y)));
                    // ...and every tongue wanders on its OWN phase. _Sway is one
                    // number per material, so without this the whole fire leans
                    // as a single bush and the tongues stay in the rows the mesh
                    // put them in. Weighted by COLOR.g, so the BED (which barely
                    // surges) also barely wanders: a bed of embers that slid
                    // about would read as a puddle of light rather than as the
                    // seat of a fire.
                    //
                    // ---- ModBuild 145: THIS WAS THE LOUDEST "ZAPPELN" --------
                    // `p.x += w.z * wob` displaced the WHOLE card (h*h, so the
                    // entire upper two thirds of a 37 cm tongue) by up to 10.4 cm
                    // sideways at 7.96 Hz: 3.6 m/s rms, 183 m/s^2 rms, sixteen
                    // direction reversals a second. Nothing else in the shader
                    // came close, and a 3.6 cm band was driving a 40 cm object.
                    // Worse, X took w.z and Z took w.y, so the two axes ran at
                    // 8 Hz and 2.8 Hz and the card shivered along a LINE.
                    //
                    // The body of the tongue now takes the two slow bands on both
                    // axes with the weights swapped and one sign flipped, so the
                    // tip traces an ellipse rather than a line — one wave call,
                    // no extra cost, and a wander that reads as gas turning over
                    // rather than as a tremble. Each axis' weights still sum to
                    // 1, so the authored 10.4 cm is exactly preserved.
                    float wob = _Sway * 1.9 * v.color.g * h * h;
                    p.x += (0.62 * w.y + 0.38 * w.w) * wob;
                    p.z += (0.62 * w.w - 0.38 * w.y) * wob;
                    // ...and the fast pair survives as a WRINKLE at the very tip:
                    // h^4, so it is 6 % of itself at half height and worth 2.5 cm
                    // where the tongue is thinnest. That is a 3.6 cm structure
                    // moving 2.5 cm at 8 Hz, which is precisely what the band is
                    // for. rms speed on X falls 3.64 -> 1.19 m/s and rms
                    // acceleration 183 -> 46 m/s^2, with only a tenth of the
                    // power left above 6 Hz and the tip's liveliness still
                    // visibly there.
                    float wrinkle = _Sway * 0.45 * v.color.g * h * h * h * h;
                    p.x += w.z * wrinkle;
                    p.z += w.x * wrinkle;
                    // The whole fire grows with the infusion: at the waning
                    // plateau it is a fire DYING BACK — smaller and lower — not
                    // the same fire turned down.
                    //
                    // sqrt, not the gate itself, and the first bake is why: with
                    // a linear gate the waning plateau (0.40, breathing
                    // 0.28..0.52) came out at 40% of the fire's energy and the
                    // previews of it showed a room with a few embers in it. A
                    // waning infusion is still an infusion and the user has to
                    // see it burning. sqrt(0.40) = 0.63 is a fire on its way out;
                    // it is still 0 at 0, so nothing pops in, and the curve is
                    // steepest near zero, which is where the ramp needs to be
                    // gentle.
                    gate = sqrt(gate);
                    tall *= 0.50 + 0.50 * gate;
                    // FIRE+EARTH — it SMOULDERS. Wet growth on burning wood does
                    // not flame cleanly: the tongues are shorter and the energy
                    // stays down in the glowing bed. `tall` is the parameter the
                    // single-element path already owns, so this is a modulation
                    // and not a new layer; the bed is unaffected because the bed
                    // is short already and the multiply is on the whole fire.
                    tall *= 1.0 - 0.42 * pair.earth;

                    // ---- THE PUFF, i.e. the top of the plume.
                    // A card that leaves the fire, rises, cools and goes out, on
                    // its own cycle, from its own place in that cycle. frac() of
                    // a clock is a pure function with no state and no birth
                    // event: at any instant the puffs of one fire are spread
                    // through their lives because UV1.w spreads them, and a
                    // player who looks away and back sees a different set.
                    //
                    // ============ "SIE SCHWEBEN UND GEHÖREN NICHT DAZU" ========
                    // USER, hardware, ModBuild 149, and it is the SECOND round
                    // in which he has rejected this population in his own words
                    // ("Feuerherde die über dem Baum schweben", then "Diese
                    // tanzenden Feuer die einfach darüber schweben mag ich nicht
                    // so wirklich weil sie erscheinen als ob sie schweben und
                    // nicht dazu gehören").
                    //
                    // He is right, and the reason is physics rather than taste.
                    // A parcel of gas that has genuinely left the luminous zone
                    // of a fire cools below incandescence within a few tens of
                    // centimetres: what detaches from a fire and stays visible
                    // is SMOKE, which is dark, and EMBERS, which are points. It
                    // is never a 40 cm sheet of flame-coloured light hanging in
                    // clear air a metre over the fuel — and this pass is
                    // ADDITIVE, so the one thing it cannot draw is the dark
                    // thing that is actually up there.
                    //
                    // The cue is not lost by taming it, because the cue is
                    // already carried twice: the SPARKS are the pieces that
                    // leave (his own verdict this round, verbatim: "Die Funken
                    // gefallen mir gut"), and the three pairings below are what
                    // a piece BECOMES. What this card goes back to being is the
                    // top of the plume — born inside the tongues, rising a
                    // third of a fire-height, and out before it can clear the
                    // flame body. See EnvRoomBuilder.FireMesh for the geometry
                    // half (birth height 0.30-0.55 -> 0.10-0.30 of the fire,
                    // rise 0.38-0.68 -> 0.20-0.36) and for where the energy the
                    // shorter life gives up is put back.
                    if (kind > 1.5)
                    {
                        // ======= A POPULATION WITH ONE PERIOD IS A LOOP ========
                        // USER, hardware, ModBuild 149: "Das Feuer zieht in
                        // einem Loop in eine Richtung, glitcht dann zurück und
                        // beginnt diesen Loop von vorne."
                        //
                        // MEASURED, not guessed. The preview harness renders a
                        // 60-frame series 33 ms apart (ENV_PREVIEW_FIRELOOP);
                        // differenced against its own first frame, the burning
                        // snag's flame crop has a sharp similarity minimum at
                        // t = 1.77-1.80 s — SSD 0.0017 against a series mean of
                        // 0.0055, a 3.2x dip — and 1 / _PuffHz = 1 / 0.55 =
                        // 1.818 s. The whole fire repeats at the puff period,
                        // because EVERY puff card in it ran at exactly that
                        // rate and only their PHASES were spread. Spreading
                        // phase makes the population look unsynchronised at any
                        // one instant; it does nothing at all to the period of
                        // the ensemble, and the period is what the eye finds
                        // when it watches for two seconds.
                        //
                        // On top of that each card's motion inside its cycle is
                        // MONOTONE — climb*age up and drift*age downwind — and
                        // then it teleports back to its birth point. That is
                        // "zieht in eine Richtung ... glitcht dann zurück ...
                        // von vorne", exactly, one card at a time.
                        //
                        // THE RULE, and it is the same one the erosion wrap
                        // obeys one screen down: A WRAPPING QUANTITY IS ONLY
                        // INVISIBLE IF ITS WRAP IS A PERIOD OF EVERYTHING THAT
                        // READS IT — and if a whole POPULATION shares one wrap,
                        // the population itself is the thing that reads it and
                        // the picture has that period whatever the phases are.
                        // So the rate is now per card as well: `tp` is this
                        // card's own turn (COLOR.r, already spread by Hash3),
                        // and 0.72..1.28 of the authored rate puts the cycles
                        // between 1.42 s and 2.53 s. The mean is unchanged, so
                        // "a fire sheds something about every quarter second"
                        // still holds; what is gone is the single number that
                        // every piece in the room was counting to.
                        age = frac(bt * (_PuffHz / max(_FireHz, 0.01))
                                      * (0.72 + 0.56 * tp) + v.fp.w);
                        // ---- WHAT A DETACHED PIECE IS, and it is the one thing
                        // the pairings change about a CARD rather than about a
                        // number. All three are products of two elements, so a
                        // fire with one element up sheds ordinary embers.
                        //   SMOKE  (Fire+Light) it is not burning any more, it is
                        //          the soot above the flame — and it is only
                        //          visible because there is a swollen moon to
                        //          light it. This is the pairing's whole content:
                        //          a fire next to a brighter moon that gains a
                        //          plume instead of merely looking weaker.
                        //   STEAM  (Fire+Ice) it is water, not soot: white, low,
                        //          slow, and thickest right at the seat where the
                        //          two are actually meeting.
                        //   SMOULDER (Fire+Earth) it barely leaves at all — a
                        //          dull red ember that lifts a hand's breadth and
                        //          sinks back.
                        pr = float3(pair.light, pair.ice, pair.earth);
                        // ...and each of them changes how LONG the piece lasts,
                        // which is the same alpha envelope the ember already has:
                        // smoke outlives an ember by a long way, steam a little,
                        // a smoulder hardly at all.
                        //
                        // ---- ModBuild 150: THE ENVELOPE IS THE WHOLE FIX -----
                        // The shipped pair was (fade in over 0..0.10, fade out
                        // over 0.30..1.00), i.e. a mean envelope of 0.60 spread
                        // over the ENTIRE cycle: the card was drawn, at some
                        // brightness, for every instant of its rise, and the
                        // last half of that rise is above the flame body. That
                        // is the hovering blob, in one number.
                        //
                        // `tEnd` is when the piece is GONE, and it is clamped
                        // under 1 on purpose: at age = 1 the card teleports
                        // back to its birth point, so anything still carrying
                        // alpha there is a visible reset — the second half of
                        // the user's "glitcht dann zurück". The fade-out starts
                        // at a fifth of that, so the piece is at full strength
                        // only while it is still inside the tongues and spends
                        // the rest of its short life going out.
                        //
                        // In still air (every pr = 0) that is 0.11..0.55, a
                        // mean envelope of 0.285 against 0.600 — and with the
                        // rise cut to 0.20-0.36 fire-heights in the mesh, the
                        // card's base has moved 0.20 x 0.55 = 0.11 of a
                        // fire-height by the time it is out. It cannot clear
                        // the flame it came from, which is the requirement.
                        float tEnd = min(0.55 + 0.42 * pr.x + 0.18 * pr.y
                                              - 0.12 * pr.z, 0.98);
                        cardA = smoothstep(0.0, 0.08, age)
                              * (1.0 - smoothstep(tEnd * 0.20, tEnd, age));
                        // FIRE+AIR: MORE GLUT, and it lives longer because it is
                        // being fed on the way. His own example, so it is the one
                        // that has to be unmistakable — the piece is visible over
                        // three quarters of its cycle instead of a third, which
                        // is the same thing as several times as many in the air.
                        //
                        // KEPT, and kept LONG, because a piece that is being
                        // thrown downwind at half a metre a second is not the
                        // thing he objected to: a hovering blob is one that
                        // does not appear to be going anywhere. What it may not
                        // do is still be drawn at age 1, so the fade now ends
                        // at 0.96 instead of at exactly 1.0 — a card that is
                        // cut off at the wrap is a pop whatever else is right.
                        cardA = max(cardA, smoothstep(0.0, 0.07, age)
                                           * (1.0 - smoothstep(0.42 + 0.30 * pair.air,
                                                               0.80 + 0.16 * pair.air, age))
                                           * saturate(pair.air * 1.6));
                    }
                }

                // the flame grows from its WICK: the mesh's origin is the wick, so
                // scaling y about it lengthens the flame instead of lifting it off
                // the candle (CrossQuadMesh puts uv.y=0 at y=0). For a bonfire the
                // origin is the seat of the fire and the same argument holds.
                p.y *= tall * lick;
                p.x += sway + _GustDir.x * _Gust * gustMul * g * h * h;
                p.z += sway * 0.6 + _GustDir.z * _Gust * gustMul * g * h * h;
                // ...and a detached puff travels, AFTER the fire's own growth: it
                // has already left, so it does not grow with what it left.
                if (_Bonfire > 0.5 && v.fp.y > 1.5)
                {
                    // HOW FAR IT GOES. Four terms on ONE parameter — the authored
                    // rise — rather than four behaviours:
                    //   AIR   thrown much further (his example: "noch mehr Glut")
                    //   LIGHT smoke climbs far past where an ember dies
                    //   ICE   steam is heavy and slow, and hangs at the seat
                    //   EARTH a smoulder barely lifts at all
                    // IT GROWS AS IT GOES, and that is what turns a scatter of
                    // embers into a PLUME. A smoke puff entrains air and swells;
                    // a steam puff does the same only more so and lower down; a
                    // smoulder's murk creeps. The scale is about the fire's own
                    // origin — which for a card that has already left is exactly
                    // right, because it enlarges the card AND lifts it in one
                    // multiply. (The card cannot be scaled about its own base:
                    // it does not know where that is, UV1 being full. The
                    // difference is that the plume climbs a little faster than
                    // authored, which is the direction a plume errs in anyway.)
                    float grow = 1.0 + 1.45 * pr.x * age + 0.95 * pr.y * age
                                     + 0.55 * pr.z * age;
                    p.y *= grow;
                    p.xz *= grow;
                    // ...and AIR is 0.45 and not 1.30 — ModBuild 149. See the
                    // drift below; the same argument governs both, and a piece
                    // that is thrown 2.3 fire-heights up is as much of a ray as
                    // one thrown three metres sideways.
                    float climb = v.fp.z * (1.0 + 0.45 * pair.air + 2.40 * pr.x
                                            - 0.55 * pr.y - 0.62 * pr.z);
                    p.y += climb * age;
                    // it drifts with the room's draught as it rises, which is what
                    // ties it to the same air the flames lean in — and under
                    // FIRE+AIR that drift becomes a visible EMBER TRAIL running
                    // downwind off whatever is burning, which in the wood is the
                    // thing the pairing is meant to be recognised by.
                    //
                    // ============ "DIE STRAHLEN SIND EXTREM LANG" ============
                    // USER, hardware, feuer5.jpg, ModBuild 149. This line was
                    // the loudest of the four Air terms and the arithmetic is
                    // not close: `rise` is up to 0.95 fire-heights, age^2 is 1
                    // at the end of the cycle, and 0.35 + 3.10 is 3.45 — so on
                    // the burning snag a detached piece travelled up to
                    // 0.95 x 1.05 x 3.45 = 3.4 METRES downwind while climbing
                    // another 2.3, i.e. a four-metre trajectory shed by a fire
                    // one metre tall. Nine times the still-air distance.
                    //
                    // Two things are wrong with it and both are fixed here:
                    //
                    //  1. THE COEFFICIENT. 3.10 -> 0.80, so the piece goes
                    //     1.15 x rise instead of 3.45 x rise: 1.15 m on the
                    //     snag, 0.48 m on a log fire. An ember off a real fire
                    //     in a breeze travels about its own fire's width before
                    //     it is out, and that is now what this says.
                    //  2. THE age^2. A quadratic in age is an ACCELERATION, so
                    //     the piece covers three quarters of its whole journey
                    //     in the last half of its life — the population is
                    //     therefore piled up at the far end of the trajectory
                    //     with a thin trail behind it, and a thin trail of
                    //     evenly spaced pieces along a straight line IS a ray,
                    //     which is what the screenshot shows. Linear in age
                    //     spreads the same pieces evenly along a shorter line,
                    //     which is a scatter of embers.
                    // Both halves of the pairing the user asked for survive
                    // untouched: the pieces are still visible over three
                    // quarters of their cycle instead of a third (cardA above,
                    // "noch mehr Glut"), and the fire still flickers harder
                    // (GhvrFireDepth). What is gone is the geometry.
                    float drift = v.fp.z * age * (0.35 + 0.80 * pair.air);
                    p.x += _GustDir.x * drift;
                    p.z += _GustDir.z * drift;
                }
                // ---- THE CEILING, and it is the only place anything in this
                // shader knows how tall the WHOLE fire has got. See
                // EnvFire.cginc's GHVR_FIRE_SOFT/CAP block for the arithmetic
                // that says a fire could reach four of its own heights and for
                // why this is a soft knee and not a clamp. Bonfires only: a
                // candle's mesh is its own height and has nothing to bound.
                if (_Bonfire > 0.5)
                {
                    float fh = max(_FireH, 1e-3);
                    float soft = fh * GHVR_FIRE_SOFT;
                    if (p.y > soft)
                    {
                        // p.y = soft + span * (1 - exp(-(p.y - soft)/span)):
                        // value `soft` and slope 1 at the join, asymptote at
                        // soft + span = CAP. One exp, on the ~15 % of vertices
                        // that are above the knee at all.
                        float span = fh * (GHVR_FIRE_CAP - GHVR_FIRE_SOFT);
                        p.y = soft + span * (1.0 - exp(-(p.y - soft) / span));
                    }
                }
                // HEIGHT IN THE FIRE'S OWN FRAME — the temperature ramp's input.
                // Taken after every displacement, so a surging tongue really does
                // redden as it stretches.
                o.fx = float4(saturate(p.y / max(_FireH, 1e-3)), age, cell, cardA);
                o.pr = float4(pr, texPh);   // .w = this card's place in the fast band

                // ---- SHELF RIDERS: is this flame standing on the bookshelf? ---
                GhvrTip tip = GhvrTipNow(t);
                float life = 1.0;
                if (tip.live > 0.5)
                {
                    // 1. THE BEND, about the flame's OWN base (the mesh origin is
                    //    the wick / the seat), against the shelf's rotation and
                    //    weighted by height. A flame goes up whatever the thing
                    //    holding it is doing; _TipUse.w is how much of the
                    //    rotation it refuses (0.8 for a candle, less for a bed of
                    //    fire lying on a board that is turning under it).
                    p.xyz = GhvrTipRot(p.xyz, float3(0, 0, 0), tip.axis,
                                       -tip.ang * _TipUse.w * h * h);
                    // 2. THE RIGID PART, which is what keeps the wick welded to
                    //    the candle and the candle standing on the shelf.
                    p.xyz = GhvrTipRot(p.xyz, tip.pivot, tip.axis, tip.ang);
                    // 3. THE LAG. Hot gas has momentum the wax does not, so the
                    //    plume trails the swing instead of arriving with it.
                    p.xyz += GhvrTipLag(tip, p.xyz, h);

                    if (_TipUse.z > 0.5)
                    {
                        life = GhvrTipFlameLife(tip);
                        // A GUTTERING FLAME IS SHORT AND UNEVEN. The length goes
                        // with the life so the flame shrinks into the wick rather
                        // than fading as a full-size ghost of itself.
                        p.y *= 0.30 + 0.70 * life;
                        bright *= life * GhvrTipRelightFlare(tip, life);
                        // ...and while the shelf is actually swinging it flutters
                        // hard, four times faster than it breathes at rest
                        bright *= 1.0 - 0.45 * saturate(abs(tip.vel) * 2.2)
                                  * saturate(0.5 + 0.5 * GhvrWave4(float4(t * 11.0, 0, 0, 0)).x);
                    }
                }

                o.pos = UnityObjectToClipPos(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                if (_Bonfire > 0.5)
                {
                    // ONE waveform for the flame and for the light it casts (see
                    // EnvFire.cginc). Per-card phase, shared rate: the parts of
                    // one fire are independent, a fire and its wash are not.
                    // Both the rate AND the depth go through the pair functions,
                    // so FIRE+AIR reaches the flame and the pool identically.
                    //
                    // ModBuild 145 — THE PHASE IS `v.color.r`, NOT `* 6.2831853`.
                    // The wave's argument is in CYCLES (GhvrWave4 takes frac of
                    // it), so the 2*pi was a leftover from a radians formulation
                    // and made the card's brightness wrap 6.28 times across the
                    // same COLOR.r that phases its shape: a card's glow and a
                    // card's leap were two uncorrelated signals. Two independent
                    // random signals per card is twice the visual noise of one
                    // and carries no more information — and "more uncorrelated
                    // noise" is a fair paraphrase of "zappelt". Now a tongue
                    // brightens as it rises, which is also what a tongue does.
                    // ---- THE FACE FADE, and it is the answer to the Striche.
                    // The card does not move; its ENERGY falls to zero as the
                    // view approaches its plane. Read the STRICHE block at the
                    // top for the ruling this resolves and the arithmetic of
                    // the three-quad rosette that makes it view-independent.
                    //
                    // The normal is taken AS AUTHORED, not from the deformed
                    // position: a tongue that has surged, leaned and torn is
                    // still the same sheet of gas and the sheet's own plane is
                    // what a viewer is or is not looking along. Deriving it
                    // from `p` would also have cost a cross product per vertex
                    // for a quantity that changes by a few degrees.
                    //
                    // Per VERTEX and not per fragment. Across a 35 cm card at
                    // two metres the view direction turns by about 10 degrees,
                    // so the fade varies smoothly over the quad instead of
                    // being flat — which is if anything the more correct
                    // picture and is certainly the cheaper one; and it is
                    // interpolated, so there is no edge in it anywhere.
                    //
                    // It multiplies o.fl ONLY. Putting it in cardA as well
                    // would have squared it (the fragment premultiplies c.a
                    // into c.rgb and the blend then applies c.a again), which
                    // would turn a 37-degree ramp into an 18-degree one and
                    // bring back a hard-edged version of the same problem.
                    float3 nW = UnityObjectToWorldNormal(v.normal);
                    float3 pW = mul(unity_ObjectToWorld, float4(p.xyz, 1.0)).xyz;
                    float3 vD = _WorldSpaceCameraPos - pW;
                    float face = abs(dot(normalize(nW), normalize(vD)));
                    // The default (-1, 0) makes this exactly 1 for every face
                    // in [0,1], so a material that sets no fade is the shipped
                    // shader; see the property for why it is not (0, 0).
                    face = smoothstep(_FaceFade.x, _FaceFade.y, face);
                    // ...AND THE LAST FEW PER CENT COME OUT OF THE ALPHA, which
                    // is a second application of the same number and is here for
                    // a reason the energy argument above does not cover.
                    //
                    // A card at 70-89 degrees off face-on is not merely dim, it
                    // is ALIASED: its whole width lands in one or two pixels, the
                    // sprite lookup and the erosion lookup both take a step of
                    // most of a cell per pixel, and the hardware's mip and
                    // anisotropic filtering produce vertical smears with
                    // staircase edges. The first render of this round shows one
                    // over the forest's misty gap — a dark red column with hard
                    // steps in it, four times the height of the fire. That is not
                    // a brightness fault and dimming it to 3 % does not remove
                    // it, because the thing that draws the eye is the STRUCTURE,
                    // not the level.
                    //
                    // So the residue is taken out of the alpha as well, and only
                    // the residue: smoothstep(0, 0.28, face) is exactly 1 for
                    // every card the fade leaves above 28 %, so the whole of the
                    // band the energy arithmetic above is about is untouched, and
                    // only the last stretch — 68 to 75 degrees, where a card is
                    // three pixels wide and aliasing — is driven to nothing.
                    // Two multiplications compose there (fl and alpha, and the
                    // premultiply squares the alpha again), which is what makes
                    // it a hard cut-off at the very end of a soft ramp rather
                    // than a hard ramp.
                    cardA *= smoothstep(0.0, 0.28, face);
                    o.fx.w = cardA;

                    o.fl = GhvrFireFlicker(t, GhvrFireHz(pair, _FireHz),
                                           v.color.r,
                                           GhvrFireDepth(pair, _Flicker))
                         * face
                         * bright
                         // per-card share of the fire's energy, and the surge
                         // shows in the light as well as in the shape — a tongue
                         // that leaps is a tongue that is burning harder. The
                         // SLOW half of the surge (lickE), so that a card is one
                         // brightness and a fluttering tip does not paint a
                         // gradient up it — see lickE where it is computed.
                         * v.color.a * (0.55 + 0.45 * lickE) * gate * cardA
                         // SMOKE AND STEAM CARRY MORE THAN AN EMBER DOES. A
                         // detached ember is a spark's worth of energy; a lit
                         // plume is a body of scattering matter and is the whole
                         // content of Fire+Light. Same parameter (this card's
                         // share), more of it, and only when both elements are up.
                         * (1.0 + 2.30 * pr.x + 1.10 * pr.y);
                }
                else
                {
                    // brightness flicker — same sine family AND same rate as the
                    // EnvRoom light slot this candle drives
                    float f = 0.42 * sin(ft * 11.3 + _Phase)
                            + 0.33 * sin(ft *  6.1 + 1.7 + _Phase * 1.3)
                            + 0.25 * sin(ft * 19.7 + 4.2 + _Phase * 0.7);
                    f = f * 0.70 + 0.30 * sin(ft * 1.9 + _Phase * 0.5);
                    // a flame that is bent by a draught also burns brighter
                    o.fl = (1.0 + _Flicker * 0.35 * f)
                         * (1.0 + 0.55 * _Gust * gustMul * abs(g)) * bright;
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 c;
                if (_Bonfire > 0.5)
                {
                    // THE ATLAS. ROUND FIRST — `cell` is a small integer that has
                    // been through a perspective-correct interpolator and that is
                    // not exact; floor(1.9999998) is 1 and the card would be drawn
                    // with its neighbour's shape. (The same last-bit trap cost
                    // this project a review round in EnvHaunt's atlas; the fix is
                    // one add.)
                    float ci = floor(i.fx.z + 0.5);
                    float2 cel = float2(fmod(ci, GHVR_FIRE_COLS), floor(ci / GHVR_FIRE_COLS));
                    float2 a = saturate(uv) * (1.0 - 2.0 * GHVR_FIRE_INSET) + GHVR_FIRE_INSET;
                    c = tex2D(_MainTex, (a + cel) * GHVR_FIRE_CELL);
                    // RGB IS NOT THE SPRITE. It is the erosion field, and this
                    // card's colour comes entirely from GhvrFireRamp below, so
                    // the tap's rgb is thrown away here rather than being
                    // multiplied through _Tint and the ramp as a noise pattern.
                    c.rgb = fixed3(1, 1, 1);
                    c.a *= i.fx.w;      // a detached puff is born and dies

                    // ---- THE ANIMATED EROSION -----------------------------
                    // The technique Vefects' whole pack is built on, and the
                    // one thing this fire has never had. Read the FLICKER block
                    // at the top of the file; in three lines:
                    //
                    //   ONE EXTRA TAP, on the atlas itself. R is the coarse
                    //   octave (one tile over the 512 image), G the fine one
                    //   (four tiles) — so a single fetch is two octaves, and
                    //   because the scroll is in UV the fine one also boils
                    //   four times faster. There is no second sampler and no
                    //   second texture in the bundle.
                    //
                    //   THE V OFFSET IS PRE-FRAC'D IN THE VERTEX SHADER
                    //   (i.pr.w) and the card's own U offset with it
                    //   (i.fire.z), for the reasons in the v2f comment: a
                    //   free-running clock through a half-precision varying
                    //   quantises, and both of these are constant over a card
                    //   so the wrap can never fall inside a triangle.
                    //
                    //   IT IS SAMPLED ON THE RAW CARD UV, deliberately NOT on
                    //   the cell-inset `a`: the field tiles across the whole
                    //   atlas with wrapU/wrapV Repeat (see the .meta), so
                    //   confining it to a cell is exactly the wrong thing —
                    //   the confinement is what would put a seam in it.
                    float2 ev = float2(i.uv.x * _ErodeParams.x + i.fire.z,
                                       i.pr.w - i.uv.y * _ErodeParams.y);
                    fixed2 fld = tex2D(_MainTex, ev).rg;
                    // ...and the two octaves, mixed by _ErodeMix.z.
                    float er = lerp(fld.r, fld.g, _ErodeMix.z);
                    // A FLAME IS ANCHORED WHERE IT IS FED. The bottom of a
                    // tongue and the seat of a bed are the fuel and may not
                    // dissolve; the tip is gas that has left it and may vanish
                    // entirely. _ErodeMix.y is the base's share (0.22), so the
                    // weight runs 0.22 at the foot to 1.0 at the tip.
                    er *= _ErodeMix.x * (_ErodeMix.y
                                         + (1.0 - _ErodeMix.y) * i.uv.y);
                    // The pre-boost is inside its own saturate so the two
                    // clamps compose the way fire_atlas_pipeline.py's
                    // simulate_erosion() composes them — that function is what
                    // the ArtE energy compensation is derived from, and if
                    // these two expressions ever stop matching, the fire
                    // silently changes brightness in both rooms.
                    c.a = saturate(saturate(c.a * _ErodeParams.w) - er);
                }
                else
                {
                    // wick-anchored UV wobble: zero at base, grows toward the
                    // tip. CANDLES ONLY as of ModBuild 148 — on a bonfire it ran
                    // at 2.1 and 1.16 Hz (the frequencies of a 1-2 m structure)
                    // to displace a 2 cm feature, which the ModBuild 145 block
                    // already called out as inverted, and the erosion above is
                    // the term that does that job at the right scale. On a 2 cm
                    // teardrop it is correct and is untouched.
                    float t = (_Time.y + _GhvrTimeOfs) * _Rate;
                    float wob = (sin(t * 13.1 + i.uv.y * 9.0 + _Phase)
                               + sin(t * 7.3 + 2.1 + _Phase)) * 0.012 * i.uv.y;
                    c = tex2D(_MainTex, uv + float2(wob, 0));
                }
                c *= _Tint;
                // ELEMENT ART: under Fire the core burns toward white while the
                // edge keeps the candle's own amber — a hotter flame, not a
                // recoloured one. i.uv.y is the height up the sprite, so the
                // whitening is strongest at the base where a real flame is
                // hottest. Exactly 0 when Fire is 0.
                c.rgb = lerp(c.rgb, c.rgb * float3(1.16, 1.06, 0.82),
                             saturate(i.fire.x * (1.0 - i.uv.y * 0.6)));
                // FIRE REAL — the vertical temperature ramp, THREE stops and
                // measured up the whole FIRE rather than up this card (see
                // _FireH). A candle flame is one colour because it is 2 cm tall;
                // a burning crate is white-blue where it is fed, orange through
                // the body and dark red where the tongues tear off, and a piece
                // that has detached is cooling all the way down. That gradient is
                // most of what makes a thing read as BURNING rather than as a
                // warm sprite — and the previous two attempts had no white in
                // them anywhere, which is why they read as light the colour of
                // fire. Bonfire materials only; the candles keep their _Tint.
                if (_Bonfire > 0.5)
                {
                    // FIRE+EARTH: a smouldering fire is COOLER all the way up —
                    // the same ramp, entered further along, so the white-hot band
                    // shrinks toward nothing and the body is already at the tip
                    // colour. `age` is what the ramp already uses to cool a piece
                    // that has detached, so adding to it is a modulation of an
                    // existing parameter rather than a second colour path.
                    // FIRE+ICE does a little of the same, and for the same
                    // physical reason: something is taking heat out of the flame.
                    //
                    // ...AND THE PER-CARD TEMPERATURE TIER — ModBuild 148, and
                    // the previous round named it and could not reach it from
                    // its own files. COLOR.b is how far out of the seat this
                    // card stands (FireMesh's `outw`), and it enters the ramp's
                    // cooling argument exactly as `age` does: a tongue standing
                    // at the rim of a fire is fed by less and is cooler than one
                    // in the middle of it, so the fire is white-hot at its core
                    // and dark red at its edges IN PLAN as well as in height.
                    // Without it, thirty cards at one height are thirty cards of
                    // one colour, which is a sheet; with it the same thirty are
                    // a hot centre with cool tongues coming off it, which is a
                    // fire. One MAD, and `age` is already the parameter — so
                    // this is a modulation of an existing term and not a second
                    // colour path, the same rule the pairings above follow.
                    //
                    // ...and `age` enters at GHVR_PUFF_COOL and not at 1.0 —
                    // ModBuild 150. A piece now dies at 0.55 of its cycle
                    // instead of at 1.0 (see the envelope in the vertex
                    // shader), so at the shipped weight it only ever reached
                    // 0.55 of the way along the cooling axis and the LAST
                    // thing the player saw of it was still orange. "Anything
                    // leaving the flame body loses its colour and its
                    // brightness fast" is the requirement, and 1.90 x 0.55 =
                    // 1.05 puts a piece at the tip stop — (0.60, 0.085, 0.018),
                    // a third of the body's luminance and almost monochrome
                    // red — by the time it goes out. `age` is exactly 0 on a
                    // bed or a tongue card, so this reaches nothing else.
                    float cool = i.fx.y * GHVR_PUFF_COOL + 0.42 * i.pr.z
                               + 0.18 * i.pr.y + _Tier * i.fire.y;
                    float3 fc = GhvrFireRamp(_BaseCol.rgb, _CoreCol.rgb, _TipCol.rgb,
                                             i.fx.x, cool);
                    // ...and WHAT a detached piece is made of. Both of these are
                    // reached only when the piece is old (it has left the flame)
                    // AND both of the pair's elements are up, so an ordinary
                    // ember is untouched. Additive is the right blend for both:
                    // moonlit smoke and steam are things that SCATTER light
                    // toward you, and neither may occlude — see the report for
                    // the one thing that costs (a smoke plume cannot darken what
                    // is behind it in a single additive pass).
                    float t2 = saturate(i.fx.y * 1.6);
                    fc = lerp(fc, _SmokeCol.rgb, saturate(i.pr.x * t2));
                    fc = lerp(fc, _SteamCol.rgb, saturate(i.pr.y * t2));
                    c.rgb *= fc;
                }
                c.rgb *= c.a * i.fl; // premodulate: alpha drives additive energy
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
