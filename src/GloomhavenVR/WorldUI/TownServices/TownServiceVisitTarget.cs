using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Compatibility facade for resident availability. Visits now start through proximity;
/// only tangible offerings and cabinet controls expose pointer targets.</summary>
internal sealed class TownServiceVisitTarget : IPokeable, IDisposable
{
    internal static byte ServiceOf(EGuildmasterMode mode) => mode == EGuildmasterMode.Merchant ? (byte)1
        : mode == EGuildmasterMode.Temple ? (byte)2 : mode == EGuildmasterMode.Enchantress ? (byte)3 : (byte)0;
    internal static bool Replaces(EGuildmasterMode mode)
    {
        byte service = ServiceOf(mode);
        return service != 0 && WorldUIConfig.ImmersiveTownServices.Value
            && ((service != 1 && service != 3) || TownServiceEnhancementHandoff.Enabled)
            && TownServicePopulation.Available(service);
    }

    // Residents are approached, not pressed. The former torso-sized trigger had no visible
    // counterpart and stopped the beam beside moving arms and in empty space. Physical stock,
    // category buttons, rollers and offerings retain their own visible, exact input surfaces.
    internal TownServiceVisitTarget(byte service, Transform station) { }
    internal void Tick(bool visible) { }
    internal static float OccludingDistance(Vector3 origin, Vector3 direction, float maximum) => float.PositiveInfinity;
    internal static void TickLaser() { }
    public void OnPokeEnter(VRHand hand) { }
    public void OnPokeExit(VRHand hand) { }
    public void OnPoke(VRHand hand) { }
    public void Dispose() { }
}
