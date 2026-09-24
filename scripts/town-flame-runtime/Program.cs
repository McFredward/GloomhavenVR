using System;
using System.IO;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class FlameTestClock { internal static float Now; }
public static class InteractionProgram
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static float Clock(MeshRenderer renderer, int slot = 0)
    { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, slot); return block.GetFloat("_TownAnimationTime"); }
    public static int Run()
    {
        checks = 0;
        Shader shader = Shader.Find("GloomhavenVR/TownFlame");
        Check(shader != null, "production flame shader is available");
        checks += BillboardRender.Run(shader);
        checks += CoreCoverage.Run(shader);
        var atlas = new Texture2D(8, 1, TextureFormat.RGBA32, false) { name = "flame-test-atlas", filterMode = FilterMode.Point };
        atlas.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.gray }); atlas.Apply();
        var sourceMaterial = new Material(shader);
        sourceMaterial.mainTexture = atlas; sourceMaterial.SetFloat("_Toggle_Flipbook", 1f);
        sourceMaterial.SetFloat("_FlipbookTileX", 8f); sourceMaterial.SetFloat("_TownVisibility", .7f);
        var source = GameObject.CreatePrimitive(PrimitiveType.Quad); source.name = "owner";
        var target = GameObject.CreatePrimitive(PrimitiveType.Quad); target.name = "observer";
        var other = GameObject.CreatePrimitive(PrimitiveType.Quad); other.name = "other observer";
        var renderer = target.GetComponent<MeshRenderer>();
        var originalBlock = new MaterialPropertyBlock(); originalBlock.SetFloat("_UnrelatedFixtureProperty", .314f);
        renderer.SetPropertyBlock(originalBlock, 0);
        source.GetComponent<MeshRenderer>().sharedMaterial = sourceMaterial;
        var assets = new TownServiceAssets(); assets.Register("fixture-flame", shader); assets.Register("fixture-atlas", atlas);
        var owner = new TownServiceBinding(source.transform);
        var remote = new TownServiceBinding(target.transform);
        var peer = new TownServiceBinding(other.transform);
        try
        {
            TownServiceFrame Frame(float clock, float sample, uint session = 1)
            {
                sourceMaterial.SetFloat("_TownAnimationTime", clock); TownServiceMaterial.Reset();
                return TownServiceDelta.Retain(new TownServiceFrame { Session = session, SampleTime = sample, Structure = owner.Structure, Nodes = owner.Read(assets) });
            }
            void Apply(TownServiceBinding binding, float clock, float sample, float now, uint session = 1)
            { FlameTestClock.Now = now; var frame = Frame(clock, sample, session); binding.Validate(frame, assets); binding.Apply(frame, assets); }
            Apply(remote, 10f, 100f, 200f);
            Material shared = renderer.sharedMaterial;
            Check(shared.GetFloat("_TownAnimationTime") == 0f, "pooled flame clock is canonical and immutable");
            Apply(peer, 50f, 100f, 200f);
            Check(other.GetComponent<MeshRenderer>().sharedMaterial == shared, "independent clocks share one immutable material");
            remote.TickAnimation(200.15f); peer.TickAnimation(200.15f);
            Check(Mathf.Abs(Clock(renderer) - 10.15f) < .0001f, "flame advances between owner packets");
            Check(Mathf.Abs(Clock(other.GetComponent<MeshRenderer>()) - 50.15f) < .0001f, "different observers retain independent clock epochs");
            Check(sourceMaterial.GetFloat("_TownAnimationTime") == 50f && shared.GetFloat("_TownAnimationTime") == 0f, "playback never edits source or pooled material");
            var preserved = new MaterialPropertyBlock(); renderer.GetPropertyBlock(preserved, 0);
            Check(Mathf.Abs(preserved.GetFloat("_UnrelatedFixtureProperty") - .314f) < .00001f, "unrelated slot properties survive playback");
            Check(Mathf.Abs(shared.GetFloat("_TownVisibility") - .7f) < .00001f && shared.mainTexture == atlas, "all nonclock appearance survives canonicalization");
            RenderCompare(source, target, other, sourceMaterial, remote, 10.15f, 200.15f);
            for (int n = 0; n < 100; n++) remote.TickAnimation(200.15f);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 10000; n++) remote.TickAnimation(200.15f);
            Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "steady flame clock playback does not allocate managed memory");
            for (int n = 1; n <= 1000; n++)
            {
                float elapsed = n * .05f;
                Apply(remote, 10f + elapsed, 100f + elapsed, 200f + elapsed + (n % 3) * .01f);
                Check(renderer.sharedMaterial == shared, "clock packets do not create new Unity materials");
            }
            Check(Mathf.Abs(Clock(renderer) - 60.01f) < .001f, "coalesced late sample preserves elapsed owner clock");
            Apply(remote, 2f, 200f, 302f, 2);
            Check(Mathf.Abs(Clock(renderer) - 2f) < .0001f, "new session resets the exact first owner clock");
            remote.TickAnimation(302.1f);
            Apply(remote, 999f, 199f, 302.2f, 2);
            Check(Mathf.Abs(Clock(renderer) - 2.1f) < .0001f, "older owner sample cannot replace current clock");
            sourceMaterial.color = Color.red;
            Apply(remote, 2.3f, 200.3f, 302.3f, 2);
            Check(renderer.sharedMaterial != shared && renderer.sharedMaterial.color == Color.red, "nonclock material changes retain separate exact identity");
            Check(other.GetComponent<MeshRenderer>().sharedMaterial == shared && shared.color == Color.white, "one visitor cannot recolor another visitor material");
            // Turning a slot into a different shader must release only its owned clock override.
            sourceMaterial.shader = Shader.Find("Unlit/Texture"); assets.Register("fixture-unlit", sourceMaterial.shader);
            Apply(remote, 0f, 201f, 303f, 2);
            renderer.GetPropertyBlock(preserved, 0);
            Check(!preserved.HasFloat(Shader.PropertyToID("_TownAnimationTime")) && Mathf.Abs(preserved.GetFloat("_UnrelatedFixtureProperty") - .314f) < .00001f,
                "shader replacement restores the original slot property block");
        }
        finally
        {
            owner.Dispose(); remote.Dispose(); peer.Dispose();
            var restored = new MaterialPropertyBlock(); renderer.GetPropertyBlock(restored, 0);
            Check(Mathf.Abs(restored.GetFloat("_UnrelatedFixtureProperty") - .314f) < .00001f, "binding disposal preserves preexisting property block");
            Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(other);
            Object.DestroyImmediate(sourceMaterial); Object.DestroyImmediate(atlas);
        }
        return checks;
    }

    private static void RenderCompare(GameObject source, GameObject target, GameObject other, Material material,
        TownServiceBinding remote, float clock, float now)
    {
        var cameraObject = new GameObject("flame review camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -2f); camera.orthographic = true; camera.orthographicSize = .6f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var rt = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        camera.targetTexture = rt;
        Color32[] Read(GameObject shown)
        {
            source.SetActive(shown == source); target.SetActive(shown == target); other.SetActive(false);
            camera.Render(); RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
            return pixels.GetPixels32();
        }
        try
        {
            material.SetFloat("_TownAnimationTime", 10f); Color32[] before = Read(source);
            material.SetFloat("_TownAnimationTime", clock); Color32[] expected = Read(source);
            target.SetActive(true); remote.TickAnimation(now); Color32[] actual = Read(target);
            int changed = 0, error = 0;
            for (int i = 0; i < actual.Length; i++) { if (!before[i].Equals(expected[i])) changed++; if (!actual[i].Equals(expected[i])) error++; }
            Check(changed > 1000, "production shader changes visible flipbook frame between packets");
            Check(error == 0, "rendered remote intermediate flame frame exactly matches owner");
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-flameEvidence");
            if (at >= 0) File.WriteAllBytes(Path.Combine(args[at + 1], "intermediate-flame.png"), pixels.EncodeToPNG());
        }
        finally
        {
            source.SetActive(true); target.SetActive(true); other.SetActive(true);
            RenderTexture.active = null; camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels);
        }
    }
}
