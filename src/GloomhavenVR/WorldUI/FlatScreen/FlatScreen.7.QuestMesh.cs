using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private Mesh? _questQuadMesh;
    private Mesh? _questBackMesh;

    // B676 has nonblack capture pixels and healthy tracked skinned hands, but
    // the primitive world-screen consumers remain absent. Re-upload only these
    // owned unlit consumers from their readable CPU geometry, with explicit
    // colour and ordinary position/UV streams. This is a controlled native-mesh
    // boundary repair, not proof that the current headset's builtin buffer is
    // malformed. Preserve its exact vertices, UVs, winding and placement; never
    // modify or destroy Unity's shared original primitive.
    private static void RebuildQuestScreenMesh(GameObject screen, ref Mesh? owned, string purpose)
    {
        MeshFilter? filter = screen.GetComponent<MeshFilter>();
        if (filter == null) return;
        Mesh? rebuilt = QuestPrimitiveMesh.Rebuild(filter.sharedMesh, purpose);
        if (rebuilt == null) return;
        owned = rebuilt;
        filter.sharedMesh = rebuilt;
    }

    private static void ReleaseQuestScreenMesh(ref Mesh? owned)
    {
        if (owned != null) Object.Destroy(owned);
        owned = null;
    }
}
