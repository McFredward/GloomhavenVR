using System;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class MenuDepthCharacterProgram
{
    private sealed class ShadowScene737 : IDisposable
    {
        internal readonly Scene Scene = new Scene();
        internal readonly Material Material;
        private readonly Material _proxyMaterial;
        internal readonly Light PreviewLight, WorldLight;
        internal readonly Renderer PreviewReceiver, WorldReceiver;
        internal readonly Renderer ShadowOnly;
        internal readonly Camera World;
        internal readonly RenderTexture WorldTarget;
        private readonly ShadowQuality _quality = QualitySettings.shadows;
        private readonly float _distance = QualitySettings.shadowDistance;
        private readonly int _cascades = QualitySettings.shadowCascades;
        private readonly ShadowResolution _resolution = QualitySettings.shadowResolution;
        private readonly AmbientMode _ambientMode = RenderSettings.ambientMode;
        private readonly Color _ambient = RenderSettings.ambientLight;

        internal ShadowScene737()
        {
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 40f; QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = ShadowResolution.High;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .12f;
            Scene.Source.renderingPath = RenderingPath.Forward;
            foreach (Renderer renderer in Scene.Manager.Holder.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Material = new Material(Shader.Find("Standard")); Material.color = Color.white;
            Material.SetFloat("_Glossiness", 0f);
            (PreviewReceiver, PreviewLight) = Pair(Scene.Manager.Holder, 8, Material);
            _proxyMaterial = new Material(Material); _proxyMaterial.color = Color.red;
            ShadowOnly = Cube(Scene.Manager.Holder, 8, _proxyMaterial, "authored invisible shadow proxy",
                new Vector3(0f, .65f, -.25f), new Vector3(.8f, .8f, .1f));
            ShadowOnly.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var worldRoot = new GameObject("unrelated scenario shadow scene");
            worldRoot.transform.position = new Vector3(100f, 100f, 100f);
            (WorldReceiver, WorldLight) = Pair(worldRoot.transform, 9, Material);
            World = new GameObject("unrelated scenario camera").AddComponent<Camera>();
            World.transform.position = worldRoot.transform.position + new Vector3(0f, .65f, -2.56f);
            World.enabled = false; World.stereoTargetEye = StereoTargetEyeMask.None;
            World.clearFlags = CameraClearFlags.SolidColor; World.backgroundColor = Color.clear;
            World.cullingMask = 1 << 9; World.renderingPath = RenderingPath.Forward;
            World.fieldOfView = 40f; World.nearClipPlane = .1f; World.farClipPlane = 10f;
            World.allowHDR = false; World.allowMSAA = false;
            WorldTarget = new RenderTexture(256, 256, 24); WorldTarget.Create(); World.targetTexture = WorldTarget;
        }

        private static (Renderer, Light) Pair(Transform root, int layer, Material material)
        {
            Renderer receiver = Cube(root, layer, material, "shadow receiver",
                new Vector3(0f, .65f, .45f), new Vector3(1.35f, 1.35f, .08f));
            Cube(root, layer, material, "actual native shadow caster",
                new Vector3(-.18f, .83f, 0f), new Vector3(.25f, .28f, .26f));
            Light light = new GameObject("owned directional shadow key").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.type = LightType.Directional; light.transform.rotation = Quaternion.LookRotation(new Vector3(.65f, -.5f, 1f));
            light.color = Color.white; light.intensity = 1.2f; light.renderMode = LightRenderMode.ForcePixel;
            light.shadows = LightShadows.Hard; light.shadowStrength = 1f;
            light.shadowBias = .005f; light.shadowNormalBias = 0f; light.cullingMask = 1 << layer;
            return (receiver, light);
        }

        internal static Renderer Cube(Transform root, int layer, Material material, string name, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = layer;
            go.transform.SetParent(root, false); go.transform.localPosition = position; go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true; return renderer;
        }

        internal Texture2D Native(string path) { Scene.Source.Render(); return Read(Scene.Native, path); }
        internal Texture2D Scenario(string path) { World.Render(); return Read(WorldTarget, path); }
        public void Dispose()
        {
            QualitySettings.shadows = _quality; QualitySettings.shadowDistance = _distance;
            QualitySettings.shadowCascades = _cascades; QualitySettings.shadowResolution = _resolution;
            RenderSettings.ambientMode = _ambientMode; RenderSettings.ambientLight = _ambient;
            WorldTarget.Release(); Object.DestroyImmediate(WorldTarget); Object.DestroyImmediate(Material); Object.DestroyImmediate(_proxyMaterial);
            Scene.Dispose();
        }
    }

    public static int Shadowless737(string output)
    {
        _assertions = 0; ActivateCharacters();
        using (var fixture = new ShadowScene737())
        {
            Scene scene = fixture.Scene;
            Texture2D nativeShadowed = fixture.Native(Path.Combine(output, "native-with-shadows.png"));
            fixture.PreviewLight.shadows = LightShadows.None;
            Texture2D nativeClean = fixture.Native(Path.Combine(output, "native-without-shadows.png"));
            float nativeDelta = Difference(nativeShadowed, nativeClean);
            Check(nativeDelta > .1f, "the actual forward native fixture has observable cast and received shadow pixels");
            fixture.PreviewLight.shadows = LightShadows.Hard;
            Texture2D worldShadowed = fixture.Scenario(Path.Combine(output, "scenario-before.png"));
            fixture.WorldLight.shadows = LightShadows.None;
            Texture2D worldClean = fixture.Scenario(Path.Combine(output, "scenario-without-shadows.png"));
            Check(Difference(worldShadowed, worldClean) > .1f, "the unrelated scenario actually renders observable shadows");
            fixture.WorldLight.shadows = LightShadows.Hard;

            MenuDepthCharacters.Tick();
            Renderer[] originals = scene.Manager.Holder.GetComponentsInChildren<Renderer>(true);
            int callbacks = 0; float worldDifference = 0f;
            bool actorsSuppressed = true, lightsSuppressed = true, originalLighting = true, scenarioUntouched = true;
            Color lightColor = fixture.PreviewLight.color; float intensity = fixture.PreviewLight.intensity;
            scene.Effect.BeforeBlit = () =>
            {
                callbacks++;
                foreach (Renderer renderer in originals)
                    actorsSuppressed &= renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows;
                lightsSuppressed &= fixture.PreviewLight.shadows == LightShadows.None;
                originalLighting &= fixture.PreviewLight.color == lightColor && fixture.PreviewLight.intensity == intensity;
                scenarioUntouched &= QualitySettings.shadows == ShadowQuality.All && fixture.WorldLight.shadows == LightShadows.Hard
                    && fixture.WorldReceiver.shadowCastingMode == ShadowCastingMode.On && fixture.WorldReceiver.receiveShadows;
                RenderTexture active = RenderTexture.active;
                try
                {
                    Texture2D nested = fixture.Scenario(Path.Combine(output, "scenario-nested-" + callbacks + ".png"));
                    worldDifference = Mathf.Max(worldDifference, Difference(worldShadowed, nested)); Object.DestroyImmediate(nested);
                }
                finally { RenderTexture.active = active; }
            };

            Vector3[] eyes = { scene.NeutralEye(scene.Image), scene.NeutralEye(scene.Image) + scene.Root.right * .14f };
            for (int i = 0; i < eyes.Length; i++)
            {
                Texture2D eye = scene.Capture(eyes[i], i != 0, Path.Combine(output, "shadowless-eye-" + i + ".png"));
                Check(Peak(eye).r > .25f, "shadowless preview retains real shaded colour pixels rather than removing its figure");
                int proxyPixels = 0;
                foreach (Color32 pixel in eye.GetPixels32()) if (pixel.r > 80 && pixel.r > pixel.g * 2 && pixel.r > pixel.b * 2) proxyPixels++;
                Check(proxyPixels == 0, "an original invisible shadow-only proxy never becomes visible in the actual eye raster");
                fixture.PreviewLight.shadows = LightShadows.None;
                Texture2D reference = scene.Capture(eyes[i], i != 0, Path.Combine(output, "shadowless-light-reference-" + i + ".png"));
                Check(Difference(eye, reference) < .02f,
                    "actual preview eye pixels are independent of authored shadow maps while normal lighting remains");
                fixture.PreviewLight.shadows = LightShadows.Hard;
                Check(fixture.PreviewReceiver.shadowCastingMode == ShadowCastingMode.On && fixture.PreviewReceiver.receiveShadows,
                    "the original preview renderer's cast and receive permissions return after every eye");
                Check(fixture.ShadowOnly.shadowCastingMode == ShadowCastingMode.ShadowsOnly && !fixture.ShadowOnly.forceRenderingOff,
                    "an original shadow-only renderer returns its exact casting and visibility flags");
                scene.CheckRestored(); Object.DestroyImmediate(eye); Object.DestroyImmediate(reference);
            }
            Check(actorsSuppressed, "actual preview callbacks see every original renderer without dynamic shadows");
            Check(lightsSuppressed, "native custom shaders receive no owned preview shadow map");
            Check(originalLighting, "native lit material colours and light intensities remain original");
            Check(scenarioUntouched, "preview suppression never changes global quality or unrelated scenario shadows");
            Check(callbacks == 4 && worldDifference < .02f,
                "nested scenario camera pixels preserve their real shadows during the entire preview lease");
            scene.Effect.BeforeBlit = null;
            Texture2D nativeAfter = fixture.Native(Path.Combine(output, "native-after.png"));
            Check(Difference(nativeShadowed, nativeAfter) < .02f,
                "subsequent original native camera pixels retain their exact authored shadows");

            // Native pooled membership can change below an existing actor without
            // changing its identity; a new detached companion is also legitimate.
            Renderer born = ShadowScene737.Cube(scene.Manager.Holder, 8, fixture.Material, "new pooled equipment",
                new Vector3(.35f, .8f, .1f), new Vector3(.1f, .1f, .1f));
            var detached = new GameObject("actual detached preview companion");
            detached.transform.position = scene.Manager.Holder.position;
            Renderer companion = ShadowScene737.Cube(detached.transform, 8, fixture.Material, "companion outside holder",
                new Vector3(-.35f, .7f, .05f), new Vector3(.12f, .14f, .12f));
            scene.Manager.ReplaceOriginal(scene.Manager.Holder, detached.transform);
            bool observedBirths = false;
            scene.Effect.BeforeBlit = () => observedBirths = born.shadowCastingMode == ShadowCastingMode.Off && !born.receiveShadows
                && companion.shadowCastingMode == ShadowCastingMode.Off && !companion.receiveShadows;
            Texture2D fresh = scene.Capture(eyes[0], false, Path.Combine(output, "fresh-renderer-membership.png"));
            Check(observedBirths && born.shadowCastingMode == ShadowCastingMode.On && born.receiveShadows
                && companion.shadowCastingMode == ShadowCastingMode.On && companion.receiveShadows,
                "fresh pooled renderers and original detached preview actors are suppressed and restored");
            Object.DestroyImmediate(fresh);
            bool closed = false;
            scene.Effect.BeforeBlit = () => { closed = true; ShutdownCharacters(); };
            try { MenuDepthSources.BeforeCapture(scene.Root, new MenuDepthEye(false, eyes[0], scene.UiCamera)); }
            catch (InvalidOperationException) { }
            finally { MenuDepthSources.AfterCapture(); }
            Check(closed && fixture.PreviewLight.shadows == LightShadows.Hard && fixture.PreviewReceiver.receiveShadows
                && fixture.PreviewReceiver.shadowCastingMode == ShadowCastingMode.On,
                "native shutdown inside the real render restores borrowed preview shadows before retirement");
            File.WriteAllText(Path.Combine(output, "shadow-pixel-measurements.json"),
                "{\"native_shadow_delta\":" + nativeDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"nested_scenario_delta\":" + worldDifference.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}\n");
            Object.DestroyImmediate(nativeShadowed); Object.DestroyImmediate(nativeClean);
            Object.DestroyImmediate(worldShadowed); Object.DestroyImmediate(worldClean); Object.DestroyImmediate(nativeAfter);
        }
        return _assertions;
    }

    public static int ShadowThrow737(string output)
    {
        _assertions = 0; ActivateCharacters();
        using (var fixture = new ShadowScene737())
        {
            Scene scene = fixture.Scene; MenuDepthCharacters.Tick(); bool threw = false;
            try { MenuDepthSources.BeforeCapture(scene.Root, new MenuDepthEye(false, scene.NeutralEye(scene.Image), scene.UiCamera)); }
            catch (InvalidOperationException error) { threw = error.Message.Contains("injected shadow render failure"); }
            finally { MenuDepthSources.AfterCapture(); }
            Check(threw, "injected failure interrupts the actual borrowed native preview render");
            Check(fixture.PreviewLight.shadows == LightShadows.Hard && fixture.PreviewReceiver.receiveShadows
                && fixture.PreviewReceiver.shadowCastingMode == ShadowCastingMode.On,
                "throwing native preview renders restore every borrowed shadow permission");
            Check(fixture.WorldLight.shadows == LightShadows.Hard && fixture.WorldReceiver.receiveShadows
                && QualitySettings.shadows == ShadowQuality.All,
                "throwing native previews preserve unrelated scenario and global shadow state");
            scene.CheckRestored();
        }
        return _assertions;
    }
}
