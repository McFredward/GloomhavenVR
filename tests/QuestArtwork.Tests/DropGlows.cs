using System;
using System.Collections;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class DiagnosticProgram
{
    private static IEnumerator QualifyDropGlows()
    {
        RenderTexture priorTarget = RenderTexture.active;
        bool priorColor = GL.sRGBWrite;
        Camera priorCamera = Camera.current;
        var host = Keep(new GameObject("actual-control-board-cues"));
        var wanted = host.AddComponent<ActualWanted>();
        for (int i = 0; i < 2; i++)
        {
            var slot = Keep(new GameObject("native-slot-" + i)).transform;
            slot.SetParent(host.transform, false); slot.localPosition = new Vector3(i * .15f, 0, 0);
            wanted._slots[i] = slot;
        }
        wanted.Build();
        GameObject cue = wanted._wantedHighlights[0]!;
        Mesh native = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<MeshFilter>().sharedMesh;
        foreach (GameObject? item in wanted._wantedHighlights)
        {
            Check(item != null && !item.activeSelf, "wanted cues remain initially hidden");
            var geometry = item!.GetComponent<MeshFilter>().sharedMesh;
            Check(geometry != native && geometry.HasVertexAttribute(VertexAttribute.Color),
                "actual wanted-slot consumer owns explicit white COLOR on Quest");
            Check(item.GetComponent<QuestGlowMeshOwner>() != null,
                "wanted-slot upload is owned by the visual lifetime");
            for (int vertex = 0; vertex < native.vertexCount; vertex++)
                Check(geometry.vertices[vertex] == native.vertices[vertex]
                    && geometry.uv[vertex] == native.uv[vertex] && geometry.colors[vertex] == Color.white,
                    "actual wanted geometry retains native positions, UVs and default white");
        }
        Vector3 offset = CardsConfig.SlotOverlayOffset(CardsConfig.CurrentBoard).Value;
        Check(cue.transform.localScale == new Vector3(.0635f * 1.45f, .088f * 1.45f, 1)
            && cue.transform.localPosition == new Vector3(offset.x + .004f, offset.y, -.004f + offset.z),
            "native wanted size, per-board seat and pair spread remain exact");
        var pulse = cue.GetComponent<MonoBehaviour>();
        foreach (MonoBehaviour component in cue.GetComponents<MonoBehaviour>())
            if (component.GetType().Name == "SlotPulse") pulse = component;
        Check(pulse != null && pulse.GetType().Name == "SlotPulse", "actual wanted pulse is retained");
        MethodInfo tick = pulse!.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tick.Invoke(pulse, null);
        float amount = Mathf.Lerp(.30f, .85f, .5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f));
        Material material = cue.GetComponent<MeshRenderer>().sharedMaterial;
        Check(material.color == new Color(.25f * amount, .85f * amount, .6f * amount, amount),
            "actual original wanted pulse retains its waveform and brightness");
        Check(material.GetInt("_SrcBlend") == (int)BlendMode.One
            && material.GetInt("_DstBlend") == (int)BlendMode.One
            && material.GetInt("_ZWrite") == 0 && material.GetInt("_ZTest") == (int)CompareFunction.LessEqual,
            "drop cues retain additive light over the existing board");

        var gold = Keep(CardGlow.CreateGlowQuad("held-card-snap", host.transform, Vector3.one * .1f,
            new Vector3(0, 0, -.006f), new Color(1, .85f, .3f, .95f)));
        Mesh uploaded = gold.GetComponent<MeshFilter>().sharedMesh;
        Check(uploaded != native && uploaded.HasVertexAttribute(VertexAttribute.Color),
            "actual drop cue owns explicit white COLOR on Quest");
        QuestStandalonePlatform.Enabled = false;
        var desktop = Keep(CardGlow.CreateGlowQuad("desktop-unchanged", host.transform, Vector3.one,
            Vector3.zero, Color.white));
        Check(desktop.GetComponent<MeshFilter>().sharedMesh == native
            && desktop.GetComponent<QuestGlowMeshOwner>() == null, "desktop cue retains native mesh by identity");
        QuestStandalonePlatform.Enabled = true;

        // Actual additive draw, independent of any native mesh's missing COLOR
        // default. A solid occluder must still hide it: no always-on-top repair.
        var target = Keep(new RenderTexture(96, 96, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        target.Create();
        var camera = Keep(new GameObject("private-glow-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.targetTexture = target; camera.orthographic = true;
        camera.orthographicSize = .08f; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.1f, .08f, .06f, 1); camera.transform.position = new Vector3(0, 0, -2);
        camera.cullingMask = 1 << 30;
        gold.layer = 30; gold.SetActive(true);
        bool stereo = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
        Shader.DisableKeyword("STEREO_INSTANCING_ON");
        Color32 Capture()
        {
            camera.Render(); RenderTexture prior = RenderTexture.active;
            var image = Keep(new Texture2D(96, 96, TextureFormat.RGBA32, false, true));
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 96, 96), 0, 0); image.Apply(false); return image.GetPixels32()[48 * 96 + 48]; }
            finally { RenderTexture.active = prior; }
        }
        gold.SetActive(false); Color32 background = Capture(); gold.SetActive(true);
        Color32 lit = Capture();
        UnityEngine.Debug.Log("Owned additive cue pixels: background=" + background + " cue=" + lit
            + " shader=" + gold.GetComponent<Renderer>().sharedMaterial.shader.name);
        Check(lit.r > background.r + 10 && lit.g > background.g + 10 && lit.b > background.b,
            "actual additive cue draws nonzero native GPU pixels");
        var blocker = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); blocker.layer = 30;
        blocker.transform.localScale = Vector3.one;
        blocker.transform.position = new Vector3(0, 0, -.02f);
        var opaque = Keep(new Material(material.shader) { color = Color.black });
        opaque.SetInt("_SrcBlend", (int)BlendMode.One); opaque.SetInt("_DstBlend", (int)BlendMode.Zero);
        opaque.SetInt("_ZWrite", 1); opaque.SetInt("_VertexColor", 0); opaque.renderQueue = 2000;
        blocker.GetComponent<MeshRenderer>().sharedMaterial = opaque;
        Color32 hidden = Capture();
        Check(hidden.r == 0 && hidden.g == 0 && hidden.b == 0, "actual board cue still respects nearer opaque geometry");
        var inactiveBoard = Keep(new GameObject("never-opened-board")); inactiveBoard.SetActive(false);
        GameObject neverShown = CardGlow.CreateGlowQuad("never-shown-drop-cue", inactiveBoard.transform,
            Vector3.one, Vector3.zero, Color.white);
        Mesh neverShownMesh = neverShown.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(neverShown);
        gold.SetActive(true); Object.DestroyImmediate(gold);
        yield return null;
        Check(uploaded == null && native != null, "owned cue mesh is released with its visual");
        Check(neverShownMesh == null, "never-shown inactive board cue also releases its owned mesh");
        host.SetActive(false); blocker.SetActive(false);
        if (stereo) Shader.EnableKeyword("STEREO_INSTANCING_ON");
        Camera.SetupCurrent(priorCamera); RenderTexture.active = priorTarget; GL.sRGBWrite = priorColor;
    }
}
