using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The existing physical item-card rim and reverse, shared by counter cards and
/// their inert multiplayer template. The face remains the game's original ItemCardUI.</summary>
internal static class TownServiceCardBody
{
    /// <summary>Unit width/height; scale x/y to the original face's measured physical size.
    /// Z remains one so the existing card mesh retains its real millimetre thickness.</summary>
    internal static GameObject Create(Transform parent)
    {
        var body = new GameObject("PhysicalCardBody");
        body.transform.SetParent(parent, false);
        CardMesh.AttachBody(body.AddComponent<MeshFilter>(), CardBodyKind.Item, 1f, 1f);
        body.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            CardMesh.CreateEdgeMaterial(CardBodyKind.Item),
            CardMesh.CreateBackMaterial(CardBodyKind.Item)
        };
        VRLayers.Apply(body);
        return body;
    }
}
