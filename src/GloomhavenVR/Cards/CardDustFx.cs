using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Issue 2 dust for VRCards (user: "crumble to dust / emerge from dust instead of shrink/grow — more
/// EPIC"). ONE pooled world-space <see cref="ParticleSystem"/> serves EVERY card (Emit with per-
/// particle position/velocity/colour — no per-card systems, no per-frame allocations; the only
/// allocation is the one-time pool build). A deliberate sibling of <see cref="WorldUI.ButtonDissolveFx"/>
/// — same look (a few dozen small quads, short life ~0.3 s, sideways/settling drift + alpha fade) —
/// spread over the CARD FACE rectangle instead of a cap disc. Purely local visuals (never synced); the
/// logical hide/show is always immediate (the caller drops input first). Lazily rebuilt if a scene
/// unload destroyed the pool object; degrades to no dust in a shader-less environment.
/// </summary>
internal static class CardDustFx
{
    /// <summary>
    /// THE SINGLE GATE (user ruling 2026-08-03: "kam während dessen so eine sehr große
    /// Funken/Partikel Animation über das gesamte Spielfeld (wahrscheinlich ausgehend von der
    /// abgeworfenen Karte). Geh da rein, das soll deaktiviert werden!"). Gated here rather than at
    /// the two call sites so no future emit path can miss it. Default OFF; the code stays so the
    /// effect can be revived once the world-scale sizing is reworked.
    /// </summary>
    private static bool Enabled => CardsConfig.CardDust != null && CardsConfig.CardDust.Value;

    private const int VanishCount = 30; // crumble puff — a bit denser than a button (a card is bigger)
    private const int AppearCount = 18; // fewer, converging — a quick "assembling" shimmer

    private static ParticleSystem? _ps;

    /// <summary>
    /// Crumble burst: motes spawn across the card face rectangle (<paramref name="right"/>/
    /// <paramref name="up"/> half-extents <paramref name="halfW"/>/<paramref name="halfH"/> in world
    /// meters, centred on <paramref name="center"/>) and drift sideways + settle down + puff toward
    /// the viewer along <paramref name="outNormal"/>, fading over their lifetime, in <paramref name="color"/>.
    /// </summary>
    internal static void EmitVanish(Vector3 center, Vector3 right, Vector3 up, Vector3 outNormal,
        float halfW, float halfH, Color color)
    {
        if (!Enabled)
            return;
        if (_ps == null)
            BuildPool();
        if (_ps == null)
            return; // shader-less environment — vanish degrades to the fade alone
        color.a = 1f;
        float span = Mathf.Clamp(Mathf.Max(halfW, halfH) * 2f, 0.01f, 0.6f); // scales drift/size
        // A common sideways sweep so the powder reads as "swept away", plus per-particle jitter.
        Vector3 sweep = Vector3.Cross(outNormal, Vector3.up);
        if (sweep.sqrMagnitude < 1e-4f)
            sweep = right;
        sweep.Normalize();
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < VanishCount; i++)
        {
            float rx = (UnityEngine.Random.value - 0.5f) * 2f;
            float ry = (UnityEngine.Random.value - 0.5f) * 2f;
            ep.position = center + right * (rx * halfW) + up * (ry * halfH)
                          + outNormal * (span * 0.04f * UnityEngine.Random.value);
            ep.velocity = (sweep * (0.6f + 0.5f * UnityEngine.Random.value)
                           + Vector3.down * (0.35f * UnityEngine.Random.value)      // settling
                           + outNormal * (0.2f + 0.25f * UnityEngine.Random.value)) // puff toward the viewer
                          * span;
            ep.startLifetime = 0.26f + 0.18f * UnityEngine.Random.value;
            ep.startSize = span * (0.05f + 0.05f * UnityEngine.Random.value);
            ep.startColor = color;
            _ps.Emit(ep, 1);
        }
    }

    /// <summary>
    /// Materialize shimmer (the crumble in reverse): motes spawn on a ring OUTSIDE the card face and
    /// drift INWARD toward <paramref name="center"/>, fading as they arrive, so the card reads as
    /// coalescing into place under the fade-in. Same pooled system / args as <see cref="EmitVanish"/>.
    /// </summary>
    internal static void EmitAppear(Vector3 center, Vector3 right, Vector3 up, Vector3 outNormal,
        float halfW, float halfH, Color color)
    {
        if (!Enabled)
            return;
        if (_ps == null)
            BuildPool();
        if (_ps == null)
            return; // shader-less environment — appear degrades to the fade alone
        color.a = 1f;
        float span = Mathf.Clamp(Mathf.Max(halfW, halfH) * 2f, 0.01f, 0.6f);
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < AppearCount; i++)
        {
            float rx = (UnityEngine.Random.value - 0.5f) * 2f;
            float ry = (UnityEngine.Random.value - 0.5f) * 2f;
            // Start OUTSIDE the card rectangle (×1.4) so the inward drift is clearly a gathering.
            Vector3 offset = right * (rx * halfW * 1.4f) + up * (ry * halfH * 1.4f);
            ep.position = center + offset + outNormal * (span * 0.12f * UnityEngine.Random.value);
            Vector3 inward = offset.sqrMagnitude > 1e-8f ? -offset.normalized : -right;
            ep.velocity = (inward * (0.9f + 0.4f * UnityEngine.Random.value)
                           + outNormal * 0.12f) * span; // slight lift toward the face so motes settle ONTO it
            ep.startLifetime = 0.14f + 0.10f * UnityEngine.Random.value;
            ep.startSize = span * (0.045f + 0.045f * UnityEngine.Random.value);
            ep.startColor = color;
            _ps.Emit(ep, 1);
        }
    }

    private static void BuildPool()
    {
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return;
        var go = new GameObject("GloomhavenVR.CardDustFx");
        _ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = _ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = true; // keeps the system simulating; idle cost ~zero with no live particles
        main.maxParticles = 512;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = _ps.emission;
        emission.enabled = false; // burst-only via Emit
        ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new UnityEngine.Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        ParticleSystem.SizeOverLifetimeModule size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = new Material(shader);
        renderer.sortingOrder = 2;
        Core.VRLayers.Apply(go);
        _ps.Play(); // armed — particles only exist after Emit
    }
}
