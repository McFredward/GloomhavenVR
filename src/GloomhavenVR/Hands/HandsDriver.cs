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

    private void OnEnable() => VRModeStateMachine.ModeChanged += OnModeChanged;

    private void OnDisable() => VRModeStateMachine.ModeChanged -= OnModeChanged;

    private void Update()
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

        if (_simActive)
            AnimateSimulation();
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
