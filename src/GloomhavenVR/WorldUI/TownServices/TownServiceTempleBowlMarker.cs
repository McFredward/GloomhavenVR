using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-interactive image of the original purse above the real donation bowl.</summary>
internal sealed class TownServiceTempleBowlMarker : IDisposable
{
    private readonly GameObject _root;
    private readonly bool _ownsBlessing;
    private readonly List<Material> _materials = new();
    private ParticleSystem? _blessing;
    private Material? _blessingMaterial;
    private Texture2D? _blessingTexture;
    private GameObject? _bag;
    private float _visibility;
    internal GameObject Root => _root;
    internal Transform? Visual => _bag != null ? _bag.transform : null;

    /// <param name="stationSpace">True when the marker is owned by the permanent priestess
    /// station rather than by its temporary shared-bowl frame.</param>
    internal TownServiceTempleBowlMarker(Transform parent, bool stationSpace = false)
    {
        _ownsBlessing = stationSpace;
        _root = new GameObject("GloomhavenVR.Temple.GhostPurse");
        _root.transform.SetParent(parent, false);
        _root.transform.localPosition = stationSpace
            ? TownServiceRitualLayout.Origin + TownServiceTempleBowl.PurseSeat
            : TownServiceTempleBowl.PurseSeat;
        _root.transform.localRotation = Quaternion.identity;
        VRLayers.Apply(_root);
    }

    internal void Tick(bool shown)
    {
        if (_root == null) return;
        if (_bag == null && TownServiceDecor.MoneyBagTemplate != null)
        {
            _bag = UnityEngine.Object.Instantiate(TownServiceDecor.MoneyBagTemplate.gameObject, _root.transform, false);
            _bag.name = "OriginalPurseSilhouette";
            _bag.SetActive(true);
            foreach (Collider collider in _bag.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Renderer renderer in _bag.GetComponentsInChildren<Renderer>(true))
            {
                Material[] copies = renderer.sharedMaterials;
                for (int i = 0; i < copies.Length; i++)
                {
                    if (copies[i] == null) continue;
                    Material copy = GhostMaterial(copies[i]);
                    _materials.Add(copy); copies[i] = copy;
                }
                renderer.sharedMaterials = copies;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            // The permanent resident owns the shared blessing. The private browsing marker is
            // only a destination guide; giving both markers a ParticleSystem caused two effects
            // merely by approaching the priestess, before any donation callback had run.
            if (_ownsBlessing) BuildBlessing();
        }
        _visibility = Mathf.MoveTowards(_visibility, shown ? 1f : 0f, Time.unscaledDeltaTime / .12f);
        float pulse = .96f + .06f * Mathf.Sin(Time.unscaledTime * 4f);
        _root.transform.localScale = Vector3.one * pulse;
        foreach (Material material in _materials)
            if (material != null && material.HasProperty("_Color"))
            {
                Color tint = material.GetColor("_Color");
                tint.a = _visibility * .30f;
                material.SetColor("_Color", tint);
            }
        if (_bag != null) _bag.SetActive(_visibility > .01f);
    }

    /// <summary>The original donation has committed. This is a bounded cosmetic response;
    /// it never predicts payment and never invokes a service callback.</summary>
    internal void Bless(float elapsed = 0f)
    {
        if (_blessing == null) return;
        // The blessing revision/age is replicated by the existing resident presentation.
        // Restarting the same seeded local-space system and advancing it by that age gives every
        // observer the same bounded beat without publishing individual particles or predicting a
        // transaction. ParticleSystem owns the motes; no legible mesh shards are flown by hand.
        float age = Mathf.Clamp(elapsed, 0f, 1.65f);
        _blessing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _blessing.randomSeed = 0x475652u;
        _blessing.Play(true);
        if (age > 0f) _blessing.Simulate(age, true, false, true);
    }

    private static Material GhostMaterial(Material source)
    {
        // TownNpc is deliberately opaque and its visibility channel is a dissolve. A guide
        // needs real perspective-correct transparency, so retain the original maps while
        // moving this owned copy onto Unity's guaranteed transparent Standard pass.
        Shader shader = Shader.Find("Standard")
            ?? throw new InvalidOperationException("Standard shader unavailable for temple guide");
        var copy = new Material(source) { shader = shader, renderQueue = (int)RenderQueue.Transparent };
        Color blue = new(.16f, .55f, 1f, .30f);
        copy.SetColor("_Color", blue);
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", new Color(.035f, .13f, .28f, 1f));
        if (copy.HasProperty("_Mode")) copy.SetFloat("_Mode", 3f);
        if (copy.HasProperty("_SrcBlend")) copy.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (copy.HasProperty("_DstBlend")) copy.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (copy.HasProperty("_ZWrite")) copy.SetInt("_ZWrite", 0);
        copy.DisableKeyword("_ALPHATEST_ON"); copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        copy.EnableKeyword("_ALPHABLEND_ON"); copy.EnableKeyword("_EMISSION");
        return copy;
    }

    private void BuildBlessing()
    {
        if (_bag == null || _blessing != null) return;
        var effect = new GameObject("TempleBlessingParticles") { layer = VRLayers.ModLayer };
        // AddComponent<ParticleSystem> starts its default play-on-awake system immediately.
        // Build inactive and clear it before activation so constructing the station cannot emit
        // a single proximity-triggered frame. Bless() is the only play edge.
        effect.SetActive(false);
        effect.transform.SetParent(_root.transform, false);
        effect.transform.localPosition = new Vector3(0f, -.018f, 0f);
        _blessing = effect.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = _blessing.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1.65f;
        main.useUnscaledTime = true; main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.maxParticles = 72;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.72f, 1.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.14f, .34f);
        main.startSize = new ParticleSystem.MinMaxCurve(.005f, .014f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(.22f, .62f, 1f, .72f), new Color(1f, .82f, .30f, .82f));
        main.gravityModifier = -.025f;

        ParticleSystem.EmissionModule emission = _blessing.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26, 34) });
        ParticleSystem.ShapeModule shape = _blessing.shape;
        shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = .055f; shape.radiusThickness = .55f;
        ParticleSystem.NoiseModule noise = _blessing.noise;
        noise.enabled = true; noise.strength = .055f; noise.frequency = .48f;
        noise.scrollSpeed = .25f; noise.damping = true;
        ParticleSystem.ColorOverLifetimeModule colour = _blessing.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(.30f, .68f, 1f), 0f),
                new GradientColorKey(new Color(1f, .82f, .36f), .58f),
                new GradientColorKey(new Color(.25f, .55f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.85f, .10f),
                new GradientAlphaKey(.62f, .62f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);
        ParticleSystem.SizeOverLifetimeModule size = _blessing.sizeOverLifetime;
        size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, .2f), new Keyframe(.18f, 1f), new Keyframe(1f, .15f)));

        // The legacy additive pass has deterministic soft-alpha behavior in the game's built-in
        // render pipeline. Particle Standard defaults to an opaque mode on some installations,
        // which exposed each generated texture as the cyan/orange square seen in the headset.
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit")
            ?? throw new InvalidOperationException("Particle shader unavailable for temple blessing");
        _blessingTexture = BuildParticleTexture();
        _blessingMaterial = new Material(shader) { name = "Temple blessing particles" };
        _blessingMaterial.mainTexture = _blessingTexture;
        ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = _blessingMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        _blessing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.SetActive(true);
        _blessing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static Texture2D BuildParticleTexture()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        { name = "Temple blessing soft mote", wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear, anisoLevel = 2 };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
            float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.7f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels); texture.Apply(true, true);
        return texture;
    }

    public void Dispose()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        if (_blessingMaterial != null) UnityEngine.Object.Destroy(_blessingMaterial);
        if (_blessingTexture != null) UnityEngine.Object.Destroy(_blessingTexture);
        _materials.Clear();
    }
}
