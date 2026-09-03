using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Marks a uGUI raycast target that exists ONLY for the fingertip poke (ModBuild 403). The laser
/// ignores it: <see cref="UguiPointer"/>'s far-ray top-hit resolution skips any result whose
/// GameObject carries this component and takes the next one down, so a pad enlarged for a finger
/// never enlarges what the beam can click.
///
/// <para>Why a marker and not a layer or a second raycaster: the poke and the laser share one
/// <c>GraphicRaycaster</c> per canvas by design (the whole modality/lock gating lives on that one
/// component), and a marker read on the result list is the smallest thing that separates the two
/// without touching how either of them raycasts.</para>
///
/// <para>ModBuild 405: the marker cuts both ways. The FINGER's top-hit resolution prefers a result
/// carrying this component wherever it ranks in the depth sort — a pad is a child of the small
/// button it serves and therefore sorts under every later sibling that overlaps it, so without
/// the preference the big area won at any pixel inside the plate. A pad exists only where the
/// finger is meant to win; its presence in the list is the decision.</para>
/// </summary>
internal sealed class PokeOnlyTarget : MonoBehaviour
{
}
