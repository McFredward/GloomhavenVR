using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Compact character status on the non-dominant wrist (ROADMAP P3c #5): HP, XP,
/// gold, level and condition counts, look-at activated (watch-check gesture).
///
/// Data sources (verified via ilspycmd, ScenarioRuleLibrary.dll — the same values
/// NewPartyDisplayUI/ActorStatPanel render): <c>CActor.Health / MaxHealth
/// (public int)</c>, <c>public int XP =&gt; m_XP;</c>, <c>public int Gold =&gt; m_Gold;</c>,
/// <c>CActor.Level</c>, <c>public CTokens Tokens</c> with
/// <c>GetAllPositiveConditions()/GetAllNegativeConditions()</c>;
/// actor picked from <c>Choreographer.CurrentPlayerActor</c> (own turn) falling back
/// to <c>InitiativeTrack.Instance.SelectedActor().Actor</c> (the pattern UndoButton
/// itself uses). The 2D party HUD canvases are deeply embedded in
/// <c>NewPartyDisplayUI</c> layout groups, so a minimal TMP panel with live values is
/// built instead of converting them (documented decision — reuse was evaluated).
///
/// Refresh cadence: the panel text rebuilds at 4 Hz (StringBuilder, only assigned on
/// change); the per-frame path only does the look-at test and alpha fade —
/// allocation-free.
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

    public void Tick()
    {
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
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _hand = null;
    }

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
        Shutdown();
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
        rect.sizeDelta = new Vector2(240f, 150f);
        rect.localScale = Vector3.one * 0.0004f; // 0.4 mm/px → 9.6 × 6 cm watch face

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
        textRect.sizeDelta = new Vector2(-16f, -12f);
        WorldUIAssets.TryAssignGameFont(_text);

        _lastText = string.Empty;
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
        _sb.Length = 0;
        if (actor == null)
        {
            _sb.Append("<alpha=#88>no character");
        }
        else
        {
            _sb.Append("<b>").Append(actor.CharacterName).Append("</b>  L").Append(actor.Level).Append('\n');
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

    private static CPlayerActor? ResolveActor()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return null;

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
