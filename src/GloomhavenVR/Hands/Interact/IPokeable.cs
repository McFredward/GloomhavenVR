namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Something a fingertip can press (FROZEN Phase-2 API).
///
/// Implement on a MonoBehaviour and register it together with a Collider via
/// <see cref="VRInteractables.RegisterPokeable"/> — or derive from
/// <see cref="PokeableBehaviour"/> which does the registration on enable/disable.
/// The collider should be a primitive or convex collider (the poke test uses
/// <c>Collider.ClosestPoint</c>, which does not support non-convex meshes).
///
/// Callback order per interaction: OnPokeEnter → (OnPoke)* → OnPokeExit.
/// All callbacks fire on the main thread from the hand update loop; keep them cheap
/// and never block (VREvents threading rules apply).
/// </summary>
internal interface IPokeable
{
    /// <summary>Fingertip entered hover range (~3.5 cm). Highlight yourself here.</summary>
    void OnPokeEnter(VRHand hand);

    /// <summary>Fingertip left hover range (also called when the interactor is disabled mid-hover).</summary>
    void OnPokeExit(VRHand hand);

    /// <summary>
    /// Fingertip pressed the surface (contact within the ~8 mm fingertip radius).
    /// Fires once per press; re-arms after the fingertip retracts.
    /// </summary>
    void OnPoke(VRHand hand);
}
