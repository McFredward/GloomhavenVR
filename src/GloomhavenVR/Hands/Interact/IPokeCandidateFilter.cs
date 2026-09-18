using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Optional ownership gate for overlapping collider targets. It can only remove a candidate;
/// the ordinary foreground distance, contact threshold and grip/re-arm rules still decide.
/// </summary>
internal interface IPokeCandidateFilter
{
    bool AcceptsPokePoint(Vector3 point);
}
