using System;
using System.Collections;
using GloomhavenVR.Cards;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class DiagnosticProgram
{
    private static IEnumerator QualifyNativeBoardGuides()
    {
        foreach (string style in new[] { "Steel", "Oak", "Bronze" })
        {
            IEnumerator probe = ProbeNativeBoardGuide(style);
            while (probe.MoveNext()) yield return null;
        }
    }
    private static IEnumerator ProbeNativeBoardGuide(string style)
    {
        GameObject prefab = Resources.Load<GameObject>("NativeBoard" + style);
        CardsConfig.CurrentBoard = (ControlBoard)Enum.Parse(typeof(ControlBoard), style);
        Check(prefab != null, "actual authored board is present: " + style);
        var model = Keep(Object.Instantiate(prefab!));
        Transform Find(string name) => Array.Find(model.GetComponentsInChildren<Transform>(true), t => t.name == name)!;
        Transform first = Find("Slot1"), second = Find("Slot2"), shortRest = Find("ShortRestToken"), longRest = Find("LongRestToken");
        Check(first != null && second != null && shortRest != null && longRest != null, "actual board retains its four native orientation anchors");
        Vector3 u = (second!.position - first!.position).normalized;
        Vector3 s = (shortRest!.position - longRest!.position).normalized;
        Vector3 normal = Vector3.Cross(u, s).normalized;
        Vector3 vertical = Vector3.Cross(normal, u).normalized;
        Quaternion face = Quaternion.LookRotation(normal, vertical);
        first.rotation = second.rotation = face;
        first.localScale *= NativeBoardContract.SlotScale; second.localScale *= NativeBoardContract.SlotScale;
        Material opaque = Resources.Load<Material>("NativeBoardMaterial" + style);
        Check(opaque != null && opaque.shader != null && opaque.shader.name == "GloomhavenVR/BoardLit"
            && opaque.mainTexture != null, "actual authored BoardLit/material/texture path is present: " + style);
        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = opaque!;
            renderer.sharedMaterials = materials;
        }
        var guides = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            guides[i] = Keep(CardGlow.CreateGlowQuad("actual-native-wanted-" + i, i == 0 ? first : second,
                new Vector3(.0635f * NativeBoardContract.Wanted(style), .088f * NativeBoardContract.Wanted(style), 1), new Vector3(0, 0, -.004f), new Color(.25f, .85f, .6f, .7f)));
            QuestCardGuideWitness.Attach(guides[i], model.transform);
        }
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
        var camera = Keep(new GameObject("authored-board-depth-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .35f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 30;
        camera.transform.position = (first.position + second.position) * .5f - normal;
        camera.transform.rotation = face; camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        var target = Keep(new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        target.Create(); camera.targetTexture = target;
        GloomhavenVR.WorldUI.CanvasConversion.WorldCamera = camera;
        Color32[] Capture()
        {
            camera.Render(); var prior = RenderTexture.active;
            var image = Keep(new Texture2D(256, 256, TextureFormat.RGBA32, false, true));
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply(false); return image.GetPixels32(); }
            finally { RenderTexture.active = prior; }
        }
        yield return null;
        Color32[] backdrop = Capture();
        var counts = new int[2];
        for (int i = 0; i < 2; i++)
        {
            guides[i].SetActive(true); Color32[] visible = Capture();
            for (int p = 0; p < visible.Length; p++) if (visible[p].g > backdrop[p].g + 10) counts[i]++;
            guides[i].SetActive(false);
            Check(guides[i].GetComponent<QuestCardGuideWitness>().Submitted
                && guides[i].GetComponent<QuestCardGuideWitness>().Facts.Contains("board=" + style),
                "real board guide retains actual source style and head submission facts");
            UnityEngine.Debug.Log("Actual authored guide depth board=" + style + " slot=" + i + " visiblePixels=" + counts[i]
                + " position=" + guides[i].transform.position + " slotScale=" + guides[i].transform.lossyScale
                + " submitted=" + guides[i].GetComponent<QuestCardGuideWitness>().Facts);
        }
        Check(counts[0] > 1000 && counts[1] > 1000, "both authored board guides retain real additive pixels: " + style);
        Metrics += style + "GuidePixels=" + counts[0] + "/" + counts[1] + ";";
        model.SetActive(false);
        CardsConfig.CurrentBoard = ControlBoard.Oak;
    }
}
