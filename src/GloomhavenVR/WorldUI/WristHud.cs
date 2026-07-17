using System.Text;
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
    private const float ShowDot = 0.55f;
    private const float HideDot = 0.35f;
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

        // Look-at gate: the HUD sits on the back of the wrist (+Y of the wrist
        // anchor, Phase-2 HandRig contract); visible while that side faces the HMD.
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
    }

    private void DetachEvents()
    {
        if (!_eventsAttached)
            return;
        _eventsAttached = false;
        VREvents.ChoreographerMessage -= OnGameFlowChanged;
        VREvents.CardSelectionChanged -= OnCardSelectionChanged;
        VRModeStateMachine.ModeChanged -= OnModeChanged;
    }

    // Turn/round messages, card (de)selection and mode flips all potentially change
    // whose character the HUD shows — force the next shown tick to refresh at once.
    private void OnGameFlowChanged(ChoreoMessageEvent e) => _nextRefresh = 0f;
    private void OnCardSelectionChanged(CardSelectionEvent e) => _nextRefresh = 0f;
    private void OnModeChanged(VRModeChange e) => _nextRefresh = 0f;

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
        // Watch position: slightly toward the forearm, floating just above the skin.
        _root.transform.localPosition = new Vector3(0f, 0.02f, -0.05f);
        // Canvas front faces -forward → forward must point INTO the arm (-Y of the
        // wrist) so the panel reads from the back-of-hand side.
        _root.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

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

    private void RefreshText()
    {
        if (_text == null)
            return;

        CPlayerActor? actor = ResolveActor();
        RefreshIdentity(actor);

        _sb.Length = 0;
        if (actor == null)
        {
            _sb.Append("<alpha=#88>no character");
        }
        else
        {
            _sb.Append("<alpha=#AA>Level ").Append(actor.Level).Append("<alpha=#FF>\n");
            _sb.Append("<color=#ff6a5e>HP ").Append(actor.Health).Append('/').Append(actor.MaxHealth).Append("</color>   ");
            _sb.Append("<color=#7fd4ff>XP ").Append(actor.XP).Append("</color>\n");
            _sb.Append("<color=#ffd45e>Gold ").Append(actor.Gold).Append("</color>\n");

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
                    _sb.Append("<alpha=#88>no conditions");
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
        string context =
            actor == null ? string.Empty :
            VRModeStateMachine.CurrentMode == VRMode.CardSelection ? "selecting cards" :
            choreographer != null && ReferenceEquals(actor, choreographer.CurrentPlayerActor) ? "current turn" :
            "selected";
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
}
