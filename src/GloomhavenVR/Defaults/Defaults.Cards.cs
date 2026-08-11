// GENERATED-AND-HAND-EDITABLE — the single home of every shipped config default.
//
// WHY THIS FILE EXISTS
// --------------------
// The mod binds ~550 config entries across 18 module config classes. Their default values
// were literals at the Bind call, which meant re-basing the shipped defaults onto a tuned
// setup was a hunt through the whole codebase. They all live here now, one per line, each
// tagged with the config identity it feeds:
//
//     internal const float TrayForward = 0.77584f;   // => [Cards] TrayForward
//
// The `// => [Section] Key` annotation is the machine-readable part: scripts/rebase-defaults.py
// finds an entry by it, so a line may move but its annotation must stay exact.
//
// RULES
//   * one entry per line, initialiser a LITERAL (or a `new Vector3(...)`/`new Color(...)` of
//     literals) — never an expression that reads another entry;
//   * `const` wherever C# allows it, so the compiler inlines it and the compiled form is
//     identical to the old literal-at-the-bind;
//   * `static readonly` only for Vector2/Vector3/Color, which cannot be const;
//   * the *_ByBoard / *_ByStyle arrays exist so a per-variant bind inside a loop can index
//     them; they are assembled from the named entries above them and hold no literals.
//
// Editing: change the number, rebuild. Or drop a tuned cfg into .planning/debug/default/ and
// run `python3 scripts/rebase-defaults.py apply`.


using UnityEngine;
using GloomhavenVR.Cards;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Cards/CardsConfig.cs ------------------------------------------------------
    internal const int DevFakeHand = 0;                                                               // => [Cards] DevFakeHand
    internal const string RevealMode = "tilt";                                                        // => [Cards] RevealMode
    internal const float RevealEnterDegrees = 70f;                                                    // => [Cards] RevealEnterDegrees
    internal const float RevealExitDegrees = 5f;                                                      // => [Cards] RevealExitDegrees
    internal const float FanRadius = 0.16f;                                                           // => [Cards] FanRadius
    internal const float FanArcDegrees = 70f;                                                         // => [Cards] FanArcDegrees  (legacy: read once as the seed for its successor)
    internal const float FanPalmOffset = 0.09f;                                                       // => [Cards] FanPalmOffset
    internal const float CardWidth = 0.0635f;                                                         // => [Cards] CardWidth
    internal const float InspectScale = 1.6f;                                                         // => [Cards] InspectScale
    internal const float Cards_HeldTiltDegrees = 20f;                                                 // => [Cards] HeldTiltDegrees  (legacy: read once as the seed for its successor)
    internal const float HeldFaceBias = 65f;                                                          // => [Cards] HeldFaceBias
    internal const float HeldForward = 0.005f;                                                        // => [Cards] HeldForward
    internal const float HeldOffPalm = 0.0148f;                                                       // => [Cards] HeldOffPalm
    internal static readonly Vector3 HeldPinchOffset = new Vector3(-0.055f, 0.035f, 0f);              // => [Cards] HeldPinchOffset
    internal const float TrayForward = 0.58529f;                                                      // => [Cards] TrayForward
    internal const float TrayDown = 0.09393f;                                                         // => [Cards] TrayDown
    internal const float TrayRight = -0.264224f;                                                      // => [Cards] TrayRight
    internal const float TrayTilt = 30f;                                                              // => [Cards] TrayTilt  (legacy: read once as the seed for its successor)
    internal const float TrayYaw = -41.0015f;                                                         // => [Cards] TrayYaw
    internal const float TrayScale = 0.57247f;                                                        // => [Cards] TrayScale
    internal const bool TrayFollow = false;                                                           // => [Cards] TrayFollow
    internal const BoardMoveMode BoardMoveMode = Cards.BoardMoveMode.Free;                            // => [Cards] BoardMoveMode
    internal const float TrayPitch = 32.953f;                                                         // => [Cards] TrayPitch
    internal const float BoardPitchMinDegrees = -45f;                                                 // => [Cards] BoardPitchMinDegrees  (legacy: superseded by the per-board BoardPitchMin_<board>)
    internal const float BoardPitchMaxDegrees = 45f;                                                  // => [Cards] BoardPitchMaxDegrees  (legacy: superseded by the per-board BoardPitchMax_<board>)
    internal const float CardLerpSpeed = 14f;                                                         // => [Cards] CardLerpSpeed
    internal const float SlotCardInset = 0.004f;                                                      // => [Cards] SlotCardInset
    // [Cards] SlotCardFill is GONE (retired 2026-08-11, user report "das Karten-Overlay soll auch
    // die Karten regieren, die auf dem Board liegen"). It sized the card in the recess while the
    // blinking overlays sized themselves off a code literal, so the two could never be brought into
    // register. Its 1.45 lives on as the SEED of the per-board SlotOverlayScale_{board} below, which
    // now sizes the overlay AND the card together. No line here on purpose — a tuned cfg still
    // carrying the key is reported UNMAPPED by scripts/rebase-defaults.py, which is right for a
    // retired key.
    internal const float RoundButtonDiameter = 0.105f;                                                // => [Cards] RoundButtonDiameter  (legacy: read once as the seed for its successor)
    internal const float RoundButtonThickness = 0.012f;                                               // => [Cards] RoundButtonThickness  (legacy: read once as the seed for its successor)
    internal const float RestButtonInsetX = 0.024f;                                                   // => [Cards] RestButtonInsetX  (legacy: read once as the seed for its successor)
    internal const float ConfirmUndoInsetX = 0.014f;                                                  // => [Cards] ConfirmUndoInsetX  (legacy: read once as the seed for its successor)
    internal const float FanStepDegrees_Items = 10f;                                                  // => [Cards] FanStepDegrees_Items
    internal const float FanStepDegrees_Discard = 10f;                                                // => [Cards] FanStepDegrees_Discard
    internal const float FanStepDegrees_Burnt = 10f;                                                  // => [Cards] FanStepDegrees_Burnt
    internal const float FanRadiusFactor_Items = 1.7f;                                                // => [Cards] FanRadiusFactor_Items
    internal const float FanRadiusFactor_Discard = 1.7f;                                              // => [Cards] FanRadiusFactor_Discard
    internal const float FanRadiusFactor_Burnt = 1.7f;                                                // => [Cards] FanRadiusFactor_Burnt

    // ---- THE ITEM FAN's OPEN/CLOSE ANIMATION (user report 2026-08-08: "Ich mag die Animation im
    // Item-Pile sehr aber sie ist (insbesondere in mixed Reality) etwas zu dezent.") --------------
    //
    // He LIKES the motion — these dials do not replace it, they give it presence. The shipped
    // numbers ARE the louder look; nobody should have to tune anything to get what he asked for.
    //
    // WHAT THE ANIMATION USED TO BE, and why it read as "dezent" on a passthrough background: every
    // chip started on the items stack at 0.35× size and flew to its arc slot on ONE shared
    // exponential home-lerp — no stagger (all twelve moved as a single blob), no arc (a straight
    // chord), no overshoot (an exponential only ever decelerates, so the motion has no end — it
    // just stops being visible), and no rotation at all (BeginEmerge never touched the pose that
    // SetHome had already written). Against a black VR skybox that is enough. Against a lit living
    // room it is not: passthrough hands the eye a background that is already full of edges,
    // contrast and its own parallax, and a short, smooth, simultaneous, purely-translational move
    // is exactly the class of motion that background swallows.
    //
    // The five axes below are the ones that survive it, each for a stated reason — see the Loc
    // descriptions and Cards/ItemsPile.cs's emerge/collapse region for the derivation.
    // ─── SECOND PRESENCE PASS (user report 2026-08-09): "Die Animation auf der Item-Pile ist immer
    // noch zu dezent - insbesondere im mixed reality modus kaum erkennbar - es soll aber eine
    // ÄHNLICHE Animation bleiben aber die Sichtbarkeit erhöhen."
    //
    // THE AMPLITUDES WERE NOT THE BINDING CONSTRAINT, and the hardware log of 2026-08-09 says so
    // with numbers rather than with an opinion. Every laser/sweep diagnostic line carries the
    // hovered chip's LIVE real-world face width, which is a direct probe of this animation's scale
    // term. One open, ModBuild 93, board world scale 36.5×:
    //
    //     0.6 cm  (the seed, on the stack)  →  4.7 cm  →  5.5 cm  →  4.7 cm  (settled)
    //
    // That 5.5 between two 4.7s IS ItemFanSettleOvershoot landing: the flight runs to completion,
    // the back-ease overshoot plays, the seed scale is honoured, nothing is being clipped or cut
    // short. Two other opens probe the same shape (0.5 → 4.2 cm, and 0.8 → 6.3 → 6.9 cm at a
    // different board scale). And nothing overrides these values: the config snapshot in
    // .planning/debug/default/ contains no ItemFan* key at all (it predates the feature), so what
    // the user judged "kaum erkennbar" was these shipped numbers, playing in full.
    //
    // WHAT IS BINDING IS TIME SPENT AT A LEGIBLE SIZE — a different axis from amplitude, and the
    // reason turning the amplitudes up again would have failed a third time. Read the same probe as
    // a size-over-time curve: the flight BEGINS at 0.5-0.8 real-world centimetres. At a normal
    // ~50 cm reading distance that is well under one degree of visual angle, and the log's own
    // warnings show what is being rendered at that size — "MIP BAKE skip: sprite 'Eagle-Eye_Goggles'
    // stays MIPLESS - per-sprite bake budget exhausted (48)", fired for EVERY item face in the very
    // frames the fan opens. A mipless sprite minified ~40× is not a small card, it is aliasing; in
    // stereo the two eyes alias differently, so it reads as shimmer rather than as an object. So
    // roughly the first half of a 0.34 s flight was spent below the resolution at which the eye can
    // tell there is a CARD there — and mixed reality removes what was left of the margin, because
    // Virtual Desktop's passthrough layer is a lower-resolution, motion-blurred video feed composited
    // against a bright, high-contrast room. The animation was not too small. It was over before it
    // became visible.
    //
    // THE SAME ANIMATION, GIVEN TIME TO BE SEEN — every axis keeps its role, none is replaced:
    //   • DURATION up 0.34 → 0.48 s (close 0.30 → 0.42). The single change the evidence demands.
    //   • SEED up 0.12 → 0.30. This deliberately walks back the previous pass's 0.35 → 0.12, which
    //     reasoned "a card that grows eightfold is coming toward you". True — but only for a viewer
    //     who can SEE it during the growth, and at 0.12 the first third of the flight is the
    //     sub-degree shimmer above. 0.30 still more than TRIPLES the card over the flight, which is
    //     a strong depth cue, and it is a legible card from the first frame it moves.
    //   • STAGGER up 0.055 → 0.075 (close 0.032 → 0.045): the moving front is the cue that survives
    //     a busy background, so it gets proportionally more of the longer window, and the centre-out
    //     ripple stays exactly the ripple it was.
    //   • ARC up 0.06 → 0.09 m: the bow toward the viewer is the one cue passthrough structurally
    //     cannot mask (stereo disparity), and it is the term that should grow WITH the flight time
    //     rather than against it.
    //   • SPIN and OVERSHOOT UNCHANGED at 52° / 1.4 — the log proves both are already arriving.
    // A 7-item fan therefore deals in 0.48 + 3 × 0.075 = 0.70 s, up from 0.51 s: still a deliberate
    // gesture answering a deliberate poke on the items stack, and still recognisably the animation
    // he said he liked.
    //
    // CONSIDERED AND REJECTED — an MrBacking plate behind the flying chips. Plates exist for THIN
    // content floating over the room (glyphs, converted panels); an item chip is already opaque
    // geometry with its own AlphaTest backing slab at queue 2450 with ZWrite ON, which per
    // MrBacking's own sorting contract discards a plate over the whole card silhouette per pixel.
    // A plate would therefore have added nothing behind the card and a visible dark rectangle
    // around it — and a rectangle that flies with the card is a different animation, which is
    // exactly what he asked us not to do.
    internal const float ItemFanOpenDuration = 0.34f;                                                 // => [Cards] ItemFanOpenDuration
    internal const float ItemFanOpenStagger = 0.055f;                                                 // => [Cards] ItemFanOpenStagger
    internal const float ItemFanOpenArc = 0.06f;                                                      // => [Cards] ItemFanOpenArc
    internal const float ItemFanOpenSpinDegrees = 52f;                                                // => [Cards] ItemFanOpenSpinDegrees
    internal const float ItemFanSeedScale = 0.12f;                                                    // => [Cards] ItemFanSeedScale
    internal const float ItemFanSettleOvershoot = 1.4f;                                               // => [Cards] ItemFanSettleOvershoot
    internal const float ItemFanCloseDuration = 0.3f;                                                 // => [Cards] ItemFanCloseDuration
    internal const float ItemFanCloseStagger = 0.032f;                                                // => [Cards] ItemFanCloseStagger

    // ---- THE "AN ITEM CAN BE USED" CUE ON THE CLOSED ITEMS PILE (user report 2026-08-09, the THIRD
    // on this area: "Die Animation über dem Pile die anzeigt dass ein Gegenstand genutzt werden kann
    // ist immer noch zu dezent und kann man schnell übersehen. Ich mag die Animation aber sie muss
    // mehr herausstechen.") ------------------------------------------------------------------------
    //
    // He LIKES the drifting gold embers. They stay. What changed is the two things that decide
    // whether a cue is SEEN by someone who is not looking at it — and neither of them is amplitude,
    // which is why the previous two rounds of "turn it up" did not land.
    //
    // WHAT THE CUE USED TO BE, in its own words: PileViewer.PileStack.SetUsableHighlight's doc block
    // said "SUBTLE BY CONSTRUCTION: ~5 motes a second, each a few millimetres across, living under
    // two seconds, at well under half opacity, drifting a couple of centimetres. At any instant there
    // are under a dozen on screen — a shimmer you notice in peripheral vision, not an effect that
    // competes with the board." Every clause of that is a design decision against being noticed, and
    // it was written for a black VR skybox. Against chroma-keyed passthrough — a live video feed of a
    // lit room, full of its own edges, contrast, grain and parallax — "a shimmer" is below the noise
    // floor. And the ModBuild 94 finding compounds it: the mip-bake budget starved item art, so what
    // little there was ALIASED, and in stereo the two eyes alias differently. Half of "dezent" was
    // literally shimmer.
    //
    // THE TWO AXES THAT ACTUALLY DECIDE IT, and the reason each number below exists:
    //
    //   • RHYTHM, not brightness. The retina's PERIPHERY is what "übersehen" is about — the player
    //     is looking at their hand or at the map, not at the pile — and the periphery is a TRANSIENT
    //     detector: it answers to sudden change and is nearly blind to a slow ramp. A steady trickle
    //     of motes and a sine-breathing outline are both continuous states, i.e. the two rhythms the
    //     periphery reports least. The cue now runs on a double HEARTBEAT with a REST between beats
    //     (WorldUI/SoftCueArt.Heartbeat) — the rest is what makes the next beat a change rather than
    //     a continuation. The ember emission bursts on that beat, the item-card frames in the open
    //     fan beat on the same clock, and so does the pile's new ring, so the whole item cue speaks
    //     with one pulse instead of three unrelated flickers.
    //
    //   • A CHANGING SILHOUETTE, not a changing brightness. Passthrough competes with the mod on
    //     contrast and wins; it contains nothing that changes SIZE. So each beat now throws a soft
    //     ROUND ring of light outward off the pile, growing and fading — the one class of motion a
    //     busy room structurally cannot mask, and in stereo unambiguously in front of the room. It
    //     is round, not rectangular, on purpose: PileStack's own doc records the ruling that a frame
    //     around the stack "would be exactly the rectangle of light the user rejected", and a ring is
    //     the hollow sibling of the mote texture the cue is already made of, so this is the SAME
    //     visual family enlarged, not a new effect. Two rings share the period at opposite phases, so
    //     the cue is never continuous and never silent for long.
    //
    // …and the third thing, which is neither: a cue drawn in ONE tone can only be seen where it
    // differs in luminance from a background nobody controls. Every band in this family is now
    // two-tone — a bright core with a dark contour on both sides — so it keeps a luminance edge over
    // a white wall and over a dark room alike. See the CONTOUR note in WorldUI/SoftCueArt.cs.
    //
    // These numbers ARE the louder look; nobody should have to tune anything to get what he asked
    // for. 0 on the amplitudes is the honest "turn it back down" position and is reachable from the
    // in-VR steppers.
    internal const float ItemCueBeatSeconds = 1.25f;                                                  // => [Cards] ItemCueBeatSeconds
    internal const float ItemCueRingReach = 2.3f;                                                     // => [Cards] ItemCueRingReach
    internal const float ItemCueRingAlpha = 0.95f;                                                    // => [Cards] ItemCueRingAlpha
    internal const float ItemCueEmberRate = 22f;                                                      // => [Cards] ItemCueEmberRate
    internal const float ItemCueEmberSize = 2.1f;                                                     // => [Cards] ItemCueEmberSize

    // ---- THE ITEM-USE BERTH — the recess on the board a card is laid into to use it (user report
    // 2026-08-09: "Überarbeite das Aussehen des Item-Overlays. Aktuell ist es einfach so ein
    // schwarzes Rechteck, das am Rand pulsiert. Das sieht nicht sehr gut aus. Überlege dir eine
    // andere Darstellung die visuell ansprechender ist aber immer noch das selbe vermittelt.") -----
    //
    // WHAT IT WAS: three stacked quads — a gold 1.12× frame, an opaque near-black 1.04× inner plate,
    // and a 1.28× additive gold quad breathing on PlayTray.SlotPulse's sine. On a board that hangs in
    // the air with a real room behind it (this recess sits BELOW the board's lower edge — it has no
    // opaque slab behind it, unlike the two play slots it was copied from), that is a black rectangle
    // with a glowing rim, and "schwarzes Rechteck" is a precise description rather than an opinion.
    // It also fails the mixed-reality rule from the other direction: a cue whose identity is
    // "dark" cannot work over a dark room, and near the BLACK chroma-key preset a dark plate is not
    // a rectangle at all, it is a hole cut through to the passthrough camera.
    //
    // WHAT IT IS NOW — an OPEN BERTH, not a plate. Four pieces, in the board's own vocabulary:
    //   1. a card-shaped, constant-thickness, two-tone SOFT OUTLINE at exactly the size the card
    //      lands at (the ItemsPile clear-area factor), with rounded corners: the same SoftCueArt
    //      outline language the item cards' "usable" frame and the initiative ring already wear, so
    //      the destination is drawn in the same hand as the thing that will fill it;
    //   2. NOTHING opaque inside it — the middle is left open, so in mixed reality the player's own
    //      room shows through the berth and the widget can never be a dark rectangle again. What
    //      fills it instead is a faint ADDITIVE warm field (ItemBerthGlow) — light added, not
    //      darkness laid on, i.e. the mod's existing gold-glow voice (CardGlow.MakeGlowMaterial, the
    //      same material the slot snap telegraph is made of);
    //   3. an INWARD ring ping that closes onto the card rect on the shared item beat — the
    //      "put it HERE" sentence, the exact mirror of the pile's outward "look here" ring, and the
    //      replacement for the border sine;
    //   4. an ARRIVAL and a DEPARTURE. The recess used to blink in and out on a raw SetActive, the
    //      last unanimated transition in the item flow and a straight breach of the standing
    //      "nothing pops" rule. It now grows in with a back-ease overshoot and collapses out.
    //
    // AND IT IS STILL NOT A BUTTON (the constraint that forced the "USE" caption's restyle one round
    // earlier). Every one of the mod's buttons is a raised, filled KEYCAP with a bright face and
    // travel; this is a hollow outline with an open middle, no face, no travel and no press state.
    // A hole you put something into and a cap you push are now maximally different objects.
    internal const float ItemBerthRingThickness = 0.0042f;                                            // => [Cards] ItemBerthRingThickness
    internal const float ItemBerthGlow = 0.34f;                                                       // => [Cards] ItemBerthGlow
    internal const float ItemBerthPingSeconds = 1.5f;                                                 // => [Cards] ItemBerthPingSeconds
    internal const float ItemBerthPingReach = 1.5f;                                                   // => [Cards] ItemBerthPingReach
    internal const float ItemBerthRevealSeconds = 0.26f;                                              // => [Cards] ItemBerthRevealSeconds

    internal const float BoardMinWidthMeters = 0.18f;                                                 // => [Cards] BoardMinWidthMeters
    internal const float BoardMaxWidthMeters = 1.4f;                                                  // => [Cards] BoardMaxWidthMeters
    internal const bool SpawnLeftOfHead = true;                                                       // => [Cards] SpawnLeftOfHead
    internal const float SpawnSideMeters = 0.45f;                                                     // => [Cards] SpawnSideMeters
    internal const float SpawnForwardMeters = 0.28f;                                                  // => [Cards] SpawnForwardMeters
    internal const float SpawnDownMeters = 0.32f;                                                     // => [Cards] SpawnDownMeters
    internal const bool GameCardParticles = false;                                                    // => [Cards] GameCardParticles
    internal const bool CardDust = false;                                                             // => [Cards] CardDust
    internal const bool WantedSlotHint = true;                                                        // => [Cards] WantedSlotHint
    internal const bool PileViewer = true;                                                            // => [Cards] PileViewer
    internal const bool ActivePile = true;                                                            // => [Cards] ActivePile
    internal const bool FaceMipBake = true;                                                           // => [Cards] FaceMipBake
    internal const float DissolveFloorFraction = 0.004f;                                              // => [Cards] DissolveFloorFraction
    internal const ControlBoard Board = ControlBoard.Oak;                                             // => [Cards] Board
    internal static readonly Vector3 ItemUseSlotOffset_Oak = new Vector3(0f, 0f, 0f);                 // => [Cards] ItemUseSlotOffset_Oak
    internal static readonly Vector3 ItemUseSlotOffset_Steel = new Vector3(0f, 0f, 0f);               // => [Cards] ItemUseSlotOffset_Steel
    internal static readonly Vector3 ItemUseSlotOffset_Bronze = new Vector3(0f, 0f, 0f);              // => [Cards] ItemUseSlotOffset_Bronze
    internal static readonly Vector3 ItemCardOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] ItemCardOffset_Oak
    internal static readonly Vector3 ItemCardOffset_Steel = new Vector3(0f, 0f, 0f);                  // => [Cards] ItemCardOffset_Steel
    internal static readonly Vector3 ItemCardOffset_Bronze = new Vector3(0f, 0f, 0f);                 // => [Cards] ItemCardOffset_Bronze
    internal const float BoardTilt_Oak = 30f;                                                         // => [Cards] BoardTilt_Oak
    internal const float BoardTilt_Steel = 30f;                                                       // => [Cards] BoardTilt_Steel
    internal const float BoardTilt_Bronze = 30f;                                                      // => [Cards] BoardTilt_Bronze
    internal const float BoardPitchMin_Oak = -45f;                                                    // => [Cards] BoardPitchMin_Oak
    internal const float BoardPitchMin_Steel = -31.067f;                                              // => [Cards] BoardPitchMin_Steel
    internal const float BoardPitchMin_Bronze = -45f;                                                 // => [Cards] BoardPitchMin_Bronze
    internal const float BoardPitchMax_Oak = 45f;                                                     // => [Cards] BoardPitchMax_Oak
    internal const float BoardPitchMax_Steel = 54.353f;                                               // => [Cards] BoardPitchMax_Steel
    internal const float BoardPitchMax_Bronze = 45f;                                                  // => [Cards] BoardPitchMax_Bronze
    internal const float BoardYaw_Oak = 0f;                                                           // => [Cards] BoardYaw_Oak
    internal const float BoardYaw_Steel = 0f;                                                         // => [Cards] BoardYaw_Steel
    internal const float BoardYaw_Bronze = 0f;                                                        // => [Cards] BoardYaw_Bronze
    internal const float BoardScale_Oak = 0.92378f;                                                   // => [Cards] BoardScale_Oak
    internal const float BoardScale_Steel = 0.29909f;                                                 // => [Cards] BoardScale_Steel
    internal const float BoardScale_Bronze = 0.4f;                                                    // => [Cards] BoardScale_Bronze
    internal static readonly Vector3 AssetRotation_Oak = new Vector3(0f, 0f, 0f);                     // => [Cards] AssetRotation_Oak  (legacy: read once as the seed for its successor)
    internal static readonly Vector3 AssetRotation_Steel = new Vector3(0f, 0f, 0f);                   // => [Cards] AssetRotation_Steel  (legacy: read once as the seed for its successor)
    internal static readonly Vector3 AssetRotation_Bronze = new Vector3(0f, 0f, 0f);                  // => [Cards] AssetRotation_Bronze  (legacy: read once as the seed for its successor)
    internal const float AssetYawDegrees_Oak = 0f;                                                    // => [Cards] AssetYawDegrees_Oak
    internal const float AssetYawDegrees_Steel = 0f;                                                  // => [Cards] AssetYawDegrees_Steel
    internal const float AssetYawDegrees_Bronze = 0f;                                                 // => [Cards] AssetYawDegrees_Bronze
    internal const float AssetRollDegrees_Oak = 0f;                                                   // => [Cards] AssetRollDegrees_Oak
    internal const float AssetRollDegrees_Steel = 0f;                                                 // => [Cards] AssetRollDegrees_Steel
    internal const float AssetRollDegrees_Bronze = 0f;                                                // => [Cards] AssetRollDegrees_Bronze
    internal static readonly Vector3 BoardPosOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] BoardPosOffset_Oak
    internal static readonly Vector3 BoardPosOffset_Steel = new Vector3(0f, 0f, 0f);                  // => [Cards] BoardPosOffset_Steel
    internal static readonly Vector3 BoardPosOffset_Bronze = new Vector3(0f, 0f, 0f);                 // => [Cards] BoardPosOffset_Bronze
    internal const ButtonShape RestButtonShape_Oak = ButtonShape.Round;                               // => [Cards] RestButtonShape_Oak
    internal const ButtonShape RestButtonShape_Steel = ButtonShape.Round;                             // => [Cards] RestButtonShape_Steel
    internal const ButtonShape RestButtonShape_Bronze = ButtonShape.Round;                            // => [Cards] RestButtonShape_Bronze
    internal const ButtonShape GenericButtonShape_Oak = ButtonShape.Square;                           // => [Cards] GenericButtonShape_Oak
    internal const ButtonShape GenericButtonShape_Steel = ButtonShape.Square;                         // => [Cards] GenericButtonShape_Steel
    internal const ButtonShape GenericButtonShape_Bronze = ButtonShape.Square;                        // => [Cards] GenericButtonShape_Bronze
    internal static readonly Vector2 ActiveGridSpacing_Oak = new Vector2(1.06f, 0.7f);                // => [Cards] ActiveGridSpacing_Oak
    internal static readonly Vector2 ActiveGridSpacing_Steel = new Vector2(1.06f, 0.7f);              // => [Cards] ActiveGridSpacing_Steel
    internal static readonly Vector2 ActiveGridSpacing_Bronze = new Vector2(1.06f, 0.7f);             // => [Cards] ActiveGridSpacing_Bronze
    internal const float PileScale_Oak = 1f;                                                          // => [Cards] PileScale_Oak
    internal const float PileScale_Steel = 1f;                                                        // => [Cards] PileScale_Steel
    internal const float PileScale_Bronze = 1f;                                                       // => [Cards] PileScale_Bronze
    internal const float PileSpacing_Oak = 0.116f;                                                    // => [Cards] PileSpacing_Oak
    internal const float PileSpacing_Steel = 0.116f;                                                  // => [Cards] PileSpacing_Steel
    internal const float PileSpacing_Bronze = 0.116f;                                                 // => [Cards] PileSpacing_Bronze
    internal const float ElementsScale_Oak = 1f;                                                      // => [Cards] ElementsScale_Oak
    internal const float ElementsScale_Steel = 1f;                                                    // => [Cards] ElementsScale_Steel
    internal const float ElementsScale_Bronze = 1f;                                                   // => [Cards] ElementsScale_Bronze
    internal static readonly Vector3 ClusterOffset_Oak = new Vector3(0f, 0f, 0f);                     // => [Cards] ClusterOffset_Oak
    internal static readonly Vector3 ClusterOffset_Steel = new Vector3(0f, 0f, 0f);                   // => [Cards] ClusterOffset_Steel
    internal static readonly Vector3 ClusterOffset_Bronze = new Vector3(0f, 0f, 0f);                  // => [Cards] ClusterOffset_Bronze
    internal const float ClusterScale_Oak = 1f;                                                       // => [Cards] ClusterScale_Oak
    internal const float ClusterScale_Steel = 1f;                                                     // => [Cards] ClusterScale_Steel
    internal const float ClusterScale_Bronze = 1f;                                                    // => [Cards] ClusterScale_Bronze
    internal const float DecisionScale_Oak = 1.6f;                                                    // => [Cards] DecisionScale_Oak
    internal const float DecisionScale_Steel = 1.6f;                                                  // => [Cards] DecisionScale_Steel
    internal const float DecisionScale_Bronze = 1.6f;                                                 // => [Cards] DecisionScale_Bronze
    internal const bool BoardScaleDefault04Applied = false;                                           // => [Cards] BoardScaleDefault04Applied  (pinned: one-shot migration marker — a fresh install must start false)
    internal const bool FanCurveByFill = true;                                                        // => [Cards] FanCurveByFill
    internal const int FanMaxHandForCurve = 10;                                                       // => [Cards] FanMaxHandForCurve
    internal const float FanFlatCurvatureFactor = 0.55f;                                              // => [Cards] FanFlatCurvatureFactor
    internal const float FanTiltFactor = 0.85f;                                                       // => [Cards] FanTiltFactor
    internal const float FanSplitMultiplier = 0.02f;                                                  // => [Cards] FanSplitMultiplier
    internal const float FanSplitFalloff = 1.6f;                                                      // => [Cards] FanSplitFalloff
    internal const float FanSelectedPopForward = 0.035f;                                              // => [Cards] FanSelectedPopForward
    internal const CardGrabButton GrabButton = CardGrabButton.Trigger;                                // => [Cards] GrabButton
    internal const float FanFollowSmoothing = 16f;                                                    // => [Cards] FanFollowSmoothing
    internal const float FanFollowDeadzone = 0.004f;                                                  // => [Cards] FanFollowDeadzone
    internal const bool RevealIgnoreWhenGrabbing = true;                                              // => [Cards] RevealIgnoreWhenGrabbing
    internal const float FanOpenDuration = 0.14f;                                                     // => [Cards] FanOpenDuration
    internal const float FanOpenStagger = 0.02f;                                                      // => [Cards] FanOpenStagger
    internal const float FanCloseDuration = 0.12f;                                                    // => [Cards] FanCloseDuration

    // ---- THE HAND FAN's CHARACTER-SWAP EXCHANGE (user report 2026-08-09: "Wenn man die Handkarten
    // anschaut während man den Character wechselt gefällt mir die jetzige Animation nicht - mach
    // auch hier eine neue coolere Tauschanimation rein die den Fächer austauscht.") --------------
    //
    // WHAT IT USED TO DO, and why it read wrong: nothing. CardsDriver.Rebuild simply handed the fan
    // the OTHER character's cards (CardFan.SetCards), which cleared the list and re-laid it out —
    // so the outgoing hand's VR cards were parked (teleported into the pool) in the same frame the
    // incoming ones appeared at their arc slots. Whatever the counts happened to be, the player saw
    // a CONTENT EDIT: n cards blinked out, m cards blinked in, and any card that existed in both
    // frames just slid to a new slot. Nothing left, nothing arrived, nothing moved as one thing —
    // which is exactly the standing ruling ("everything that moves must move WITH an animation")
    // being broken at the one moment the player is staring straight at the fan.
    //
    // WHAT IT DOES NOW — ONE WIPE ACROSS THE PALM, not two animations glued together. The outgoing
    // hand is GATHERED into a point one FanSwapTravel past the arc's high-index end; the incoming
    // hand is DEALT OUT of the mirror-image point past the low-index end. Both waves are sequenced
    // by card index with the SAME FanSwapStagger, so both moving fronts travel the same way across
    // the hand, and FanSwapOverlap starts each slot's arrival while that slot's departure is still
    // in the air. The two halves separate in DEPTH rather than colliding: the leaver ducks AWAY
    // from the viewer by FanSwapArc, the arriver bows TOWARD them by the same amount, and their
    // rolls (FanSwapSpinDegrees) are oppositely signed — so the new hand visibly passes in FRONT of
    // the old one. See Cards/CardFan.cs's exchange region for the derivation of every one of those
    // choices, and Cards/ItemsPile.cs for the five-axis vocabulary they are written in.
    //
    // The numbers below ARE the shipped look: nobody should have to tune anything to get what he
    // asked for. Every amplitude reaches the previous (instant) behaviour at 0, which is the honest
    // "turn it back down" position and is reachable from the debug steppers.
    internal const float FanSwapDuration = 0.24f;                                                     // => [Cards] FanSwapDuration
    internal const float FanSwapStagger = 0.024f;                                                     // => [Cards] FanSwapStagger
    internal const float FanSwapOverlap = 0.66f;                                                      // => [Cards] FanSwapOverlap
    internal const float FanSwapTravel = 0.075f;                                                      // => [Cards] FanSwapTravel
    internal const float FanSwapArc = 0.06f;                                                          // => [Cards] FanSwapArc
    internal const float FanSwapSpinDegrees = 58f;                                                    // => [Cards] FanSwapSpinDegrees
    internal const float FanSwapSeedScale = 0.16f;                                                    // => [Cards] FanSwapSeedScale
    internal const float FanSwapSettleOvershoot = 1.5f;                                               // => [Cards] FanSwapSettleOvershoot
    internal const string FanRevealSound = "PlaySound_EnemyCardDraw";                                 // => [Cards] FanRevealSound
    internal const string FanHideSound = "PlaySound_UICardTabSelect";                                 // => [Cards] FanHideSound
    internal const string CardGrabSound = "PlaySound_UICardTabSelect";                                // => [Cards] CardGrabSound
    internal const string CardPlaceSound = "PlaySound_CardUI_SelectCard";                             // => [Cards] CardPlaceSound
    internal const string CardTakeBackSound = "PlaySound_UIUndoHex";                                  // => [Cards] CardTakeBackSound
    internal const float FanPerCardStepDegrees = 14f;                                                 // => [Cards] FanPerCardStepDegrees
    internal const float FanArcSweepDegrees = 103f;                                                   // => [Cards] FanArcSweepDegrees
    internal const float FanEffectiveRadius = 0.2192f;                                                // => [Cards] FanEffectiveRadius
    internal const float FanHoverSplitScale = 1.4f;                                                   // => [Cards] FanHoverSplitScale
    internal static readonly Vector3 BrowseFanOffset = new Vector3(0f, -0.37f, -0.15f);               // => [Cards] BrowseFanOffset
    internal const float FanSideDepthCurve = 0f;                                                      // => [Cards] FanSideDepthCurve
    internal const float FanCurvePower = 2f;                                                          // => [Cards] FanCurvePower
    internal const int FanCurveMinCards = 3;                                                          // => [Cards] FanCurveMinCards
    internal const bool FanGazeBias = false;                                                          // => [Cards] FanGazeBias
    internal const float FanFaceViewer = 1f;                                                          // => [Cards] FanFaceViewer
    internal const float FanGazeApexFollow = 1f;                                                      // => [Cards] FanGazeApexFollow
    internal const float FanGazeSmoothing = 6f;                                                       // => [Cards] FanGazeSmoothing
    internal static readonly Vector3[] ItemUseSlotOffset_ByBoard = { ItemUseSlotOffset_Oak, ItemUseSlotOffset_Steel, ItemUseSlotOffset_Bronze };
    internal static readonly Vector3[] ItemCardOffset_ByBoard = { ItemCardOffset_Oak, ItemCardOffset_Steel, ItemCardOffset_Bronze };
    internal static readonly float[] BoardTilt_ByBoard = { BoardTilt_Oak, BoardTilt_Steel, BoardTilt_Bronze };
    internal static readonly float[] BoardPitchMin_ByBoard = { BoardPitchMin_Oak, BoardPitchMin_Steel, BoardPitchMin_Bronze };
    internal static readonly float[] BoardPitchMax_ByBoard = { BoardPitchMax_Oak, BoardPitchMax_Steel, BoardPitchMax_Bronze };
    internal static readonly float[] BoardYaw_ByBoard = { BoardYaw_Oak, BoardYaw_Steel, BoardYaw_Bronze };
    internal static readonly float[] BoardScale_ByBoard = { BoardScale_Oak, BoardScale_Steel, BoardScale_Bronze };
    internal static readonly Vector3[] AssetRotation_ByBoard = { AssetRotation_Oak, AssetRotation_Steel, AssetRotation_Bronze };
    internal static readonly float[] AssetYawDegrees_ByBoard = { AssetYawDegrees_Oak, AssetYawDegrees_Steel, AssetYawDegrees_Bronze };
    internal static readonly float[] AssetRollDegrees_ByBoard = { AssetRollDegrees_Oak, AssetRollDegrees_Steel, AssetRollDegrees_Bronze };
    internal static readonly Vector3[] BoardPosOffset_ByBoard = { BoardPosOffset_Oak, BoardPosOffset_Steel, BoardPosOffset_Bronze };
    internal static readonly ButtonShape[] RestButtonShape_ByBoard = { RestButtonShape_Oak, RestButtonShape_Steel, RestButtonShape_Bronze };
    internal static readonly ButtonShape[] GenericButtonShape_ByBoard = { GenericButtonShape_Oak, GenericButtonShape_Steel, GenericButtonShape_Bronze };
    internal static readonly Vector2[] ActiveGridSpacing_ByBoard = { ActiveGridSpacing_Oak, ActiveGridSpacing_Steel, ActiveGridSpacing_Bronze };
    internal static readonly float[] PileScale_ByBoard = { PileScale_Oak, PileScale_Steel, PileScale_Bronze };
    internal static readonly float[] PileSpacing_ByBoard = { PileSpacing_Oak, PileSpacing_Steel, PileSpacing_Bronze };
    internal static readonly float[] ElementsScale_ByBoard = { ElementsScale_Oak, ElementsScale_Steel, ElementsScale_Bronze };
    internal static readonly Vector3[] ClusterOffset_ByBoard = { ClusterOffset_Oak, ClusterOffset_Steel, ClusterOffset_Bronze };
    internal static readonly float[] ClusterScale_ByBoard = { ClusterScale_Oak, ClusterScale_Steel, ClusterScale_Bronze };
    internal static readonly float[] DecisionScale_ByBoard = { DecisionScale_Oak, DecisionScale_Steel, DecisionScale_Bronze };
    internal static readonly Vector3 RestButtonOffset_Oak = new Vector3(0.008f, 0f, -0.007f);         // => [Cards] RestButtonOffset_Oak
    internal static readonly Vector3 RestButtonOffset_Steel = new Vector3(-0.44f, 0f, -0.047f);       // => [Cards] RestButtonOffset_Steel
    internal static readonly Vector3 RestButtonOffset_Bronze = new Vector3(-0.44f, 0f, -0.047f);      // => [Cards] RestButtonOffset_Bronze
    internal const float RestButtonDiameter_Oak = 0.091f;                                             // => [Cards] RestButtonDiameter_Oak
    internal const float RestButtonDiameter_Steel = 0.071f;                                           // => [Cards] RestButtonDiameter_Steel
    internal const float RestButtonDiameter_Bronze = 0.071f;                                          // => [Cards] RestButtonDiameter_Bronze
    internal static readonly Vector3 ConfirmUndoOffset_Oak = new Vector3(-0.008f, 0f, 0.009f);        // => [Cards] ConfirmUndoOffset_Oak
    internal static readonly Vector3 ConfirmUndoOffset_Steel = new Vector3(0.462f, 0.006f, -0.047f);  // => [Cards] ConfirmUndoOffset_Steel
    internal static readonly Vector3 ConfirmUndoOffset_Bronze = new Vector3(0.447f, 0.006f, -0.007f);  // => [Cards] ConfirmUndoOffset_Bronze
    // [Cards] ConfirmUndoSize_{board} is GONE (retired 2026-08: the dial only fed the non-default
    // ROUND cap shape after the button-family split; the cap size is [BoardButtons] Width/Height
    // now, for both shapes). No lines here on purpose — a tuned cfg that still carries the keys is
    // reported as UNMAPPED by scripts/rebase-defaults.py, which is exactly right for retired keys.
    internal static readonly Vector3 SlotOverlayOffset_Oak = new Vector3(0.002f, -0.002f, 0.004f);    // => [Cards] SlotOverlayOffset_Oak
    internal static readonly Vector3 SlotOverlayOffset_Steel = new Vector3(0.018f, -0.002f, 0.004f);  // => [Cards] SlotOverlayOffset_Steel
    internal static readonly Vector3 SlotOverlayOffset_Bronze = new Vector3(0.018f, -0.002f, 0.019f);  // => [Cards] SlotOverlayOffset_Bronze
    internal const float SlotOverlaySpacing_Oak = -0.008f;                                            // => [Cards] SlotOverlaySpacing_Oak
    internal const float SlotOverlaySpacing_Steel = 0.002f;                                           // => [Cards] SlotOverlaySpacing_Steel
    internal const float SlotOverlaySpacing_Bronze = 0.002f;                                          // => [Cards] SlotOverlaySpacing_Bronze
    // THE OVERLAY/CARD SIZE, shared by the blinking wanted-glow and the card that lands in it. 1.45
    // is the retired [Cards] SlotCardFill carried over unchanged, so the card's fit in the physical
    // recess is exactly what it was; the overlay grew 1.36 -> 1.45 to meet it. See PlayTray.4.Slots.
    internal const float SlotOverlayScale_Oak = 1.45f;                                                // => [Cards] SlotOverlayScale_Oak
    internal const float SlotOverlayScale_Steel = 1.45f;                                              // => [Cards] SlotOverlayScale_Steel
    internal const float SlotOverlayScale_Bronze = 1.45f;                                             // => [Cards] SlotOverlayScale_Bronze
    internal static readonly Vector3 InitiativeOffset_Oak = new Vector3(0f, 0.17f, -0.048f);          // => [Cards] InitiativeOffset_Oak
    internal static readonly Vector3 InitiativeOffset_Steel = new Vector3(0f, 0.2f, -0.07f);          // => [Cards] InitiativeOffset_Steel
    internal static readonly Vector3 InitiativeOffset_Bronze = new Vector3(0f, 0.2f, -2e-09f);        // => [Cards] InitiativeOffset_Bronze
    internal const float DecisionGap_Oak = 0.013678f;                                                 // => [Cards] DecisionGap_Oak
    internal const float DecisionGap_Steel = 0.042675f;                                               // => [Cards] DecisionGap_Steel
    internal const float DecisionGap_Bronze = 0.042675f;                                              // => [Cards] DecisionGap_Bronze
    internal static readonly Vector3 PickBannerOffset_Oak = new Vector3(1e-10f, 0.02f, 0f);           // => [Cards] PickBannerOffset_Oak
    internal static readonly Vector3 PickBannerOffset_Steel = new Vector3(0f, 0.095f, 0f);        // => [Cards] PickBannerOffset_Steel
    internal static readonly Vector3 PickBannerOffset_Bronze = new Vector3(1e-10f, 0.06f, 0f);        // => [Cards] PickBannerOffset_Bronze
    // TOOLTIP AREA offsets (key kept as HoverHintOffset_* for cfg compatibility). ZERO IS THE
    // RE-DERIVED DEFAULT for all three boards on purpose: the area's anchor is COMPUTED per board
    // from the tray's measured renderer extents (PlayTray.MeasureBoardLocalExtents — top-left
    // corner of the VISIBLE board, frame included), so "starts top-left" already holds on Oak,
    // Steel and Bronze without a per-board constant that could drift from the real meshes.
    internal static readonly Vector3 HoverHintOffset_Oak = new Vector3(0.12f, 2e-09f, 0f);            // => [Cards] HoverHintOffset_Oak
    internal static readonly Vector3 HoverHintOffset_Steel = new Vector3(0f, 0f, 0f);                 // => [Cards] HoverHintOffset_Steel
    internal static readonly Vector3 HoverHintOffset_Bronze = new Vector3(0f, 0f, 0f);                // => [Cards] HoverHintOffset_Bronze
    internal static readonly Vector3 AssetOffset_Oak = new Vector3(0f, 0f, 0f);                       // => [Cards] AssetOffset_Oak
    internal static readonly Vector3 AssetOffset_Steel = new Vector3(0f, 0f, 0f);                     // => [Cards] AssetOffset_Steel
    internal static readonly Vector3 AssetOffset_Bronze = new Vector3(0f, -0.11f, 0.08f);             // => [Cards] AssetOffset_Bronze
    internal const float AssetPitchDegrees_Oak = 0f;                                                  // => [Cards] AssetPitchDegrees_Oak
    internal const float AssetPitchDegrees_Steel = 0f;                                                // => [Cards] AssetPitchDegrees_Steel
    internal const float AssetPitchDegrees_Bronze = 57f;                                              // => [Cards] AssetPitchDegrees_Bronze
    internal const float RestButtonSpacing_Oak = 0f;                                                  // => [Cards] RestButtonSpacing_Oak
    internal const float RestButtonSpacing_Steel = -0.044f;                                           // => [Cards] RestButtonSpacing_Steel
    internal const float RestButtonSpacing_Bronze = -0.044f;                                          // => [Cards] RestButtonSpacing_Bronze
    internal const float GenericButtonSpacing_Oak = -0.008f;                                          // => [Cards] GenericButtonSpacing_Oak
    internal const float GenericButtonSpacing_Steel = 0.01f;                                          // => [Cards] GenericButtonSpacing_Steel
    internal const float GenericButtonSpacing_Bronze = 0.06f;                                         // => [Cards] GenericButtonSpacing_Bronze
    internal static readonly Vector3 ActiveOffset_Oak = new Vector3(0f, 0f, 0f);                      // => [Cards] ActiveOffset_Oak
    internal static readonly Vector3 ActiveOffset_Steel = new Vector3(0f, 0f, -0.04f);                // => [Cards] ActiveOffset_Steel
    internal static readonly Vector3 ActiveOffset_Bronze = new Vector3(0f, 0f, -0.04f);               // => [Cards] ActiveOffset_Bronze
    internal const float ActiveCardScale_Oak = 1f;                                                    // => [Cards] ActiveCardScale_Oak
    internal const float ActiveCardScale_Steel = 0.82f;                                               // => [Cards] ActiveCardScale_Steel
    internal const float ActiveCardScale_Bronze = 0.82f;                                              // => [Cards] ActiveCardScale_Bronze
    internal static readonly Vector3 PileOffset_Oak = new Vector3(0f, 0f, 0f);                        // => [Cards] PileOffset_Oak
    internal static readonly Vector3 PileOffset_Steel = new Vector3(0f, 0f, -0.04f);                  // => [Cards] PileOffset_Steel
    internal static readonly Vector3 PileOffset_Bronze = new Vector3(0f, 0f, 2e-09f);                 // => [Cards] PileOffset_Bronze
    internal static readonly Vector3 ObjectivesOffset_Oak = new Vector3(0f, 0.026f, 0f);              // => [Cards] ObjectivesOffset_Oak
    internal static readonly Vector3 ObjectivesOffset_Steel = new Vector3(0f, 0.032f, -0.042f);       // => [Cards] ObjectivesOffset_Steel
    internal static readonly Vector3 ObjectivesOffset_Bronze = new Vector3(0f, 0.032f, -0.007f);      // => [Cards] ObjectivesOffset_Bronze
    internal const float ObjectivesScale_Oak = 0.95f;                                                 // => [Cards] ObjectivesScale_Oak
    internal const float ObjectivesScale_Steel = 0.95f;                                               // => [Cards] ObjectivesScale_Steel
    internal const float ObjectivesScale_Bronze = 0.95f;                                              // => [Cards] ObjectivesScale_Bronze
    internal const float ObjectivesWidth_Oak = 0.8f;                                                  // => [Cards] ObjectivesWidth_Oak
    internal const float ObjectivesWidth_Steel = 0.8f;                                                // => [Cards] ObjectivesWidth_Steel
    internal const float ObjectivesWidth_Bronze = 0.8f;                                               // => [Cards] ObjectivesWidth_Bronze
    internal static readonly Vector3 ElementsOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] ElementsOffset_Oak
    internal static readonly Vector3 ElementsOffset_Steel = new Vector3(0f, 0f, -0.04f);              // => [Cards] ElementsOffset_Steel
    internal static readonly Vector3 ElementsOffset_Bronze = new Vector3(0f, 0f, 2e-09f);             // => [Cards] ElementsOffset_Bronze
    internal static readonly Vector3 PinOffset_Oak = new Vector3(0f, 0f, 0f);                         // => [Cards] PinOffset_Oak
    internal static readonly Vector3 PinOffset_Steel = new Vector3(-0.02f, -0.022f, 0f);              // => [Cards] PinOffset_Steel
    internal static readonly Vector3 PinOffset_Bronze = new Vector3(-0.02f, -0.022f, 0f);             // => [Cards] PinOffset_Bronze
    internal static readonly Vector3 ReadoutOffset_Oak = new Vector3(0.008f, -0.004f, -0.024f);       // => [Cards] ReadoutOffset_Oak
    internal static readonly Vector3 ReadoutOffset_Steel = new Vector3(-0.04f, 0.022f, -0.044f);      // => [Cards] ReadoutOffset_Steel
    internal static readonly Vector3 ReadoutOffset_Bronze = new Vector3(-0.04f, 0.022f, -0.004f);     // => [Cards] ReadoutOffset_Bronze
    // The Y of these three is 0 BY RE-BASING, not by never having been tuned (ModBuild 90): the
    // shipped −0.157 moved to PlayTray.DecisionMountBase, where it always belonged, on the build
    // that made this dial actually displace the decision area. Same seat, same picture; the dial
    // now starts from "no displacement". CardsConfig.DecisionOffsetYRebased carries an existing
    // config file across the same step. Do not "restore" the −0.157 — it would drop the whole
    // area (row + prompt text + use bars) 157 mm below the shipped seat.
    internal static readonly Vector3 DecisionOffset_Oak = new Vector3(1e-10f, 0.088f, 1e-10f);        // => [Cards] DecisionOffset_Oak
    internal static readonly Vector3 DecisionOffset_Steel = new Vector3(0f, 0f, 0f);              // => [Cards] DecisionOffset_Steel
    internal static readonly Vector3 DecisionOffset_Bronze = new Vector3(1e-10f, 0f, 0f);             // => [Cards] DecisionOffset_Bronze

    /// <summary>One-shot marker for the DecisionOffset_*.y re-base above — a fresh install must
    /// start false so the (no-op on a fresh file) migration runs once and stamps itself.</summary>
    internal const bool DecisionOffsetYRebased = false;                                              // => [Cards] DecisionOffsetYRebased  (pinned: one-shot migration marker — a fresh install must start false)
}
