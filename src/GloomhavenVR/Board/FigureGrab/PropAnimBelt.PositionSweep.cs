using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// ROUND THIRTEEN — THE PROP'S <b>INPUTS</b>, READ AS A FUNCTION OF <b>WORLD POSITION</b>.
///
/// <para><b>THE OBSERVATION THAT FORCED THIS ARM.</b> The user, 2026-09-06, verbatim:
/// <i>"Wenn das weiße Highlighting in der Hand auftritt und ich es physisch nach oben fliegen
/// lasse in der Hand, 'entkomme' ich dem Weißen und es geht wieder weg. Also ist es vielleicht
/// doch ein Licht? Auf jeden Fall scheint die Position im Raum einen Einfluss zu haben."</i>
/// He does not change a state, wait out a timer or re-grab: he <b>translates the prop upward</b>
/// and the white goes. That makes the effect a function of WORLD POSITION, and after twelve rounds
/// of state probes it is the first term nobody has tested.</para>
///
/// <para><b>WHY EVERY PREVIOUS ARM COULD READ ZERO AND BE HONEST.</b>
/// <c>BOARD PROP STANDING WATCH</c> read <c>1796 frames, CHANGES: 0</c> and concluded that a board
/// prop's own STATE never flashes. That conclusion stands — <b>for state</b>. It says nothing about
/// the prop's <b>INPUTS</b>. A realtime light, a projector, a screen-space occlusion map or a
/// volume the prop merely SAMPLES is not the prop's state, is not under the prop's transform, and
/// would leave every animator / material / property-block / object-graph probe in this file reading
/// exactly zero while the picture changes. That is the documented shape of a defect that survives
/// twelve clean measurements: the blind spot is the lead.</para>
///
/// <para><b>WHAT THIS ARM MEASURES, AND WHY IT TAKES MORE THAN ONE POSITION PER FRAME.</b> The
/// user's own experiment is a TRANSLATION, and a single-position reading cannot see a gradient. So
/// on one and the same frame this arm evaluates the prop's inputs at a LADDER of positions —
/// <c>+0 m, +0.5 m, +1 m, +2 m</c> straight up from the held prop's own bounds centre — and reports
/// the DIFFERENCE between the rungs. Four channels, because those are the four ways a picture can
/// depend on where an object is without anything about the object changing:</para>
/// <list type="number">
/// <item><b>Realtime lights.</b> Every enabled <see cref="Light"/> in the scene whose culling mask
/// admits the prop's layer and whose reach covers the rung, with its type, range, intensity, colour
/// luminance, render mode, distance and attenuated contribution. A point or spot light with a range
/// is position-dependent BY CONSTRUCTION, and "lift it out of the lamp" is exactly what the user
/// describes. NOTE what the previous lighting arm did and did not do: it read
/// <c>LightProbes.GetInterpolatedProbe</c>, which is a BAKED AMBIENT term, at ONE position. It is
/// blind to every realtime light in the scene. See <see cref="AppendProbePopulation"/>.</item>
/// <item><b>The per-pixel light cap.</b> <see cref="QualitySettings.pixelLightCount"/> is small on
/// this rig by the user's own tuning, and Unity picks the N most important lights PER RENDERER from
/// its position. A prop that MOVES can therefore promote a light from vertex to pixel treatment and
/// back with no state change anywhere — a flash with no animator behind it. This arm computes the
/// top-N set at each rung and reports whether the SET flips between them.</item>
/// <item><b>The screen-space occlusion map.</b> A prop registered with
/// <c>TilesOcclusionGenerator</c> is DRAWN INTO the same <c>_ObjectOcclusion</c> map its shader
/// SAMPLES, at a screen-space UV. That lookup is position-dependent by construction. This arm does
/// not read the texture back (a GPU read-back is a subsystem, and this file has been burned by
/// instruments that photograph a buffer we ourselves empty). It reconstructs the coverage
/// ANALYTICALLY: which registered renderers' screen-space extents contain the rung's screen point,
/// and whether the HELD PROP IS ONE OF THEM. That NAMES the coverer instead of returning a number.
/// </item>
/// <item><b>Projectors.</b> <c>ObjectPosToMaterial</c> writes <c>_ObjPos</c> through
/// <c>GetComponent&lt;Projector&gt;()</c>, and a previous round found the trap carries ZERO
/// Projectors — but that measured THE PROP. A projector elsewhere in the scene that projects ONTO
/// the prop is a different object entirely and could never appear in that count. This arm tests
/// containment in every scene projector's frustum, at every rung.</item>
/// </list>
///
/// <para><b>THE READING, IN NUMBERS.</b> Grep <c>] [Props] HELD-PROP POSITION SWEEP</c>.</para>
/// <list type="bullet">
/// <item><b>WORKING</b> — the line appears once per hold with <c>SAMPLES</c> ≥ 1 and
/// <c>SCENE LIGHTS</c> ≥ 1. Below that it measured nothing and excludes nothing.</item>
/// <item><b>THE DIAGNOSTIC IS THE DIRECTION, NOT THE COUNT — and the ModBuild 464 log corrected
/// this bullet.</b> The first version said <c>LIGHTS THAT THE LIFT ESCAPES</c> above 0 named the
/// flash. 464 read <b>escapes 23 and enters 33</b>: in a 21-light room a 2 m translation changes
/// set membership in both directions, so a non-zero escape count means only that lights have
/// ranges. What discriminates is <c>LUMINANCE RATIO</c>. The user's white GOES AWAY on the lift, so
/// a light-arriving-at-the-prop model needs the luminance to FALL with height — ratio above
/// <b>2.0</b>. 464 measured <b>0.629</b> on a monotonically RISING ladder (2.3579 / 2.6301 /
/// 3.1286 / 3.3029): he lifts the prop into 1.6x MORE light and the white goes.
/// <b>THE LIGHT CHANNEL IS CLOSED</b>, and closed by a non-zero reading pointing the wrong way,
/// which is stronger than a zero.</item>
/// <item><b>WHAT A ZERO MEANS PER CHANNEL, because three of the four can return one for different
/// reasons.</b> The pixel-set channel cannot fire at all at a per-pixel cap of 0 (464: cap 0) — a
/// NON-reading. The projector channel contained the prop at no rung, with 1 projector in the
/// scene. The occlusion channel is decided by the PARTICIPATION clause, not by the coverage count:
/// <c>ObjectOcclusionVolume.OnEnable</c> passes <c>GetComponent&lt;MeshRenderer&gt;()</c> and
/// <c>AddObjectRenderer</c> early-returns on null, so a volume on a SKINNED prop registers nothing
/// while still counting as an enabled volume on the hush line. At 0 registered renderers the map
/// cannot reach this prop and strand 5 is retired by construction. With all four closed the only
/// position term left is the shader's own VIEW-dependence — lifting also ROTATES the prop against
/// the eye, and §13.2 recorded the ring reading white FACE-ON and bronze EDGE-ON; the <c>POSE</c>
/// columns on the HOME TWIN line are the shipped control.</item>
/// <item><b>STILL BEYOND THE INSTRUMENT</b> — <c>SCENE LIGHTS 0</c> (nothing to rank),
/// <c>GENERATOR: none</c> (the occlusion channel did not run), or <c>SAMPLES 0</c>. Each of those
/// is printed as its own clause so an untaken channel can never read as a taken one that returned
/// zero, which is the confusion this whole file exists to stop making.</item>
/// </list>
///
/// <para><b>IT WRITES NOTHING.</b> Every call in this part is a read. It does not move the prop —
/// the rungs are arithmetic on a position, not a transform write — and it does not re-suppress
/// anything. The prop's own attention animation is already hushed for the length of the hold
/// (ModBuild 454 onwards, on the user's explicit instruction) and that is NOT the flash; nothing
/// here adds a second hush.</para>
/// </summary>
internal static partial class PropAnimBelt
{
    /// <summary>The ladder, in metres straight up from the held prop's bounds centre. Rung 0 is the
    /// prop where it actually is; the last rung is the user's own escape ("nach oben fliegen
    /// lassen"). Four rungs and not two, because two positions give a difference and four give a
    /// GRADIENT — if a light drops out between +0.5 m and +1 m, that names its range.</summary>
    private static readonly float[] SweepRungs = { 0f, 0.5f, 1f, 2f };

    /// <summary>Frames between samples. At the 38.5 fps this rig measured, ten frames is ~0.26 s,
    /// so the 360-frame verdict window holds ~36 samples — enough that a flash the user reacts to
    /// by lifting cannot fall entirely between two of them.</summary>
    private const int SweepEvery = 10;

    /// <summary>Scene lights and projectors are re-enumerated on this cadence and NOT per sample.
    /// <c>FindObjectsOfType</c> is this project's default performance suspect and one line of it
    /// once owned 12.6 ms of an 11.11 ms frame; at 120 frames it runs ~3 times in a whole window.
    /// The refresh COUNT is printed so a stale population cannot be mistaken for a small one.</summary>
    private const int SweepRescanFrames = 120;

    /// <summary>Population caps. Every one of them is printed as found-vs-walked, because a
    /// truncated list is not absence and this project has already read an ellipsis as proof that
    /// something never appeared.</summary>
    private const int SweepLightCap = 256;
    private const int SweepProjCap = 32;
    private const int SweepOccCap = 256;

    /// <summary>How many lights / renderers / projectors the line NAMES before it stops naming
    /// them. Totals are printed either way.</summary>
    private const int SweepNameCap = 6;

    /// <summary>The largest per-pixel light set this arm reconstructs. Unity's cap is a quality
    /// setting and is typically 0..4 here; the value actually in force is printed beside it.</summary>
    private const int SweepTopCap = 8;

    private static bool _swArmed;
    private static int _swSamples;
    private static int _swNextFrame;
    private static int _swRescanFrame;
    private static int _swRescans;

    private static Light[] _swLights = System.Array.Empty<Light>();
    private static Projector[] _swProjectors = System.Array.Empty<Projector>();
    private static int _swLightsFound, _swProjFound;

    /// <summary>Per-rung aggregates over the window. Index is the rung.</summary>
    private static readonly int[] SwReachMax = new int[4];
    private static readonly float[] SwLumLast = new float[4];
    private static readonly float[] SwLumLo = new float[4];
    private static readonly float[] SwLumHi = new float[4];
    private static readonly int[] SwOccMax = new int[4];
    private static readonly int[] SwProjMax = new int[4];
    private static readonly int[] SwSelfCover = new int[4];

    /// <summary>Lights that reach rung 0 but NOT the top rung on some sample — i.e. the lights the
    /// user's lift ESCAPES. This is the whole point of the arm.</summary>
    private static int _swEscapeCount;
    private static readonly List<string> SwEscapes = new(SweepNameCap);

    /// <summary>Lights that reach the TOP rung but not rung 0 — the lift walking INTO something.
    /// Recorded separately because "worse when lifted" is a different finding from "better".</summary>
    private static int _swEnterCount;
    private static readonly List<string> SwEnters = new(SweepNameCap);

    /// <summary>Samples on which the reconstructed top-N per-pixel light SET differed between rung
    /// 0 and the top rung, and one example naming both sets.</summary>
    private static int _swTopFlips;
    private static string _swTopExample = string.Empty;

    /// <summary>Samples on which the analytic occlusion coverage differed between rung 0 and the
    /// top rung, and the coverers named at rung 0.</summary>
    private static int _swOccDiffs;

    /// <summary>Rung evaluations that fell BEHIND the generator camera and could not be projected
    /// at all. Counted separately and printed, because a rung the map's camera cannot see returns
    /// coverage 0 for exactly the same reason an uncovered rung does, and this file has paid
    /// repeatedly for letting an untaken reading wear a taken one's clothes.</summary>
    private static int _swOccOffScreen;
    private static readonly List<string> SwOccNames = new(SweepNameCap);

    private static readonly List<string> SwProjNames = new(SweepNameCap);

    private static string _swGenCamera = string.Empty;
    private static string _swHeadCamera = string.Empty;
    private static bool _swGenIsHead;
    private static int _swOccRegistered, _swOccRoom;
    private static bool _swGenSeen;

    /// <summary>ROUND FOURTEEN, CHANNEL 5 — DOES THE PROP PARTICIPATE IN THE OCCLUSION MAP AT
    /// ALL? Measured exactly, because the ModBuild 464 sweep read <c>self-covering on 0 of 36
    /// sample(s)</c> at rung 0 — where the sample point IS the prop's own bounds centre, so a
    /// registered prop would necessarily have covered itself — and that is strong but INDIRECT.
    /// <c>ObjectOcclusionVolume.OnEnable</c> is
    /// <c>AddObjectRenderer(GetComponent&lt;MeshRenderer&gt;())</c> and <c>AddObjectRenderer</c>
    /// early-returns on null: a volume sitting on an object whose renderer is a
    /// <b>SkinnedMeshRenderer</b> registers NOTHING while still counting as an enabled volume.
    /// The hush line's "1 ObjectOcclusionVolume(s), 1 ENABLED" cannot tell those apart. This
    /// does.</summary>
    private static string _swOccVolumes = string.Empty;
    private static int _swOccPropRenderers = -1;

    /// <summary>The globals the occlusion term is gated on, read back rather than assumed.</summary>
    private static string _swOccGlobals = string.Empty;

    /// <summary>ROUND FOURTEEN, CHANNEL 6 — THE TWO SCREEN SPACES, PER RUNG. The map is authored in
    /// the GENERATOR camera's screen space and sampled in the HEAD camera's, so the gap between the
    /// two UVs is the camera mismatch expressed as a number instead of a sentence.</summary>
    private static readonly float[] SwHeadUvYLo = new float[4];
    private static readonly float[] SwHeadUvYHi = new float[4];
    private static readonly float[] SwHeadUvXLo = new float[4];
    private static readonly float[] SwHeadUvXHi = new float[4];
    private static readonly float[] SwUvGapHi = new float[4];
    private static readonly int[] SwHeadOffScreen = new int[4];

    private static int _swLayer = -1;
    private static string _swLayerName = string.Empty;
    private static int _swPixelCap;

    /// <summary>Scratch for the top-N reconstruction: contribution and index, per rung, cleared per
    /// use so nothing allocates inside the window.</summary>
    private static readonly float[] SwTopScore = new float[SweepTopCap];
    private static readonly int[] SwTopIndexA = new int[SweepTopCap];
    private static readonly int[] SwTopIndexB = new int[SweepTopCap];

    private static readonly List<Light> SwLightScratch = new(64);
    private static readonly List<Projector> SwProjScratch = new(8);

    // ---------------------------------------------------------------------------------------------

    /// <summary>Arm the sweep alongside the verdict window. Nothing here is gated on the flash
    /// being visible, because nothing in this file can tell when it is.</summary>
    private static void ArmPositionSweep()
    {
        _swArmed = true;
        _swSamples = 0;
        _swNextFrame = Time.frameCount;
        _swRescanFrame = int.MinValue;
        _swRescans = 0;
        _swEscapeCount = _swEnterCount = _swTopFlips = _swOccDiffs = 0;
        _swOccOffScreen = 0;
        _swTopExample = string.Empty;
        _swGenSeen = false;
        _swGenCamera = _swHeadCamera = string.Empty;
        _swGenIsHead = false;
        _swOccRegistered = _swOccRoom = 0;
        _swLayer = -1;
        _swLayerName = string.Empty;
        _swPixelCap = 0;
        SwEscapes.Clear();
        SwEnters.Clear();
        SwOccNames.Clear();
        SwProjNames.Clear();
        for (int r = 0; r < SweepRungs.Length; r++)
        {
            SwReachMax[r] = SwOccMax[r] = SwProjMax[r] = SwSelfCover[r] = 0;
            SwLumLast[r] = 0f;
            SwLumLo[r] = float.MaxValue;
            SwLumHi[r] = float.MinValue;
            SwHeadUvXLo[r] = SwHeadUvYLo[r] = float.MaxValue;
            SwHeadUvXHi[r] = SwHeadUvYHi[r] = float.MinValue;
            SwUvGapHi[r] = 0f;
            SwHeadOffScreen[r] = 0;
        }
        _swOccVolumes = string.Empty;
        _swOccGlobals = string.Empty;
        _swOccPropRenderers = -1;
    }

    /// <summary>One sample: the prop's inputs at every rung, on the SAME frame. Called from
    /// <c>SampleVerdict</c> after the twin, so the held lead renderer is already resolved for this
    /// tick and nothing here has to re-walk the prop.</summary>
    private static void SamplePositionSweep()
    {
        if (!_swArmed || _heldLead == null)
            return;
        if (Time.frameCount < _swNextFrame)
            return;
        _swNextFrame = Time.frameCount + SweepEvery;

        RefreshSweepScene();

        Renderer lead = _heldLead;
        Vector3 basePos = lead.bounds.center;
        _swLayer = lead.gameObject.layer;
        _swLayerName = LayerMask.LayerToName(_swLayer);
        _swPixelCap = QualitySettings.pixelLightCount;
        _swSamples++;

        int top = SweepRungs.Length - 1;
        GameObject? visual = _vBelt != null ? _vBelt.Visual : null;
        SampleSweepLights(basePos, lead);
        SampleSweepOcclusion(basePos, lead, visual, top);
        SampleSweepProjectors(basePos, top);
    }

    /// <summary>Re-enumerate the scene's lights and projectors on a cadence. Both
    /// <c>FindObjectsOfType</c> calls return ACTIVE-AND-ENABLED objects only, by contract — a light
    /// on a deactivated object paints nothing, so it is correctly absent, and saying so here is what
    /// stops a later reader treating the count as a scene census.</summary>
    private static void RefreshSweepScene()
    {
        if (Time.frameCount < _swRescanFrame)
            return;
        _swRescanFrame = Time.frameCount + SweepRescanFrames;
        _swRescans++;

        try { _swLights = Object.FindObjectsOfType<Light>(); }
        catch { _swLights = System.Array.Empty<Light>(); }
        try { _swProjectors = Object.FindObjectsOfType<Projector>(); }
        catch { _swProjectors = System.Array.Empty<Projector>(); }
        _swLightsFound = _swLights.Length;
        _swProjFound = _swProjectors.Length;
    }

    /// <summary>Every reaching light at every rung, plus the two readings that decide the round:
    /// which lights the LIFT ESCAPES, and whether the top-N per-pixel set flips.</summary>
    private static void SampleSweepLights(Vector3 basePos, Renderer lead)
    {
        int top = SweepRungs.Length - 1;
        int walk = _swLights.Length < SweepLightCap ? _swLights.Length : SweepLightCap;

        for (int r = 0; r < SweepRungs.Length; r++)
        {
            Vector3 p = basePos + (Vector3.up * SweepRungs[r]);
            int reach = 0;
            float lum = 0f;
            for (int i = 0; i < walk; i++)
            {
                float c = LightContribution(_swLights[i], p, _swLayer);
                if (c <= 0f)
                    continue;
                reach++;
                lum += c;
            }
            if (reach > SwReachMax[r])
                SwReachMax[r] = reach;
            SwLumLast[r] = lum;
            if (lum < SwLumLo[r]) SwLumLo[r] = lum;
            if (lum > SwLumHi[r]) SwLumHi[r] = lum;
        }

        // THE ESCAPE SET. A light that reaches the prop where it is and does NOT reach it two
        // metres higher is precisely what "ich entkomme dem Weissen" describes, and naming it with
        // its range and both distances is the difference between a cause and a coincidence.
        Vector3 p0 = basePos;
        Vector3 pT = basePos + (Vector3.up * SweepRungs[top]);
        for (int i = 0; i < walk; i++)
        {
            Light l = _swLights[i];
            float c0 = LightContribution(l, p0, _swLayer);
            float cT = LightContribution(l, pT, _swLayer);
            if (c0 > 0f && cT <= 0f)
            {
                _swEscapeCount++;
                if (SwEscapes.Count < SweepNameCap)
                    SwEscapes.Add(DescribeSweepLight(l, p0, pT, c0, cT));
            }
            else if (cT > 0f && c0 <= 0f)
            {
                _swEnterCount++;
                if (SwEnters.Count < SweepNameCap)
                    SwEnters.Add(DescribeSweepLight(l, p0, pT, c0, cT));
            }
        }

        // THE PER-PIXEL SET. Unity ranks a renderer's lights from ITS OWN position, so a moving
        // renderer can re-rank them with nothing else in the scene changing. The cap in force is
        // printed beside the sets, because at a cap of 0 this channel cannot flip at all and the
        // reading must not look like an exclusion.
        int cap = _swPixelCap < SweepTopCap ? _swPixelCap : SweepTopCap;
        if (cap <= 0)
            return;
        int nA = SelectTopLights(p0, cap, SwTopIndexA, walk);
        int nB = SelectTopLights(pT, cap, SwTopIndexB, walk);
        bool same = nA == nB;
        if (same)
        {
            for (int i = 0; i < nA && same; i++)
            {
                bool found = false;
                for (int j = 0; j < nB; j++)
                {
                    if (SwTopIndexA[i] == SwTopIndexB[j])
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    same = false;
            }
        }
        if (same)
            return;
        _swTopFlips++;
        if (_swTopExample.Length == 0)
            _swTopExample = "at rung 0 {" + JoinLightNames(SwTopIndexA, nA) + "} but at +"
                            + SweepRungs[top].ToString("0.#") + " m {"
                            + JoinLightNames(SwTopIndexB, nB) + "}";
    }

    /// <summary>Fill <paramref name="into"/> with the indices of the <paramref name="cap"/>
    /// highest-contributing lights at <paramref name="p"/>, best first. A selection and not a sort:
    /// the cap is small and this runs inside a sampled window.</summary>
    private static int SelectTopLights(Vector3 p, int cap, int[] into, int walk)
    {
        int n = 0;
        for (int i = 0; i < walk; i++)
        {
            float c = LightContribution(_swLights[i], p, _swLayer);
            if (c <= 0f)
                continue;
            int at = n;
            while (at > 0 && SwTopScore[at - 1] < c)
            {
                if (at < cap)
                {
                    SwTopScore[at] = SwTopScore[at - 1];
                    into[at] = into[at - 1];
                }
                at--;
            }
            if (at >= cap)
                continue;
            SwTopScore[at] = c;
            into[at] = i;
            if (n < cap)
                n++;
        }
        return n;
    }

    private static string JoinLightNames(int[] idx, int n)
    {
        if (n <= 0)
            return "<none>";
        var sb = new StringBuilder(64);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
                sb.Append(", ");
            Light l = _swLights[idx[i]];
            sb.Append(l != null ? l.name : "<destroyed>");
        }
        return sb.ToString();
    }

    /// <summary>The light's attenuated Rec.709 contribution at a world point, or 0 when it cannot
    /// reach it at all. The attenuation is Unity's legacy point falloff
    /// <c>1 / (1 + 25 (d/r)^2)</c> — the reading that matters is the COMPARISON between two
    /// positions, so a monotone estimate is sufficient and the formula is stated here so the number
    /// on the line is interpretable rather than magic.</summary>
    private static float LightContribution(Light? l, Vector3 p, int layer)
    {
        if (l == null || !l.isActiveAndEnabled)
            return 0f;
        if (layer >= 0 && (l.cullingMask & (1 << layer)) == 0)
            return 0f;
        if (l.intensity <= 0f)
            return 0f;

        Color c = l.color;
        float lum = (0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b);
        if (lum <= 0f)
            return 0f;

        switch (l.type)
        {
            case LightType.Directional:
                return l.intensity * lum;
            case LightType.Point:
            case LightType.Spot:
            {
                float r = l.range;
                if (r <= 0f)
                    return 0f;
                Vector3 d = p - l.transform.position;
                float dist = d.magnitude;
                if (dist > r)
                    return 0f;
                float t = dist / r;
                float atten = 1f / (1f + (25f * t * t));
                if (l.type == LightType.Spot)
                {
                    float half = l.spotAngle * 0.5f;
                    if (half <= 0f)
                        return 0f;
                    float ang = dist > 1e-5f
                        ? Vector3.Angle(l.transform.forward, d / dist)
                        : 0f;
                    if (ang > half)
                        return 0f;
                    // A soft edge over the outer quarter of the cone, so a prop sitting exactly on
                    // the rim does not read as a hard on/off between two samples.
                    float u = ang / half;
                    float cone = u <= 0.75f ? 1f : Mathf.Clamp01((1f - u) / 0.25f);
                    atten *= cone;
                }
                return l.intensity * lum * atten;
            }
            default:
                // Area and disc lights are BAKE-ONLY in the built-in pipeline and contribute
                // nothing at runtime. Returning 0 is correct; they are counted in the population.
                return 0f;
        }
    }

    private static string DescribeSweepLight(Light l, Vector3 p0, Vector3 pT, float c0, float cT)
    {
        Transform t = l.transform;
        Transform? parent = t.parent;
        string path = parent != null ? parent.name + "/" + t.name : t.name;
        float d0 = Vector3.Distance(t.position, p0);
        float dT = Vector3.Distance(t.position, pT);
        return "'" + path + "' " + l.type + " range " + l.range.ToString("0.##")
               + " wu, intensity " + l.intensity.ToString("0.##") + ", renderMode " + l.renderMode
               + ", shadows " + l.shadows + ", baked " + l.bakingOutput.isBaked
               + ", mask 0x" + l.cullingMask.ToString("X8")
               + ", distance " + d0.ToString("0.###") + " wu at rung 0 -> " + dT.ToString("0.###")
               + " wu at the top rung, contribution " + c0.ToString("0.####") + " -> "
               + cT.ToString("0.####");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>The analytic occlusion-map lookup. NOT a GPU read-back: this reconstructs which
    /// REGISTERED renderers cover the rung's screen point in the generator camera, which names the
    /// coverer instead of returning an unattributable number.</summary>
    private static void SampleSweepOcclusion(Vector3 basePos, Renderer lead, GameObject? visual,
                                             int top)
    {
        TilesOcclusionGenerator? gen = TilesOcclusionGenerator.s_Instance;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head != null && _swHeadCamera.Length == 0)
            _swHeadCamera = head.name;
        if (gen == null)
            return;

        Camera? cam = gen.GetComponent<Camera>();
        if (cam == null)
            return;
        _swGenSeen = true;
        if (_swGenCamera.Length == 0)
            _swGenCamera = cam.name;
        _swGenIsHead = head != null && ReferenceEquals(cam, head);

        if (_swOccGlobals.Length == 0)
            _swOccGlobals = DescribeOcclusionGlobals();

        List<MeshRenderer> reg = gen.m_ObjectRenderers;
        _swOccRegistered = reg != null ? reg.Count : 0;
        if (_swOccPropRenderers < 0 && visual != null)
            MeasureOcclusionParticipation(visual, reg);
        _swOccRoom = gen.m_RoomRenderers != null ? gen.m_RoomRenderers.Count : 0;
        if (reg == null || reg.Count == 0)
            return;

        int walk = reg.Count < SweepOccCap ? reg.Count : SweepOccCap;
        int cover0 = 0, coverT = 0;
        for (int r = 0; r < SweepRungs.Length; r++)
        {
            Vector3 p = basePos + (Vector3.up * SweepRungs[r]);
            Vector3 vp = cam.WorldToViewportPoint(p);
            // CHANNEL 6, and it is taken even when the generator's own projection fails: what a
            // screen-space sampler actually fetches is the HEAD camera's UV, and that number is
            // worth having whether or not the map's own camera can see the point.
            if (head != null)
            {
                Vector3 hv = head.WorldToViewportPoint(p);
                if (hv.z > 0f)
                {
                    if (hv.x < SwHeadUvXLo[r]) SwHeadUvXLo[r] = hv.x;
                    if (hv.x > SwHeadUvXHi[r]) SwHeadUvXHi[r] = hv.x;
                    if (hv.y < SwHeadUvYLo[r]) SwHeadUvYLo[r] = hv.y;
                    if (hv.y > SwHeadUvYHi[r]) SwHeadUvYHi[r] = hv.y;
                    if (hv.x < 0f || hv.x > 1f || hv.y < 0f || hv.y > 1f)
                        SwHeadOffScreen[r]++;
                    if (vp.z > 0f)
                    {
                        float gap = new Vector2(hv.x - vp.x, hv.y - vp.y).magnitude;
                        if (gap > SwUvGapHi[r]) SwUvGapHi[r] = gap;
                    }
                }
                else
                {
                    SwHeadOffScreen[r]++;
                }
            }
            if (vp.z <= 0f)
            {
                _swOccOffScreen++;
                continue;
            }
            int cover = 0;
            bool self = false;
            for (int i = 0; i < walk; i++)
            {
                MeshRenderer mr = reg[i];
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;
                if (!ViewportBoundsContain(cam, mr.bounds, vp.x, vp.y))
                    continue;
                cover++;
                // AGAINST THE PROP'S OWN VISUAL, never against transform.root: a held prop is
                // reparented under the hand, so its root is the VR rig and a root test would call
                // half the scene "self".
                if (ReferenceEquals(mr, lead)
                    || (visual != null && mr.transform.IsChildOf(visual.transform)))
                    self = true;
                if (r == 0 && SwOccNames.Count < SweepNameCap)
                    SwOccNames.Add("'" + mr.name + "'" + (ReferenceEquals(mr, lead) ? " (THE HELD PROP ITSELF)" : string.Empty));
            }
            if (cover > SwOccMax[r])
                SwOccMax[r] = cover;
            if (self)
                SwSelfCover[r]++;
            if (r == 0) cover0 = cover;
            if (r == top) coverT = cover;
        }
        if (cover0 != coverT)
            _swOccDiffs++;
    }

    /// <summary>Name every <c>ObjectOcclusionVolume</c> under the prop and say, for each, whether
    /// it actually registered anything: the component's own object must carry a
    /// <see cref="MeshRenderer"/>, because <c>OnEnable</c> passes
    /// <c>GetComponent&lt;MeshRenderer&gt;()</c> and <c>AddObjectRenderer</c> early-returns on
    /// null. Then count how many renderers under the prop are genuinely in the generator's list.
    /// Taken ONCE per window: the answer is a property of the prefab, not of the frame.</summary>
    private static void MeasureOcclusionParticipation(GameObject visual, List<MeshRenderer>? reg)
    {
        var sb = new StringBuilder(256);
        var vols = new List<ObjectOcclusionVolume>(4);
        visual.GetComponentsInChildren(true, vols);
        int registered = 0;
        for (int i = 0; i < vols.Count && i < SweepNameCap; i++)
        {
            ObjectOcclusionVolume v = vols[i];
            if (v == null)
                continue;
            var mr = v.GetComponent<MeshRenderer>();
            bool inList = mr != null && reg != null && reg.Contains(mr);
            if (inList)
                registered++;
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append('\'').Append(v.name).Append("' enabled ").Append(v.enabled)
              .Append(", MeshRenderer on the same object: ").Append(mr != null ? "YES" : "**NO**")
              .Append(", present in m_ObjectRenderers: ").Append(inList ? "YES" : "**NO**");
        }
        if (vols.Count == 0)
            sb.Append("none under this prop");
        else if (vols.Count > SweepNameCap)
            sb.Append("; and ").Append(vols.Count - SweepNameCap).Append(" more not named");
        _swOccVolumes = sb.ToString();

        int under = 0;
        if (reg != null)
        {
            var rends = new List<MeshRenderer>(8);
            visual.GetComponentsInChildren(true, rends);
            for (int i = 0; i < rends.Count; i++)
            {
                if (rends[i] != null && reg.Contains(rends[i]))
                    under++;
            }
        }
        _swOccPropRenderers = under;
    }

    /// <summary>The occlusion globals, read back. <c>_EnableOcclusionMap</c> is the shader-side
    /// MASTER GATE — proven by disassembly for <c>ParticleMasterUnlitAdd_Shd</c>
    /// (<c>tools/ShaderDisasm/FINDINGS.md</c>): the whole occlusion term is wrapped in
    /// <c>movc r0.x, (_EnableOcclusionMap != 0), computed, 1</c>, so at 0 it is bypassed
    /// entirely. The texture dimensions are the space the lookup is authored in, which is what
    /// makes the camera mismatch a number rather than a sentence.</summary>
    private static string DescribeOcclusionGlobals()
    {
        var sb = new StringBuilder(192);
        sb.Append("_EnableOcclusionMap=")
          .Append(Shader.GetGlobalFloat("_EnableOcclusionMap").ToString("0.###"));
        AppendGlobalTexture(sb, "_ObjectOcclusion");
        AppendGlobalTexture(sb, "_TilesOcclusionMap");
        return sb.ToString();
    }

    private static void AppendGlobalTexture(StringBuilder sb, string name)
    {
        Texture? t = Shader.GetGlobalTexture(name);
        sb.Append(", ").Append(name).Append('=');
        if (t == null)
        {
            // NOT proof of absence: a texture bound through a CommandBuffer temporary-RT id may
            // not resolve through this accessor. Say so rather than let a null read as unbound.
            sb.Append("<null through Shader.GetGlobalTexture, which does NOT prove unbound — a "
                      + "CommandBuffer temporary RT id need not resolve here>");
            return;
        }
        sb.Append(t.width).Append('x').Append(t.height);
    }

    /// <summary>Does the screen-space extent of <paramref name="b"/> contain the viewport point
    /// (<paramref name="vx"/>, <paramref name="vy"/>)? The eight bounds corners are projected and
    /// their axis-aligned viewport box tested — the same conservative extent the occlusion map's
    /// blurred quarter-resolution silhouette actually paints, and deliberately no tighter: a tight
    /// box is not the rect, and the map is blurred twice before anything samples it.</summary>
    private static bool ViewportBoundsContain(Camera cam, Bounds b, float vx, float vy)
    {
        Vector3 c = b.center, e = b.extents;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        bool any = false;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z));
            Vector3 v = cam.WorldToViewportPoint(corner);
            if (v.z <= 0f)
                continue;
            any = true;
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.y > maxY) maxY = v.y;
        }
        return any && vx >= minX && vx <= maxX && vy >= minY && vy <= maxY;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>Every scene projector whose frustum contains the rung. A projector that paints ONTO
    /// the prop hangs off no object in the prop's hierarchy, so the round that counted zero
    /// Projectors UNDER the trap could not have seen one.</summary>
    private static void SampleSweepProjectors(Vector3 basePos, int top)
    {
        int walk = _swProjectors.Length < SweepProjCap ? _swProjectors.Length : SweepProjCap;
        if (walk == 0)
            return;
        for (int r = 0; r < SweepRungs.Length; r++)
        {
            Vector3 p = basePos + (Vector3.up * SweepRungs[r]);
            int hits = 0;
            for (int i = 0; i < walk; i++)
            {
                Projector pr = _swProjectors[i];
                if (pr == null || !pr.isActiveAndEnabled)
                    continue;
                if (_swLayer >= 0 && (pr.ignoreLayers & (1 << _swLayer)) != 0)
                    continue;
                if (!ProjectorContains(pr, p))
                    continue;
                hits++;
                if (r == 0 && SwProjNames.Count < SweepNameCap)
                    SwProjNames.Add("'" + pr.name + "' (ortho " + pr.orthographic + ", fov "
                                    + pr.fieldOfView.ToString("0.#") + ", near "
                                    + pr.nearClipPlane.ToString("0.##") + ", far "
                                    + pr.farClipPlane.ToString("0.##") + ", ignoreLayers 0x"
                                    + pr.ignoreLayers.ToString("X8") + ")");
            }
            if (hits > SwProjMax[r])
                SwProjMax[r] = hits;
        }
    }

    private static bool ProjectorContains(Projector pr, Vector3 world)
    {
        Vector3 local = pr.transform.InverseTransformPoint(world);
        if (local.z < pr.nearClipPlane || local.z > pr.farClipPlane)
            return false;
        float halfH = pr.orthographic
            ? pr.orthographicSize
            : local.z * Mathf.Tan(pr.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * pr.aspectRatio;
        return Mathf.Abs(local.x) <= halfW && Mathf.Abs(local.y) <= halfH;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>The population of BAKED LIGHT PROBES in the scene — the falsifier for the arm
    /// beside this one.
    ///
    /// <para>The <c>LIGHTING, HELD vs HOME</c> line reads
    /// <c>LightProbes.GetInterpolatedProbe</c> at two positions and the ModBuild 463 log returns
    /// <c>HELD 0.0252..0.0252; HOME 0.0252..0.0252; WORST DIFFERENCE 0</c> over 72 samples. Two
    /// positions more than a metre apart returning the SAME value to four decimal places on every
    /// one of 72 samples is not two readings agreeing; it is a CONSTANT. Unity returns
    /// <c>RenderSettings.ambientProbe</c> from that call when the scene carries no baked probe set
    /// or the point falls outside the tetrahedralisation — so at a probe count of 0 that arm is
    /// reading the scene's ambient term at both ends and its zero excludes NOTHING.</para>
    ///
    /// <para>This clause prints the count so the next reader decides it from a number instead of
    /// from the arm's own prose. It is deliberately NOT phrased as a verdict: a NON-ZERO count
    /// would mean the probes are real and their agreement is a genuine reading.</para></summary>
    private static void AppendProbePopulation(StringBuilder sb)
    {
        LightProbes? probes = LightmapSettings.lightProbes;
        int count = probes != null ? probes.count : 0;
        sb.Append("BAKED LIGHT PROBES IN THE SCENE: ").Append(count)
          .Append(count == 0
              ? " — SO THE 'LIGHTING, HELD vs HOME' ARM ON THE HOME TWIN LINE IS READING THE "
                + "AMBIENT PROBE AT BOTH ENDS. LightProbes.GetInterpolatedProbe falls back to "
                + "RenderSettings.ambientProbe when there is no baked set, which is why it returns "
                + "the same value at two positions a metre apart on every sample. ITS ZERO "
                + "DIFFERENCE EXCLUDES NOTHING, and it never could have seen a realtime light in "
                + "the first place — a light probe is a BAKED AMBIENT term. That is the hole this "
                + "sweep exists to fill. "
              : " — the probe set is real, so the 'LIGHTING, HELD vs HOME' arm's agreement is a "
                + "genuine reading of the baked ambient at both positions. It still says nothing "
                + "about any REALTIME light, which is what the rungs below measure. ");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>Emit the sweep. One line per hold, its own grep token, closed by the same reason the
    /// verdict window closed on.</summary>
    private static void EmitPositionSweep(string why)
    {
        if (!_swArmed)
            return;
        _swArmed = false;

        var sb = new StringBuilder(4096);
        sb.Append("[Props] HELD-PROP POSITION SWEEP — ").Append(_swSamples)
          .Append(" sample(s), closed because ").Append(why)
          .Append(". WHY THIS ARM EXISTS, AND IT IS THE USER'S OWN EXPERIMENT: \"wenn ich es "
                  + "physisch nach oben fliegen lasse in der Hand, 'entkomme' ich dem Weissen und "
                  + "es geht wieder weg … die Position im Raum scheint einen Einfluss zu haben\". "
                  + "He changes NO state and waits out NO timer — he TRANSLATES the prop — so the "
                  + "effect is a function of WORLD POSITION, and twelve rounds of state probes "
                  + "could not have seen that term. BOARD PROP STANDING WATCH's 1796 frames and "
                  + "CHANGES: 0 closed the prop's own STATE and did not touch its INPUTS: a light, "
                  + "a projector or a screen-space map the prop merely SAMPLES leaves every one of "
                  + "those probes reading zero while the picture changes. This arm therefore "
                  + "evaluates the prop's inputs at FOUR POSITIONS ON THE SAME FRAME — +0, +0.5, "
                  + "+1 and +2 m straight up from its own bounds centre — because a single "
                  + "position cannot see a gradient. ");

        if (_swSamples == 0)
        {
            sb.Append("NOT TAKEN: no drawing renderer on the held prop for any sampled frame, so "
                      + "this excludes NOTHING and is not a zero reading. ");
            // HW-VERIFY: an untaken sweep must be as loud as a taken one — this file has twice
            // read a window that never opened as a window that returned zero, and that is the
            // single most expensive mistake in its record.
            VRLog.Note("FigureGrab", sb.ToString());
            return;
        }

        AppendProbePopulation(sb);

        sb.Append("POPULATIONS (re-enumerated ").Append(_swRescans)
          .Append(" time(s), NOT per sample — FindObjectsOfType is this project's default "
                  + "performance suspect): SCENE LIGHTS ").Append(_swLightsFound)
          .Append(" active-and-enabled (walked ")
          .Append(_swLightsFound < SweepLightCap ? _swLightsFound : SweepLightCap)
          .Append(" of them), PROJECTORS ").Append(_swProjFound)
          .Append(" (walked ").Append(_swProjFound < SweepProjCap ? _swProjFound : SweepProjCap)
          .Append("). THE PROP'S LAYER, which is what every culling mask below is tested against: ")
          .Append(_swLayer).Append(" '")
          .Append(_swLayerName.Length == 0 ? "<unnamed>" : _swLayerName)
          .Append("'. PER-PIXEL LIGHT CAP IN FORCE: ").Append(_swPixelCap)
          .Append(_swPixelCap == 0
              ? " — AT ZERO NO LIGHT IS EVER PROMOTED TO PER-PIXEL, so the pixel-set channel below "
                + "cannot flip and its zero is a NON-READING rather than an exclusion. "
              : " — Unity picks that many important lights PER RENDERER FROM ITS OWN POSITION, so a "
                + "prop that MOVES can re-rank them with nothing else in the scene changing. ");

        AppendSweepRungs(sb);

        // ---- THE READING THAT DECIDES THE ROUND ----
        sb.Append("LIGHTS THAT THE LIFT ESCAPES (reach the prop where it is, do NOT reach it at "
                  + "the top rung): ").Append(_swEscapeCount);
        if (SwEscapes.Count > 0)
            sb.Append(", naming up to ").Append(SweepNameCap).Append(": ")
              .Append(string.Join("; ", SwEscapes));
        sb.Append(". LIGHTS THE LIFT WALKS INTO (the mirror reading, which would mean lifting makes "
                  + "it WORSE): ").Append(_swEnterCount);
        if (SwEnters.Count > 0)
            sb.Append(", naming: ").Append(string.Join("; ", SwEnters));

        sb.Append(". TOP-N PER-PIXEL LIGHT SET FLIPPED between rung 0 and the top rung on ")
          .Append(_swTopFlips).Append(" of ").Append(_swSamples).Append(" sample(s)");
        if (_swTopExample.Length > 0)
            sb.Append(", for example ").Append(_swTopExample);

        sb.Append(". OCCLUSION MAP: ");
        if (!_swGenSeen)
        {
            sb.Append("GENERATOR none — TilesOcclusionGenerator.s_Instance was null or carried no "
                      + "Camera on every sampled frame, so THIS CHANNEL DID NOT RUN and its zero "
                      + "is not an exclusion. ");
        }
        else
        {
            sb.Append("generator camera '").Append(_swGenCamera).Append("', head camera '")
              .Append(_swHeadCamera.Length == 0 ? "<none>" : _swHeadCamera).Append("', SAME CAMERA: ")
              .Append(_swGenIsHead)
              .Append(_swGenIsHead
                  ? " — so the map is rendered from the eye that samples it. "
                  : " — SO THE MAP IS RENDERED FROM ONE VIEWPOINT AND SAMPLED IN THE SCREEN SPACE "
                    + "OF ANOTHER, which makes every lookup a function of where the object is on "
                    + "the WRONG screen. ")
              .Append(_swOccRegistered).Append(" object renderer(s) registered (walked ")
              .Append(_swOccRegistered < SweepOccCap ? _swOccRegistered : SweepOccCap)
              .Append("), ").Append(_swOccRoom).Append(" room renderer(s). RUNG EVALUATIONS THAT "
                  + "FELL BEHIND THAT CAMERA and could not be projected at all: ")
              .Append(_swOccOffScreen).Append(" of ").Append(_swSamples * SweepRungs.Length)
              .Append(" — an off-screen rung returns coverage 0 for a DIFFERENT reason than an "
                  + "uncovered one, and a high number here means this channel did not run rather "
                  + "than that it found nothing. COVERAGE DIFFERED between rung 0 and the top rung "
                  + "on ").Append(_swOccDiffs).Append(" of ").Append(_swSamples).Append(" sample(s)");
            if (SwOccNames.Count > 0)
                sb.Append("; the renderers covering rung 0 include ")
                  .Append(string.Join(", ", SwOccNames));
            sb.Append(". This is an ANALYTIC reconstruction of which registered renderers' "
                      + "screen-space extents contain the rung, NOT a texture read-back — a "
                      + "read-back is a subsystem, and this file has already photographed a buffer "
                      + "it empties itself. **DOES THIS PROP PARTICIPATE IN THE MAP AT ALL?** "
                      + "renderers under the prop present in m_ObjectRenderers: ")
              .Append(_swOccPropRenderers < 0 ? "<not measured>" : _swOccPropRenderers.ToString())
              .Append("; the volumes themselves — ")
              .Append(_swOccVolumes.Length == 0 ? "<not measured>" : _swOccVolumes)
              .Append(". READ THAT FIRST AND READ IT BEFORE ANYTHING ELSE ON THIS LINE: "
                      + "ObjectOcclusionVolume.OnEnable is AddObjectRenderer(GetComponent"
                      + "<MeshRenderer>()) and AddObjectRenderer EARLY-RETURNS ON NULL, so a "
                      + "volume on an object whose renderer is a SkinnedMeshRenderer registers "
                      + "NOTHING while still counting as an enabled volume on the hush line. AT "
                      + "ZERO REGISTERED RENDERERS THE PROP IS NEITHER DRAWN INTO THIS MAP NOR "
                      + "REMOVABLE FROM IT, which retires strand 5 by construction rather than by "
                      + "experiment and makes the ModBuild 459 'null perturbation' a test that "
                      + "could not have had an effect either way. GLOBALS: ")
              .Append(_swOccGlobals.Length == 0 ? "<not read>" : _swOccGlobals)
              .Append(". ");
        }

        sb.Append("PROJECTORS CONTAINING THE PROP");
        if (SwProjNames.Count > 0)
            sb.Append(" at rung 0: ").Append(string.Join("; ", SwProjNames));
        else
            sb.Append(": none at any rung");
        sb.Append(". A previous round counted 0 Projectors UNDER the trap and that measured THE "
                  + "PROP; a projector painting ONTO it is a different object and could never have "
                  + "appeared in that count. ");

        AppendScreenSpaces(sb);
        AppendSweepVerdict(sb);

        // HW-VERIFY: this is the first reading in this investigation that measures the prop's
        // INPUTS as a function of WORLD POSITION, which is the term the user's own experiment
        // isolates. The round's answer is the DIFFERENCE between the rungs on this line; an EMPTY
        // difference is also an answer and moves the lead to the shader's view-dependence. It must
        // stay at a tier the DEFAULT log level prints — scripts/check-hw-verify.py enforces the
        // position of this marker directly above the call.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>CHANNEL 6 — the coordinate a screen-space sampler ACTUALLY fetches, per rung,
    /// beside the coordinate the map is AUTHORED in.</summary>
    private static void AppendScreenSpaces(StringBuilder sb)
    {
        sb.Append("THE TWO SCREEN SPACES, PER RUNG — because a screen-space lookup does not "
                  + "sample where the object IS, it samples where the object LANDS ON A SCREEN, "
                  + "and this build has two of them. HEAD-CAMERA viewport UV is what any shader "
                  + "reading a global occlusion texture through ComputeScreenPos actually fetches; "
                  + "UV GAP is its distance from the GENERATOR camera's UV for the same world "
                  + "point, i.e. the camera mismatch as a number: ");
        for (int r = 0; r < SweepRungs.Length; r++)
        {
            if (r > 0)
                sb.Append(" | ");
            bool taken = SwHeadUvXLo[r] != float.MaxValue;
            sb.Append('+').Append(SweepRungs[r].ToString("0.#")).Append(" m: ");
            if (!taken)
            {
                sb.Append("never in front of the head camera on any sample");
                continue;
            }
            sb.Append("head UV x ").Append(SwHeadUvXLo[r].ToString("0.###")).Append("..")
              .Append(SwHeadUvXHi[r].ToString("0.###")).Append(", y ")
              .Append(SwHeadUvYLo[r].ToString("0.###")).Append("..")
              .Append(SwHeadUvYHi[r].ToString("0.###")).Append(", OUTSIDE the 0..1 frame on ")
              .Append(SwHeadOffScreen[r]).Append(" of ").Append(_swSamples)
              .Append(" sample(s), worst UV gap to the generator camera ")
              .Append(SwUvGapHi[r].ToString("0.###"));
        }
        sb.Append(". READ IT LIKE THIS: a UV GAP near 0 would mean the two cameras agree and the "
                  + "mismatch is harmless; a gap of order 0.5 or more means the shader fetches a "
                  + "part of the map that belongs to a DIFFERENT PART OF THE WORLD, and it does so "
                  + "for every fragment of every shader that reads one of these globals — a real "
                  + "defect whether or not it is this one. AND THE SECOND NUMBER IS THE ONE THAT "
                  + "TESTS THE USER'S LIFT DIRECTLY: if the head UV leaves the 0..1 frame at the "
                  + "top rung and not at rung 0, then 'lifting escapes it' and 'the sample point "
                  + "left the screen' are the SAME EVENT, and the occlusion channel survives the "
                  + "light channel's death. If the head UV is inside the frame at every rung, that "
                  + "coincidence does not exist and the lift is not moving the lookup out of "
                  + "anything. ");
    }

    private static void AppendSweepRungs(StringBuilder sb)
    {
        sb.Append("THE LADDER, one row per rung, every row read on the SAME FRAME as every other: ");
        for (int r = 0; r < SweepRungs.Length; r++)
        {
            if (r > 0)
                sb.Append(" | ");
            sb.Append('+').Append(SweepRungs[r].ToString("0.#")).Append(" m: lights reaching at "
                      + "most ").Append(SwReachMax[r])
              .Append(", total attenuated luminance last ").Append(SwLumLast[r].ToString("0.####"))
              .Append(" over ")
              .Append((SwLumLo[r] == float.MaxValue ? 0f : SwLumLo[r]).ToString("0.####"))
              .Append("..").Append((SwLumHi[r] == float.MinValue ? 0f : SwLumHi[r]).ToString("0.####"))
              .Append(", occlusion coverage at most ").Append(SwOccMax[r])
              .Append(" (self-covering on ").Append(SwSelfCover[r]).Append(" sample(s))")
              .Append(", projectors at most ").Append(SwProjMax[r]);
        }
        sb.Append(". THE LUMINANCE IS Rec.709 colour times intensity times Unity's legacy point "
                  + "falloff 1/(1+25(d/r)^2), summed over every reaching light — a MONOTONE "
                  + "ESTIMATE, stated here so the number is interpretable rather than magic, and "
                  + "sufficient because what this arm reads is the RATIO between two positions and "
                  + "not an absolute. ");
    }

    /// <summary>The pre-registered reading, written into the line itself so the next round does not
    /// have to reconstruct it from this file.</summary>
    private static void AppendSweepVerdict(StringBuilder sb)
    {
        int top = SweepRungs.Length - 1;
        float l0 = SwLumHi[0] == float.MinValue ? 0f : SwLumHi[0];
        float lT = SwLumHi[top] == float.MinValue ? 0f : SwLumHi[top];
        float ratio = lT > 1e-6f ? l0 / lT : (l0 > 1e-6f ? float.PositiveInfinity : 1f);

        sb.Append("LUMINANCE RATIO rung 0 : top rung = ")
          .Append(float.IsPositiveInfinity(ratio) ? "infinite (the top rung is lit by NOTHING)" : ratio.ToString("0.###"))
          .Append(". HOW TO READ THIS LINE, AND THE ModBuild 464 LOG CORRECTED IT: the ESCAPE "
                  + "AND ENTER COUNTS ARE NOT DIAGNOSTIC AND THE FIRST VERSION OF THIS SENTENCE "
                  + "SAID THEY WERE. 464 read escapes 23 and enters 33 on a 2 m lift through a "
                  + "21-light room; in any lit scene a translation of that size changes set "
                  + "membership in BOTH directions and a non-zero escape count means only that "
                  + "lights have ranges. **THE DIAGNOSTIC IS THE DIRECTION.** The user's white GOES "
                  + "AWAY when he lifts, so a model in which light arriving at the prop paints it "
                  + "requires the luminance to FALL with height, i.e. a LUMINANCE RATIO above 1 "
                  + "and materially so — the bar is 2.0. 464 measured 0.629: the ladder rises "
                  + "monotonically (2.3579, 2.6301, 3.1286, 3.3029) and he lifts the prop into "
                  + "1.6x MORE light while the white goes. THAT CLOSES THE LIGHT CHANNEL, and it "
                  + "closes it in the strongest way available — not by a zero, but by a non-zero "
                  + "reading pointing the wrong way. It closes 'light reaching the prop'; it does "
                  + "not touch the shader's own VIEW-dependence (§13.2: the ring reads white "
                  + "FACE-ON and bronze EDGE-ON, and lifting a prop in a hand also ROTATES it "
                  + "against the eye), for which the POSE columns on the HOME TWIN line are the "
                  + "shipped control. THE OTHER CHANNELS: the pixel-set channel cannot fire at a "
                  + "per-pixel cap of 0 and its zero is a NON-READING; the projector channel "
                  + "contained the prop at no rung; and the occlusion channel is now decided by "
                  + "the PARTICIPATION clause above rather than by the coverage count — at 0 "
                  + "registered renderers under the prop it cannot reach this prop at all, and at "
                  + "1 or more the UV gap and the off-frame counts in THE TWO SCREEN SPACES are "
                  + "what say whether the lift moves the lookup. ");
    }
}
