using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physical Ready/Undo/Skip button cluster at the table edge (ROADMAP P3c #1).
///
/// CLICK ROUTING (decided per button from the decompiled code, all signatures
/// verified against the real GH.Runtime.dll with ilspycmd 8.2, 2026-07-15):
///
/// All three buttons are clicked via <c>ExecuteEvents.pointerClickHandler</c> on the
/// REAL uGUI button GameObject — exactly the way the game's own hotkey bridge does it
/// (<c>BaseButtons.clickButton(GameObject)</c>: <c>if (Objbutton.GetComponent&lt;Selectable&gt;()
/// .IsInteractable()) ExecuteEvents.Execute(Objbutton, new PointerEventData(EventSystem.current),
/// ExecuteEvents.pointerClickHandler);</c> — IL 36 B). This runs the FULL guard chain:
///
/// - Ready — target <c>ReadyButton.ButtonComponent.gameObject</c>
///   (<c>public Button ButtonComponent =&gt; readyButton;</c>, an <c>ExtendedButton</c>).
///   <c>ExtendedButton.OnPointerClick</c> gates on
///   <c>InteractabilityManager.ShouldAllowClickForExtendedButton</c> (FTUE/tutorial),
///   then the prefab's onClick reaches <c>public void OnClick(bool networkActionIfOnline
///   = true)</c> which re-checks <c>ButtonComponent.enabled</c>, the warning mask and
///   <c>readyButton.interactable</c>, and (mouse mode — guaranteed by
///   <see cref="InputModeGuard"/>) commits synchronously via <c>public void
///   OnClickInternal(bool networkActionIfOnline = true)</c> (IL 1099 B). Calling
///   OnClickInternal directly would SKIP those guards and the MP synchronizer path —
///   deliberately not done (UI-ARCH §9.8).
/// - Undo — target <c>UndoButton.m_UndoButton.gameObject</c>. Prefab onClick →
///   <c>public void OnClick(bool networkActionIfOnline = true)</c> (checks
///   <c>m_UndoButton.interactable</c> + InteractabilityManager + plays the undo sound)
///   → <c>public bool OnClickInternal(bool networkActionIfOnline = true, GameAction
///   action = null)</c> (IL 670 B).
/// - Skip — target <c>SkipButton.skipButton.gameObject</c> (plain <c>Button</c>).
///   Prefab onClick → <c>public void OnClickFromButton()</c> which in mouse mode goes
///   straight to <c>public void OnClick(bool networkActionIfOnline = true)</c>.
///
/// MODALITY: ExecuteEvents bypasses GraphicRaycasters, so the game's raycaster-based
/// UI lock would NOT stop it (UI-ARCH §9.3). Pokes are therefore additionally gated on
/// <see cref="CanvasConversion.IsLockedNow"/> (mirrors <c>UIManager.ToggleLockUI</c>
/// via VREvents plus the phase-banner soft lock) and on the game's own
/// <c>Selectable.IsInteractable()</c>.
///
/// STATE MIRRORING (per frame, allocation-free): label text mirrors the live
/// <c>buttonText.text</c> TMP field of each real button — that string is already
/// localized by the game itself for the current <c>ReadyButton.EButtonState</c>
/// (16-member enum, verified) / undo / skip flows, so every state shows exactly the
/// words the 2D UI would show. The enum drives the accent color (confirm-ish states
/// glow warmer). Interactability mirrors <c>readyButton.interactable</c> /
/// <c>m_UndoButton.interactable</c> / <c>skipButton.interactable</c> — recomputed
/// every frame by the game's own <c>CheckButtonInteractability()</c> (UI-ARCH §9.4).
/// Disabled or hidden buttons are dimmed/hidden and their poke collider is disabled,
/// so they produce no poke events and no haptics.
///
/// Visuals: procedural base+cap meshes now; bundle prefabs
/// (<c>Assets/Bundle/Table/Button_Ready.prefab</c> etc., see the Table README asset
/// wishlist) are probed first and used when present. The cluster is hidden in
/// <see cref="VRMode.Menu2D"/>.
/// </summary>
internal sealed class ButtonCluster
{
    private const float CapPressDepth = 0.008f; // 8 mm cap travel (real meters)

    private GameObject? _root;
    private PhysicalButton? _ready;
    private PhysicalButton? _undo;
    private PhysicalButton? _skip;
    private bool _visible;

    public void Init()
    {
        // Built lazily in Tick when a scenario + rig exist.
    }

    public void Tick()
    {
        Choreographer? choreographer = Choreographer.s_Choreographer;
        bool wantCluster = WorldUIConfig.ButtonCluster.Value
                           && WorldUIConfig.ConversionActive
                           && VRModeStateMachine.CurrentMode != VRMode.Menu2D
                           && choreographer != null;

        if (!wantCluster)
        {
            SetVisible(false);
            return;
        }

        if (_root == null)
            Build();
        if (_root == null)
            return;

        SetVisible(true);
        PlaceCluster();

        bool locked = CanvasConversion.IsLockedNow;
        _ready!.MirrorReady(choreographer!.readyButton, locked);
        _undo!.MirrorUndo(choreographer.m_UndoButton, locked);
        _skip!.MirrorSkip(choreographer.m_SkipButton, locked);

        _ready.Animate();
        _undo.Animate();
        _skip.Animate();
    }

    public void Shutdown()
    {
        _ready?.Destroy();
        _undo?.Destroy();
        _skip?.Destroy();
        _ready = _undo = _skip = null;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }

    // ---- construction --------------------------------------------------------------------

    private void Build()
    {
        if (!PanelLayout.TryGetPose(PanelSlot.ButtonCluster, out _, out _))
            return; // no anchor yet

        _root = new GameObject("GloomhavenVR.ButtonCluster");

        // Left-to-right: Undo | Ready (center, larger) | Skip. Offsets in real meters.
        _undo = PhysicalButton.Create(_root.transform, "Undo", new Vector3(-0.11f, 0f, 0f), 0.038f,
            new Color(0.75f, 0.45f, 0.25f), ClickUndo);
        _ready = PhysicalButton.Create(_root.transform, "Ready", Vector3.zero, 0.05f,
            new Color(0.25f, 0.7f, 0.35f), ClickReady);
        _skip = PhysicalButton.Create(_root.transform, "Skip", new Vector3(0.11f, 0f, 0f), 0.038f,
            new Color(0.35f, 0.55f, 0.8f), ClickSkip);

        // Mod layer (render-only — pokes go through the VRInteractables registry).
        VRLayers.Apply(_root);
        VRLog.Info("WorldUI", "ButtonCluster built (Undo | Ready | Skip).");
    }

    private void PlaceCluster()
    {
        if (_root == null || !PanelLayout.TryGetPose(PanelSlot.ButtonCluster, out Vector3 pos, out Quaternion rot))
            return;
        float scale = PanelLayout.WorldScale;
        Transform t = _root.transform;
        t.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 180f, 0f)); // +Z toward player
        t.localScale = Vector3.one * scale;
    }

    private void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            if (!visible)
                return;
        }
        _visible = visible;
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    // ---- click routing (see class doc for the per-button decision) -------------------------

    private static void ClickReady()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || choreographer.readyButton == null)
            return;
        ClickUiButton(choreographer.readyButton.ButtonComponent);
    }

    private static void ClickUndo()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || choreographer.m_UndoButton == null)
            return;
        ClickUiButton(choreographer.m_UndoButton.m_UndoButton);
    }

    private static void ClickSkip()
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || choreographer.m_SkipButton == null)
            return;
        ClickUiButton(choreographer.m_SkipButton.skipButton);
    }

    /// <summary>
    /// The game's own programmatic click (BaseButtons.clickButton pattern):
    /// Selectable.IsInteractable precheck + ExecuteEvents.pointerClickHandler.
    /// Extra gate: the mirrored UI lock (ExecuteEvents bypasses raycasters).
    /// </summary>
    private static void ClickUiButton(Selectable? button)
    {
        if (button == null || !button.gameObject.activeInHierarchy || !button.IsInteractable())
            return;
        if (CanvasConversion.IsLockedNow)
        {
            VRLog.Debug("WorldUI", "ButtonCluster: click swallowed — UI locked (modality respected).");
            return;
        }
        ExecuteEvents.Execute(button.gameObject,
            new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
    }

    // ---- one physical button ----------------------------------------------------------------

    /// <summary>
    /// A single pokeable 3D button: base + travelling cap + world TMP label.
    /// Poke events arrive via the Phase-2 <see cref="IPokeable"/> registration; the
    /// PokeInteractor supplies hover/click haptics only while the collider is enabled.
    /// </summary>
    private sealed class PhysicalButton : IPokeable
    {
        private GameObject _rootGo = null!;
        private Transform _cap = null!;
        private BoxCollider _collider = null!;
        private Renderer _capRenderer = null!;
        private Renderer _baseRenderer = null!;
        private TextMeshPro _label = null!;
        private System.Action _onClick = null!;
        private Color _accent;

        private float _capRestY;
        private float _pressT;       // 1 = fully pressed, decays to 0
        private bool _interactable = true;
        private string? _mirroredText;
        private Color _appliedColor;

        public static PhysicalButton Create(Transform parent, string name, Vector3 localPos,
            float radius, Color accent, System.Action onClick)
        {
            var button = new PhysicalButton { _onClick = onClick, _accent = accent };

            GameObject? prefab = WorldUIAssets.TryLoadPrefab($"Assets/Bundle/Table/Button_{name}.prefab")
                                 ?? WorldUIAssets.TryLoadPrefab("Assets/Bundle/Table/ReadyButton.prefab");
            if (prefab != null)
                button.BuildFromPrefab(prefab, parent, name, localPos, radius);
            else
                button.BuildProcedural(parent, name, localPos, radius);

            VRInteractables.RegisterPokeable(button, button._collider);
            return button;
        }

        private void BuildFromPrefab(GameObject prefab, Transform parent, string name, Vector3 localPos, float radius)
        {
            _rootGo = Object.Instantiate(prefab, parent, worldPositionStays: false);
            _rootGo.name = $"GloomhavenVR.Button_{name}";
            _rootGo.transform.localPosition = localPos;

            Transform? cap = _rootGo.transform.Find("Cap");
            _cap = cap != null ? cap : _rootGo.transform;
            _capRenderer = (_cap.GetComponentInChildren<Renderer>() ?? _rootGo.GetComponentInChildren<Renderer>())!;
            _baseRenderer = _rootGo.GetComponentInChildren<Renderer>()!;
            _collider = _cap.GetComponent<BoxCollider>() ?? _cap.gameObject.AddComponent<BoxCollider>();
            _capRestY = _cap.localPosition.y;
            CreateLabel(_rootGo.transform.Find("LabelAnchor"), radius);
        }

        private void BuildProcedural(Transform parent, string name, Vector3 localPos, float radius)
        {
            _rootGo = new GameObject($"GloomhavenVR.Button_{name}");
            _rootGo.transform.SetParent(parent, worldPositionStays: false);
            _rootGo.transform.localPosition = localPos;

            // Base plate (flat box).
            GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            Object.Destroy(basePlate.GetComponent<Collider>());
            basePlate.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            basePlate.transform.localScale = new Vector3(radius * 2.4f, 0.012f, radius * 2.4f);
            basePlate.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            _baseRenderer = basePlate.GetComponent<Renderer>();
            _baseRenderer.sharedMaterial = WorldUIAssets.CreateFlatMaterial(new Color(0.16f, 0.14f, 0.12f));

            // Travelling cap (squashed cylinder).
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Cap";
            Object.Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            cap.transform.localScale = new Vector3(radius * 2f, 0.009f, radius * 2f); // cylinder height = 2*y
            cap.transform.localPosition = new Vector3(0f, 0.024f, 0f);
            _cap = cap.transform;
            _capRestY = _cap.localPosition.y;
            _capRenderer = cap.GetComponent<Renderer>();
            _capRenderer.sharedMaterial = WorldUIAssets.CreateFlatMaterial(_accent);

            // Poke collider slightly proud of the cap (primitive box — poke contract).
            _collider = _rootGo.AddComponent<BoxCollider>();
            _collider.center = new Vector3(0f, 0.026f, 0f);
            _collider.size = new Vector3(radius * 2f, 0.03f, radius * 2f);

            CreateLabel(null, radius);
        }

        private void CreateLabel(Transform? anchor, float radius)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(anchor != null ? anchor : _rootGo.transform, worldPositionStays: false);
            if (anchor == null)
            {
                labelGo.transform.localPosition = new Vector3(0f, 0.012f, -(radius * 2.4f) * 0.5f - 0.012f);
                labelGo.transform.localRotation = Quaternion.Euler(55f, 180f, 0f);
            }
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.fontSize = 0.35f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.rectTransform.sizeDelta = new Vector2(0.22f, 0.05f);
            _label.color = Color.white;
            _label.text = string.Empty;
        }

        public void Destroy()
        {
            VRInteractables.UnregisterPokeable(this);
            if (_rootGo != null)
                Object.Destroy(_rootGo);
        }

        // ---- mirroring -------------------------------------------------------------------

        public void MirrorReady(ReadyButton? real, bool locked)
        {
            if (real == null)
            {
                SetState(visible: false, interactable: false, null, null);
                return;
            }
            bool visible = real.gameObject.activeInHierarchy && real.IsVisibility;
            bool canUse = visible && real.IsInteractable && !locked
                          && !real.warningMask.gameObject.activeSelf;
            // Warm accent while the state is a "confirm" flavor (>= CONTINUE commits StepComplete).
            Color accent = real.buttonState >= ReadyButton.EButtonState.EREADYBUTTONCONTINUE
                ? new Color(0.85f, 0.65f, 0.2f)
                : _accent;
            SetState(visible, canUse, real.buttonText != null ? real.buttonText.text : null, accent);
        }

        public void MirrorUndo(UndoButton? real, bool locked)
        {
            if (real == null)
            {
                SetState(visible: false, interactable: false, null, null);
                return;
            }
            bool visible = real.gameObject.activeInHierarchy;
            bool canUse = visible && real.m_UndoButton != null && real.m_UndoButton.interactable && !locked;
            SetState(visible, canUse, real.m_ButtonText != null ? real.m_ButtonText.text : null, null);
        }

        public void MirrorSkip(SkipButton? real, bool locked)
        {
            if (real == null)
            {
                SetState(visible: false, interactable: false, null, null);
                return;
            }
            bool visible = real.gameObject.activeInHierarchy;
            bool canUse = visible && real.skipButton != null && real.skipButton.interactable && !locked;
            SetState(visible, canUse, real.buttonText != null ? real.buttonText.text : null, null);
        }

        private void SetState(bool visible, bool interactable, string? text, Color? accentOverride)
        {
            if (_rootGo == null)
                return;
            if (_rootGo.activeSelf != visible)
                _rootGo.SetActive(visible);
            if (!visible)
                return;

            if (_interactable != interactable)
            {
                _interactable = interactable;
                // Disabled: dimmed, and the collider goes away so the PokeInteractor
                // produces neither events nor haptics.
                _collider.enabled = interactable;
            }

            Color baseColor = accentOverride ?? _accent;
            Color applied = interactable ? baseColor : baseColor * 0.35f;
            if (applied != _appliedColor)
            {
                _appliedColor = applied;
                _capRenderer.sharedMaterial.color = applied;
            }
            if (_label != null)
            {
                _label.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                // Reference compare first: the game only reassigns the string on change.
                if (!ReferenceEquals(text, _mirroredText) && text != _mirroredText)
                {
                    _mirroredText = text;
                    _label.text = text ?? string.Empty;
                    if (_label.font == null)
                        WorldUIAssets.TryAssignGameFont(_label);
                }
            }
        }

        /// <summary>Cap travel animation (code-driven, no Animator).</summary>
        public void Animate()
        {
            if (_pressT <= 0f)
                return;
            _pressT = Mathf.Max(0f, _pressT - Time.deltaTime * 6f);
            float y = _pressT > 0f
                ? _capRestY - CapPressDepth * Mathf.Sin(_pressT * Mathf.PI)
                : _capRestY;
            Vector3 p = _cap.localPosition;
            p.y = y;
            _cap.localPosition = p;
        }

        // ---- IPokeable -------------------------------------------------------------------

        public void OnPokeEnter(VRHand hand) { }

        public void OnPokeExit(VRHand hand) { }

        public void OnPoke(VRHand hand)
        {
            if (!_interactable)
                return;
            _pressT = 1f;
            _onClick();
        }
    }
}
