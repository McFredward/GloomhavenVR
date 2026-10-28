using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DiagnosticProgram
{
    public static int Checks;
    public static string Metrics = "exact native numerical flame mask in Linear world space";
    private static readonly List<Object> Owned = new();
    private static T Keep<T>(T value) where T : Object { Owned.Add(value); return value; }
    private static void Check(bool value, string why) { Checks++; if (!value) throw new Exception(why); }
    public static void Cleanup() { foreach (Object value in Owned) if (value != null) Object.DestroyImmediate(value); Owned.Clear(); }
    public static IEnumerator Run()
    {
        Check(QualitySettings.activeColorSpace == ColorSpace.Linear, "native mask fixture uses actual Quest Linear color space");
        var original = Resources.Load<Material>("NativeFire");
        var burn = Resources.Load<Texture2D>("NativeBurn");
        Check(original != null && original.shader != null && original.shader.isSupported && burn != null,
            "exact owned original fire shader, material and burn mask are loaded");
        Check(GraphicsFormatUtility.IsSRGBFormat(burn!.graphicsFormat), "original native burn mask retains its authored sRGB texture");
        QuestStandalonePlatform.Enabled = false;
        Check(QuestCardFireMask.For(burn) == null, "desktop keeps its exact native mask binding");
        QuestStandalonePlatform.Enabled = true;
        Texture2D alias = QuestCardFireMask.For(burn)!;
        Check(alias != null && alias != burn && !GraphicsFormatUtility.IsSRGBFormat(alias.graphicsFormat)
            && alias.width == burn.width && alias.height == burn.height && alias.mipmapCount == burn.mipmapCount
            && alias.format == burn.format && alias.filterMode == burn.filterMode && alias.wrapMode == burn.wrapMode,
            "numerical alias retains native format, dimensions, mip chain and sampling without sRGB decode");
        Check(QuestCardFireMask.For(burn) == alias && QuestCardFireMask.Matches(alias, burn)
            && !QuestCardFireMask.Matches(burn, alias), "native mask aliases are reused and identified by exact source identity");
        Check(QuestCardFireMask.For(alias) == null, "an already numerical texture keeps its exact binding without a second copy");
        var material = Keep(new Material(original!)); material.renderQueue = 3001;
        material.SetColor("_TintColor", new Color(1,.3f,0,.8f)); material.SetFloat("_FXAnim", .5f);
        material.SetFloat("_FlowSpeed", .5f); material.SetFloat("_OffsetStrength", .2f);
        material.SetFloat("_ThinHighlights", .463f); material.SetFloat("_ThinHighlight_Min", 0);
        material.SetFloat("_ThinHighlight_Max", .195f); material.SetFloat("_Flow_NoiseTiling", 6); material.SetFloat("_Glow", 3);
        var camera = Keep(new GameObject("native-fire-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.6f;
        camera.transform.position = new Vector3(0,0,-5); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black; camera.cullingMask = 1 << 30;
        var target = Keep(new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));
        target.Create(); camera.targetTexture = target;
        var host = Keep(new GameObject("actual-card-host", typeof(RectTransform), typeof(Canvas)));
        host.layer = 30; host.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; host.transform.localScale = Vector3.one * .01f;
        var faceObject = Keep(new GameObject("actual-adopted-card", typeof(RectTransform), typeof(FullAbilityCard), typeof(CardEffects)));
        faceObject.layer = 30; faceObject.transform.SetParent(host.transform, false);
        var face = faceObject.GetComponent<FullAbilityCard>(); var fx = faceObject.GetComponent<CardEffects>(); face.cardEffects = fx;
        var imageObject = Keep(new GameObject("UIFX_Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
        imageObject.transform.SetParent(faceObject.transform, false); imageObject.layer = 30;
        var image = imageObject.GetComponent<Image>(); image.rectTransform.sizeDelta = new Vector2(330,480); image.material = material;
        fx._uiFxOverlay = fx._headerImage = image; fx.overlayFrameBurn = burn;
        Color32[] Capture(Texture texture, float animation = .5f)
        {
            material.SetTexture("_ParticleTexture", texture); material.SetFloat("_FXAnim", animation);
            image.SetMaterialDirty(); Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active;
            var read = Keep(new Texture2D(512,512,TextureFormat.RGBA32,false,true));
            try { RenderTexture.active = target; read.ReadPixels(new Rect(0,0,512,512),0,0); read.Apply(); return read.GetPixels32(); }
            finally { RenderTexture.active = previous; }
        }
        int Count(Color32[] pixels) { int count = 0; foreach (Color32 pixel in pixels) if (pixel.r > 8 || pixel.g > 8 || pixel.b > 8) count++; return count; }
        yield return null;
        int wrong = Count(Capture(burn)); Color32[] numericalPixels = Capture(alias!); int correct = Count(numericalPixels);
        // Independent PNG decode/re-upload: the reference never consumes the
        // production compressed blocks or either private alias.
        var reference = Keep(new Texture2D(2,2,TextureFormat.RGBA32,true,true));
        Check(reference.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath,"Resources/NativeBurn.png"))),
            "independent original PNG mask reference decodes");
        reference.filterMode = burn.filterMode; reference.wrapMode = burn.wrapMode; reference.mipMapBias = burn.mipMapBias;
        reference.Apply(true,true);
        int referencePixels = Count(Capture(reference));
        UnityEngine.Debug.Log("Native fire deterministic Linear pixels sRGB=" + wrong + " numeric=" + correct);
        Check(correct > 10000 && wrong < correct / 20 && Math.Abs(correct-referencePixels) < referencePixels / 30,
            "exact native flame mask recovers original Gamma-space coverage in Linear without a brightness change");
        Check(Count(Capture(alias!, 0)) == 0, "native zero animation remains invisible");
        Check(Count(Capture(alias!, .25f)) > 500 && Count(Capture(alias!, .25f)) < correct,
            "native flame timeline retains its original partial mask progression");
        var gpuSource = Resources.Load<Texture2D>("NativeBurnGpu");
        Check(gpuSource != null && !gpuSource.isReadable && gpuSource.updateCount == 0,
            "GPU-only control is an unchanged imported native texture");
        Texture2D gpuAlias = QuestCardFireMask.For(gpuSource!)!;
        Check(gpuAlias != null && !gpuSource!.isReadable && Capture(gpuAlias!).SequenceEqual(numericalPixels),
            "GPU-only native textures use an exact ordinary block copy without XR readback");
        material.SetTexture("_ParticleTexture", burn); material.SetFloat("_FXAnim", .5f);
        Color tint = material.GetColor("_TintColor");
        var space = new AdoptedCardFxSpace(); space.Maintain(face, (RectTransform)host.transform);
        Check(material.GetTexture("_ParticleTexture") == alias && fx.overlayFrameBurn == burn
            && material.GetColor("_TintColor") == tint && material.GetFloat("_FXAnim") == .5f
            && material.GetFloat("_Glow") == 3 && image.isActiveAndEnabled,
            "actual adopted binding changes only numerical sampling and retains native animation, red tint, asset and lifecycle");
        QuestStandalonePlatform.Enabled = false; space.Restore();
        Check(material.GetTexture("_ParticleTexture") == burn, "release restores native mask even after the Quest capability stops");
        QuestStandalonePlatform.Enabled = true; space.Maintain(face, (RectTransform)host.transform);
        Texture ghost = original!.GetTexture("_ParticleTexture"); material.SetTexture("_ParticleTexture", ghost); space.Restore();
        Check(material.GetTexture("_ParticleTexture") == ghost, "release preserves a later game-authored ghost-mask handover");
        var itemObject = Keep(new GameObject("hosted-original-item",typeof(RectTransform),typeof(ItemCardUI),typeof(ItemCardEffects)));
        itemObject.transform.SetParent(host.transform,false); ((RectTransform)itemObject.transform).sizeDelta=new Vector2(330,480);
        var item = itemObject.GetComponent<ItemCardUI>(); var itemFx = itemObject.GetComponent<ItemCardEffects>(); item.cardEffects=itemFx;
        var itemFlameObject = Keep(new GameObject("not-a-name-based-overlay",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)));
        itemFlameObject.transform.SetParent(itemObject.transform,false); var itemImage=itemFlameObject.GetComponent<Image>();
        var itemMaterial=Keep(new Material(material)); itemMaterial.SetTexture("_ParticleTexture",burn); itemImage.material=itemMaterial;
        itemFx.overlayFrameBurn=burn; itemFx.overlayFrameGhost=ghost; itemFx.SetNativeOverlay(itemImage);
        CardFxBounds.Reseat(item);
        Check(item.GetComponent<QuestHostedItemFireMask>()==null,"uninitialized native item never acquires or rewrites a flame material");
        itemFx.cardBounds=new Vector2(330,480); host.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        CardFxBounds.Reseat(item);
        Check(item.GetComponent<QuestHostedItemFireMask>()==null,"screen-space native item keeps the original rendering path");
        host.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace; CardFxBounds.Reseat(item);
        var hosted=item.GetComponent<QuestHostedItemFireMask>();
        void TickHosted() => hosted!.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(hosted,null);
        TickHosted();
        Check(hosted!=null&&QuestCardFireMask.Matches(itemMaterial.GetTexture("_ParticleTexture"),burn)
            &&itemFx.NativeOverlay==itemImage&&itemFx.overlayFrameBurn==burn,
            "original hosted item uses its exact fgFx burn mask without a graphic-name search");
        var cloneOwner=new GloomhavenVR.Net.RemoteCardArt(); cloneOwner.Configure(burn,ghost,itemFx);
        var cloneMaterial=Keep(new Material(itemMaterial)); cloneOwner.Own(cloneMaterial); image.material=cloneMaterial;
        cloneOwner.NativeFlame(image,cloneMaterial,false);
        Check(QuestCardFireMask.Matches(cloneMaterial.GetTexture("_ParticleTexture"),burn),
            "current native clone owns its numerical burn mask after the native texture write");
        itemObject.SetActive(false);
        Check(itemMaterial.GetTexture("_ParticleTexture")==burn&&QuestCardFireMask.Matches(cloneMaterial.GetTexture("_ParticleTexture"),burn),
            "pooling the original restores its sampler without destroying the simultaneous clone view");
        cloneOwner.NativeFlame(image,cloneMaterial,true);
        Check(cloneMaterial.GetTexture("_ParticleTexture")==ghost,"current native clone ghost handover keeps the original ghost texture");
        cloneOwner.LegacyFlame(cloneMaterial,false);
        Check(QuestCardFireMask.Matches(cloneMaterial.GetTexture("_ParticleTexture"),burn)&&cloneMaterial.GetFloat("_Glow")==3,
            "legacy ability and item clone flames own the same exact numerical burn input");
        cloneOwner.LegacyFlame(cloneMaterial,true);
        Check(cloneMaterial.GetTexture("_ParticleTexture")==ghost&&cloneMaterial.GetFloat("_Glow")==0,
            "legacy clone ghost handover releases its numerical burn lease");
        var nativeItemMaterial=Keep(new Material(itemMaterial)); image.material=nativeItemMaterial;
        cloneOwner.ItemFlame(image,nativeItemMaterial,false);
        Check(QuestCardFireMask.Matches(nativeItemMaterial.GetTexture("_ParticleTexture"),burn)&&cloneOwner.ItemCallbackSawNumeric,
            "native item clone owns its numerical burn mask after the native texture write");
        var itemMaterials=new GloomhavenVR.Net.ActualItemMaterials();itemMaterials._materials.Add(image,nativeItemMaterial);
        cloneOwner.ReleaseItemMaterials(itemMaterials);
        Check(nativeItemMaterial.GetTexture("_ParticleTexture")==burn,"native item material retirement restores and releases its own sampler lease");
        cloneOwner.NativeFlame(image,cloneMaterial,false); cloneOwner.Release();
        Check(cloneMaterial.GetTexture("_ParticleTexture")==burn,"clone release restores its native mask before destroying its owned material");
        itemObject.SetActive(true); itemMaterial.SetTexture("_ParticleTexture",burn); TickHosted();
        itemMaterial.SetTexture("_ParticleTexture",ghost); TickHosted();
        Check(itemMaterial.GetTexture("_ParticleTexture")==ghost,"hosted item spent handover keeps native ghost sampling");
        itemMaterial.SetTexture("_ParticleTexture",burn); TickHosted(); QuestStandalonePlatform.Enabled=false; TickHosted();
        Check(itemMaterial.GetTexture("_ParticleTexture")==burn,"hosted original releases its numerical view when Quest support stops");
        QuestStandalonePlatform.Enabled=true;
        QuestCardFireMask.Release(burn);
        Check(QuestCardFireMask.Matches(alias, burn), "one remaining consumer retains a shared live numerical view");
        QuestCardFireMask.Release(burn); QuestCardFireMask.Release(gpuSource);
        yield return null;
        Check(alias == null && gpuAlias == null && burn != null && gpuSource != null,
            "last mask lease releases private GPU textures while preserving native assets");
        Metrics += "; originalSrgbPixels=" + wrong + "; numericalMaskPixels=" + correct + "; gpuOnlyPixels=" + correct;
        foreach (Object value in Owned) if (value != null) Object.Destroy(value);
    }
}
