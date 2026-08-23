using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE ENCHANTRESS' CARD LIST BELONGS IN THE ENCHANTRESS' WINDOW.
///
/// <para><b>USER REPORT, 2026-08-23, verbatim:</b> <i>"2) Bei der Magiererin muss man Karten
/// auswählen, um diese dann zu verbessern. Die Kartenauswahl für die Magierin spawnt hingegen auf dem
/// Fenster der Character-UI (siehe magierin.jpg). Das soll nicht sein, die Kartenauswahl soll auf der
/// linken Seite des Fensters (IN dem Fenster) der Magierin enthalten sein, dass man alles was die
/// Magierin betrifft auch in diesem Fenster erledigen kann."</i></para>
///
/// <para>The photograph <c>.planning/debug/magierin.jpg</c> shows three columns: the character UI
/// ("DIE LÖSCHER"), the card list ("GEMEISTERTE FERTIGKEITEN") flush against its right edge on the
/// SAME dark slab, and — a window's width away to the right — the "MAGIERIN" window with the selected
/// card and its "KAUFEN" column. Everything the enchantress does happens in the third window except
/// the one act that starts it.</para>
///
/// <para><b>THE TWO OBJECTS, NAMED FROM SOURCE AND FROM THE LOG — NOT FROM THE CAPTION.</b></para>
/// <list type="bullet">
///   <item><description><b>THE LIST</b> is
///   <c>UIPartyCharacterEnhancementAbilityCardsDisplay</c>, reached as
///   <c>NewPartyDisplayUI.PartyDisplay.EnhancementCardsDisplay</c> (NewPartyDisplayUI.cs:97 and :262).
///   Its GameObject is <c>'Enhance Ability Cards Content Display'</c> and it is a CHILD of the party
///   display prefab — the ModBuild 152 census printed the whole path,
///   <c>…/New Party display Variant/Enhance Ability Cards Content Display/Container/Header/Title</c>
///   (Player.log:1872). Its authored width is 368 px (the ModBuild 196 sub-view census, quoted in
///   CanvasConversion.3.Fit.cs:1138).</description></item>
///   <item><description><b>THE ENCHANTRESS WINDOW</b> is <c>UINewEnhancementWindow</c> on
///   <c>'Enhancements Window Variant'</c>, <c>UIWindowID.EnhancementShop</c>, path
///   <c>Campaign Canvas/UI Guildmaster HUD/Enhancements Window Variant</c>, authored rect 1920x1080 —
///   the WINDOW IDENTITY line at Player.log:5745, printed by this mod's own catch-all. It is reached
///   here as <c>GuildmasterDestinations.ModeWindow(EGuildmasterMode.Enchantress)</c>, i.e. off
///   <c>UIGuildmasterHUD.enhancementWindow</c>'s own serialized reference — never by name, never by a
///   scene sweep.</description></item>
/// </list>
///
/// <para><b>THEIR RELATIONSHIP: UNRELATED SUBTREES THAT SHARE ONE COMPONENT INSTANCE.</b> They are
/// not parent/child and not siblings — one lives under <c>Campaign Canvas/UI Guildmaster HUD</c>, the
/// other under the party display. <c>UINewEnhancementWindow</c> holds the SAME
/// <c>UIPartyCharacterEnhancementAbilityCardsDisplay</c> instance in its own
/// <c>[SerializeField] cardsDisplay</c> (UINewEnhancementWindow.cs:65) and drives it directly —
/// <c>cardsDisplay.Display(…)</c> on character pick (:223), <c>Deselect</c> (:253), <c>Hide</c>
/// (:264), <c>OnAddedEnhancement</c> (:542), <c>RefreshMode</c> (:629/:648). The scene wires one
/// object into two owners; the game's own navigation state agrees
/// (<c>EnhancmentCardSelectState</c> reaches it through
/// <c>NewPartyDisplayUI.PartyDisplay.EnhancementCardsDisplay</c>). So the list is the enchantress'
/// instrument that merely LIVES in the party display's prefab.</para>
///
/// <para><b>WHY IT RENDERS WHERE IT DOES TODAY — AND IT IS NOT AN ADOPTION.</b> The log settles it:
/// there is no <c>Adopted nested canvas 'Enhance Ability Cards …'</c> line anywhere in the session
/// (the only adoptions into <c>Panel_Modal_New Party display</c> are 'Content', 'Full',
/// 'UI Party Inventory Item Tooltip' and 'UI Battle Goal Picker Window'), and there is no
/// <c>MAP ROOM: window … is NOT floated on its own</c> line for it either — because it carries NO
/// <c>UIWindow</c> at all and therefore can never be a float, an adoption or a "parent wins" subject.
/// It renders inside the character UI for the plainest possible reason: <b>it is a child transform of
/// the party display</b>, and this mod's fixed-fit branch recognises it as ONE OF THE SIX SERIALIZED
/// SUB-VIEW ROOTS (<c>CanvasConversion.CollectActiveSubViews</c> asks
/// <c>display.EnhancementCardsDisplay</c> by reference, CanvasConversion.3.Fit.cs:2806) and SEATS it
/// against the character column's right edge inside the party window
/// (the ModBuild 200 seam, CanvasConversion.3.Fit.cs:1272). The seam is why it is flush against the
/// column with no gap — the mod put it exactly where it was told to. The photograph is the fixed-fit
/// working correctly on a sub-view that belongs somewhere else.</para>
///
/// <para><b>WHY THIS IS A PARK AND NOT ONE OF THE OTHER TWO SHAPES.</b> The repo has three ways to
/// draw X inside Y and this reuses <c>StoryComposite</c>'s, with its discipline copied verbatim
/// (record the home BEFORE the first move, own the size, re-measure live every tick, hand back
/// verbatim, refuse nothing and claim nothing):</para>
/// <list type="bullet">
///   <item><description><b>The adoption path</b> (<c>ModalFallback.10.CatchAll</c> /
///   <c>CanvasConversion.2.Adopt</c>) draws a NESTED CANVAS inside its host without moving anything —
///   it is the right tool when the object is already laid out inside the host. Here the object is
///   laid out inside the WRONG host, and adoption cannot move it. It also needs a <c>Canvas</c> and a
///   <c>UIWindow</c> to hang the decision on, and the list has neither.</description></item>
///   <item><description><b><c>MapTravelConfirm</c></b> parks a game control into a floated window at
///   a measured anchor and is the closest in spirit, but it is built around a control whose home is a
///   container it can be lifted out of by <c>FieldInfo</c> and whose destination it must SWEEP to
///   find a free edge. Here both ends are known by reference and the destination's free band is
///   measurable directly.</description></item>
///   <item><description><b><c>StoryComposite</c></b> is the exact analogue — one rect out of window A
///   into window B, restored verbatim — and it carries two rounds of corrections this class would
///   otherwise have had to re-learn: the ModBuild 232 lesson that collapsing a STRETCH child's
///   anchors to a point sets its drawn size to zero (so the measured size must be captured BEFORE the
///   anchor write and re-asserted every tick), and the ModBuild 234 lesson that a placement must be
///   made against measured INK, not against an authored rect.</description></item>
/// </list>
///
/// <para><b>HOW THE ANCHOR IS DERIVED — FROM THE ENCHANTRESS WINDOW'S OWN GEOMETRY, LIVE.</b> No
/// hand-tuned constant decides where the list goes. Each tick:</para>
/// <list type="number">
///   <item><description>the enchantress window's own PAINTED content is unioned with the fit's own
///   visibility verdict (<c>CanvasConversion.CountsAsFitContent</c> — the same predicate the host fit
///   uses, so the placement and the fit can never disagree about what is drawn), <b>excluding the
///   parked list itself</b> and <b>excluding full-frame backdrop plates</b> by the fit's own plate
///   test (width ≥ 0.80 and height ≥ 0.95 of the frame — <c>FixedFitPlate*Fraction</c>). The plate
///   exclusion is load-bearing: <c>'Enhancements Window Variant'</c> carries an <c>Image</c> on its
///   own root (the WINDOW IDENTITY component list at Player.log:5745), so without it the ink would be
///   the whole frame and there would be no band at all;</description></item>
///   <item><description>the SLOT is the band between the window's left rect edge and the left edge of
///   that ink, full window height;</description></item>
///   <item><description>the list is scaled by <c>min(slotW/listW, slotH/listH, 1)</c> — never
///   magnified — and CENTRED in the slot. Centring is what supplies the margin: the gap on each side
///   is half the slack, so it is derived from the two measurements and there is nothing to
///   tune.</description></item>
/// </list>
///
/// <para><b>THE LAYER TRANSFER, WHICH NEITHER EXISTING PARK NEEDED.</b> This is the first park that
/// crosses two converted panels of which one is SUPERSAMPLED. 'New Party display' runs on a private
/// capture layer (26 in the logged session) and every transform under it has been swept onto it;
/// 'Enhancements Window Variant' is not supersampled and draws straight into the eye. Re-parenting
/// alone would leave the whole list on layer 26, rendered by nothing but the party panel's capture
/// camera — which no longer frames it — i.e. [[one-shared-layer-leaks]] in its purest form: a
/// perfectly parked, perfectly sized, totally invisible list. So the park writes the DESTINATION
/// WINDOW ROOT'S OWN LAYER over the moved subtree and the hand-back writes the HOME PARENT'S OWN
/// LAYER back. Both are read live off the object the list is about to sit beside, so neither can go
/// stale: whatever supersampling does to either panel while the list is away, "the layer my new
/// siblings are on" is right by construction, and the owning panel's next sweep finds the value it
/// would have written anyway.</para>
///
/// <para><b>IT CANNOT PRODUCE ANY OF THE THREE ModBuild 231/232/233/234 DEADLOCKS, AND THE REASON IS
/// STRUCTURAL RATHER THAN CAREFUL.</b> The list is not a <c>UIWindow</c>. This class therefore never
/// enters a convert-loop decision at all: it holds NOTHING back (no <c>HoldsBack</c>, no
/// <c>FloatRefusalTable</c> row, no <c>CurtainRefuses</c>), so the churn fuse has nothing of ours to
/// count and cannot session-suppress anything; it raises NO CLAIM, so no claim can outlive the thing
/// it was raised for and strand a window; and the list can never float on its own, so it can never be
/// adopted and converted at the same time — the <c>DOUBLE HOST</c> audit in
/// <c>ModalFallback.10.CatchAll</c> has nothing to find here. The one window whose presentation this
/// changes is the party display, and it changes it by REMOVING a sub-view from its fit for the
/// duration, which is the fit's ordinary no-sub-view state.</para>
///
/// <para><b>A WINDOW MUST NEVER BE INVISIBLE.</b> Every precondition that is not yet true is a WAIT
/// at Info — the enchantress window not floated, the geometry not measurable, the list not laid out,
/// the slot too narrow to hold the list at a readable scale — and a WAIT leaves the list exactly
/// where it is today, drawn by the party display's fixed fit as it always was. Nothing here can
/// produce a state in which the list is drawn nowhere.</para>
///
/// <para><b>MULTIPLAYER.</b> Local presentation only: two transform re-parents and a layer write, on
/// this client's own scene objects, driven by this client's own <c>UIGuildmasterHUD</c>. Nothing is
/// read from or written to the wire, and no game state is touched — no <c>Show</c>, no <c>Hide</c>,
/// no <c>SetActive</c>, no <c>CanvasGroup</c>. A second player runs the identical code against his
/// own singletons.</para>
///
/// <para>GREP: <c>ENCHANTRESS COMPOSITE PARKED</c> / <c>ENCHANTRESS COMPOSITE HANDED BACK</c> /
/// <c>ENCHANTRESS COMPOSITE WAIT</c> / <c>ENCHANTRESS COMPOSITE ONE WINDOW: CONFIRMED</c> /
/// <c>ENCHANTRESS COMPOSITE ONE WINDOW: NOT ACHIEVED</c>.</para>
/// </summary>
internal static class EnchantressComposite
{
    private const string Scope = "WorldUI";

    /// <summary>A rect smaller than this in either axis has not been laid out yet — the same floor
    /// and the same reason as <c>StoryComposite.MinParkSizePx</c>.</summary>
    private const float MinParkSizePx = 2f;

    /// <summary>Below this the re-place writes nothing. <c>StoryComposite.OffsetEpsilonPx</c>'s value
    /// and its reason: the pose is computed from float measurements, so exact equality would be
    /// defeated by the last bit while anything visible is orders of magnitude above it.</summary>
    private const float OffsetEpsilonPx = 0.5f;

    /// <summary>
    /// The smallest uniform scale the park may draw the list at. Below it the park is declined as a
    /// WAIT and the list stays where the fixed fit has it.
    ///
    /// <para>DERIVED, not picked: the ModBuild 200 legibility note measured this game's body caps at
    /// 13.5 authored px and the character window at 0.875 mm per authored px, i.e. ~34 arc-minutes at
    /// the 1.2 m reading distance. Half of that is ~17 arc-minutes, which is roughly where the same
    /// note puts the floor of comfortable reading. A slot that cannot hold the list at half size is
    /// not a place to put a list of card names, and the honest answer there is to leave it where the
    /// player can already read it. On the shipped prefab this branch does not run: the list is 368 px
    /// wide against a band of several hundred, so the scale comes out at 1.</para>
    /// </summary>
    private const float MinReadableScale = 0.5f;

    /// <summary>The fit's own full-frame plate test, borrowed by value so the band this class
    /// measures and the union the fit measures agree about what a backdrop is. See
    /// <c>CanvasConversion.FixedFitPlateWidthFraction</c> / <c>…HeightFraction</c>.</summary>
    private const float PlateWidthFraction = 0.80f;
    private const float PlateHeightFraction = 0.95f;

    /// <summary>Slack allowed by the falsifier's containment test, in the enchantress window's own
    /// authored px. The list is placed to fit exactly, and a full-height list against a full-height
    /// window is an exact float comparison — two pixels of tolerance is below anything the player can
    /// see and above the last bit of the arithmetic.</summary>
    private const float ContainTolerancePx = 2f;

    /// <summary>How far out of the character UI's own plane a point may be and still count as "inside
    /// that window", in that window's authored px (one px ≈ 0.875 mm on the measured host, so this is
    /// ~44 mm). Two panels that are literally coplanar and touching would be caught; two panels a
    /// window's width apart in the map room's arc are not. This is the GEOMETRIC half of the
    /// falsifier's second clause — the ancestry half is exact on its own.</summary>
    private const float CharacterUiPlaneDepthPx = 50f;

    // ---- the park ------------------------------------------------------------------------------

    private static RectTransform? _parked;
    private static UIWindow? _parkHost;

    private static Transform? _home;
    private static int _homeIndex;
    private static Vector2 _homeAnchorMin;
    private static Vector2 _homeAnchorMax;
    private static Vector2 _homePivot;
    private static Vector2 _homeAnchoredPos;
    private static Vector2 _homeSizeDelta;
    private static Quaternion _homeRotation = Quaternion.identity;
    private static Vector3 _homeScale = Vector3.one;
    private static LayoutElement? _addedIgnore;

    /// <summary>The value <c>ignoreLayout</c> had on a LayoutElement that was ALREADY on the list, so
    /// the hand-back restores what was there instead of assuming false. StoryComposite assumes false
    /// and gets away with it because its subject has no LayoutElement; "restore verbatim" is the rule
    /// and a field that is guessed at is not restored verbatim.</summary>
    private static bool _homeIgnoreLayout;

    /// <summary>The size the composite OWNS for the parked list, captured BEFORE the anchor write —
    /// the ModBuild 232 lesson. A stretch child's <c>sizeDelta</c> is an INSET PAIR, so collapsing its
    /// anchors to a point without writing the measured size leaves it 0x0.</summary>
    private static Vector2 _parkedSize;

    /// <summary>The character UI window the list came out of, resolved once at park time by walking
    /// UP from the home parent to the nearest ancestor <c>UIWindow</c>. That is a question about
    /// ANCESTRY and is exactly what the walk answers — not a "does it relate to an X" test
    /// ([[containment-is-not-identity]]).</summary>
    private static RectTransform? _homeWindowRect;
    private static string _homeWindowName = "<unresolved>";

    /// <summary>Per-transform layer the park overwrote, so the hand-back can state what it restores.
    /// The VALUE written back is re-read live from the home parent (see <see cref="WriteLayer"/>);
    /// this list exists so the two log lines can print the same number the player's GPU saw.</summary>
    private static int _parkedFromLayer = -1;
    private static int _parkedToLayer = -1;
    private static int _parkedLayerCount;

    /// <summary>What <see cref="ApplyPose"/> last placed against, printed by every line that quotes a
    /// position so no line can assert a basis it did not use
    /// ([[an-instrument-can-assert-a-cause]]).</summary>
    private static string _poseBasis = "nothing has been placed yet";
    private static Vector2 _lastSlot;
    private static float _lastScale = 1f;

    /// <summary>The captured band and the captured scale — see the seam note in <see cref="Tick"/>.
    /// Re-derived only when <see cref="_parkFrame"/> stops matching the window's live rect.</summary>
    private static Rect _slot;
    private static float _slotScale = 1f;
    private static bool _slotCaptured;
    private static Vector2 _parkFrame;

    private static string _waitReported = string.Empty;
    private static string _oneWindowVerdict = string.Empty;
    private static int _oneWindowReports;

    /// <summary>Cap on the falsifier's lines per session. <c>StoryComposite.MaxOneWindowReports</c>'s
    /// number and its reason: the verdict is change-gated, and a state that oscillates should say so
    /// a handful of times and then stop rather than fill the log.</summary>
    private const int MaxOneWindowReports = 6;

    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly List<Graphic> PaintScratch = new(64);
    private static readonly List<Transform> LayerScratch = new(256);

    /// <summary>
    /// One tick. Called from <c>ModalFallback.TickCatchAll</c>, above every early return in it, so a
    /// tick in which no unknown window is tracked still runs the hand-back path.
    /// </summary>
    internal static void Tick()
    {
        UIWindow? shop = ShopWindow();
        RectTransform? list = ListRect();

        // ---- the four preconditions, in the order in which they can first be false -------------
        if (shop == null || !shop.IsOpen)
        {
            Release("the enchantress window is not open");
            Wait(string.Empty);
            return;
        }
        var win = shop.transform as RectTransform;
        if (win == null)
        {
            Release("the enchantress window has no RectTransform");
            Wait($"'{shop.name}' has no RectTransform, so there is no frame to place anything in");
            return;
        }
        ConvertedPanel? panel = ModalFallback.PanelFor(shop);
        if (panel == null || !ModalFallback.FloatIsLive(shop))
        {
            Release("the enchantress window stopped being a live float");
            Wait($"the enchantress window '{shop.name}' (ID {shop.ID}) is open but is NOT a live "
                 + $"floated panel yet (panel={(panel != null ? "present" : "none")}, "
                 + $"FloatIsLive={ModalFallback.FloatIsLive(shop)}). Until it is, there is no world "
                 + "frame to park into and the list stays exactly where the party display's fixed fit "
                 + "draws it.");
            return;
        }
        if (list == null || !list.gameObject.activeInHierarchy)
        {
            Release("the game switched the card list off (UIPartyCharacterEnhancementAbilityCards"
                    + "Display.Hide deactivates its own GameObject — the enchantress calls it from "
                    + "ExitShop, UINewEnhancementWindow.cs:264)");
            Wait($"the enchantress window '{shop.name}' is floated but the card list is not being "
                 + "drawn yet (NewPartyDisplayUI.EnhancementCardsDisplay is "
                 + $"{(list == null ? "unreachable" : "inactive")}) — it appears when a character is "
                 + "picked, UINewEnhancementWindow.OnSelectedCharacter:223.");
            return;
        }
        Vector2 listSize = _parked != null && ReferenceEquals(_parked, list) ? _parkedSize : list.rect.size;
        if (listSize.x < MinParkSizePx || listSize.y < MinParkSizePx)
        {
            Release("the card list's rect went degenerate");
            Wait($"the card list '{list.name}' has not finished laying out — its rect is "
                 + $"{listSize.x:F0}x{listSize.y:F0} px, below the {MinParkSizePx:F0} px floor. A park "
                 + "measured now would own a zero size, which is the ModBuild 232 defect this class "
                 + "inherits the fix for.");
            return;
        }

        // ---- THE SLOT IS A CAPTURE, NEVER A PER-TICK READING -----------------------------------
        //
        // This is the ModBuild 200 seam rule applied to the other window, and it is the difference
        // between "the list is on the left" and "the list moves every time the enchantress window
        // animates". The window's own ink is NOT static: UIEnchantressEffect.Play/Stop drives a swirl,
        // the card holder appears and disappears with the selection, the buy/sell tabs toggle. A slot
        // re-derived from that union every tick would re-seat the list on each of those events, which
        // is exactly the "man sieht wie es dahin springt" complaint the fixed fit already had to
        // learn its way out of (CanvasConversion.3.Fit.cs:1276 — "the seam is a capture, never a
        // reading"). So the band is measured ONCE per park and re-measured only when the DESTINATION
        // WINDOW'S OWN FRAME changes size, which is the one event that genuinely invalidates it.
        bool frameMoved = _parked != null
                          && (Mathf.Abs(win.rect.width - _parkFrame.x) > OffsetEpsilonPx
                              || Mathf.Abs(win.rect.height - _parkFrame.y) > OffsetEpsilonPx);
        if (_parked == null || !_slotCaptured || frameMoved)
        {
            if (!TryDeriveSlot(win, panel, list, listSize, out Rect fresh, out float freshScale,
                               out string freshBasis))
            {
                Release("the enchantress window's free band stopped being measurable");
                Wait(freshBasis);
                return;
            }
            _slot = fresh;
            _slotScale = freshScale;
            _poseBasis = freshBasis;
            _parkFrame = win.rect.size;
            _slotCaptured = true;
        }

        if (_parked == null || !ReferenceEquals(_parked, list) || !ReferenceEquals(_parkHost, shop))
        {
            if (_parked != null)
                Release("the enchantress window or the card list changed under the park");
            if (!Park(shop, win, list, listSize))
                return;
            // Release() (above, and the one inside a failed Park) drops the capture flag as a matter
            // of course. The band was derived for THIS window and THIS list a few statements ago and
            // the park did not move anything the band was measured from, so re-assert it rather than
            // pay for an identical second measurement on the next tick.
            _parkFrame = win.rect.size;
            _slotCaptured = true;
        }

        // The game re-parented it out from under us: hand back and stop. MapTravelConfirm's
        // ownership re-check, for its reason — a subtree that is no longer ours is never written to.
        if (_parked == null)
            return;
        if (_parked.parent == null || !ReferenceEquals(_parked.parent, win))
        {
            Release("the game re-parented the card list");
            return;
        }

        // The composite OWNS the size; re-assert it if anything outside this class drove it.
        if (_parked.rect.size != _parkedSize)
            _parked.sizeDelta = _parkedSize;

        ApplyPose(win, _slot, _slotScale);
        ClampPointer(win);
        ReportOneWindow(shop, win, list);
    }

    /// <summary>Session reset — called from <c>ModalFallback.CatchAllReset</c> beside the other
    /// per-session resets. Hands the list back FIRST (a reset must never leave a game object parked
    /// under a window this mod is about to forget) and then clears every latch.</summary>
    internal static void Reset()
    {
        Release("the world UI layer was reset");
        _waitReported = string.Empty;
        _oneWindowVerdict = string.Empty;
        _oneWindowReports = 0;
        _poseBasis = "nothing has been placed yet";
        _lastSlot = Vector2.zero;
        _lastScale = 1f;
        _slotCaptured = false;
        _slot = default;
        _slotScale = 1f;
        _parkFrame = Vector2.zero;
    }

    // ---- resolution ----------------------------------------------------------------------------

    /// <summary>The enchantress window off <c>UIGuildmasterHUD</c>'s own serialized reference. Never
    /// a scene sweep, never a name ([[findobjectsoftype-is-the-default-suspect]]).</summary>
    private static UIWindow? ShopWindow()
    {
        try
        {
            return GuildmasterDestinations.ModeWindow(EGuildmasterMode.Enchantress);
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    /// <summary>The card list off <c>NewPartyDisplayUI</c>'s own serialized reference — the SAME
    /// object <c>CanvasConversion.CollectActiveSubViews</c> asks for, so the fit and this class can
    /// never disagree about which transform the sub-view is. The singleton accessor can throw before
    /// the party display exists, hence the guard.</summary>
    private static RectTransform? ListRect()
    {
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            if (display == null)
                return null;
            UIPartyCharacterEnhancementAbilityCardsDisplay cards = display.EnhancementCardsDisplay;
            return cards != null ? cards.transform as RectTransform : null;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    // ---- the slot ------------------------------------------------------------------------------

    /// <summary>
    /// THE BAND ON THE ENCHANTRESS WINDOW'S LEFT, and the scale that fits the list into it.
    ///
    /// <para>Everything here is a measurement of the destination made this tick. The union excludes
    /// the parked list (or the reading would include what we are placing) and excludes full-frame
    /// plates by the fit's own test (or the window's own root <c>Image</c> would fill the frame and
    /// there would be no band). Returns false — a WAIT, never a refusal — when the window has no
    /// measurable content ink or when the band cannot hold the list at
    /// <see cref="MinReadableScale"/>.</para>
    /// </summary>
    private static bool TryDeriveSlot(RectTransform win, ConvertedPanel? panel, RectTransform list,
                                      Vector2 listSize, out Rect slot, out float scale, out string basis)
    {
        slot = default;
        scale = 1f;
        Rect frame = win.rect;
        if (frame.width < MinParkSizePx || frame.height < MinParkSizePx)
        {
            basis = $"the enchantress window's own rect is {frame.width:F0}x{frame.height:F0} px — "
                    + "not measurable yet, so there is no frame to derive a band from";
            return false;
        }

        if (!TryContentBounds(win, win, panel, list, out Rect ink, out int counted) || counted == 0)
        {
            basis = $"the enchantress window has no measurable content ink this tick (0 of its "
                    + "graphics passed the fit's own visibility verdict once full-frame backdrop "
                    + "plates and the card list itself are excluded), so the left band cannot be "
                    + "derived from anything and no hand-tuned fallback is going to be invented for "
                    + "it";
            return false;
        }

        float slotWidth = ink.xMin - frame.xMin;
        slot = Rect.MinMaxRect(frame.xMin, frame.yMin, ink.xMin, frame.yMax);
        _lastSlot = new Vector2(slotWidth, frame.height);
        if (slotWidth < MinParkSizePx)
        {
            basis = $"the enchantress window's own content starts at x={ink.xMin:F0} px, which is its "
                    + $"own left edge ({frame.xMin:F0} px) — there is no free band on the left at all "
                    + $"this tick (ink {ink.width:F0}x{ink.height:F0} px from {counted} graphic(s))";
            return false;
        }

        scale = Mathf.Min(slotWidth / listSize.x, frame.height / listSize.y);
        if (scale > 1f)
            scale = 1f;   // never magnify: the list is authored at the size the game wants it read at
        if (scale < MinReadableScale)
        {
            basis = $"the free band on the enchantress window's left is only {slotWidth:F0}x"
                    + $"{frame.height:F0} px (its own content ink, {counted} graphic(s), starts at "
                    + $"x={ink.xMin:F0}), which would draw the {listSize.x:F0}x{listSize.y:F0} px card "
                    + $"list at scale {scale:F2} — below the {MinReadableScale:F2} readable floor";
            return false;
        }

        basis = $"the band between the enchantress window's own left rect edge ({frame.xMin:F0} px) "
                + $"and the LEFT EDGE OF ITS OWN PAINTED CONTENT (x={ink.xMin:F0} px, unioned from "
                + $"{counted} graphic(s) with CanvasConversion.CountsAsFitContent, full-frame backdrop "
                + $"plates and the card list itself excluded) — a slot {slotWidth:F0}x"
                + $"{frame.height:F0} px, in which the list is CENTRED, so the margin on each side is "
                + "half the slack and nothing here is a dial";
        return true;
    }

    /// <summary>
    /// PAINTED BOUNDS of <paramref name="root"/> in <paramref name="win"/>'s local (authored uGUI)
    /// space, with the FIT'S OWN visibility verdict.
    ///
    /// <para>Borrowed rather than re-invented, for <c>StoryComposite.TryPaintedBounds</c>'s reason:
    /// the numbers produced here are compared against the numbers the FIT produces in the same log,
    /// and a second, weaker predicate is how <c>MrBacking.GlyphTrueRect</c> once unioned back in the
    /// text the fit had already judged invisible.</para>
    ///
    /// <para>Two exclusions this variant adds, both stated in the caller's basis string: the subtree
    /// under <paramref name="exclude"/> (the thing being placed must not size its own slot) and
    /// FULL-FRAME PLATES (a window-sized backdrop is not content — the fit's own
    /// <c>FixedFitPlate*Fraction</c> test, by value).</para>
    /// </summary>
    private static bool TryContentBounds(RectTransform root, RectTransform win, ConvertedPanel? panel,
                                         RectTransform? exclude, out Rect local, out int counted)
    {
        local = default;
        counted = 0;
        try
        {
            PaintScratch.Clear();
            root.GetComponentsInChildren(includeInactive: false, PaintScratch);
            if (PaintScratch.Count == 0)
                return false;
            if (panel != null)
                CanvasConversion.BeginContentQuery();

            Vector2 frame = win.rect.size;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            Vector3[] corners = Corners;
            for (int i = 0; i < PaintScratch.Count; i++)
            {
                Graphic g = PaintScratch[i];
                if (g == null)
                    continue;
                RectTransform rt = g.rectTransform;
                if (rt == null)
                    continue;
                if (exclude != null && (ReferenceEquals(rt, exclude) || rt.IsChildOf(exclude)))
                    continue;
                if (panel != null)
                {
                    if (!CanvasConversion.CountsAsFitContent(panel, g))
                        continue;
                }
                else if (!CountsAsPaintedHere(g))
                {
                    continue;
                }
                Vector2 size = rt.rect.size;
                if (size.x < 1f || size.y < 1f)
                    continue;
                rt.GetWorldCorners(corners);
                float gMinX = float.MaxValue, gMinY = float.MaxValue;
                float gMaxX = float.MinValue, gMaxY = float.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    Vector3 p = win.InverseTransformPoint(corners[c]);
                    if (p.x < gMinX) gMinX = p.x;
                    if (p.x > gMaxX) gMaxX = p.x;
                    if (p.y < gMinY) gMinY = p.y;
                    if (p.y > gMaxY) gMaxY = p.y;
                }
                // THE PLATE TEST, by value from the fit (FixedFitPlateWidthFraction / …Height).
                if (frame.x > 1f && frame.y > 1f
                    && gMaxX - gMinX >= frame.x * PlateWidthFraction
                    && gMaxY - gMinY >= frame.y * PlateHeightFraction)
                    continue;
                if (gMinX < minX) minX = gMinX;
                if (gMinY < minY) minY = gMinY;
                if (gMaxX > maxX) maxX = gMaxX;
                if (gMaxY > maxY) maxY = gMaxY;
                counted++;
            }
            PaintScratch.Clear();
            if (counted == 0)
                return false;
            local = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
        catch (System.Exception)
        {
            // A measurement is never worth a throw reaching the modal tick. The caller treats a
            // false as a WAIT and the list stays where it is.
            PaintScratch.Clear();
            counted = 0;
            return false;
        }
    }

    /// <summary>The minimal "is this drawn?" test, used ONLY when there is no converted panel to ask
    /// the fit's own verdict of. Deliberately weaker and deliberately stated as such — the same
    /// fallback, for the same reason, as <c>StoryComposite.CountsAsPaintedHere</c>.</summary>
    private static bool CountsAsPaintedHere(Graphic g) =>
        g.enabled && g.gameObject.activeInHierarchy
        && (g.canvasRenderer == null || !g.canvasRenderer.cull)
        && g.color.a > 0.02f
        && !g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    // ---- park / unpark -------------------------------------------------------------------------

    private static bool Park(UIWindow shop, RectTransform win, RectTransform list, Vector2 measured)
    {
        try
        {
            // MEASURED AND RECORDED FIRST — everything below changes what this rect would answer.
            _home = list.parent;
            _homeIndex = list.GetSiblingIndex();
            _homeAnchorMin = list.anchorMin;
            _homeAnchorMax = list.anchorMax;
            _homePivot = list.pivot;
            _homeAnchoredPos = list.anchoredPosition;
            _homeSizeDelta = list.sizeDelta;
            _homeRotation = list.localRotation;
            _homeScale = list.localScale;
            _homeWindowRect = null;
            _homeWindowName = "<none above the card list>";
            for (Transform? t = _home; t != null; t = t.parent)
            {
                var w = t.GetComponent<UIWindow>();
                if (w == null)
                    continue;
                _homeWindowRect = t as RectTransform;
                _homeWindowName = t.name;
                break;
            }

            // ignoreLayout BEFORE the move, so the destination's layout (if it ever grows one) never
            // rebuilds with this rect in its rectChildren — MapTravelConfirm's discipline.
            var le = list.GetComponent<LayoutElement>();
            _addedIgnore = null;
            _homeIgnoreLayout = false;
            if (le == null)
            {
                le = list.gameObject.AddComponent<LayoutElement>();
                _addedIgnore = le;
            }
            else
            {
                _homeIgnoreLayout = le.ignoreLayout;
            }
            le.ignoreLayout = true;

            list.SetParent(win, worldPositionStays: false);
            list.SetAsLastSibling();
            list.anchorMin = new Vector2(0.5f, 0.5f);
            list.anchorMax = new Vector2(0.5f, 0.5f);
            list.pivot = new Vector2(0.5f, 0.5f);
            list.sizeDelta = measured;          // ← the size the composite now owns (ModBuild 232)
            list.localRotation = Quaternion.identity;
            _parkedSize = measured;
            _parked = list;
            _parkHost = shop;

            // THE LAYER, read live off the destination's own root — see the class note.
            _parkedToLayer = win.gameObject.layer;
            _parkedFromLayer = list.gameObject.layer;
            _parkedLayerCount = WriteLayer(list, _parkedToLayer);

            // Placed BEFORE the line that reports it, so the position the line quotes is the one the
            // player is about to see and not the pre-move zero ([[an-instrument-can-assert-a-cause]]).
            ApplyPose(win, _slot, _slotScale);
            LogParked(shop, win, list);
            // A park is an EDGE, so the WAIT latch is cleared with it: if the same precondition goes
            // false again after this park, that is a NEW not-yet about a new attempt and it must
            // print. A latch that outlives the state it was set for is how a log stops reporting.
            _waitReported = string.Empty;
            return true;
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"ENCHANTRESS COMPOSITE: parking the card list threw ({e.GetType().Name}) "
                              + "— unwinding to the split presentation, which is the status quo and is "
                              + "what the player already has.");
            Release("the park itself failed");
            return false;
        }
    }

    /// <summary>Write <paramref name="layer"/> over the whole subtree and return how many objects
    /// changed. Stops at foreign <c>Renderer</c>/<c>Camera</c> subtrees for
    /// <c>PanelSupersample.ApplyCaptureLayer</c>'s reason: 3D content inside a uGUI window is owned by
    /// another camera that culls BY LAYER ([[canvasrenderer-is-not-a-renderer]]).</summary>
    private static int WriteLayer(Transform root, int layer)
    {
        int moved = 0;
        LayerScratch.Clear();
        LayerScratch.Add(root);
        while (LayerScratch.Count > 0)
        {
            int last = LayerScratch.Count - 1;
            Transform t = LayerScratch[last];
            LayerScratch.RemoveAt(last);
            if (t == null)
                continue;
            if (!ReferenceEquals(t, root)
                && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;   // and NOT its children either
            if (t.gameObject.layer != layer)
            {
                t.gameObject.layer = layer;
                moved++;
            }
            for (int i = t.childCount - 1; i >= 0; i--)
                LayerScratch.Add(t.GetChild(i));
        }
        LayerScratch.Clear();
        return moved;
    }

    /// <summary>
    /// Centre the parked list in the slot, both axes, in the enchantress window's own local space.
    ///
    /// <para>The parked rect's anchors are collapsed to the window's centre and its pivot is
    /// (0.5,0.5), so <c>anchoredPosition</c> IS its centre in this space and the slot's centre is the
    /// whole answer. Re-applied every tick because the window's own content moves (its buy/sell tabs
    /// and the enchantress swirl are animated), change-gated on <see cref="OffsetEpsilonPx"/> so a
    /// settled layout costs one comparison and no write.</para>
    /// </summary>
    private static void ApplyPose(RectTransform win, Rect slot, float scale)
    {
        if (_parked == null)
            return;
        _lastScale = scale;
        var wantScale = new Vector3(scale, scale, 1f);
        if ((_parked.localScale - wantScale).sqrMagnitude > 1e-6f)
            _parked.localScale = wantScale;
        // THE PIVOT SUBTRACTION IS NOT DECORATION. `slot` is in the window's LOCAL space, whose
        // origin is the window's own pivot; `anchoredPosition` is measured from the ANCHOR, which
        // with anchorMin=anchorMax=(0.5,0.5) is the centre of the window's rect — i.e.
        // `win.rect.center` in that same local space. The two coincide only when the window's pivot
        // is (0.5,0.5). It is on this prefab, which is exactly why an assumption here would ship and
        // then break on the first window whose pivot is not centred. One subtraction buys the
        // guarantee ([[verify-outcome-not-path]]).
        Vector2 want = slot.center - win.rect.center;
        if ((_parked.anchoredPosition - want).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            _parked.anchoredPosition = want;
    }

    /// <summary>
    /// KEEP THE LIST'S OWN POINTER INSIDE THE LIST.
    ///
    /// <para><c>UIPartyCharacterEnhancementAbilityCardsDisplay.Display</c> calls
    /// <c>verticalPointer.PointAt(characterUI.CardPointReference)</c> — and
    /// <c>VerticalPointerUI.PointAt</c> writes a WORLD y taken from a rect in the CHARACTER UI
    /// (VerticalPointerUI.cs:5-10). With the list parked in another window that y is measured in a
    /// frame the pointer no longer lives in, so the little marker would sit at an arbitrary height —
    /// in the worst case outside the list entirely.</para>
    ///
    /// <para>So its world y is CLAMPED into the parked list's own world rect, and only when it is
    /// outside: an in-range value is left alone, so this is a clamp and not a write war
    /// ([[dont-win-a-write-war]]) — the game writes once per character pick, this corrects that one
    /// value and then compares equal forever. Presentation only: it moves a decorative marker, and it
    /// is reverted with everything else because the marker is a child of the rect that goes home.
    /// </para>
    /// </summary>
    private static void ClampPointer(RectTransform win)
    {
        if (_parked == null)
            return;
        try
        {
            Transform? pointerT = null;
            var pointer = _parked.GetComponentInChildren<VerticalPointerUI>(includeInactive: false);
            if (pointer != null)
                pointerT = pointer.transform;
            if (pointerT == null)
                return;
            Vector3 local = _parked.InverseTransformPoint(pointerT.position);
            Rect r = _parked.rect;
            float clamped = Mathf.Clamp(local.y, r.yMin, r.yMax);
            if (Mathf.Abs(clamped - local.y) <= OffsetEpsilonPx)
                return;
            local.y = clamped;
            pointerT.position = _parked.TransformPoint(local);
        }
        catch (System.Exception)
        {
            // A decorative marker is never worth a throw reaching the modal tick.
        }
    }

    /// <summary>
    /// Hand the list back, verbatim. Every field the park overwrote is restored from the record taken
    /// BEFORE the first move, in the order that makes each write mean what it meant when it was taken
    /// (<c>sizeDelta</c> before <c>anchoredPosition</c>: for a stretch child <c>sizeDelta</c> is the
    /// INSET PAIR, not a size, and leaving the park's absolute pixels in it would hand the character
    /// UI back a list inset by its own width on every edge — the other half of the ModBuild 232
    /// defect).
    /// </summary>
    private static void Release(string why)
    {
        if (_parked == null)
        {
            _parkHost = null;
            return;
        }
        RectTransform list = _parked;
        _parked = null;
        _parkHost = null;
        _parkedSize = Vector2.zero;
        _oneWindowVerdict = string.Empty;
        _slotCaptured = false;   // the next park measures the band afresh
        try
        {
            if (_addedIgnore != null)
            {
                Object.Destroy(_addedIgnore);
                _addedIgnore = null;
            }
            else
            {
                var le = list != null ? list.GetComponent<LayoutElement>() : null;
                if (le != null)
                    le.ignoreLayout = _homeIgnoreLayout;   // verbatim, not assumed-false
            }
            if (list == null || _home == null)
                return;
            list.SetParent(_home, worldPositionStays: false);
            list.SetSiblingIndex(Mathf.Clamp(_homeIndex, 0, Mathf.Max(0, _home.childCount - 1)));
            list.anchorMin = _homeAnchorMin;
            list.anchorMax = _homeAnchorMax;
            list.pivot = _homePivot;
            list.sizeDelta = _homeSizeDelta;
            list.anchoredPosition = _homeAnchoredPos;
            list.localRotation = _homeRotation;
            list.localScale = _homeScale;
            // THE LAYER GOES BACK TO WHAT THE LIST'S NEW SIBLINGS ARE ON, READ LIVE — not to the
            // number the park recorded. That is the whole point: while the list was away the party
            // display may have engaged or released supersampling, and "the layer my siblings are on"
            // is right in both cases, where a recorded 26 could strand the list on a dead capture
            // layer. See the class note.
            int homeLayer = _home.gameObject.layer;
            int moved = WriteLayer(list, homeLayer);
            VRLog.Info(Scope, $"ENCHANTRESS COMPOSITE HANDED BACK — {why}. The card list "
                              + $"'{list.name}' is back under '{_home.name}' (inside "
                              + $"'{_homeWindowName}') at sibling index {_homeIndex} with its authored "
                              + $"anchors ({_homeAnchorMin.x:F2},{_homeAnchorMin.y:F2})-"
                              + $"({_homeAnchorMax.x:F2},{_homeAnchorMax.y:F2}), pivot "
                              + $"({_homePivot.x:F2},{_homePivot.y:F2}), sizeDelta "
                              + $"({_homeSizeDelta.x:F0},{_homeSizeDelta.y:F0}), offset "
                              + $"({_homeAnchoredPos.x:F0},{_homeAnchoredPos.y:F0}), rotation and "
                              + $"scale ({_homeScale.x:F3}) restored VERBATIM, and {moved} object(s) "
                              + $"put back on layer {homeLayer} (read live off the home parent, not "
                              + $"off the {_parkedFromLayer} this park recorded, so a party display "
                              + "that engaged or released supersampling in the meantime still gets the "
                              + "layer its own sweep expects). From the next fit pass "
                              + "CanvasConversion.CollectActiveSubViews sees it inside the party "
                              + "window again and the ModBuild 200 seam draws it exactly as it did "
                              + "before this mod touched it. NOTHING WAS CLAIMED AND NOTHING WAS HELD "
                              + "BACK, so there is no claim to lapse and no window waiting on this "
                              + "line.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"ENCHANTRESS COMPOSITE: handing the card list back threw "
                              + $"({e.GetType().Name}) — it may be left under the enchantress window's "
                              + "root. It is the GAME's own object, the game re-parents nothing, and "
                              + "UINewEnhancementWindow.OnSelectedCharacter re-lays it out on the next "
                              + "character pick.");
        }
        finally
        {
            _home = null;
            _homeWindowRect = null;
            _parkedLayerCount = 0;
            _parkedFromLayer = -1;
            _parkedToLayer = -1;
        }
    }

    // ---- instrumentation -----------------------------------------------------------------------

    private static void LogParked(UIWindow shop, RectTransform win, RectTransform list)
    {
        Rect frame = win.rect;
        VRLog.Info(Scope, $"ENCHANTRESS COMPOSITE PARKED — the enchantress' card list is now drawn "
                          + $"INSIDE the enchantress' own window. OUT OF: '{_homeWindowName}' (the "
                          + $"character UI), from parent '{(_home != null ? _home.name : "<none>")}' at "
                          + $"sibling index {_homeIndex}. INTO: '{shop.name}' (ID {shop.ID}), whose own "
                          + $"rect is {frame.width:F0}x{frame.height:F0} authored px. THE LIST: "
                          + $"'{list.name}' = NewPartyDisplayUI.EnhancementCardsDisplay = "
                          + $"UINewEnhancementWindow.CardsDisplay (one instance, two owners), "
                          + $"{_parkedSize.x:F0}x{_parkedSize.y:F0} px — the size the composite now "
                          + "OWNS and re-asserts every tick, captured BEFORE the anchor collapse "
                          + "because a stretch child's sizeDelta is an inset pair and collapsing its "
                          + "anchors without this leaves it 0x0. THE ANCHOR, DERIVED NOT DIALLED: "
                          + $"{_poseBasis}; the list is drawn at scale {_lastScale:F3} and centred at "
                          + $"({(_parked != null ? _parked.anchoredPosition.x : 0f):F0},"
                          + $"{(_parked != null ? _parked.anchoredPosition.y : 0f):F0}) in that "
                          + $"window's local space. THE LAYER: {_parkedLayerCount} object(s) moved from "
                          + $"layer {_parkedFromLayer} to layer {_parkedToLayer} — the destination "
                          + "window ROOT'S own layer, read live, because 'New Party display' is "
                          + "supersampled onto a private capture layer and a subtree that keeps it "
                          + "after leaving that host is rendered by nothing at all. WHAT THIS DOES NOT "
                          + "DO: it claims nothing, refuses nothing and holds nothing back — the card "
                          + "list carries no UIWindow, so it is not in any float decision, the churn "
                          + "fuse has nothing of ours to count and the DOUBLE HOST audit has nothing "
                          + "to find. The party display simply fits with one fewer sub-view until the "
                          + "list is handed back.");
    }

    /// <summary>One WAIT line per distinct reason, at Info. A precondition that is not yet true is a
    /// NOT-YET and not a fault: the list stays exactly where the party display's fixed fit draws it,
    /// so nothing is ever invisible on this path.</summary>
    private static void Wait(string why)
    {
        if (why.Length == 0)
        {
            _waitReported = string.Empty;
            return;
        }
        if (why == _waitReported)
            return;
        _waitReported = why;
        VRLog.Info(Scope, "ENCHANTRESS COMPOSITE WAIT — " + why + ". Nothing has been moved and "
                          + "nothing will be until this reads differently; the card list is drawn "
                          + "where it is today, by the party display's fixed fit, which is the state "
                          + "the player already has.");
    }

    /// <summary>
    /// THE FALSIFIER. It is TRUE only if the card list is measurably inside the enchantress window
    /// and measurably NOT inside the character UI, both read back off the objects THIS TICK.
    ///
    /// <para>Five clauses, and each is a measurement rather than a memory of what this class did:</para>
    /// <list type="number">
    ///   <item><description>the list's transform chain REACHES the enchantress window's rect — uGUI
    ///   draws by hierarchy, so this is the exact form of "inside";</description></item>
    ///   <item><description>it does NOT reach the character UI window's rect;</description></item>
    ///   <item><description>all four of the list's WORLD corners, expressed in the enchantress
    ///   window's local space, lie inside that window's rect (± <see cref="ContainTolerancePx"/>) —
    ///   the geometric form of "inside", which catches a park that is parented correctly and placed
    ///   off the frame;</description></item>
    ///   <item><description>NONE of those world corners lies inside the character UI window's rect
    ///   within <see cref="CharacterUiPlaneDepthPx"/> of its plane — the geometric half of "not in
    ///   the character UI";</description></item>
    ///   <item><description>the list is active and its rect is non-degenerate, so a CONFIRMED can
    ///   never be said about something that draws nothing.</description></item>
    /// </list>
    ///
    /// <para>GREP: <c>ENCHANTRESS COMPOSITE ONE WINDOW: CONFIRMED</c> — the fix.
    /// <c>ENCHANTRESS COMPOSITE ONE WINDOW: NOT ACHIEVED</c> — the complaint, with the same numbers
    /// so a failure is as legible as a success.</para>
    /// </summary>
    private static void ReportOneWindow(UIWindow shop, RectTransform win, RectTransform list)
    {
        bool inShopTree = list.IsChildOf(win) && !ReferenceEquals(list, win);
        bool inCharTree = _homeWindowRect != null && list.IsChildOf(_homeWindowRect);
        bool active = list.gameObject.activeInHierarchy;
        Vector2 size = list.rect.size;
        bool sized = size.x >= MinParkSizePx && size.y >= MinParkSizePx;

        list.GetWorldCorners(Corners);
        Vector3[] corners = Corners;
        int insideShop = 0;
        Rect shopRect = win.rect;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = win.InverseTransformPoint(corners[i]);
            if (p.x >= shopRect.xMin - ContainTolerancePx && p.x <= shopRect.xMax + ContainTolerancePx
                && p.y >= shopRect.yMin - ContainTolerancePx && p.y <= shopRect.yMax + ContainTolerancePx)
                insideShop++;
        }
        int insideChar = 0;
        if (_homeWindowRect != null)
        {
            Rect charRect = _homeWindowRect.rect;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = _homeWindowRect.InverseTransformPoint(corners[i]);
                if (Mathf.Abs(p.z) > CharacterUiPlaneDepthPx)
                    continue;
                if (p.x >= charRect.xMin && p.x <= charRect.xMax
                    && p.y >= charRect.yMin && p.y <= charRect.yMax)
                    insideChar++;
            }
        }

        bool ok = inShopTree && !inCharTree && insideShop == 4 && insideChar == 0 && active && sized;

        string measured =
            $"card list '{list.name}' {size.x:F0}x{size.y:F0} px at scale {_lastScale:F3}, "
            + $"activeInHierarchy={active}; parented inside enchantress window '{shop.name}' "
            + $"(ID {shop.ID}, rect {shopRect.width:F0}x{shopRect.height:F0} px)={inShopTree}; "
            + $"parented inside character UI '{_homeWindowName}'={inCharTree}; {insideShop} of its 4 "
            + $"world corners fall inside the enchantress window's rect (± {ContainTolerancePx:F0} px) "
            + $"and {insideChar} of 4 fall inside the character UI's rect within "
            + $"{CharacterUiPlaneDepthPx:F0} px of its plane; the slot it was placed in measured "
            + $"{_lastSlot.x:F0}x{_lastSlot.y:F0} px and the placement basis was — {_poseBasis}";

        string verdict = ok ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _oneWindowVerdict || _oneWindowReports >= MaxOneWindowReports)
            return;
        _oneWindowVerdict = verdict;
        _oneWindowReports++;

        if (ok)
        {
            VRLog.Info(Scope, "ENCHANTRESS COMPOSITE ONE WINDOW: CONFIRMED — the card selection is "
                              + "inside the enchantress' window, on its left, and is NOT inside the "
                              + "character UI. MEASURED THIS TICK: " + measured + ". USER REPORT THIS "
                              + "LINE ANSWERS: \"Die Kartenauswahl für die Magierin spawnt hingegen "
                              + "auf dem Fenster der Character-UI … die Kartenauswahl soll auf der "
                              + "linken Seite des Fensters (IN dem Fenster) der Magierin enthalten "
                              + "sein.\"");
            return;
        }
        VRLog.Warn(Scope, "ENCHANTRESS COMPOSITE ONE WINDOW: NOT ACHIEVED — the card selection is not "
                          + "yet measurably inside the enchantress window and out of the character "
                          + "UI. MEASURED THIS TICK: " + measured + ". READ IT LIKE THIS: parented "
                          + "inside enchantress=False with a PARKED line above means the game "
                          + "re-parented the list and the next tick will hand it back; a corner count "
                          + "below 4 means the placement put it off the frame, so read the slot and "
                          + "the basis in the same sentence — a slot wider than the window means the "
                          + "ink union picked up something that should have been excluded as a plate; "
                          + "0x0 or activeInHierarchy=False means the game switched the list off under "
                          + "a standing park, which the next tick releases.");
    }
}
