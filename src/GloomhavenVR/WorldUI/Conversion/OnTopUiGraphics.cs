using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Force a uGUI subtree to WIN THE DEPTH TEST against the world's opaque geometry — the shared
/// "on-top UI material" helper (initiative hover popup round 3; see
/// <c>Surfaces/TablePanelSurfaces.cs</c> for the report that motivated it).
///
/// ─── THE PROBLEM THIS SOLVES ────────────────────────────────────────────────────────────────────
/// A converted panel is a WORLD-SPACE canvas. Unity's UI shader family (UI/Default, the TMP
/// distance-field shaders, TMP/Sprite) all declare <c>ZTest [unity_GUIZTestMode]</c>, and on a
/// world-space canvas that global resolves to LEqual — every UI fragment depth-tests against
/// whatever opaque, depth-writing geometry was drawn before it (opaque queue ≤ 2500 vs the UI's
/// ~3000). Content that is coplanar with its panel normally sits proud of everything (the MR
/// backing plate is 2 mm behind the canvas plane by design), but content that reaches OVER a
/// raised 3D feature — the control board's raised wooden rail under the initiative row — lands
/// BEHIND that feature's depth and is z-rejected: a razor cut along the feature's silhouette.
///
/// ─── WHAT IT DOES ───────────────────────────────────────────────────────────────────────────────
/// Swaps every <see cref="Graphic"/> under a given root onto a CLONE of its own material whose
/// only difference is <c>ZTest Always</c>:
/// <list type="bullet">
/// <item><c>unity_GUIZTestMode = Always</c> — not a declared shader Property (bracket lookup
///   only), so <c>HasProperty</c> is false, but <c>SetInt</c> still creates the per-material
///   override that beats Unity's global (the ActorBars wall-occlusion pass proved this exact
///   mechanism on hardware, in the LEqual direction).</item>
/// <item><c>_ZTestMode = Always</c> where the shader declares it (some TMP variants).</item>
/// </list>
/// EVERYTHING ELSE IS DELIBERATELY UNTOUCHED:
/// <list type="bullet">
/// <item><b>renderQueue stays authored (~3000).</b> Within one canvas, paint order is hierarchy
///   order; against other renderers Unity's transparent sort is sortingLayer → sortingOrder →
///   renderQueue → distance, and the panel ladder (CanvasConversion.8.Order) already decides
///   panel-vs-panel by sortingOrder. Keeping the queue means every EXISTING ordering
///   relationship — the MR plate under the content (2998 vs 3000), the game's on-top board HUD
///   widgets (queue 4000/4003) and the ray visuals (5000) over everything — survives verbatim.
///   The ONLY change is that the treated fragments stop LOSING the depth test.</item>
/// <item><b>ZWrite stays authored (off for the whole UI family)</b>, so a treated graphic never
///   owns the depth buffer and cannot punch holes into anything drawn after it.</item>
/// </list>
///
/// ─── THE ANIMATED-MATERIAL TRAP (why <see cref="Apply"/> excludes FX-driver subtrees) ──────────
/// The one hazard in swapping a uGUI material is a game component that HOLDS and ANIMATES the
/// original: <c>CardEffects</c>/<c>ItemCardEffects</c> swap card images onto a custom
/// screen-space shader keyed by <c>_PosAndBounds</c> (the "card renders DEEP BLACK" incident —
/// <c>Net/RemoteCardArt.StripFragileEffects</c> documents it; CardFace.Maintain records the
/// original burn/lose-popup failure), <c>UIFX_MaterialFX_Control</c> instantiates a material per
/// image in Awake and animates <c>_FXAnim</c> on that instance, and
/// <c>UITextMeshProMaterialAnimator</c> lerps <c>fontMaterial</c> between two assets. Swapping a
/// graphic such a driver owns would freeze (or fight) the animation. So <see cref="Apply"/>
/// collects those driver components under the root FIRST and skips every graphic below one. For
/// the initiative hover popup this is belt only: <c>MonsterBaseUI</c>, <c>MonsterRoundCardUI</c>,
/// <c>InitiativeTrackEnemyBehaviour</c>, <c>LayoutRow</c> and <c>TextLocalizedListener</c>
/// contain ZERO material references (verified in the decompiled sources) — but the popup's card
/// body is re-instantiated from a game prefab every generation, so the exclusion re-proves it
/// at runtime instead of trusting yesterday's prefab.
///
/// ─── TMP IS NOT TOUCHED THROUGH <c>Graphic.material</c> ────────────────────────────────────────
/// TMP overrides the <c>material</c> accessor: the GETTER alone mints a per-text instance
/// (<c>fontMaterial</c> semantics) — reading it on every popup label would leak one material per
/// label per card generation as a side effect. TMP texts and their sprite/fallback sub-meshes are
/// therefore swapped through <c>fontSharedMaterial</c>/<c>sharedMaterial</c>, which assign
/// WITHOUT instantiating; the clone carries the same atlas texture, so TMP's padding math and
/// dynamic-atlas repacks are unaffected.
///
/// ─── CLONES ARE CACHED FOR THE SESSION, NEVER DESTROYED ────────────────────────────────────────
/// One clone per DISTINCT source material (UI default, one per TMP font atlas, the sprite
/// atlas…), shared by every graphic that wears that source — treated graphics keep batching, and
/// the cache stays a handful of materials for a whole session (capped defensively). Deliberately
/// never destroyed: <c>Net.RemoteWidgetMirror.Pair.CopyMaterial</c> shares a mirrored panel's
/// Image/RawImage materials BY REFERENCE onto peer-board clones every drive tick, so destroying a
/// clone on restore could leave a mirror rendering a destroyed material for a frame. A cached,
/// immortal clone makes that window impossible; the mirror simply re-copies the restored original
/// on its next drive. (While treated, the mirrored copy of the subtree is on-top too — which is
/// the RIGHT picture: the mirrored popup faces the same raised furniture on the remote board.)
///
/// ─── RESTORE DISCIPLINE ────────────────────────────────────────────────────────────────────────
/// Every swap is recorded (graphic → its exact original reference) and handed back by
/// <see cref="RestoreAll"/> — but only where the graphic STILL wears our clone (reference check):
/// if the game re-assigned a material meanwhile, the game won and restore must not stomp it.
/// Records of Unity-destroyed graphics are dropped (their clone is cache-owned, nothing leaks).
/// </summary>
internal sealed class OnTopUiGraphics
{
    // ---- session-long clone cache (shared across all instances) ------------------------------
    /// <summary>On-top clone per distinct source material. Never destroyed — see the class doc
    /// (RemoteWidgetMirror shares these by reference across a clone boundary).</summary>
    private static readonly Dictionary<Material, Material> CloneCache = new(8);

    /// <summary>Hard cap on the cache. Distinct UI source materials are a handful per session
    /// (UI default + one per TMP atlas); at the cap, dead (Unity-destroyed) source keys are
    /// pruned once, and if the cache is still full the swap is skipped (logged) rather than
    /// minting untracked clones.</summary>
    private const int CloneCacheCap = 64;

    /// <summary>One log line per distinct shader we ever put on-top (session-scoped) — the
    /// hardware log's way of attributing a mis-rendered graphic to this pass.</summary>
    private static readonly HashSet<string> ShaderLogged = new();

    private static readonly int GuiZTestMode = Shader.PropertyToID("unity_GUIZTestMode");
    private static readonly int ZTestMode = Shader.PropertyToID("_ZTestMode");

    // ---- per-instance swap records ------------------------------------------------------------
    private readonly List<(Graphic g, Material orig, Material clone)> _graphics = new(64);
    private readonly List<(TMP_Text t, Material orig, Material clone)> _texts = new(32);
    private readonly List<(TMP_SubMeshUI s, Material orig, Material clone)> _subMeshes = new(8);

    /// <summary>Instance IDs of every component already treated (or deliberately skipped), so the
    /// per-frame Apply is one hash probe per already-seen graphic.</summary>
    private readonly HashSet<int> _seen = new(128);

    /// <summary>FX-driver roots found under the current Apply root (see the class doc). Rebuilt
    /// only on ticks that found at least one NEW graphic — a driver arrives with its subtree.</summary>
    private readonly List<Transform> _driverRoots = new(4);

    private static readonly List<Graphic> GraphicScratch = new(64);
    private static readonly List<MonoBehaviour> BehaviourScratch = new(32);

    /// <summary>Total components currently holding one of our clones.</summary>
    public int Count => _graphics.Count + _texts.Count + _subMeshes.Count;

    /// <summary>
    /// Prune records whose component Unity has destroyed above this many entries — the popup's
    /// card body is destroyed and re-instantiated on EVERY generation (MonsterBaseUI.cs:293/304),
    /// so a long session would otherwise accumulate one dead record per node per card. A dead
    /// record has nothing to restore (its clone is cache-owned), so pruning is loss-free.
    /// </summary>
    private const int RecordCap = 512;

    /// <summary>
    /// Swap every untreated <see cref="Graphic"/> under <paramref name="root"/> (inactive
    /// included — a popup section that toggles on later must already be treated) onto its
    /// on-top clone, except graphics under a known FX driver. Idempotent and cheap in steady
    /// state: one Graphic walk + one hash probe per node. Returns how many NEW swaps happened.
    /// </summary>
    public int Apply(Transform root, string context)
    {
        if (root == null)
            return 0;

        GraphicScratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, GraphicScratch);

        // First pass: is there anything NEW at all? (steady state exits here, no driver scan)
        bool anyNew = false;
        for (int i = 0; i < GraphicScratch.Count && !anyNew; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g != null && !_seen.Contains(g.GetInstanceID()))
                anyNew = true;
        }
        if (!anyNew)
        {
            GraphicScratch.Clear();
            return 0;
        }

        CollectDriverRoots(root);

        int swapped = 0;
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null)
                continue;
            int id = g.GetInstanceID();
            if (_seen.Contains(id))
                continue;
            _seen.Add(id); // treated OR skipped — either way judged once
            if (UnderDriverRoot(g.transform, root))
                continue; // the driver owns this graphic's material — never touch it

            try
            {
                if (g is TMP_Text text)
                {
                    // Through the SHARED accessor — the instance accessors (material/fontMaterial)
                    // mint a per-text copy on READ; see the class doc.
                    Material orig = text.fontSharedMaterial;
                    if (orig == null || !TryGetClone(orig, out Material clone))
                        continue;
                    text.fontSharedMaterial = clone;
                    _texts.Add((text, orig, clone));
                }
                else if (g is TMP_SubMeshUI sub)
                {
                    Material orig = sub.sharedMaterial;
                    if (orig == null || !TryGetClone(orig, out Material clone))
                        continue;
                    sub.sharedMaterial = clone;
                    _subMeshes.Add((sub, orig, clone));
                }
                else
                {
                    // Plain Graphic (Image/RawImage/legacy Text): the getter is side-effect-free
                    // (m_Material ?? the shared default UI material).
                    Material orig = g.material;
                    if (orig == null || !TryGetClone(orig, out Material clone))
                        continue;
                    g.material = clone;
                    _graphics.Add((g, orig, clone));
                }
                swapped++;
            }
            catch
            {
                // Leave this graphic vanilla (it stays merely depth-tested, never mis-rendered);
                // the ID stays recorded — a component that throws here throws every tick.
            }
        }
        GraphicScratch.Clear();

        PruneDeadRecords();

        if (swapped > 0)
        {
            VRLog.Info("WorldUI",
                $"ON-TOP UI ({context}): {swapped} graphic(s) swapped onto ZTest-Always clones of " +
                $"their own materials ({Count} held, {CloneCache.Count} distinct clone(s) cached, " +
                $"{_driverRoots.Count} FX-driver subtree(s) excluded). renderQueue/ZWrite untouched " +
                "— paint order and panel occlusion are exactly as before; the treated fragments " +
                "just stop losing the depth test against raised opaque geometry.");
        }
        return swapped;
    }

    /// <summary>
    /// Hand every treated component its original material back — reference-checked, so a material
    /// the game re-assigned in the meantime is never stomped — and forget everything. Clones stay
    /// in the session cache (see the class doc). Cheap no-op while nothing is held.
    /// </summary>
    public void RestoreAll(string reason)
    {
        int had = Count;
        for (int i = 0; i < _graphics.Count; i++)
        {
            (Graphic g, Material orig, Material clone) = _graphics[i];
            try
            {
                if (g != null && ReferenceEquals(g.material, clone))
                    g.material = orig;
            }
            catch { /* destroyed under us — nothing to restore */ }
        }
        for (int i = 0; i < _texts.Count; i++)
        {
            (TMP_Text t, Material orig, Material clone) = _texts[i];
            try
            {
                if (t != null && ReferenceEquals(t.fontSharedMaterial, clone))
                    t.fontSharedMaterial = orig;
            }
            catch { /* destroyed under us */ }
        }
        for (int i = 0; i < _subMeshes.Count; i++)
        {
            (TMP_SubMeshUI s, Material orig, Material clone) = _subMeshes[i];
            try
            {
                if (s != null && ReferenceEquals(s.sharedMaterial, clone))
                    s.sharedMaterial = orig;
            }
            catch { /* destroyed under us */ }
        }
        _graphics.Clear();
        _texts.Clear();
        _subMeshes.Clear();
        _seen.Clear();
        _driverRoots.Clear();
        if (had > 0)
            VRLog.Info("WorldUI", $"ON-TOP UI restore ({reason}): {had} graphic(s) back on their " +
                                  "authored materials (reference-checked; clones stay cached).");
    }

    /// <summary>The on-top clone for a source material, from the session cache or freshly minted.
    /// False only when the cache is full of LIVE keys (never expected — logged once).</summary>
    private static bool TryGetClone(Material src, out Material clone)
    {
        if (CloneCache.TryGetValue(src, out clone) && clone != null)
            return true;
        if (CloneCache.Count >= CloneCacheCap)
        {
            PruneCloneCache();
            if (CloneCache.Count >= CloneCacheCap)
            {
                if (ShaderLogged.Add("<cache-full>"))
                    VRLog.Warn("WorldUI", $"ON-TOP UI: clone cache at cap ({CloneCacheCap} live " +
                                          "source materials) — further graphics stay depth-tested.");
                return false;
            }
        }
        clone = new Material(src) { name = src.name + " (VR on-top)" };
        clone.SetInt(GuiZTestMode, (int)CompareFunction.Always);
        if (clone.HasProperty(ZTestMode))
            clone.SetInt(ZTestMode, (int)CompareFunction.Always);
        CloneCache[src] = clone;
        Shader shader = src.shader;
        string shaderName = shader != null ? shader.name : "<null shader>";
        if (ShaderLogged.Add(shaderName))
            VRLog.Info("WorldUI", $"ON-TOP UI: minted ZTest-Always clone of '{src.name}' " +
                                  $"(shader '{shaderName}') — cached for the session.");
        return true;
    }

    /// <summary>Drop cache entries whose SOURCE material Unity has destroyed (scene teardown);
    /// their clones are destroyed with them — nothing can be wearing a clone of a material that
    /// no longer exists except stale references our restore path reference-checks anyway.</summary>
    private static void PruneCloneCache()
    {
        var dead = new List<Material>(4);
        foreach (KeyValuePair<Material, Material> kv in CloneCache)
        {
            if (kv.Key == null) // Unity fake-null: destroyed, but still a real dictionary key
                dead.Add(kv.Key!);
        }
        for (int i = 0; i < dead.Count; i++)
        {
            Material clone = CloneCache[dead[i]];
            CloneCache.Remove(dead[i]);
            if (clone != null)
                Object.Destroy(clone);
        }
    }

    /// <summary>Collect the FX-driver components under <paramref name="root"/> whose subtrees
    /// <see cref="Apply"/> must not touch — ONE MonoBehaviour walk, type-checked, instead of one
    /// scene query per driver type. See the class doc's animated-material-trap section.</summary>
    private void CollectDriverRoots(Transform root)
    {
        _driverRoots.Clear();
        BehaviourScratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, BehaviourScratch);
        for (int i = 0; i < BehaviourScratch.Count; i++)
        {
            MonoBehaviour b = BehaviourScratch[i];
            if (b is CardEffects or ItemCardEffects or UIFX_MaterialFX_Control
                or UITextMeshProMaterialAnimator)
            {
                _driverRoots.Add(b.transform);
            }
        }
        BehaviourScratch.Clear();
    }

    /// <summary>Is <paramref name="t"/> at or below one of the collected driver roots? Ancestor
    /// walk bounded by <paramref name="stopAt"/> — the roots list is empty in the expected case,
    /// making this a single count check per new graphic.</summary>
    private bool UnderDriverRoot(Transform t, Transform stopAt)
    {
        if (_driverRoots.Count == 0)
            return false;
        for (Transform? link = t; link != null; link = link.parent)
        {
            for (int i = 0; i < _driverRoots.Count; i++)
            {
                if (ReferenceEquals(_driverRoots[i], link))
                    return true;
            }
            if (ReferenceEquals(link, stopAt))
                break;
        }
        return false;
    }

    /// <summary>Bounded records — see <see cref="RecordCap"/>.</summary>
    private void PruneDeadRecords()
    {
        if (Count <= RecordCap)
            return;
        _graphics.RemoveAll(static r => r.g == null);
        _texts.RemoveAll(static r => r.t == null);
        _subMeshes.RemoveAll(static r => r.s == null);
        // _seen keeps the dead IDs — instance IDs are never reused for new objects within a
        // session, so a stale ID can only ever suppress re-treating an object that no longer
        // exists. Rebuilding the set from the live records keeps it in step with the cap.
        _seen.Clear();
        for (int i = 0; i < _graphics.Count; i++)
            _seen.Add(_graphics[i].g.GetInstanceID());
        for (int i = 0; i < _texts.Count; i++)
            _seen.Add(_texts[i].t.GetInstanceID());
        for (int i = 0; i < _subMeshes.Count; i++)
            _seen.Add(_subMeshes[i].s.GetInstanceID());
    }
}
