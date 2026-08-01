using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Compact character status on the non-dominant wrist (ROADMAP P3c #5): HP, XP,
/// gold, level and condition counts, look-at activated (watch-check gesture).
/// Test #13: topped by a PROMINENT character identity row — class portrait + name
/// of the character the player is currently acting for ("whose cards am I picking").
///
/// Data sources (verified via ilspycmd, ScenarioRuleLibrary.dll — the same values
/// NewPartyDisplayUI/ActorStatPanel render): <c>CActor.Health / MaxHealth
/// (public int)</c>, <c>public int XP =&gt; m_XP;</c>, <c>public int Gold =&gt; m_Gold;</c>,
/// <c>CActor.Level</c>, <c>public CTokens Tokens</c> with
/// <c>GetAllPositiveConditions()/GetAllNegativeConditions()</c>.
///
/// Actor resolution (test #13): during <see cref="VRMode.CardSelection"/> the hand
/// the game presents wins — <c>CardsHandManager.Instance.CurrentHand/GetActiveHand()</c>
/// (CardsHandManager.cs:127/137/595, via the Cards module's read-only
/// <c>CardsGameApi.ActiveHand()</c>) → <c>CardsHandUI.PlayerActor</c>
/// (CardsHandUI.cs:208) — that is the tab-switchable multi-merc hand whose cards are
/// being selected. Otherwise <c>Choreographer.CurrentPlayerActor</c> (own turn,
/// Choreographer.cs:474) falling back to
/// <c>InitiativeTrack.Instance.SelectedActor().Actor</c> (the pattern UndoButton
/// itself uses). Class portrait: <c>UIInfoTools.Instance (UIInfoTools.cs:465)
/// .GetNewAdventureCharacterPortrait(ECharacter, …)</c> (UIInfoTools.cs:773) with
/// <c>CCharacterClass.CharacterModel =&gt; m_CharacterYML.Model</c>
/// (CCharacterClass.cs:234) — the exact call the game's own card-selection preview
/// uses (CardsHandManager.ShowPreview, CardsHandManager.cs:776); when the sprite is
/// unavailable the name stays as gold text without an icon. The 2D party HUD
/// canvases are deeply embedded in <c>NewPartyDisplayUI</c> layout groups, so a
/// minimal TMP panel with live values is built instead of converting them
/// (documented decision — reuse was evaluated).
///
/// Refresh cadence: the panel rebuilds at 4 Hz (StringBuilder, only assigned on
/// change) and IMMEDIATELY on turn/selection changes
/// (<see cref="VREvents.ChoreographerMessage"/>,
/// <see cref="VREvents.CardSelectionChanged"/>, mode changes); the per-frame path
/// only does the look-at test and alpha fade — allocation-free.
/// </summary>
internal sealed class WristHud
{
    // Look-at hysteresis on the panel NORMAL (wrist +Y). Lowered this round because
    // VISIBILITY is the #1 requirement: the back of the hand only needs to be roughly
    // toward the HMD for the watch-check glance to register.
    private const float ShowDot = 0.35f;
    private const float HideDot = 0.2f;
    private const float RefreshInterval = 0.25f;

    private readonly StringBuilder _sb = new(256);

    private GameObject? _root;
    private CanvasGroup? _group;
    private TextMeshProUGUI? _text;
    private VRHand? _hand;
    private bool _shown;
    private float _nextRefresh;
    private string _lastText = string.Empty;

    // Test #13 identity row.
    private GameObject? _portraitGo;
    private UnityEngine.UI.Image? _portrait;
    private TextMeshProUGUI? _nameText;
    private CPlayerActor? _identityActor;
    private string _lastIdentity = string.Empty;
    private bool _eventsAttached;

    // ---- Item 10 + per-style rework: live-tunable pose (the "Wrist" debug category) --------
    // The watch-face pose — its OFFSET from the wrist anchor and its TILT (pitch/yaw/roll on
    // top of the flat-on-hand base) — is re-read and re-applied every Tick (ApplyPose), so
    // nudging a stepper in the VR debug menu moves the HUD immediately.
    //
    // PER HAND STYLE (2026-07 request B): the HUD rests on the hand MESH, whose shape differs
    // per style (Glove/Plate/Arcane), so the persistent home of the pose is the PER-STYLE
    // [WristHud] section of dev.gloomhavenvr.hands.cfg (HandsConfig.StyleWrist*, seeded once
    // from the legacy global [WorldUI] WristHud* entries so a tuned pose carried over to all
    // three styles). The accessors below read/write the ACTIVE style ([Hands] HandStyle), so
    // the debug steppers edit the style currently worn and a style switch re-poses the HUD on
    // the very next Tick. The legacy [WorldUI] entries stay bound (WorldUIConfig assigns them
    // here) purely as the pre-Bind fallback + the per-style seed source; the statics are the
    // last-resort in-session fallback. The HUD's on/off toggle ([WorldUI] WristHud) is global.
#pragma warning disable CS0649
    internal static ConfigEntry<float>? PitchEntry, YawEntry, RollEntry,
                                         OffsetXEntry, OffsetYEntry, OffsetZEntry;
#pragma warning restore CS0649
    private static float _pitch, _yaw, _roll;
    private static float _offX = 0f, _offY = 0.015f, _offZ = 0.01f;

    /// <summary>The ACTIVE style's element of a per-style pose array, else <paramref name="fallback"/>.</summary>
    private static float StyleGet(ConfigEntry<float>[]? styled, float fallback)
    {
        try
        {
            return styled != null ? styled[HandsConfig.ActiveStyleIndex].Value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Write the ACTIVE style's element of a per-style pose array. False = not bound yet.</summary>
    private static bool StyleSet(ConfigEntry<float>[]? styled, float value)
    {
        try
        {
            if (styled != null)
            {
                styled[HandsConfig.ActiveStyleIndex].Value = value; // BepInEx persists on set
                return true;
            }
        }
        catch
        {
            // fall through to the legacy path
        }
        return false;
    }

    internal static float PitchDeg
    {
        get => StyleGet(HandsConfig.StyleWristPitch, PitchEntry?.Value ?? _pitch);
        set { if (!StyleSet(HandsConfig.StyleWristPitch, value)) { if (PitchEntry != null) PitchEntry.Value = value; else _pitch = value; } }
    }
    internal static float YawDeg
    {
        get => StyleGet(HandsConfig.StyleWristYaw, YawEntry?.Value ?? _yaw);
        set { if (!StyleSet(HandsConfig.StyleWristYaw, value)) { if (YawEntry != null) YawEntry.Value = value; else _yaw = value; } }
    }
    internal static float RollDeg
    {
        get => StyleGet(HandsConfig.StyleWristRoll, RollEntry?.Value ?? _roll);
        set { if (!StyleSet(HandsConfig.StyleWristRoll, value)) { if (RollEntry != null) RollEntry.Value = value; else _roll = value; } }
    }
    internal static float OffsetX
    {
        get => StyleGet(HandsConfig.StyleWristOffsetX, OffsetXEntry?.Value ?? _offX);
        set { if (!StyleSet(HandsConfig.StyleWristOffsetX, value)) { if (OffsetXEntry != null) OffsetXEntry.Value = value; else _offX = value; } }
    }
    internal static float OffsetY
    {
        get => StyleGet(HandsConfig.StyleWristOffsetY, OffsetYEntry?.Value ?? _offY);
        set { if (!StyleSet(HandsConfig.StyleWristOffsetY, value)) { if (OffsetYEntry != null) OffsetYEntry.Value = value; else _offY = value; } }
    }
    internal static float OffsetZ
    {
        get => StyleGet(HandsConfig.StyleWristOffsetZ, OffsetZEntry?.Value ?? _offZ);
        set { if (!StyleSet(HandsConfig.StyleWristOffsetZ, value)) { if (OffsetZEntry != null) OffsetZEntry.Value = value; else _offZ = value; } }
    }

    // Flat-on-hand base rotation (see Build's rotation block): panel normal = wrist +Y, plane
    // spans wrist X/Z. The live pitch/yaw/roll compose in the panel's own local frame on top.
    private static readonly Quaternion FlatBackOfHand =
        Quaternion.LookRotation(Vector3.up, Vector3.forward);

    /// <summary>
    /// Item 10: re-apply the wrist HUD pose from the live-tunable offset + tilt. Called once in
    /// Build and every Tick, so the "Wrist" debug steppers move the watch face immediately.
    /// </summary>
    private void ApplyPose()
    {
        if (_root == null)
            return;
        _root.transform.localPosition = new Vector3(OffsetX, OffsetY, OffsetZ);
        _root.transform.localRotation = FlatBackOfHand * Quaternion.Euler(PitchDeg, YawDeg, RollDeg);
    }

    public void Tick()
    {
        EnsureEvents();
        bool want = WorldUIConfig.WristHud.Value && WorldUIConfig.ConversionActive
                    && Choreographer.s_Choreographer != null;

        VRHand? hand = NonDominantHand();
        if (!want || hand == null || !hand.HasPose)
        {
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
            return;
        }

        if (_root == null || !ReferenceEquals(hand, _hand))
            Build(hand);
        if (_root == null)
            return;

        if (!_root.activeSelf)
            _root.SetActive(true);

        // Item 10: re-read + re-apply the tunable pose every tick so the "Wrist" debug
        // steppers move the HUD live (allocation-free — a Vector3 + two quaternions).
        ApplyPose();

        // Look-at gate: the HUD is a flat watch-face shelf lying in the back-of-hand
        // plane; its readable front (panel NORMAL) points out the BACK of the hand along
        // wrist +Y (= Root.up — see Build's rotation block). It is visible while that
        // normal turns toward the HMD, i.e. when you glance DOWN at the back of your hand
        // (the watch-check gesture). Gate axis is wrist +Y (Root.up), matching the normal.
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null && _group != null)
        {
            Vector3 toHead = (head.transform.position - _root.transform.position).normalized;
            float dot = Vector3.Dot(hand.Rig.Root.up, toHead);
            if (!_shown && dot > ShowDot) _shown = true;
            else if (_shown && dot < HideDot) _shown = false;

            float target = _shown ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.deltaTime * 6f);
        }

        if (_shown && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshText();
        }
    }

    public void Shutdown()
    {
        DetachEvents();
        DestroyPanel();
    }

    private void DestroyPanel()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _hand = null;
        _portraitGo = null;
        _portrait = null;
        _nameText = null;
        _identityActor = null;
        _lastIdentity = string.Empty;
    }

    // ---- live update events (test #13) ---------------------------------------------------

    private void EnsureEvents()
    {
        if (_eventsAttached)
            return;
        _eventsAttached = true;
        VREvents.ChoreographerMessage += OnGameFlowChanged;
        VREvents.CardSelectionChanged += OnCardSelectionChanged;
        VRModeStateMachine.ModeChanged += OnModeChanged;
        Core.Loc.OnChanged += OnLanguageChanged; // live language following
    }

    private void DetachEvents()
    {
        if (!_eventsAttached)
            return;
        _eventsAttached = false;
        VREvents.ChoreographerMessage -= OnGameFlowChanged;
        VREvents.CardSelectionChanged -= OnCardSelectionChanged;
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        Core.Loc.OnChanged -= OnLanguageChanged;
    }

    // Turn/round messages, card (de)selection and mode flips all potentially change
    // whose character the HUD shows — force the next shown tick to refresh at once.
    private void OnGameFlowChanged(ChoreoMessageEvent e) => _nextRefresh = 0f;
    private void OnCardSelectionChanged(CardSelectionEvent e) => _nextRefresh = 0f;
    private void OnModeChanged(VRModeChange e) => _nextRefresh = 0f;
    // Language change: the change-gated strings differ once localized, so a forced refresh re-reads them.
    private void OnLanguageChanged() => _nextRefresh = 0f;

    // ---- construction ------------------------------------------------------------------

    private static VRHand? NonDominantHand()
    {
        VRHand? primary = VRHands.Primary;
        if (primary == null)
            return VRHands.Left ?? VRHands.Right;
        return VRHands.Get(primary.Side == HandSide.Left ? HandSide.Right : HandSide.Left);
    }

    private void Build(VRHand hand)
    {
        DestroyPanel();
        _hand = hand;

        _root = new GameObject("GloomhavenVR.WristHud");
        _root.layer = 5; // UI
        Transform wrist = hand.Rig.Wrist;
        _root.transform.SetParent(wrist, worldPositionStays: false);
        // Panel position: a flat "watch face" shelf resting just proud of the BACK of the
        // hand near the wrist. Because the panel now lies FLAT in the wrist X/Z plane (see
        // rotation below), its 9.6 cm width spans wrist X and its 7.8 cm height spans wrist
        // Z (along the fingers) — NEITHER dimension extends along +Y anymore, so no large
        // radial lift is needed. We only push it ~1.5 cm out along +Y so it hovers just
        // proud of the hand mesh, and nudge it slightly toward the fingers (+Z) so the
        // tray sits over the back of the hand rather than the forearm.
        // Item 10: position (and the tilt below) now come from the live-tunable pose
        // (ApplyPose). Its defaults match the values described here — OffsetX 0, OffsetY
        // +1.5 cm (proud of the hand), OffsetZ +1 cm (toward the fingers) — so the resting
        // look is unchanged; the "Wrist" debug category nudges them live.
        // Wrist frame (HandRig contract, HandRig.cs:45): +Z along the fingers, +Y out of
        // the BACK of the hand, +X shared left/right by both hands.
        //
        // GOAL (user, hardware-tested): the panel must lie FLAT/HORIZONTAL on the back of
        // the hand — a watch face / small tray you read by glancing DOWN at your hand
        // ("flach, horizontal, am unteren Rand der Hand"). Flat-on-hand means the panel
        // NORMAL points straight out the BACK of the hand = wrist +Y, and the panel plane
        // spans wrist X and wrist Z.
        //
        // HARDWARE HISTORY (this has flip-flopped — document the FINAL mapping explicitly):
        //   - An earlier round used LookRotation(forward=+Y, up=+Z): normal on +Y but the
        //     glance/gate axis was mismatched, so it read as a vertical billboard.
        //   - Last round over-corrected to LookRotation(forward=+Z, up=-Y): that put the
        //     normal on wrist +Z (the FINGER axis), i.e. the panel stood PERPENDICULAR to
        //     the back-of-hand plane → edge-on and INVISIBLE when glancing down. Regression.
        //
        // FINAL (this round): LookRotation(forward=+Y, up=+Z) = LookRotation(Vector3.up,
        // Vector3.forward). Unity's LookRotation maps local +Z -> forward-arg and local +Y
        // -> up-arg (orthogonalised). Verified axis decomposition (Unity left-handed:
        // x = Cross(up, forward)):
        //   canvas +Z (READABLE FRONT) -> wrist +Y  (straight out the back of the hand,
        //                                             toward the down-glancing HMD => the
        //                                             text face is what you see)
        //   canvas +Y (text top)       -> wrist +Z  (toward the FINGERS => 12-o'clock of
        //                                             the watch points up your hand, so the
        //                                             stats read upright when glanced down
        //                                             at, like a watch face)
        //   canvas +X (text right)     -> wrist -X
        // Panel normal = wrist +Y and the plane spans wrist X and wrist Z = FLAT/HORIZONTAL
        // on the back of the hand. This is a PROPER rotation (det +1), so the front face is
        // NEVER mirrored: a world-space canvas always reads correctly when viewed from its
        // +Z side, which is exactly the down-glance viewer here. The look-at gate above was
        // updated to match this normal (wrist +Y = Root.up).
        //
        // FLIP FALLBACK (the ONE thing that can't be verified without a headset):
        //   * If the text reads UPSIDE-DOWN (rotated 180 deg in-plane), swap the up-arg
        //     sign — LookRotation(Vector3.up, Vector3.back) — no gate change needed.
        //   * If you see the BACK face / it stays edge-on-invisible (normal pointing the
        //     wrong way), flip the forward-arg — LookRotation(Vector3.down, Vector3.forward)
        //     — AND flip the gate axis to Vector3.Dot(-hand.Rig.Root.up, toHead).
        // Item 10: base flat-on-hand rotation (LookRotation(up, forward)) composed with the
        // live pitch/yaw/roll, plus the offset above — all applied by ApplyPose.
        ApplyPose();

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = CanvasConversion.WorldCamera;
        var rect = (RectTransform)_root.transform;
        rect.sizeDelta = new Vector2(240f, 196f); // +46 px identity row (test #13)
        rect.localScale = Vector3.one * 0.0004f; // 0.4 mm/px → 9.6 × 7.8 cm watch face

        _group = _root.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var bg = new GameObject("Background");
        bg.layer = 5;
        bg.transform.SetParent(_root.transform, worldPositionStays: false);
        var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);
        MrBacking.Opacify(bgImage); // 0.82 backdrop lets the room shimmer through in MR
        var bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        // Identity row (test #13): class portrait + prominent name at the top.
        _portraitGo = new GameObject("Portrait");
        _portraitGo.layer = 5;
        _portraitGo.transform.SetParent(_root.transform, worldPositionStays: false);
        _portrait = _portraitGo.AddComponent<UnityEngine.UI.Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget = false;
        var portraitRect = (RectTransform)_portraitGo.transform;
        portraitRect.anchorMin = new Vector2(0f, 1f);
        portraitRect.anchorMax = new Vector2(0f, 1f);
        portraitRect.pivot = new Vector2(0f, 1f);
        portraitRect.anchoredPosition = new Vector2(8f, -8f);
        portraitRect.sizeDelta = new Vector2(44f, 44f);
        _portraitGo.SetActive(false); // enabled once a sprite resolves

        var nameGo = new GameObject("Name");
        nameGo.layer = 5;
        nameGo.transform.SetParent(_root.transform, worldPositionStays: false);
        _nameText = nameGo.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 21f;
        _nameText.alignment = TextAlignmentOptions.MidlineLeft;
        _nameText.richText = true;
        _nameText.enableWordWrapping = false;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        var nameRect = (RectTransform)nameGo.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.offsetMin = new Vector2(60f, -52f);
        nameRect.offsetMax = new Vector2(-8f, -8f);
        WorldUIAssets.TryAssignGameFont(_nameText);

        var textGo = new GameObject("Text");
        textGo.layer = 5;
        textGo.transform.SetParent(_root.transform, worldPositionStays: false);
        _text = textGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 24f;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.richText = true;
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 6f);
        textRect.offsetMax = new Vector2(-8f, -58f);
        WorldUIAssets.TryAssignGameFont(_text);

        _lastText = string.Empty;
        _lastIdentity = string.Empty;
        _identityActor = null;
        _nextRefresh = 0f;
        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        VRLayers.Apply(_root);
        VRLog.Info("WorldUI", $"WristHud built on {hand.Side} wrist.");
    }

    // ---- data --------------------------------------------------------------------------

    // ---- stale-value diagnostics (user bug: "wrist info doesn't update on gold/XP") ------
    private CPlayerActor? _lastValueActor;
    private int _lastHp, _lastMaxHp, _lastXp, _lastGold, _lastLevel;
    private float _lastValueLog;

    private void RefreshText()
    {
        if (_text == null)
            return;

        // STALE-VALUE FIX: the flow-facing resolution (choreographer message actor / hand
        // actor / initiative-track actor) can hand back a SNAPSHOT clone (CActor.Clone is
        // MemberwiseClone; messages and UI caches carry actor objects). A clone's
        // m_Gold/m_XP are frozen at capture time, so the 4 Hz poll re-read the same numbers
        // forever. The RULES mutate the live instances in ScenarioManager.Scenario
        // .PlayerActors (LootTile → AddGold, GainXP — CActor.cs:1813/2011), so the resolved
        // IDENTITY is re-mapped onto the live scenario actor by ActorGuid before any stat
        // is read. liveRemap=True in the log line below is the hardware proof that a stale
        // instance was actually being displayed.
        CPlayerActor? resolved = ResolveActor();
        CPlayerActor? actor = ToLiveActor(resolved);
        bool liveRemap = !ReferenceEquals(resolved, actor);
        RefreshIdentity(actor);

        if (actor != null)
        {
            int hp = actor.Health, maxHp = actor.MaxHealth, xp = actor.XP,
                gold = actor.Gold, level = actor.Level;
            bool actorChanged = !ReferenceEquals(actor, _lastValueActor);
            if (actorChanged || hp != _lastHp || maxHp != _lastMaxHp || xp != _lastXp
                || gold != _lastGold || level != _lastLevel)
            {
                // Throttled hardware-proof line: displayed values changed (old → new).
                float now = Time.unscaledTime;
                if (now - _lastValueLog >= 0.5f)
                {
                    _lastValueLog = now;
                    string oldVals = actorChanged
                        ? "(new actor)"
                        : $"HP {_lastHp}/{_lastMaxHp}, XP {_lastXp}, Gold {_lastGold}, L{_lastLevel}";
                    VRLog.Info("WorldUI", $"WristHud values: '{actor.CharacterName}' {oldVals} → " +
                                          $"HP {hp}/{maxHp}, XP {xp}, Gold {gold}, L{level} " +
                                          $"(liveRemap={liveRemap}).");
                }
                _lastValueActor = actor;
                _lastHp = hp; _lastMaxHp = maxHp; _lastXp = xp;
                _lastGold = gold; _lastLevel = level;
            }
        }
        else
        {
            _lastValueActor = null;
        }

        _sb.Length = 0;
        if (actor == null)
        {
            _sb.Append("<alpha=#88>").Append(Core.Loc.Mod("no_character"));
        }
        else
        {
            _sb.Append("<alpha=#AA>").Append(Core.Loc.Game("GUI_LEVEL", "Level")).Append(' ')
               .Append(actor.Level).Append("<alpha=#FF>\n");
            _sb.Append("<color=#ff6a5e>").Append(Core.Loc.Mod("hp")).Append(' ')
               .Append(actor.Health).Append('/').Append(actor.MaxHealth).Append("</color>   ");
            _sb.Append("<color=#7fd4ff>").Append(Core.Loc.Mod("xp")).Append(' ')
               .Append(actor.XP).Append("</color>\n");
            _sb.Append("<color=#ffd45e>").Append(Core.Loc.Mod("gold")).Append(' ')
               .Append(actor.Gold).Append("</color>\n");

            CTokens tokens = actor.Tokens;
            if (tokens != null)
            {
                // 4 Hz refresh — the two list allocations here are acceptable
                // (never in the per-frame path).
                var positives = tokens.GetAllPositiveConditions();
                var negatives = tokens.GetAllNegativeConditions();
                if (positives.Count > 0 || negatives.Count > 0)
                {
                    for (int i = 0; i < positives.Count; i++)
                        _sb.Append("<color=#9fe08a>+").Append(positives[i]).Append("</color> ");
                    for (int i = 0; i < negatives.Count; i++)
                        _sb.Append("<color=#e08a8a>-").Append(negatives[i]).Append("</color> ");
                }
                else
                {
                    _sb.Append("<alpha=#88>").Append(Core.Loc.Mod("no_conditions"));
                }
            }
        }

        string text = _sb.ToString();
        if (text != _lastText)
        {
            _lastText = text;
            _text.text = text;
            if (_text.font == null)
                WorldUIAssets.TryAssignGameFont(_text);
        }
    }

    /// <summary>
    /// Identity row (test #13): portrait sprite is fetched only when the resolved
    /// actor changes; the name/context string rebuilds at refresh cadence and is
    /// only assigned on change (same pattern as the stats text).
    /// </summary>
    private void RefreshIdentity(CPlayerActor? actor)
    {
        if (_nameText == null)
            return;

        if (!ReferenceEquals(actor, _identityActor))
        {
            _identityActor = actor;
            Sprite? sprite = null;
            if (actor != null)
            {
                // Verified: UIInfoTools.Instance (UIInfoTools.cs:465),
                // GetNewAdventureCharacterPortrait (UIInfoTools.cs:773),
                // CCharacterClass.CharacterModel (CCharacterClass.cs:234) — the call
                // the game's card-selection preview makes (CardsHandManager.cs:776).
                // Guarded: modded/custom classes may lack a config → text fallback.
                try
                {
                    UIInfoTools tools = UIInfoTools.Instance;
                    if (tools != null)
                        sprite = tools.GetNewAdventureCharacterPortrait(actor.CharacterClass.CharacterModel);
                }
                catch (System.Exception ex)
                {
                    VRLog.Debug("WorldUI", $"WristHud: no class portrait ({ex.GetType().Name}) — name-only identity.");
                }
            }
            if (_portrait != null && _portraitGo != null)
            {
                _portrait.sprite = sprite;
                _portraitGo.SetActive(sprite != null);
            }
        }

        Choreographer choreographer = Choreographer.s_Choreographer;
        // SameActor (guid), not ReferenceEquals: the displayed actor is live-remapped
        // (ToLiveActor) and may be a different INSTANCE than the choreographer's message
        // actor while still being the same character.
        string context =
            actor == null ? string.Empty :
            VRModeStateMachine.CurrentMode == VRMode.CardSelection ? Core.Loc.Mod("selecting_cards") :
            choreographer != null && SameActor(actor, choreographer.CurrentPlayerActor) ? Core.Loc.Mod("current_turn") :
            Core.Loc.Mod("selected");
        _sb.Length = 0;
        if (actor == null)
            _sb.Append("<alpha=#88>—");
        else
            _sb.Append("<size=13><alpha=#AA>").Append(context).Append("</size>\n")
               .Append("<b><color=#ffd45e>").Append(actor.CharacterName).Append("</color></b>");

        string identity = _sb.ToString();
        if (identity != _lastIdentity)
        {
            _lastIdentity = identity;
            _nameText.text = identity;
            if (_nameText.font == null)
                WorldUIAssets.TryAssignGameFont(_nameText);
        }
    }

    private static CPlayerActor? ResolveActor()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return null;

        // Multiplayer compat (audit R1): this HUD is the LOCAL player's own watch face. When
        // ONLINE it must ALWAYS show the character THIS client controls, never whoever's turn it
        // currently is — during a REMOTE player's turn Choreographer.CurrentPlayerActor and the
        // active card hand are the remote character, so the offline resolution below would leak
        // their Level/HP/XP/Gold/conditions onto the local wrist. Ownership test is
        // CPlayerActor.IsUnderMyControl, the SAME guard CardsGameApi.IsLocalHand and
        // Net/RevealGate use. Offline (single-player) there is exactly one local player who owns
        // every merc, so this whole block is skipped and the original behaviour below runs
        // byte-for-byte.
        if (FFSNetwork.IsOnline)
        {
            // During CardSelection the tab-switchable hand identifies WHICH of the local
            // player's own mercs is currently being viewed — honour it, but only when it is a
            // hand we actually control (IsLocalHand is IsUnderMyControl-gated while online, and
            // guarantees a non-null PlayerActor), so a remote's fan can never win.
            if (VRModeStateMachine.CurrentMode == VRMode.CardSelection)
            {
                CardsHandUI? hand = Cards.CardsGameApi.ActiveHand();
                if (hand != null && Cards.CardsGameApi.IsLocalHand(hand))
                    return hand.PlayerActor;
            }
            // Otherwise resolve straight to the local player's own character, independent of the
            // active turn. A null result (spectator / no assigned actor) makes RefreshText hide
            // the HUD rather than fall through to a remote actor's stats.
            return LocalPlayerActor();
        }

        // Test #13: while cards are being selected, the character the fan belongs to
        // wins — the tab-switchable hand the game presents. Read-only access through
        // the Cards module's verified API surface (CardsGameApi.ActiveHand():
        // CardsHandManager.Instance/CurrentHand/GetActiveHand, CardsHandManager.cs:
        // 127/137/595; CardsHandUI.PlayerActor, CardsHandUI.cs:208).
        if (VRModeStateMachine.CurrentMode == VRMode.CardSelection)
        {
            CardsHandUI? hand = Cards.CardsGameApi.ActiveHand();
            if (hand != null && hand.PlayerActor != null)
                return hand.PlayerActor;
        }

        CPlayerActor? actor = choreographer.CurrentPlayerActor;
        if (actor != null)
            return actor;

        InitiativeTrack track = InitiativeTrack.Instance;
        if (track != null)
        {
            InitiativeTrackActorBehaviour selected = track.SelectedActor();
            if (selected != null && selected.Actor is CPlayerActor selectedPlayer)
                return selectedPlayer;
        }
        return null;
    }

    /// <summary>Same character? Guid compare with reference fallback — instance-safe across
    /// the message/hand snapshot clones the game passes around.</summary>
    private static bool SameActor(CPlayerActor? a, CPlayerActor? b)
    {
        if (a == null || b == null)
            return false;
        if (ReferenceEquals(a, b))
            return true;
        string ga = a.ActorGuid, gb = b.ActorGuid;
        return !string.IsNullOrEmpty(ga) && string.Equals(ga, gb, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Stale-value fix: re-map a resolved actor onto the LIVE rules instance in
    /// <c>ScenarioManager.Scenario.PlayerActors</c> (matched by ActorGuid) — the object
    /// <c>LootTile → AddGold</c> and <c>GainXP</c> actually mutate (CActor.cs:1813/2011).
    /// Falls back to the resolved object when the scenario/list is unavailable or the guid
    /// is not found (e.g. exhausted actor moved to ExhaustedPlayers), so the HUD never goes
    /// blank because of the remap.
    /// </summary>
    private static CPlayerActor? ToLiveActor(CPlayerActor? actor)
    {
        if (actor == null)
            return null;
        CScenario scenario = ScenarioManager.Scenario;
        var players = scenario?.PlayerActors;
        if (players == null)
            return actor;
        for (int i = 0; i < players.Count; i++)
        {
            CPlayerActor player = players[i];
            if (player != null && SameActor(player, actor))
                return player;
        }
        return actor;
    }

    /// <summary>
    /// Multiplayer compat (audit R1): the LOCAL player's own <see cref="CPlayerActor"/> — the
    /// character THIS client controls — independent of whose turn it is. Ownership is
    /// <c>CPlayerActor.IsUnderMyControl</c>, the same test <c>CardsGameApi.IsLocalHand</c> and
    /// <c>Net/RevealGate</c> use; the actor list is the scenario's own
    /// <c>CScenario.PlayerActors</c>. Returns null when no local-controlled actor exists
    /// (spectator / not yet assigned), so the HUD hides rather than showing a remote's stats.
    /// Only meaningful while <c>FFSNetwork.IsOnline</c> (offline every merc is under my control).
    /// </summary>
    private static CPlayerActor? LocalPlayerActor()
    {
        CScenario scenario = ScenarioManager.Scenario;
        if (scenario == null)
            return null;
        var players = scenario.PlayerActors;
        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                CPlayerActor player = players[i];
                if (player != null && player.IsUnderMyControl)
                    return player;
            }
        }
        return null;
    }
}
