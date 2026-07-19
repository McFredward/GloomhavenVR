using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GloomhavenVR.Hands;

/// <summary>
/// Persistent driver that owns both <see cref="VRHand"/>s. Hands live under the
/// Phase-1 rig root (<see cref="VRRigDriver.RigRoot"/>) so device poses share the
/// head's tracking space and diorama scale; they are torn down with the rig.
///
/// Desktop simulation ([Dev] SimulateHands, no HMD): hands are parented to the main
/// camera at diorama scale and animated procedurally (bobbing pose; hold T = trigger,
/// hold G = grip via InputSystem's Keyboard) so every consumer of the hand API can be
/// exercised flat.
///
/// RIG-REBUILD CHAIN (hardware test #3, root cause #4): this driver POLLS
/// <see cref="VRRigDriver.RigRoot"/> every frame — when the rig driver tears down and
/// rebuilds (head camera died/disabled/scene swap), the old hands root is destroyed
/// with the old rig root (it is a child) and <see cref="Build"/> re-creates the hands
/// under the new root the same/next frame. The chain is rig-state-driven, never
/// scene-driven: no scene event is needed for hands to re-home. The MainMenu failure
/// was upstream (the rig itself never rebuilt), not here.
///
/// Applies the <see cref="VRModeStateMachine"/> interactor policy on every mode change.
/// </summary>
internal sealed class HandsDriver : MonoBehaviour
{
    private const float SimScale = 12f;

    private GameObject? _handsRoot;
    private VRHand? _left;
    private VRHand? _right;
    private bool _simActive;
    private float _simTrigger;
    private float _simGrip;

    // Cached step delegates so the guarded per-frame calls allocate nothing (instance
    // method groups would allocate a fresh delegate each frame). Built once in Awake.
    private System.Action? _tickRig;
    private System.Action? _tickSim;

    private void Awake()
    {
        _tickRig = TickRig;
        _tickSim = AnimateSimulation;
    }

    private void OnEnable()
    {
        VRModeStateMachine.ModeChanged += OnModeChanged;
        // Dominance is live-switchable (Menu2D trigger switch / settings panel toggle
        // both write [Hands] PrimaryHand) — reapply the per-hand masks on change.
        Plugin.PrimaryHand.SettingChanged += OnPrimaryHandChanged;
    }

    private void OnDisable()
    {
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        Plugin.PrimaryHand.SettingChanged -= OnPrimaryHandChanged;
    }

    private void OnPrimaryHandChanged(object sender, System.EventArgs e)
    {
        ApplyMode(VRModeStateMachine.CurrentMode);
        VRLog.Info("Hands", $"[Hands] PrimaryHand is now '{Plugin.PrimaryHand.Value}' — " +
                            "per-hand interactor masks reapplied.");
    }

    private void Update()
    {
        // Both per-frame steps are ISOLATED + attributed via the shared Core.TickGuard so
        // a throw in the rig-homing/build path can't abort simulation (and vice versa) and
        // the log NAMES the thrower ("[Hands] Tick 'Hands.<step>' threw <exc + stack>")
        // instead of an anonymous per-frame NullReferenceException. Order preserved:
        // rig-homing first (sets _simActive + hand refs), simulation after. Cached
        // delegates → no per-frame allocation.
        TickGuard.Run("Hands.Rig", _tickRig!);
        if (_simActive)
            TickGuard.Run("Hands.Simulation", _tickSim!);
    }

    /// <summary>
    /// Per-frame rig-homing: resolve the parent (rig root in VR, main camera in desktop
    /// simulation) and build / reparent / tear down the hand tree to match. Extracted from
    /// <see cref="Update"/> so it can run under <see cref="TickGuard"/>.
    /// </summary>
    private void TickRig()
    {
        bool simulate = Plugin.SimulateHands.Value && !VRSession.IsRunning;

        Transform? parent = null;
        if (VRSession.IsRunning)
        {
            parent = VRRigDriver.RigRoot;
        }
        else if (simulate)
        {
            Camera? cam = Camera.main;
            if (cam != null)
                parent = cam.transform;
        }

        if (parent == null)
        {
            // Also covers the rig root being destroyed underneath us (Unity-null):
            // clear the static hand access even though the GOs are already gone.
            if (_handsRoot != null || VRHands.Left != null)
                TearDown();
            return;
        }

        if (_handsRoot == null)
            Build(parent, simulate); // includes rebuilding after external destruction
        else if (_handsRoot.transform.parent != parent)
            Reparent(parent, simulate);
    }

    private void OnDestroy() => TearDown();

    private void OnModeChanged(VRModeChange change) => ApplyMode(change.To);

    private void ApplyMode(VRMode mode)
    {
        // P5 (MISSION A.4): the policy is per-hand — the dominant hand may keep the
        // ray while the non-dominant hand owns the palm gate/fan (matrix in
        // docs/INTERFACES-P2.md §4). Dominance follows [Hands] PrimaryHand.
        bool leftIsDominant = VRHands.Primary == _left && _left != null;
        _left?.SetInteractorMask(VRModeStateMachine.InteractorsFor(
            mode, leftIsDominant ? HandRole.Dominant : HandRole.NonDominant));
        _right?.SetInteractorMask(VRModeStateMachine.InteractorsFor(
            mode, leftIsDominant ? HandRole.NonDominant : HandRole.Dominant));
    }

    // ---- build / teardown -------------------------------------------------------------

    private void Build(Transform parent, bool simulate)
    {
        _simActive = simulate;

        _handsRoot = new GameObject("GloomhavenVR.Hands");
        ConfigureRoot(parent, simulate);

        _left = CreateHand(HandSide.Left);
        _right = CreateHand(HandSide.Right);
        VRHands.Set(_left, _right);

        // Whole hand tree (palms, fingers, anchors) on the mod layer so the head
        // camera renders it regardless of the game camera's mask (CAMERA-POLICY §2).
        // No-op in desktop simulation (VRLayers.Apply gates on VRSession.IsRunning).
        VRLayers.Apply(_handsRoot);

        ApplyMode(VRModeStateMachine.CurrentMode);
        VRLog.Info("Hands", $"Hands built under '{parent.name}'{(simulate ? " (SIMULATED)" : string.Empty)}.");
    }

    private void ConfigureRoot(Transform parent, bool simulate)
    {
        Transform root = _handsRoot!.transform;
        root.SetParent(parent, worldPositionStays: false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        // Under the rig root the diorama scale is inherited; the sim parent (camera)
        // is unscaled, so apply an equivalent scale locally.
        root.localScale = simulate ? Vector3.one * SimScale : Vector3.one;
    }

    private VRHand CreateHand(HandSide side)
    {
        var go = new GameObject($"VRHand_{side}");
        go.transform.SetParent(_handsRoot!.transform, worldPositionStays: false);
        var hand = go.AddComponent<VRHand>();
        hand.Initialize(side);
        hand.SetSimulated(_simActive);
        return hand;
    }

    private void Reparent(Transform parent, bool simulate)
    {
        _simActive = simulate;
        ConfigureRoot(parent, simulate);
        _left?.SetSimulated(simulate);
        _right?.SetSimulated(simulate);
    }

    private void TearDown()
    {
        VRHands.Set(null, null);
        if (_handsRoot != null)
            Destroy(_handsRoot);
        _handsRoot = null;
        _left = null;
        _right = null;
        _simActive = false;
        VRLog.Info("Hands", "Hands torn down.");
    }

    // ---- desktop simulation --------------------------------------------------------------

    private void AnimateSimulation()
    {
        if (_left == null || _right == null)
            return;

        Keyboard? keyboard = Keyboard.current;
        float targetTrigger = keyboard != null && keyboard.tKey.isPressed ? 1f : 0f;
        float targetGrip = keyboard != null && keyboard.gKey.isPressed ? 1f : 0f;
        _simTrigger = Mathf.MoveTowards(_simTrigger, targetTrigger, Time.deltaTime * 6f);
        _simGrip = Mathf.MoveTowards(_simGrip, targetGrip, Time.deltaTime * 6f);

        float t = Time.unscaledTime;
        float bob = Mathf.Sin(t * 1.2f) * 0.02f;
        float sway = Mathf.Sin(t * 0.7f) * 0.015f;

        // Camera-local offsets (meters, scaled by the sim root): hands rest in view.
        _left.SetSimulatedInput(
            new Vector3(-0.18f + sway, -0.22f + bob, 0.5f),
            Quaternion.Euler(-20f + Mathf.Sin(t * 0.9f) * 8f, 10f, 5f),
            _simTrigger, _simGrip);
        _right.SetSimulatedInput(
            new Vector3(0.18f - sway, -0.22f - bob, 0.5f),
            Quaternion.Euler(-20f - Mathf.Sin(t * 0.9f) * 8f, -10f, -5f),
            _simTrigger, _simGrip);
    }
}
