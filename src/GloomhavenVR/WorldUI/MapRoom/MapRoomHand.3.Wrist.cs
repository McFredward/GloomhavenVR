// THE WRIST HALF of the map room's loadout hand — the selected character's info plate.
//
// USER'S ASK: "Die Kartenhand INKLUSIVE DES CHARACTERINFO AM HANDGELENK des aktuell ausgewählten
// Characters soll voll angezeigt werden." The mod already draws exactly such a plate in a scenario
// (WorldUI/WristHud) and the player has hand-tuned its pose; this is that plate, for the map phase.
//
// ─── WHY THIS IS A SECOND PLATE AND NOT A CALL INTO WristHud ──────────────────────────────────
// WristHud is a SCENARIO instrument, top to bottom, and the incompatibility is in its data model
// rather than in its gate:
//
//   * Its want-predicate is `Choreographer.s_Choreographer != null` (WristHud.cs:246) — there is no
//     Choreographer on the campaign map, by construction (it is the very fact MapRoomDriver's mode
//     predicate is POSITIVE about, so that the main menu is never mistaken for a map).
//   * Every value it renders comes off a CPlayerActor: Health/MaxHealth/XP/Gold/Level/Tokens
//     (WristHud.cs:604). THERE IS NO CPlayerActor IN THE MAP PHASE. The map-phase character model is
//     MapRuleLibrary.Party.CMapCharacter, and the game's own bridge between them —
//     CMapCharacter.GetActor() (CMapCharacter.cs:1116) — reads ScenarioManager.Scenario.PlayerActors,
//     which is null here. Calling it would throw, not return null.
//   * Its whole resolution chain (Board.CharacterFocus, Choreographer.CurrentPlayerActor,
//     CardsHandManager's active hand, InitiativeTrack) names scenario objects that do not exist.
//
// So there is nothing in WristHud to point at a CMapCharacter. What this file DOES reuse is the one
// thing that must not be re-derived: THE POSE. The plate's parent, its base rotation and its six
// live trim dials are read straight off WristHud's own statics (WristHud.OffsetX/Y/Z,
// WristHud.PitchDeg/YawDeg/RollDeg), which are the per-hand-style [WristHud] entries in
// dev.gloomhavenvr.hands.cfg. That is deliberate and it is a standing lesson in this repo: a wrist
// anchor the user's hand-tuned config is measured FROM must never be re-stated by a second file.
// Move the "Arm-HUD" steppers in the VR settings menu and BOTH plates move together, because there
// is one set of numbers.
//
// THE FRAME, restated only because getting it wrong is silent: the plate parents to HandRig.Wrist,
// whose axes are NOT the hand root's (+X across the hand, +Y ALONG THE FINGERS, +Z OUT OF THE PALM
// — measured out of all six shipped hand prefabs, Hands/HandRig.cs:60-90). A uGUI canvas is read
// from its −Z side, so the base is a HALF TURN about wrist +Y: that lands the readable face on
// wrist +Z (out of the palm, toward a player looking at their own palm) and the text top on wrist +Y
// (toward the fingers). A half turn is a proper rotation, so the text is never mirrored, and the
// wrist frame is anatomically identical on both hands, so no per-hand sign flip exists or is needed.
// Getting this backwards does not hide the plate — it shows it mirror-reversed, which is the exact
// bug WristHud's own comment block records four rounds of.
//
// ─── NOTHING RE-ORIENTS WITH THE HEAD ─────────────────────────────────────────────────────────
// The plate's POSE is a pure function of the wrist transform and the six trims. Two things do read
// the head, and neither of them is an orientation:
//   * the look-at FADE (alpha only) — the plate becomes visible when you turn your palm up to read
//     it, with the same 0.35/0.20 hysteresis WristHud uses. Its axis is the plate's OWN readable
//     face, never a rig axis, so no future re-aim can desynchronize the gate from the base rotation
//     (WristHud shipped that desynchronization twice before it was written this way).
//   * the sorting-order LADDER (an int) — without it a world-space canvas at sortingOrder 0 is
//     painted under every converted panel in the room, because Unity sorts transparents by order
//     before distance. CanvasConversion.OrderAboveDistance ranks it by MEASURED eye distance among
//     the panels, exactly as WristHud, the board tooltip and the avatar tags already do.
// Turn your head and the plate does not move by a millimetre; it only fades and re-sorts.
//
// ─── THE DATA, AND WHERE IT IS READ FROM ──────────────────────────────────────────────────────
//   name      CMapCharacter.DisplayCharacterName ?? .CharacterName   (CMapCharacter.cs:47/49)
//   level     CMapCharacter.Level                                    (CMapCharacter.cs:55)
//   xp        CMapCharacter.EXP                                      (CMapCharacter.cs:53)
//   gold      CMapCharacter.CharacterGold                            (CMapCharacter.cs, m_CharacterGold)
//   max hp    CharacterYMLData.HealthTable[Level-1]                  (CharacterYMLData.cs:24)
//   loadout   HandAbilityCardIDs.Count / MaxCards                    (CMapCharacter.cs:59/103)
//   portrait  UIInfoTools.Instance.GetNewAdventureCharacterPortrait(CharacterYMLData.Model)
//             (UIInfoTools.cs:465/773, CharacterYMLData.cs:20) — the same call WristHud makes and
//             the same call the game's own card-selection preview makes.
// There is no CURRENT hp on the map (a character's damage is scenario state), so the plate shows the
// class MAXIMUM at the character's level and says so with the level it is derived from. Conditions
// are likewise absent: CMapCharacter carries NextScenarioPositiveConditions/-Negative, which is a
// promise about the next scenario rather than a state, and showing it as a live condition row would
// be a lie in a panel whose whole job is to be trusted.

using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    /// <summary>Look-at hysteresis on the plate's own readable face — WristHud's numbers, so both
    /// plates appear at the same palm angle.</summary>
    private const float WristShowDot = 0.35f;
    private const float WristHideDot = 0.20f;

    /// <summary>Seconds between text refreshes. The per-frame path only fades and re-sorts.</summary>
    private const float WristRefreshInterval = 0.25f;

    /// <summary>Sub-step lift above the farthest-still-behind panel's ladder slot — WristHud's
    /// <c>PanelLift</c>, and the same reasoning: it must stay under
    /// <c>CanvasConversion.PanelOrderStep</c> (16) so the plate can never climb into the NEXT
    /// panel's slot, and 12 clears a window's own decorations.</summary>
    private const int WristPanelLift = 12;

    /// <summary>The half turn about wrist +Y that puts the canvas's readable −Z face out of the
    /// PALM and its text top toward the FINGERS. Identical to <c>WristHud.PalmFlat</c>; see the
    /// header for why the identity is wrong here and what it looks like when it is used.</summary>
    private static readonly Quaternion WristPalmFlat = Quaternion.Euler(0f, 180f, 0f);

    private readonly StringBuilder _wristSb = new(256);

    private GameObject? _wristRoot;
    private Canvas? _wristCanvas;
    private CanvasGroup? _wristGroup;
    private TextMeshProUGUI? _wristStats;
    private TextMeshProUGUI? _wristName;
    private GameObject? _wristPortraitGo;
    private UnityEngine.UI.Image? _wristPortrait;
    private VRHand? _wristHand;
    private bool _wristShown;
    private float _wristNextRefresh;
    private string _wristLastStats = string.Empty;
    private string _wristLastName = string.Empty;
    private int _wristAppliedOrder = int.MinValue;

    /// <summary>State-line material: which wrist, and why the plate is or is not up.</summary>
    private string _wristSideName = "?";
    private string _wristVerdict = "not evaluated";

    internal bool HasWrist => _wristRoot != null;

    // ==========================================================================================
    //  BUILD / REBUILD
    // ==========================================================================================

    /// <summary>
    /// Re-point the plate at the current character. Called from <see cref="Reconcile"/> only — the
    /// plate's CONTENT changes at that cadence, its POSE and fade every frame.
    /// </summary>
    private void RebuildWrist()
    {
        if (_character == null)
        {
            ReleaseWrist();
            _wristVerdict = "no character is resolved, so there is nothing to show";
            return;
        }
        // Force the next tick to re-read the values for the new character, rather than waiting out
        // the refresh interval with the previous character's numbers on screen.
        _wristNextRefresh = 0f;
        _wristLastStats = string.Empty;
        _wristLastName = string.Empty;
        RefreshWristPortrait();
    }

    /// <summary>
    /// Per-frame: build on the wrist if it is not there yet, re-apply the live pose, run the look-at
    /// fade and the sorting ladder, and refresh the text on the slow cadence.
    /// </summary>
    private void TickWrist()
    {
        VRHand? hand = FanHand();   // the same non-dominant hand the fan sits on, as in a scenario
        if (_character == null || hand == null || !hand.HasPose || hand.Rig.Wrist == null)
        {
            if (_wristRoot != null && _wristRoot.activeSelf)
                _wristRoot.SetActive(false);
            _wristVerdict = _character == null
                ? "no character is resolved, so there is nothing to show"
                : "the fan hand has no tracked pose this frame";
            return;
        }

        if (_wristRoot == null || !ReferenceEquals(hand, _wristHand))
            BuildWrist(hand);
        if (_wristRoot == null)
            return;

        if (!_wristRoot.activeSelf)
            _wristRoot.SetActive(true);

        ApplyWristPose();

        Camera? head = CanvasConversion.WorldCamera;
        if (head != null && _wristGroup != null)
        {
            // GATE AXIS IS THE PLATE'S OWN READABLE FACE (its −Z), never a rig axis — see the header.
            Vector3 readable = -_wristRoot.transform.forward;
            Vector3 toHead = (head.transform.position - _wristRoot.transform.position).normalized;
            float dot = Vector3.Dot(readable, toHead);
            if (!_wristShown && dot > WristShowDot) _wristShown = true;
            else if (_wristShown && dot < WristHideDot) _wristShown = false;

            _wristGroup.alpha = Mathf.MoveTowards(_wristGroup.alpha, _wristShown ? 1f : 0f,
                                                  Time.deltaTime * 6f);

            if (_wristCanvas != null && (_wristShown || _wristGroup.alpha > 0.001f))
            {
                float eyeDistance = Vector3.Distance(head.transform.position,
                                                     _wristRoot.transform.position);
                int order = CanvasConversion.OrderAboveDistance(eyeDistance, WristPanelLift);
                if (order != _wristAppliedOrder)
                {
                    _wristAppliedOrder = order;
                    _wristCanvas.sortingOrder = order;
                }
            }
        }

        _wristVerdict = _wristShown
            ? "shown (the palm is turned toward the head)"
            : "built but faded out — turn the palm up to read it";

        if (_wristShown && Time.unscaledTime >= _wristNextRefresh)
        {
            _wristNextRefresh = Time.unscaledTime + WristRefreshInterval;
            RefreshWristText();
        }
    }

    /// <summary>
    /// Re-read and re-apply the SHARED wrist pose every tick, so nudging an "Arm-HUD" stepper in the
    /// VR settings menu moves this plate immediately — and moves it identically to the scenario one,
    /// because both read the same six per-style entries.
    /// </summary>
    private void ApplyWristPose()
    {
        if (_wristRoot == null)
            return;
        _wristRoot.transform.localPosition =
            new Vector3(WristHud.OffsetX, WristHud.OffsetY, WristHud.OffsetZ);
        // BASE FIRST, THEN THE TRIM — the trims rotate in the PLATE's own frame. That is the order
        // WristHud composes in and the order the user's shipped trims were measured against; see the
        // standing warning in WristHud.ApplyPose before ever swapping it.
        _wristRoot.transform.localRotation =
            WristPalmFlat * Quaternion.Euler(WristHud.PitchDeg, WristHud.YawDeg, WristHud.RollDeg);
    }

    private void BuildWrist(VRHand hand)
    {
        ReleaseWrist();
        _wristHand = hand;
        _wristSideName = hand.Side.ToString();

        _wristRoot = new GameObject("GloomhavenVR.MapRoomWristInfo");
        _wristRoot.layer = 5; // UI (the dev-sim fallback; VRLayers.Apply re-layers it for VR below)
        _wristRoot.transform.SetParent(hand.Rig.Wrist, worldPositionStays: false);
        ApplyWristPose();

        _wristCanvas = _wristRoot.AddComponent<Canvas>();
        _wristCanvas.renderMode = RenderMode.WorldSpace;
        _wristCanvas.worldCamera = CanvasConversion.WorldCamera;
        _wristCanvas.sortingOrder = 0;   // pre-measurement seat; the ladder takes over on the first shown tick
        _wristAppliedOrder = int.MinValue;

        var rect = (RectTransform)_wristRoot.transform;
        rect.sizeDelta = new Vector2(240f, 196f);
        // 0.4 mm per uGUI pixel -> a 9.6 x 7.8 cm watch face. This is a LOCAL scale under the wrist
        // transform, which already carries the diorama scale, so the plate is that size at the eye
        // at every map zoom — the same reason the fan needs no scale term. WristHud's number.
        rect.localScale = Vector3.one * 0.0004f;

        _wristGroup = _wristRoot.AddComponent<CanvasGroup>();
        _wristGroup.alpha = 0f;
        _wristGroup.blocksRaycasts = false;
        _wristGroup.interactable = false;

        var bg = new GameObject("Background") { layer = 5 };
        bg.transform.SetParent(_wristRoot.transform, worldPositionStays: false);
        var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);
        MrBacking.Opacify(bgImage);   // 0.82 lets the room shimmer through in mixed reality
        var bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        _wristPortraitGo = new GameObject("Portrait") { layer = 5 };
        _wristPortraitGo.transform.SetParent(_wristRoot.transform, worldPositionStays: false);
        _wristPortrait = _wristPortraitGo.AddComponent<UnityEngine.UI.Image>();
        _wristPortrait.preserveAspect = true;
        _wristPortrait.raycastTarget = false;
        var portraitRect = (RectTransform)_wristPortraitGo.transform;
        portraitRect.anchorMin = new Vector2(0f, 1f);
        portraitRect.anchorMax = new Vector2(0f, 1f);
        portraitRect.pivot = new Vector2(0f, 1f);
        portraitRect.anchoredPosition = new Vector2(8f, -8f);
        portraitRect.sizeDelta = new Vector2(44f, 44f);
        _wristPortraitGo.SetActive(false);   // enabled once a sprite resolves

        var nameGo = new GameObject("Name") { layer = 5 };
        nameGo.transform.SetParent(_wristRoot.transform, worldPositionStays: false);
        _wristName = nameGo.AddComponent<TextMeshProUGUI>();
        _wristName.fontSize = 21f;
        _wristName.alignment = TextAlignmentOptions.MidlineLeft;
        _wristName.richText = true;
        _wristName.enableWordWrapping = false;
        _wristName.overflowMode = TextOverflowModes.Ellipsis;
        var nameRect = (RectTransform)nameGo.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.offsetMin = new Vector2(60f, -52f);
        nameRect.offsetMax = new Vector2(-8f, -8f);
        WorldUIAssets.TryAssignGameFont(_wristName);

        var statsGo = new GameObject("Stats") { layer = 5 };
        statsGo.transform.SetParent(_wristRoot.transform, worldPositionStays: false);
        _wristStats = statsGo.AddComponent<TextMeshProUGUI>();
        _wristStats.fontSize = 24f;
        _wristStats.alignment = TextAlignmentOptions.TopLeft;
        _wristStats.richText = true;
        var statsRect = (RectTransform)statsGo.transform;
        statsRect.anchorMin = Vector2.zero;
        statsRect.anchorMax = Vector2.one;
        statsRect.offsetMin = new Vector2(8f, 6f);
        statsRect.offsetMax = new Vector2(-8f, -58f);
        WorldUIAssets.TryAssignGameFont(_wristStats);

        _wristLastStats = string.Empty;
        _wristLastName = string.Empty;
        _wristNextRefresh = 0f;
        VRLayers.Apply(_wristRoot);
        RefreshWristPortrait();

        Vector3 palmOut = hand.Rig.Wrist.TransformDirection(Vector3.forward); // wrist +Z is out of the palm
        float readableDotPalmOut = Vector3.Dot(-_wristRoot.transform.forward, palmOut);
        VRLog.Info(Scope,
            $"MAP-ROOM WRIST plate built on the {hand.Side} wrist. It reuses the SCENARIO plate's own "
            + "pose dials (the per-style [WristHud] entries) so both move together and the user's "
            + "hand-tuned numbers are never re-stated: offset "
            + $"{WristHud.OffsetX * 1000f:0}/{WristHud.OffsetY * 1000f:0}/{WristHud.OffsetZ * 1000f:0} mm "
            + "in the wrist frame (+X across, +Y to the fingers, +Z out of the palm), trim "
            + $"{WristHud.PitchDeg:0.#}/{WristHud.YawDeg:0.#}/{WristHud.RollDeg:0.#}°, "
            + $"readable·palmOut {readableDotPalmOut:0.00} (+1 = readable from the palm side, "
            + "−1 = MIRRORED, which is the one failure this number exists to catch).");
    }

    // ==========================================================================================
    //  CONTENT
    // ==========================================================================================

    private void RefreshWristPortrait()
    {
        if (_wristPortrait == null || _wristPortraitGo == null)
            return;
        Sprite? sprite = null;
        try
        {
            CharacterYMLData? yml = _character != null ? _character.CharacterYMLData : null;
            UIInfoTools tools = UIInfoTools.Instance;
            if (yml != null && tools != null)
                sprite = tools.GetNewAdventureCharacterPortrait(yml.Model);
        }
        catch (System.Exception ex)
        {
            VRLog.Debug(Scope, $"Map-room wrist: no class portrait ({ex.GetType().Name}) — name-only identity.");
        }
        _wristPortrait.sprite = sprite;
        _wristPortraitGo.SetActive(sprite != null);
    }

    private void RefreshWristText()
    {
        if (_wristStats == null || _wristName == null)
            return;
        CMapCharacter? character = _character;

        // ---- identity row -------------------------------------------------------------------
        _wristSb.Length = 0;
        if (character == null)
        {
            _wristSb.Append("<alpha=#88>—");
        }
        else
        {
            _wristSb.Append("<size=13><alpha=#AA>").Append(Loc.Mod("selected")).Append("</size>\n")
                    .Append("<b><color=#ffd45e>").Append(DisplayName(character)).Append("</color></b>");
        }
        string identity = _wristSb.ToString();
        if (identity != _wristLastName)
        {
            _wristLastName = identity;
            _wristName.text = identity;
            if (_wristName.font == null)
                WorldUIAssets.TryAssignGameFont(_wristName);
        }

        // ---- stats --------------------------------------------------------------------------
        _wristSb.Length = 0;
        if (character == null)
        {
            _wristSb.Append("<alpha=#88>").Append(Loc.Mod("no_character"));
        }
        else
        {
            int level = 0, xp = 0, gold = 0, cards = 0, want = 0, maxHp = 0;
            try
            {
                level = character.Level;
                xp = character.EXP;
                gold = character.CharacterGold;
                want = character.MaxCards;
                cards = character.HandAbilityCardIDs != null ? character.HandAbilityCardIDs.Count : 0;
                maxHp = MaxHealthAtLevel(character, level);
            }
            catch (System.Exception ex)
            {
                VRLog.Debug(Scope, $"Map-room wrist: a stat could not be read ({ex.Message}) — "
                    + "the plate shows what it did resolve.");
            }

            _wristSb.Append("<alpha=#AA>").Append(Loc.Game("GUI_LEVEL", "Level")).Append(' ')
                    .Append(level).Append("<alpha=#FF>\n");
            if (maxHp > 0)
            {
                // MAX hp, and the plate says so: a character carries no current HP outside a
                // scenario, so printing "HP 12" unqualified would read as full health rather than
                // as the class maximum at this level.
                _wristSb.Append("<color=#ff6a5e>").Append(Loc.Mod("hp")).Append(' ')
                        .Append("<size=18>max </size>").Append(maxHp).Append("</color>   ");
            }
            _wristSb.Append("<color=#7fd4ff>").Append(Loc.Mod("xp")).Append(' ')
                    .Append(xp).Append("</color>\n");
            _wristSb.Append("<color=#ffd45e>").Append(Loc.Mod("gold")).Append(' ')
                    .Append(gold).Append("</color>\n");
            // The loadout counter, the one number this phase is actually about: it is the game's own
            // "n / MaxCards" from the card-selection screen (UIPartyCharacterAbilityCardsDisplay.cs
            // :512), so a player who has not finished choosing sees the same shortfall the flat
            // window shows them — and the fan in front of them has exactly n cards in it.
            bool complete = want <= 0 || cards == want;
            _wristSb.Append(complete ? "<color=#9fe08a>" : "<color=#e08a8a>")
                    .Append(Loc.Game("GUI_ABILITY_CARDS", "Cards")).Append(' ')
                    .Append(cards);
            if (want > 0)
                _wristSb.Append(" / ").Append(want);
            _wristSb.Append("</color>");
        }

        string stats = _wristSb.ToString();
        if (stats != _wristLastStats)
        {
            _wristLastStats = stats;
            _wristStats.text = stats;
            if (_wristStats.font == null)
                WorldUIAssets.TryAssignGameFont(_wristStats);
        }
    }

    /// <summary>
    /// The class HP maximum at <paramref name="level"/>, from the character's own YML health table
    /// (<c>CharacterYMLData.HealthTable</c>, CharacterYMLData.cs:24 — a 1-based table indexed by
    /// level). 0 = unavailable, and the plate then simply omits the row rather than printing a
    /// zero that would read as "dead".
    /// </summary>
    private static int MaxHealthAtLevel(CMapCharacter character, int level)
    {
        try
        {
            int[]? table = character.CharacterYMLData?.HealthTable;
            if (table == null || table.Length == 0)
                return 0;
            int index = Mathf.Clamp(level - 1, 0, table.Length - 1);
            return table[index];
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    // ==========================================================================================
    //  RELEASE
    // ==========================================================================================

    private void ReleaseWrist()
    {
        if (_wristRoot != null)
        {
            Object.Destroy(_wristRoot);
            _wristRoot = null;
        }
        _wristCanvas = null;
        _wristGroup = null;
        _wristStats = null;
        _wristName = null;
        _wristPortraitGo = null;
        _wristPortrait = null;
        _wristHand = null;
        _wristShown = false;
        _wristAppliedOrder = int.MinValue;
        _wristLastStats = string.Empty;
        _wristLastName = string.Empty;
        _wristSideName = "?";
        _wristVerdict = "not built";
    }
}
