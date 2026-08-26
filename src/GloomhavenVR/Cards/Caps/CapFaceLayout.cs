using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE CAP'S FACE BUDGET, AND THE CAPTION THAT HAS TO LIVE IN IT.
///
/// <para><b>THE USER REPORT THIS FILE EXISTS FOR, verbatim (2026-08-25, ModBuild 281 on hardware):</b>
/// <i>"WICHTIG: Der Text muss immer voll lesbar sein. Beim Test hatte ich einen Text der mitten drin
/// abgeschnitten war 'AUSWAHL BEEN' (siehe abgeschnitter_text.jpg). Das darf nicht passieren. Du
/// kannst eventuell auch mit Zeilenumbrüchen arbeiten, wenn es Sinn ergibt."</i>
/// The full string was <c>"Auswahl beenden"</c> — confirmed in his Player.log, line 5208:
/// <c>TOGGLE 0 = True = EREADYBUTTONENDSELECTION = Auswahl beenden = True</c>.</para>
///
/// <para><b>THE ROOT CAUSE, in four links.</b>
/// (1) <c>Core.TmpFit.Fit</c> set <c>overflowMode = TextOverflowModes.Truncate</c>, and its own class
/// doc stated the policy outright: <i>"past the readability floor the text truncates — text is always
/// fully inside its plate."</i> Truncate DISCARDS glyphs, silently, with no flag anyone reads.
/// (2) The auto-size floor was <c>fontSizeMin = 0.08</c>, so below it TMP stopped shrinking and let
/// Truncate take over.
/// (3) <b>ModBuild 281 is what made it bite:</b> the new carved symbol took the caption's room —
/// <c>CapSymbols.LabelBox</c> went from 0.85 to 0.36 of the cap HEIGHT, a 58 % cut. On the BRONZE
/// board, whose seat recess forces the cap down to 53.2 x 43.9 mm, that is a 15.8 mm band, and two
/// lines at the 0.080 floor need 19.2 mm. So the caption could not wrap, could not shrink, and was
/// cut mid-word.
/// (4) <c>BoardButton.SetLabel</c> wrote <c>_label.text</c> and never re-fitted, so even a correct
/// fit computed at BUILD time was the wrong fit for the next string.</para>
///
/// <para><b>WHY THIS COULD NOT BE FIXED BY TUNING TO A CORPUS.</b> The captions are LIVE GAME TEXT.
/// The corpus harvest for this round (every <c>readyButton.Toggle</c> / <c>m_UndoButton</c> /
/// <c>m_SkipButton.Toggle</c> / pick-<c>DialogOption</c> call site in <c>decompiled/</c>) found the
/// longest FIXED string at 23 characters — DE <c>"Wähle eine andere Karte"</c>
/// (<c>GUI_CHOOSE_OTHER_CARD</c>, <c>CardsHandUI.cs:2099</c>) — but it also found that three of the
/// keys this cap can display are <b>format strings with runtime insertions</b> and are therefore
/// UNBOUNDED:
/// <list type="bullet">
/// <item><c>GUI_END_TURN</c> / <c>GUI_END_EXTRA_TURN</c> interpolate the ACTOR'S CLASS NAME
/// (<c>Choreographer.cs:12709</c>);</item>
/// <item><c>GUI_LOSE_CARD</c> / <c>GUI_DISCARD_CARD</c> interpolate the ABILITY CARD TITLE
/// (<c>CardsHandUI.cs:2034/2038</c>) — and that pair is exactly what
/// <c>CardsGameApi.PickDialogOptionLabel(cancel: false)</c> reads;</item>
/// <item><c>GUI_CONFIRM_TARGETS</c> appends a live <c>"{1}/{2}"</c> counter
/// (<c>Choreographer.cs:4918</c>).</item>
/// </list>
/// <b>So there is no longest string, and a fix measured against a list of strings is a fix that is
/// wrong the first time somebody ends a turn as a long-named class.</b> The contract below is a
/// property instead: whatever arrives, every character of it is drawn.</para>
///
/// <para><b>THE CONTRACT.</b>
/// <list type="number">
/// <item>NOTHING IS EVER DISCARDED. <c>FitCaption</c> only ever inserts line breaks; the wire suite
/// asserts <c>StripWhitespace(result) == StripWhitespace(input)</c> over the whole harvested corpus
/// and over adversarial inputs. <c>TextOverflowModes.Truncate</c> appears nowhere in this mod any
/// more.</item>
/// <item>IT SHRINKS AND WRAPS FIRST, down to <see cref="MinFontSize"/> — chosen so a caption at the
/// floor is still ~3.2 mm of glyph height, which is legible at the distance a player reads this
/// board from. Word breaks are preferred; a single token wider than the box (a German compound out
/// of a card title) is HARD-BROKEN rather than allowed to run off the cap.</item>
/// <item>WHEN IT GENUINELY CANNOT FIT, THE FAILURE IS LOUD. <see cref="Caption.Overflows"/> is set,
/// the text is still drawn in full (the caller uses <c>TextOverflowModes.Overflow</c>), and
/// <c>Core.TmpFit</c> logs the string, the box and the size it needed — so the NEXT report names the
/// string instead of somebody finding it in a screenshot.</item>
/// </list></para>
///
/// <para><b>WHY THIS FILE IS FREE OF UNITY BEYOND Mathf/Vector2.</b> Same reason
/// <c>CapCellMath</c>, <c>ButtonStroke</c> and <c>WallStandingProp</c> are: it is linked into
/// <c>tests/GloomhavenVR.WireTests</c> and driven string by string, in CI, without a headset. The
/// defect it closes is invisible to every other gate in this repository — the build compiled, the
/// mirrors agreed, the bundle loaded, the wire coverage was unchanged, and the cap said
/// "AUSWAHL BEEN".</para>
/// </summary>
internal static class CapFaceLayout
{
    // =====================================================================================
    // PART 1 — THE BEZEL PROFILE. One definition, read by CardMesh (geometry),
    //          CapSymbols (where the symbol and the caption go) and
    //          unity/board-prep/buttons/cap_atlas.py (where the symbol is CARVED).
    // =====================================================================================

    /// <summary>
    /// THE SIGNET-PLATE PROFILE, as fractions of the cap's SHORT side. The user, verbatim:
    /// <i>"Auch die Form der Buttons gefällt mir noch nicht. Rund und Viereckig sind vorgabe, aber
    /// ansonsten darfst du gerne kreativ werden."</i>
    ///
    /// <para>Outward-to-inward the cap is now: vertical WALL, 45° outer CHAMFER, flat RIM LAND
    /// facing the viewer, 45° inner chamfer STEPPING DOWN, and the recessed FIELD that carries the
    /// symbol and the caption. The old shape was one flat plateau behind a single 7 mm chamfer.</para>
    ///
    /// <para><b>THE RIM LAND IS THE POINT, and it is a measured choice rather than a stylistic one.</b>
    /// <c>BoardLit</c> shades by world normal against two baked studio directions and never reads a
    /// scene light, so on this surface a 45° chamfer and a flat face are two FIXED values, not a
    /// highlight that moves. A flat land at the cap's frontmost plane, carrying the BRIGHT bevel
    /// material, is therefore the strongest "this is raised" cue available — it is the largest area
    /// of the cap that can hold the bevel tint without being angled away from the key. The two
    /// chamfers either side of it give the silhouette its two value steps.</para>
    ///
    /// <para><b>AND IT PAYS FOR THE CAPTION.</b> The old 7 mm chamfer is 0.159 of Bronze's short side
    /// all by itself; the whole new bezel is 0.135. That is where the caption band's extra
    /// millimetres come from — see <see cref="CaptionBoxWithSymbol"/>.</para>
    /// </summary>
    internal const float BezelChamfer = 0.060f;

    /// <summary>Flat rim land, fraction of the cap's short side. See <see cref="BezelChamfer"/>.</summary>
    internal const float BezelRim = 0.045f;

    /// <summary>Inner chamfer / recess depth, fraction of the cap's short side. The field steps
    /// BACK by this much, so the recess is a true 45° cut and the field's own normal stays −Z (it
    /// must: the caption and the carved symbol both read off a flat, viewer-facing surface).</summary>
    internal const float BezelStep = 0.030f;

    /// <summary>The whole bezel, one number. 0.135 of the cap's short side.</summary>
    internal const float BezelTotal = BezelChamfer + BezelRim + BezelStep;

    /// <summary>
    /// THE RECESSED FIELD, in cap-footprint UV, and why ONE band is correct on both axes of all
    /// three boards.
    ///
    /// <para>The bezel is <see cref="BezelTotal"/> of the cap's SHORT side, and the short side is the
    /// HEIGHT on all three fitted caps (Oak 63.0 x 56.3, Steel 63.0 x 62.1, Bronze 53.2 x 43.9 mm —
    /// <c>BoardAnchors.FitCapSize</c> against each board's measured seat recess). So vertically the
    /// field is EXACTLY [0.135, 0.865] on every board. Horizontally the bezel is the same ABSOLUTE
    /// width, so as a fraction of the width it is 0.111 (Bronze), 0.121 (Oak), 0.133 (Steel) — all
    /// three at or inside 0.135, and the tightest one decides exactly as <c>_seatMinHalf</c> decides
    /// the cap size. One band, six numbers, no per-board case.</para>
    /// </summary>
    internal const float FieldLo = 0.135f;

    /// <summary>The recessed field's far edge in cap-footprint UV. See <see cref="FieldLo"/>.</summary>
    internal const float FieldHi = 1f - FieldLo;

    /// <summary>
    /// THE SPLIT OF THE FIELD on a cap that carries BOTH a carved symbol and live text (Confirm,
    /// Undo, Skip, item-Use), in cap-footprint v measured up from the cap's bottom edge:
    /// <code>
    ///   caption band  0.135 .. 0.585     (0.45 of the cap height = 0.62 of the field)
    ///   gap           0.585 .. 0.615
    ///   symbol band   0.615 .. 0.865     (0.25 of the cap height)
    /// </code>
    /// The caption band GREW from 0.36 to 0.45 of the cap height and the symbol SHRANK from 0.32 to
    /// 0.235 of its cell. <b>That trade is the fix.</b> On Bronze — the smallest cap, and the one in
    /// the user's screenshot — 0.45 x 43.9 mm = 19.8 mm of caption band, which holds TWO LINES at
    /// font size 0.083, where the old 15.8 mm band could not hold two at the 0.080 floor and so had
    /// to cut "Auswahl beenden" in half. One line-count is the whole difference.
    ///
    /// <para>The cost is stated rather than hidden: at the across-the-table 32 px view the ModBuild
    /// 281 render sheet already found the square caps' symbols to be "a 3-4 px dark smudge —
    /// present, not identifiable". A 12 % smaller symbol is 12 % worse at that distance. The user's
    /// requirement is that the TEXT is always fully readable; that is the one he stated, and it
    /// wins.</para>
    ///
    /// <para>MIRRORED IN <c>unity/board-prep/buttons/cap_atlas.py</c> as <c>TEXT_LABEL_BOX</c> /
    /// <c>TEXT_LABEL_CENTRE_DY</c> / <c>TEXT_SIZE</c> / <c>TEXT_CY</c> / <c>PLATEAU_LO,HI</c>. A
    /// change here that is not made there puts a caption straight through a carved symbol, so the
    /// wire suite reads that file as TEXT and asserts the seven numbers agree.</para>
    /// </summary>
    internal static readonly Vector2 CaptionBoxWithSymbol = new(0.73f, 0.45f);

    /// <summary>Centre of the caption band on a symbol cap, as a fraction of the cap HEIGHT from the
    /// cap's centre. See <see cref="CaptionBoxWithSymbol"/>.</summary>
    internal const float CaptionCentreYWithSymbol = -0.14f;

    /// <summary>
    /// The caption box on a cap with NO carved symbol — every cap while the bundle has no atlas, and
    /// the <c>CapRole.Plain</c> caps. The caption owns the whole field and is centred in it.
    ///
    /// <para>This USED to be (0.92, 0.85), which is 0.92 of the cap WIDTH — wider than the plateau it
    /// sat on, so the caption overhung the chamfer and floated in mid-air past the cap's own edge.
    /// That is visible in the user's screenshot: "AUSWAHL BEEN" runs out over the bevel on both
    /// sides. The box is the FIELD now, on both axes, which is what "inside its plate" was always
    /// supposed to mean.</para>
    /// </summary>
    internal static readonly Vector2 CaptionBoxNoSymbol = new(FieldHi - FieldLo, FieldHi - FieldLo);

    // =====================================================================================
    // PART 1b — THE PER-BOARD CONSTRUCTION (ModBuild 290, round 4).
    // =====================================================================================

    /// <summary>How a square cap's corner is cut. The board tells you which: look at its frame.</summary>
    internal enum CapCorner
    {
        /// <summary>A plain right angle — what every cap on every board wore up to ModBuild 289,
        /// and what a cap with NO board (the map room's keycap-skinned furniture) still wears.</summary>
        Sharp = 0,
        /// <summary>Cut off at 45°, the way a joiner relieves a corner. The oak board.</summary>
        Clip = 1,
        /// <summary>Filleted. The steel plate's pressed corner and the bronze board's cast one.</summary>
        Round = 2,
    }

    /// <summary>The hardware at a cap's corners, if any.</summary>
    internal enum CapStud
    {
        None = 0,
        /// <summary>A round dome head. Both metal boards carry these on their own frames.</summary>
        Dome = 1,
    }

    /// <summary>
    /// WHAT THE BOARD'S OWN CONSTRUCTION IS, AS GEOMETRY — the round-4 answer to three rejections.
    ///
    /// <para><b>The complaint, verbatim, twice:</b> <i>"Die sind einheitlich und so sieht das aus
    /// wie aus den 90'igern"</i>, <i>"wie zusammengewürfelte assets mit standard meshs
    /// draufgeklatscht"</i>. Rounds 1, 2 and 3 all answered it with TEXTURE, and round 3's own
    /// generator prompt opens by telling the model that the supplied grey template "is the exact
    /// GEOMETRY of these two plates and must be followed band for band". So all three rounds poured
    /// a different material into ONE profile, identical on all three boards. That is, accurately,
    /// the same mesh with a different texture on it.</para>
    ///
    /// <para><b>Twice this user has volunteered praise, and both times the move was the same one:
    /// the mesh was adapted to the art.</b> <i>"Auf den Texturen sind Schrauben und Halzplatten etc
    /// zu sehen, also eigentlich 3-dimensionale Objekte. Sie werden aber flach nur auf der Textur
    /// dargestellt. Ich möchte, dass du das Mesh an die Textur anpasst."</i> — and, of round 2's
    /// caps, <i>"Mir gefällt dass du die buttons etwas anders vom Mesh her designt hast"</i>. So the
    /// three-dimensional features of the chosen design are built HERE, and the atlas carries only
    /// what is genuinely surface: material, wear, engraving.</para>
    ///
    /// <para><b>EVERY FIELD BELOW LIVES INSIDE THE BEZEL, d ∈ [0, <see cref="BezelTotal"/>], AND
    /// THAT IS THE LOAD-BEARING CONSTRAINT.</b> Nothing here moves <see cref="FieldLo"/>, so the
    /// recessed field is the same size and the same flat viewer-facing plane it has always been.
    /// That is what keeps the caption solver's asserted cases, <c>CapCellMath</c>, the atlas's
    /// <c>TEXT_*</c> mirrors and <c>BoardCapSymbolVectors</c> all true without touching one of them
    /// — and it is what keeps the guarantee the user actually stated, that the TEXT IS ALWAYS FULLY
    /// READABLE. A construction that ate into the field would have bought a nicer rim by shrinking
    /// the caption box, which is how "AUSWAHL BEEN" happened in the first place.</para>
    ///
    /// <para><b>IT IS DERIVED FROM <c>ControlBoard</c> AND IS THEREFORE NOT A NEW WIRE FIELD.</b>
    /// The obvious alternative was a new synced tuning record, and it would have been wrong twice
    /// over: the construction is a property of the BOARD, exactly as its atlas is, and both sides
    /// already agree on the board (<c>RemoteBoardFurniture._style</c> is handed to every cap it
    /// builds, and <c>PlayTray.NewKeycapMaterial</c> is the one call both sides mint materials
    /// through). Deriving it adds no field to sync, no version to mismatch and no way for a peer's
    /// mirror to disagree — the mirror cannot get this wrong without already having the wrong
    /// board.</para>
    /// </summary>
    internal readonly struct CapConstruction
    {
        internal readonly CapCorner Corner;
        /// <summary>Corner clip or fillet, as a fraction of the cap's SHORT side.</summary>
        internal readonly float CornerFrac;
        internal readonly CapStud Stud;
        /// <summary>Stud RADIUS as a fraction of the short side. Sized to sit on the rim land.</summary>
        internal readonly float StudFrac;
        /// <summary>Dentil blocks per straight edge, 0 for none. The oak board's own border.</summary>
        internal readonly int Dentils;
        /// <summary>How far a dentil block stands proud of the rim land, fraction of short side.</summary>
        internal readonly float DentilRise;
        /// <summary>Split the outer chamfer into two stepped terraces. Every board's option R2 has
        /// this; it is the one cue all three sheets agreed on.</summary>
        internal readonly bool Terrace;

        /// <summary>
        /// HOW FAR THE CAP'S BASE STEPS IN BEHIND ITS CROWN, as a fraction of the short side —
        /// an undercut skirt. 0 keeps the plain slab-sided cap.
        ///
        /// <para><b>THIS IS WHERE THE AREA IS, and the first cut of round 4 put everything in the
        /// wrong place.</b> The construction was confined to the bezel, d ∈ [0, 0.135], to protect
        /// the field. That is the right constraint and it had a consequence nobody had measured:
        /// the bezel is 0.135 of the SHORT side — 5.9 mm on bronze — while the cap is
        /// <c>[BoardButtons] Depth</c> THICK, shipped at 36 mm. So the biggest surface on a board
        /// cap by a wide margin is its bare vertical WALL, and the on-board render at the angle a
        /// player actually uses showed exactly that: a tall plain slab with a hairline of detail
        /// along its top edge. Every option on all three sheets is a LOW WIDE button whose edge
        /// profile is most of what you see; ours is a tall block.</para>
        ///
        /// <para>The wall costs nothing to spend, because it is not the field: stepping the base IN
        /// leaves the crown at full footprint, so the bezel bands, <see cref="FieldLo"/> and the
        /// caption box are all untouched. It also cannot foul the seat — the cap only ever gets
        /// NARROWER below the shoulder.</para>
        ///
        /// <para>Widening the bezel instead was the obvious alternative and it is the one thing
        /// this file must not do: the bezel is paid for out of the field, the field is the caption
        /// box, and a smaller caption box is how "AUSWAHL BEEN" happened.</para>
        /// </summary>
        internal readonly float UndercutFrac;

        internal CapConstruction(CapCorner corner, float cornerFrac, CapStud stud, float studFrac,
                                 int dentils, float dentilRise, bool terrace, float undercutFrac)
        {
            UndercutFrac = undercutFrac;
            Corner = corner;
            CornerFrac = cornerFrac;
            Stud = stud;
            StudFrac = studFrac;
            Dentils = dentils;
            DentilRise = dentilRise;
            Terrace = terrace;
        }

        /// <summary>True when this is the plain ModBuild 289 profile and the fast path applies.</summary>
        internal bool IsPlain =>
            Corner == CapCorner.Sharp && Stud == CapStud.None && Dentils <= 0 && !Terrace
            && UndercutFrac <= 0f;

        /// <summary>A cap whose OUTER ZONE is one flat land at the frontmost plane instead of a 45°
        /// chamfer — the square-edged plate the oak board's joinery implies, and the only way its
        /// dentil ring gets a band wide enough to read (0.105 of the short side rather than 0.045).</summary>
        internal bool FlatLand => Dentils > 0;
    }

    /// <summary>
    /// The ModBuild 289 profile, unchanged: a plain right-angled signet plate. This is what a cap
    /// with NO board gets — <c>MapButtonRail</c> and <c>MapTableLegs</c> want the keycap SURFACE and
    /// are not one of the three boards, exactly as <c>PlayTray.NewKeycapMaterial(shader, color)</c>
    /// hands them the shared grain rather than a per-board atlas cell. Their mesh is byte for byte
    /// the one they had before this existed.
    /// </summary>
    internal static readonly CapConstruction PlainConstruction =
        new(CapCorner.Sharp, 0f, CapStud.None, 0f, 0, 0f, terrace: false, undercutFrac: 0f);

    /// <summary>
    /// THE THREE BOARDS' CONSTRUCTIONS, and each number is a claim about the board that can be
    /// checked against <c>.planning/debug/round4/options_contact_sheet.png</c> and against the board
    /// renders beside it on that sheet.
    ///
    /// <list type="bullet">
    /// <item><b>Oak — clipped corners, no hardware, a dentil ring.</b> The oak board's frame is a run
    /// of small raised rectangular blocks, its corners are square and unrelieved on the outline but
    /// its carved recesses are all clipped, and there is NO METAL anywhere on its face: no rivet, no
    /// strap, no boss. So the oak cap gets the joiner's 45° corner relief and the board's own
    /// dentil border at cap scale (option OA-S3, the strongest image on any of the three sheets),
    /// and it gets no studs — the one board whose whole story is that it has no fittings.</item>
    /// <item><b>Steel — rounded corners, four dome RIVETS.</b> Its border is dentils with small round
    /// rivet heads set among them. Rivets, not screws: option ST-S3's slotted screws were the
    /// better-looking cap and the worse match, because there is not one screw slot anywhere on that
    /// board.</item>
    /// <item><b>Bronze — heavily rounded corners, four dome BOSSES, the deepest terrace.</b> Every
    /// corner and every outline on that board is rounded, and its border carries both square beads
    /// and large dome-headed bosses. Its corner fraction is the largest of the three because the
    /// board's own radius is.</item>
    /// </list>
    ///
    /// <para>Indexed by <c>(int)ControlBoard</c>. <c>ControlBoard</c> itself is not visible from this
    /// file (it lives beside Unity types); the caller does the cast, and
    /// <see cref="ConstructionFor"/> clamps.</para>
    /// </summary>
    private static readonly CapConstruction[] _byBoard =
    {
        // corner            frac    stud             frac    dentils  rise    terrace  undercut
        // Oak's DentilRise was 0.016 first and that was measured wrong rather than chosen wrong:
        // 0.016 of a 56.3 mm short side is a 0.9 mm block on a 4.1 mm footprint, and the on-board
        // render at the angle a player actually uses showed it as a row of hairline ticks with no
        // body — the block's own front face is coplanar-looking against the rim land it stands on,
        // so all it contributed was two thin side slivers. 0.034 is 1.9 mm, which reads as a
        // crenellation instead of a scratch. Judged on the picture, not on the number.
        // Oak drops the TERRACE, and that is a choice about the board rather than a saving. Its
        // signature is a run of square blocks, not a stepped moulding, and a terrace would have
        // eaten the same 0.060 band the dentil ring needs. FlatLand (from Dentils > 0) squares off
        // the outer edge, which is what a joiner's plate does and what OA-S3 shows.
        new(CapCorner.Clip,  0.100f, CapStud.None,    0f,     7,       0.034f, false, 0.085f),  // Oak
        new(CapCorner.Round, 0.100f, CapStud.Dome,    0.030f, 0,       0f,    true,  0.070f),  // Steel
        new(CapCorner.Round, 0.160f, CapStud.Dome,    0.034f, 0,       0f,    true,  0.095f),  // Bronze
    };

    /// <summary>The construction for a board index, or <see cref="PlainConstruction"/> for a cap
    /// with no board. Out-of-range clamps rather than throwing: a cap drawn with the wrong bezel is
    /// a cosmetic defect and an exception here would take the whole board's build down with it.</summary>
    internal static CapConstruction ConstructionFor(int? boardIndex)
    {
        if (boardIndex is not int i)
            return PlainConstruction;
        if (i < 0 || i >= _byBoard.Length)
            return PlainConstruction;
        return _byBoard[i];
    }

    /// <summary>Board count this table covers — pinned by the wire suite against
    /// <c>ControlBoardStyles.Count</c> so a fourth board cannot be added without a construction.</summary>
    internal static int ConstructionCount => _byBoard.Length;

    // =====================================================================================
    // PART 2 — THE CAPTION SOLVER.
    // =====================================================================================

    /// <summary>
    /// The floor the caption may shrink to (TMP world font-size units; ~0.1 m per em at size 1.0,
    /// so 0.045 is a 4.5 mm em and roughly 3.2 mm of glyph height).
    ///
    /// <para>It is LOWER than the 0.080 the old auto-size used, and deliberately: 0.080 was the point
    /// at which the old code STOPPED SHRINKING AND STARTED CUTTING. A smaller-but-whole caption is
    /// readable; half a caption is not, at any size. Below this floor the text is still drawn in
    /// full — the floor bounds how small the mod will shrink on its own, not what it will draw.</para>
    /// </summary>
    internal const float MinFontSize = 0.045f;

    /// <summary>The size below which a fit is reported as CRAMPED in the log — not a failure, a
    /// heads-up that this board/string pair is near the edge. Kept at the old auto-size floor so the
    /// number in the log means the same thing it used to.</summary>
    internal const float CrampedFontSize = 0.080f;

    /// <summary>
    /// THE CEILING, per meter of box height (about 65 % line fill). It is the same rule
    /// <c>Core.TmpFit.Fit</c> has always applied to its <c>maxFontSize</c>, kept here so a SHORT
    /// caption on the new box is the size the user has already seen and accepted rather than
    /// suddenly filling its band.
    ///
    /// <para>It is a CEILING and never a floor: a caption that needs to be smaller still gets
    /// smaller, and one that cannot fit still overflows and is logged. Without it the callers'
    /// <c>maxFontSize: 0.40</c> would be clamped only by the box, and "USE" on the Bronze cap would
    /// come out 76 % larger than "Confirm" beside it — same board, same row of keys.</para>
    /// </summary>
    internal const float CapPerMeterHeight = 6.5f;

    /// <summary>Bisection steps for the size solve. 40 halvings of a range under 1.0 resolves far
    /// past any size that can matter; the loop is pure arithmetic over a memoized width table.</summary>
    private const int SolveSteps = 40;

    /// <summary>
    /// The outcome of laying a caption into a box: the text WITH its line breaks, the size to draw
    /// it at, and whether it had to give up.
    /// </summary>
    internal readonly struct Caption
    {
        /// <summary>The caption as it must be handed to TMP — the input with <c>'\n'</c> inserted at
        /// the chosen breaks and nothing else changed. Never shorter in characters than the input.</summary>
        internal readonly string Text;

        /// <summary>The TMP world font size to draw it at.</summary>
        internal readonly float FontSize;

        /// <summary>How many lines the layout uses.</summary>
        internal readonly int Lines;

        /// <summary>Width of the widest line, in local meters at <see cref="FontSize"/>.</summary>
        internal readonly float BlockWidth;

        /// <summary>Height of the whole block, in local meters at <see cref="FontSize"/>.</summary>
        internal readonly float BlockHeight;

        /// <summary>TRUE when even <see cref="MinFontSize"/> could not get the block inside the box.
        /// The caller draws it anyway (overflowing) and logs it — see the contract in the class
        /// header. This is the flag that replaces a silent Truncate.</summary>
        internal readonly bool Overflows;

        /// <summary>TRUE when a token had to be split MID-WORD because no font size in range made it
        /// fit on one line. Nothing is dropped; the break is just not at a space. Logged, because a
        /// mid-word break is the visual shape of the very defect this file closes and a reader has
        /// to be able to tell the two apart.</summary>
        internal readonly bool HardBroke;

        internal Caption(string text, float fontSize, int lines, float w, float h,
                         bool overflows, bool hardBroke)
        {
            Text = text;
            FontSize = fontSize;
            Lines = lines;
            BlockWidth = w;
            BlockHeight = h;
            Overflows = overflows;
            HardBroke = hardBroke;
        }
    }

    /// <summary>
    /// Lay <paramref name="text"/> into a <paramref name="boxWidth"/> x <paramref name="boxHeight"/>
    /// box (local meters) at the largest font size that fits, never dropping a character.
    ///
    /// <para><paramref name="widthAtUnitFont"/> measures a single line's width in local METERS as it
    /// would be drawn at font size 1.0, with wrapping OFF. <paramref name="lineStepAtUnitFont"/> is
    /// the baseline-to-baseline distance in the same terms. Both come from the real TMP object at
    /// runtime (<c>Core.TmpFit</c>) and from a stated synthetic model in the wire tests — which is
    /// the whole reason the solve is a pure function of those two numbers.</para>
    ///
    /// <para><b>WHY BISECTION AND NOT A DIRECT SOLVE.</b> Let <c>Fits(f)</c> be "wrap greedily at an
    /// available width of <c>boxWidth / f</c>, then the block fits". Raising f narrows the available
    /// width in unit terms, which can only ADD lines, and multiplies the block height by f — so both
    /// terms of the height test are non-decreasing in f, and the width test is satisfied by
    /// construction wherever a break is possible. <c>Fits</c> is therefore MONOTONE and bisection
    /// returns the exact largest fitting size. A greedy shrink-until-it-fits loop (the obvious
    /// alternative) can stop early: shrinking may drop a line, which would have allowed a LARGER
    /// size than the one it stopped at.</para>
    /// </summary>
    internal static Caption FitCaption(string text, float boxWidth, float boxHeight,
                                       float maxFontSize,
                                       Func<string, float> widthAtUnitFont,
                                       float lineStepAtUnitFont)
    {
        string src = text ?? string.Empty;
        if (widthAtUnitFont == null || lineStepAtUnitFont <= 0f
            || boxWidth <= 0f || boxHeight <= 0f)
        {
            // Nothing measurable: hand the text back untouched at the requested size rather than
            // inventing a layout. Never returns a SHORTER string, which is the only invariant that
            // has to hold on every path through this method.
            float safe = maxFontSize > 0f ? maxFontSize : CrampedFontSize;
            return new Caption(src, safe, 1, 0f, 0f, overflows: true, hardBroke: false);
        }

        // The height-derived ceiling, exactly as Core.TmpFit.Fit has always applied it.
        float heightCap = boxHeight * CapPerMeterHeight;
        maxFontSize = maxFontSize > 0f ? Mathf.Min(maxFontSize, heightCap) : heightCap;

        var tokens = Tokenize(src);
        if (tokens.Count == 0)
            return new Caption(src, Mathf.Max(MinFontSize, Mathf.Min(maxFontSize, CrampedFontSize)),
                               0, 0f, 0f, overflows: false, hardBroke: false);

        var memo = new Dictionary<string, float>(64);
        float Measure(string s)
        {
            if (s.Length == 0)
                return 0f;
            if (memo.TryGetValue(s, out float w))
                return w;
            w = Mathf.Max(0f, widthAtUnitFont(s));
            memo[s] = w;
            return w;
        }

        // WHOLE WORDS FIRST, AND THAT ORDER IS THE POINT. Pass 1 forbids mid-word breaks
        // entirely; only if no size in range gets the text inside the box that way does pass 2
        // allow them.
        //
        // It costs size, and it is worth it. Measured on the real cap boxes with a real serif
        // face: "Bewegung überspringen" on the OAK cap solves to font 0.072 WITH breaks — three
        // lines reading "Bewegung / überspringe / n" — and to 0.057 without, two lines reading
        // "Bewegung / überspringen". The bigger one is bigger and it is worse: a line holding a
        // single orphan letter is the exact SHAPE the user reported ("AUSWAHL BEEN"), and a
        // player cannot tell a deliberate break from a cut. So the solver takes the whole words.
        var caption = Solve(tokens, boxWidth, boxHeight, maxFontSize, Measure, lineStepAtUnitFont,
                            allowHardBreak: false);
        if (caption.Overflows)
            caption = Solve(tokens, boxWidth, boxHeight, maxFontSize, Measure, lineStepAtUnitFont,
                            allowHardBreak: true);
        return caption;
    }

    /// <summary>One solve at a fixed break policy — see <see cref="FitCaption"/> for both halves
    /// and for why the bisection is exact.</summary>
    private static Caption Solve(List<string> tokens, float boxWidth, float boxHeight,
                                 float maxFontSize, Func<string, float> measure,
                                 float lineStepAtUnitFont, bool allowHardBreak)
    {
        float hi = Mathf.Max(MinFontSize, maxFontSize);

        // Does the block fit at this size? (See the monotonicity argument in the doc comment.)
        bool Fits(float f)
        {
            Layout(tokens, boxWidth / f, measure, allowHardBreak,
                   out _, out int lines, out float widest, out _);
            return widest * f <= boxWidth + 1e-6f
                   && lines * lineStepAtUnitFont * f <= boxHeight + 1e-6f;
        }

        float chosen;
        if (Fits(hi))
        {
            chosen = hi;
        }
        else if (!Fits(MinFontSize))
        {
            chosen = MinFontSize; // the loud case — laid out below, flagged, and still drawn whole
        }
        else
        {
            float lo = MinFontSize;
            for (int i = 0; i < SolveSteps; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Fits(mid))
                    lo = mid;
                else
                    hi = mid;
            }
            chosen = lo;
        }

        Layout(tokens, boxWidth / chosen, measure, allowHardBreak,
               out string laid, out int lineCount, out float widestLine, out bool hardBroke);
        float blockW = widestLine * chosen;
        float blockH = lineCount * lineStepAtUnitFont * chosen;
        bool overflows = blockW > boxWidth + 1e-6f || blockH > boxHeight + 1e-6f;
        return new Caption(laid, chosen, lineCount, blockW, blockH, overflows, hardBroke);
    }

    // ------------------------------------------------------------------ the layout ------------

    /// <summary>
    /// Split on whitespace, keeping every non-whitespace run as one token. Existing <c>'\n'</c> in
    /// the source is treated as a plain separator rather than honoured: the game's captions are
    /// single-line strings, and honouring an embedded newline would let a source string dictate a
    /// layout that cannot fit — the exact failure mode being closed here.
    /// </summary>
    private static List<string> Tokenize(string s)
    {
        var outp = new List<string>(8);
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            if (i > start)
                outp.Add(s.Substring(start, i - start));
        }
        return outp;
    }

    /// <summary>
    /// GREEDY WRAP AT A FIXED AVAILABLE WIDTH (in unit-font meters), with a mid-word break as the
    /// last resort. <b>The only two operations are "start a new line" and "split a token" — no path
    /// through this method removes a character</b>, which is the property the wire suite asserts.
    /// </summary>
    private static void Layout(List<string> tokens, float avail, Func<string, float> measure,
                               bool allowHardBreak,
                               out string text, out int lines, out float widest, out bool hardBroke)
    {
        var sb = new StringBuilder(64);
        int lineCount = 0;
        float widestSoFar = 0f;
        bool broke = false;
        float cur = 0f;              // width of the line being built
        bool lineHasContent = false;
        float space = measure(" ");

        // Locals rather than the out parameters: C# forbids capturing an `out` in a local function,
        // and the wrap reads far worse written without one.
        void EndLine()
        {
            if (!lineHasContent)
                return;
            if (cur > widestSoFar) widestSoFar = cur;
            lineCount++;
            cur = 0f;
            lineHasContent = false;
        }

        for (int t = 0; t < tokens.Count; t++)
        {
            string tok = tokens[t];
            while (tok.Length > 0)
            {
                float tw = measure(tok);
                float withSep = lineHasContent ? cur + space + tw : tw;
                if (withSep <= avail || (!lineHasContent && tw <= avail))
                {
                    if (lineHasContent)
                    {
                        sb.Append(' ');
                        cur += space;
                    }
                    sb.Append(tok);
                    cur += tw;
                    lineHasContent = true;
                    break;
                }
                if (lineHasContent)
                {
                    // The token does not fit after what is already on this line: break the LINE
                    // first and re-judge the token alone against a full-width line.
                    EndLine();
                    sb.Append('\n');
                    continue;
                }
                if (!allowHardBreak)
                {
                    // PASS 1: whole words only. The token goes on its own line and OVERHANGS, which
                    // makes `widest` exceed `avail` and therefore makes this size not fit — driving
                    // the bisection down until either the word fits whole or the floor is reached
                    // and pass 2 takes over. It must still be EMITTED: a pass-1 layout is a real
                    // candidate result and dropping the token here would be the defect itself.
                    sb.Append(tok);
                    cur += tw;
                    lineHasContent = true;
                    break;
                }
                // The token does not fit even alone on an empty line. Cut it — at the largest
                // prefix that fits, and never at zero characters (a single glyph wider than the
                // whole box still gets drawn, and the caller reports the overflow).
                int keep = LongestPrefixThatFits(tok, avail, measure);
                broke = true;
                string head = tok.Substring(0, keep);
                sb.Append(head);
                cur += measure(head);
                lineHasContent = true;
                tok = tok.Substring(keep);
                if (tok.Length > 0)
                {
                    EndLine();
                    sb.Append('\n');
                }
            }
        }
        EndLine();
        text = sb.ToString();
        lines = lineCount > 0 ? lineCount : 1;   // an all-whitespace caption still occupies one line
        widest = widestSoFar;
        hardBroke = broke;
    }

    /// <summary>
    /// Longest prefix of <paramref name="tok"/> whose width is within <paramref name="avail"/>, by
    /// binary search over the prefix length (the widths are non-decreasing in length, so the search
    /// is exact). Returns at least 1: a mid-word break must always make progress, or the wrap loop
    /// would not terminate — and a caption that hangs the game is a worse defect than a caption that
    /// overflows its plate.
    /// </summary>
    private static int LongestPrefixThatFits(string tok, float avail, Func<string, float> measure)
    {
        int lo = 1, hi = tok.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (measure(tok.Substring(0, mid)) <= avail)
                lo = mid;
            else
                hi = mid - 1;
        }
        return Mathf.Clamp(lo, 1, tok.Length);
    }

    // ------------------------------------------------------------------ the property ----------

    /// <summary>
    /// Every non-whitespace character of a string, in order. <c>StripWhitespace(before) ==
    /// StripWhitespace(after)</c> is the one thing "the caption was not cut off" means, and it is
    /// what the wire suite asserts over the harvested corpus, over adversarial inputs and over a
    /// NULL input. It is here rather than in the test so the property is stated next to the code it
    /// constrains.
    /// </summary>
    internal static string StripWhitespace(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var sb = new StringBuilder(s!.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        return sb.ToString();
    }
}
