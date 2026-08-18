using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE ONE PLACE A BUNDLED SHADER IS LOOKED UP, and the reason it exists is a bug that shipped
/// twice.
///
/// <para><b>THE TRAP.</b> <see cref="Shader.Find"/> only returns shaders that are already LOADED.
/// A shader that lives inside an AssetBundle is loaded when something pulls it in — in practice
/// when a bundle PREFAB's material references it. A shader referenced ONLY by runtime C# is
/// therefore never loaded, and <c>Shader.Find</c> returns null for it forever, even though the
/// bundle holding it is open. The bundle is not broken, the name is not misspelled, and nothing
/// throws: the feature simply takes its "shader missing" branch and the user reports that the fix
/// did nothing.</para>
///
/// <para><b>IT HAS COST TWO BUILDS.</b> Build 0258fbb shipped invisible board gear/glows because
/// <c>GloomhavenVR/Overlay</c> was looked up with a bare <c>Shader.Find</c>. The lesson was written
/// down as a COMMENT in <c>Cards/PlayTray.6.Build.cs</c> — and a comment is not a guard, so
/// ModBuild 153 lost a whole hardware round the same way: the haunt-figure albedo darkening
/// resolved <c>GloomhavenVR/HeadUnlit</c> with a bare <c>Shader.Find</c>, got null in a cellar
/// scenario where no head avatar had ever loaded that shader, and the entire darkening mechanism
/// was bypassed at its first line (LogOutput.log:1244). Every constant that round fitted from three
/// photographs went untested.</para>
///
/// <para><b>WHAT THIS CLASS GUARANTEES.</b> A lookup here does not depend on any OTHER subsystem
/// having happened to load the shader first, because step 2 loads the shader ASSET out of the
/// bundle by path — and loading an asset from an open bundle requires nothing but the bundle being
/// open. That converts "it resolves if the head avatars are up" into "it resolves if the bundle is
/// loaded", which for anything drawn inside a mod-built room is a certainty rather than a hope.</para>
///
/// <para><b>THE ASSET PATHS LIVE HERE AND NOWHERE ELSE</b> (<see cref="Paths"/>). A call site names
/// the SHADER, never the path, so a call site cannot get the folder or the casing wrong — the other
/// half of this trap, and the half that fails just as silently. <c>tests/GloomhavenVR.WireTests</c>
/// (<c>BundledShaderVectors</c>) checks the table against the real
/// <c>unity/GloomhavenVR.Assets/Assets/Bundle/**.shader</c> files in both directions, and fails the
/// build gate if a bare <c>Shader.Find</c> on a <c>GloomhavenVR/*</c> name is written anywhere in
/// <c>src/</c> at all — including in this file, which passes the name as a VARIABLE.</para>
/// </summary>
internal static class BundleShaders
{
    /// <summary>
    /// Every shader this mod ships in <c>gloomhavenvr.bundle</c> that runtime C# ever asks for, and
    /// the asset path <c>AssetBundle.LoadAsset</c> needs (BuildBundles packs everything under
    /// <c>Assets/Bundle/</c>, so the path is the project-relative path of the .shader file).
    ///
    /// <para>The "referenced by a bundle prefab?" column is what decides whether step 1 below can
    /// ever succeed on its own — and every entry goes through step 2 regardless, precisely so that
    /// answer stops mattering:</para>
    /// <list type="bullet">
    /// <item><c>BoardLit</c> — YES, bundle prefab materials use it; bare Find worked.</item>
    /// <item><c>Overlay</c> — NO, runtime C# only; bare Find returned null (build 0258fbb).</item>
    /// <item><c>MapUnlit</c> — NO, runtime C# only.</item>
    /// <item><c>HexDecalStable</c> — NO, runtime C# only.</item>
    /// <item><c>HeadUnlit</c> — only via the head-avatar mask MATERIALS, which exist only while a
    ///   head avatar is up; that is a subsystem, not a guarantee (ModBuild 153).</item>
    /// <item><c>WaterVR</c> — NO, runtime C# only, and it is the entry with the most to lose by it:
    ///   nothing in the bundle's own prefabs references it (it is put on the GAME's water quads),
    ///   so step 1 can never succeed for it and step 2 is the only mechanism that will ever
    ///   resolve it. A bare <c>Shader.Find</c> here would leave every water film on the fallback
    ///   flat sheet with nothing in the log to say why.</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, string> Paths = new()
    {
        { "GloomhavenVR/BoardLit",       "Assets/Bundle/Table/BoardLit.shader" },
        { "GloomhavenVR/Overlay",        "Assets/Bundle/Table/Overlay.shader" },
        { "GloomhavenVR/MapUnlit",       "Assets/Bundle/Table/MapUnlit.shader" },
        { "GloomhavenVR/HexDecalStable", "Assets/Bundle/Table/HexDecalStable.shader" },
        { "GloomhavenVR/HeadUnlit",      "Assets/Bundle/Head/HeadUnlit.shader" },
        { "GloomhavenVR/WaterVR",        "Assets/Bundle/Environments/WaterVR.shader" },
    };

    /// <summary>Shaders that have resolved. Only SUCCESSES are cached — a miss must be retried,
    /// because a bundle can be loaded later than the first lookup.</summary>
    private static readonly Dictionary<string, Shader> Resolved = new(8);

    /// <summary>How each resolved shader was reached, for the log and for the callers that print an
    /// outcome rather than an intention.</summary>
    private static readonly Dictionary<string, string> HowResolved = new(8);

    /// <summary>One "found" line and one "missing" line per shader, not one per frame.</summary>
    private static readonly HashSet<string> FoundLogged = new(8);
    private static readonly HashSet<string> MissLogged = new(8);

    /// <summary>Shaders whose expensive last-resort sweep (<see cref="SweepLoaded"/>) has run. It is
    /// a diagnostic-grade fallback, so it runs at most once per shader per process.</summary>
    private static readonly HashSet<string> Swept = new(8);

    /// <summary>
    /// Resolve a bundled shader, or null when no loaded bundle ships it.
    ///
    /// <para>THREE MECHANISMS, DELIBERATELY DIFFERENT ONES, because a chain of steps that all fail
    /// the same way is one step:</para>
    /// <list type="number">
    /// <item><see cref="Shader.Find"/> — free, and succeeds whenever something else already loaded
    ///   the shader (a bundle prefab's material, another feature).</item>
    /// <item><c>AssetBundle.LoadAsset&lt;Shader&gt;(path)</c> across EVERY loaded bundle — the step
    ///   that does not care what else is up. This is the one that was missing in ModBuild 153.</item>
    /// <item><c>Resources.FindObjectsOfTypeAll&lt;Shader&gt;()</c> matched by name — for the case
    ///   where the shader object IS in memory but neither of the first two reaches it (a bundle
    ///   whose asset path was renamed by the bake, an instance held only by a live material). Once
    ///   per shader per process; it also produces the inventory the miss line prints.</item>
    /// </list>
    /// </summary>
    /// <param name="shaderName">The shader's declared name, e.g. <c>GloomhavenVR/Overlay</c>. Must
    /// be a key of <see cref="Paths"/>.</param>
    /// <param name="scope">VRLog scope for the two one-shot lines.</param>
    /// <param name="whenFound">What the caller can now do, in the caller's own words.</param>
    /// <param name="whenMissing">What the caller falls back to, and what that looks like.</param>
    internal static Shader? Resolve(string shaderName, string scope, string whenFound,
                                    string whenMissing)
    {
        if (Resolved.TryGetValue(shaderName, out Shader hit) && hit != null)
            return hit;

        if (!Paths.TryGetValue(shaderName, out string assetPath))
        {
            // Not a table entry: the caller invented a name. Fail loudly rather than silently —
            // this is a coding error, not a bundle state.
            if (MissLogged.Add(shaderName))
                VRLog.Error(scope, $"BUNDLED SHADER '{shaderName}' is not in BundleShaders.Paths, so "
                                   + "its asset path is unknown and only Shader.Find can be tried. Add "
                                   + "it to the table in Core/BundleShaders.cs — the wire test "
                                   + "BundledShaderVectors checks that table against the real "
                                   + "unity/.../Assets/Bundle/**.shader files.");
            assetPath = string.Empty;
        }

        Shader? sh = Shader.Find(shaderName);
        string how = "Shader.Find — something had already loaded it (a bundle prefab's material, or "
                     + "another feature that asked first)";

        if (sh == null && assetPath.Length > 0)
        {
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b == null)
                    continue;
                Shader? s = b.LoadAsset<Shader>(assetPath);
                if (s == null)
                    continue;
                sh = s;
                how = $"AssetBundle.LoadAsset<Shader>(\"{assetPath}\") out of bundle '{b.name}' — "
                      + "Shader.Find had returned NULL, i.e. nothing else in the process had loaded "
                      + "this shader and a bare Shader.Find would have failed here";
                break;
            }
        }

        if (sh == null && Swept.Add(shaderName))
        {
            sh = SweepLoaded(shaderName);
            if (sh != null)
                how = "Resources.FindObjectsOfTypeAll<Shader>() — the shader object was in memory but "
                      + "neither Shader.Find nor the bundle asset path reached it, which means the "
                      + "path in BundleShaders.Paths no longer matches what the bake produced";
        }

        if (sh != null)
        {
            Resolved[shaderName] = sh;
            HowResolved[shaderName] = how;
            if (FoundLogged.Add(shaderName))
                VRLog.Info(scope, $"BUNDLED SHADER '{shaderName}' RESOLVED via {how}. {whenFound}");
            return sh;
        }

        if (MissLogged.Add(shaderName))
            VRLog.Error(scope, $"BUNDLED SHADER '{shaderName}' NOT RESOLVED — all three mechanisms "
                               + $"failed: Shader.Find returned null, no loaded AssetBundle holds "
                               + $"'{assetPath}', and no Shader by that name is alive in the process. "
                               + $"{whenMissing} WHAT THE PROCESS ACTUALLY HAS RIGHT NOW: {Inventory()}. "
                               + "READ THAT INVENTORY BEFORE RE-TUNING ANYTHING: if it lists no bundle "
                               + "at all the bundle never loaded (wrong Unity editor — see "
                               + "scripts/check-bundle-format.sh); if it lists the bundle but no "
                               + "GloomhavenVR/* shader is loaded, the bake stripped the shader or "
                               + "moved the asset and the path in Core/BundleShaders.cs is stale.");
        return null;
    }

    /// <summary>Every loaded <see cref="Shader"/> whose name matches, including ones held alive only
    /// by a live material. Allocation-heavy, hence once per shader per process.</summary>
    private static Shader? SweepLoaded(string shaderName)
    {
        Shader[] all = Resources.FindObjectsOfTypeAll<Shader>();
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == shaderName)
                return all[i];
        return null;
    }

    /// <summary>
    /// What a reader needs to tell "the bundle never loaded" apart from "the bundle loaded and the
    /// asset path is wrong" — the two failures that look identical from a null shader. Printed with
    /// every miss, because the ModBuild 153 log had the null and nothing else, and a human had to
    /// reason the difference out from the source.
    /// </summary>
    internal static string Inventory()
    {
        var sb = new StringBuilder(160);
        int bundles = 0;
        sb.Append("loaded AssetBundles [");
        foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (b == null)
                continue;
            if (bundles > 0)
                sb.Append(", ");
            sb.Append('\'').Append(b.name).Append('\'');
            bundles++;
        }
        sb.Append(bundles == 0 ? "NONE" : string.Empty).Append(']');

        Shader[] all = Resources.FindObjectsOfTypeAll<Shader>();
        int mine = 0;
        var names = new StringBuilder(120);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !all[i].name.StartsWith("GloomhavenVR/"))
                continue;
            if (mine > 0)
                names.Append(", ");
            names.Append(all[i].name);
            mine++;
        }
        sb.Append("; ").Append(all.Length).Append(" Shader objects alive, of which ").Append(mine)
          .Append(" are this mod's [").Append(mine == 0 ? "NONE" : names.ToString()).Append(']');
        return sb.ToString();
    }

    /// <summary>How <paramref name="shaderName"/> was reached, or a phrase saying it was not. For
    /// callers whose own diagnostics must report an OUTCOME rather than an intention.</summary>
    internal static string How(string shaderName) =>
        HowResolved.TryGetValue(shaderName, out string how) ? how : "NOT RESOLVED";

    /// <summary>Whether <paramref name="shaderName"/> is currently resolved. Never triggers a
    /// lookup — this is a report on what has already happened.</summary>
    internal static bool Has(string shaderName) =>
        Resolved.TryGetValue(shaderName, out Shader s) && s != null;
}
