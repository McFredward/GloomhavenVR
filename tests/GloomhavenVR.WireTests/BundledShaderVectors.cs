// THE GUARD FOR THE BUNDLED-SHADER TRAP.
//
// WHAT THE TRAP IS. `Shader.Find` returns only shaders that are already LOADED. A shader that
// lives inside an AssetBundle is loaded when something pulls it in — in practice when a bundle
// PREFAB's material references it. A shader referenced ONLY by runtime C# is therefore never
// loaded and `Shader.Find` returns null for it forever, with the bundle open, the name correct and
// nothing thrown. The feature quietly takes its "shader missing" branch.
//
// WHY IT IS A TEST AND NOT A COMMENT. It has now shipped twice. Build 0258fbb shipped invisible
// board gear/glows (GloomhavenVR/Overlay). The fix was written down as a comment in
// Cards/PlayTray.6.Build.cs — and a comment is not a guard, so ModBuild 153 lost an entire hardware
// round the same way: HauntFigures.Albedo resolved GloomhavenVR/HeadUnlit with a bare Shader.Find,
// got null in a cellar scenario where no head avatar had ever loaded that shader, and the whole
// albedo-darkening mechanism — every constant of it fitted from three photographs — was bypassed at
// its first line. The user photographed the unchanged picture for the sixth time.
//
// WHAT THIS FILE CHECKS, and each one would have caught a real shipped bug:
//
//   1. NO BARE LOOKUP. `Shader.Find("GloomhavenVR/...")` must not appear anywhere in src/. Every
//      bundled shader goes through Core.BundleShaders, which passes the name as a variable — so
//      this check is absolute and needs no allow-list to rot. THIS IS THE CHECK THAT WOULD HAVE
//      FAILED ModBuild 153 AT THE BUILD GATE.
//   2. EVERY TABLE ENTRY POINTS AT A REAL ASSET. The path in BundleShaders.Paths must exist under
//      unity/GloomhavenVR.Assets/ — a renamed or re-cased asset otherwise fails silently at
//      runtime, which is the SECOND half of this trap and just as quiet as the first.
//   3. THE PATH AND THE NAME AGREE. The .shader file at that path must declare exactly that
//      `Shader "GloomhavenVR/X"` name — so a copy-paste of a neighbouring path is caught.
//   4. NO SHADER IS LOOKED UP WITHOUT A TABLE ENTRY. Any "GloomhavenVR/X" string literal in real
//      (non-comment) src/ code that names a shader the bundle actually ships must be a key of the
//      table, or its lookup would have no asset path to fall back to.
//
// It reads SOURCE rather than the compiled DLL, in the manner of ConfigStepVectors (which sweeps
// src/GloomhavenVR/Defaults for the same reason): the property being checked is a property of the
// text, and the compiled form cannot show it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class BundledShaderVectors
{
    private const string HelperRelPath = "src/GloomhavenVR/Core/BundleShaders.cs";
    private const string UnityAssetsRoot = "unity/GloomhavenVR.Assets";
    private const string BundleRelRoot = "unity/GloomhavenVR.Assets/Assets/Bundle";

    /// <summary>The forbidden text. Written in pieces so that this file — which is not under src/ —
    /// could never itself be the thing that trips check 1 if the sweep were ever widened.</summary>
    private static readonly string BareLookup = "Shader" + ".Find(\"GloomhavenVR/";

    /// <summary>`{ "GloomhavenVR/Name", "Assets/Bundle/Dir/File.shader" },` in the helper's table.</summary>
    private static readonly Regex TableRow = new(
        @"\{\s*""(GloomhavenVR/[A-Za-z0-9_]+)""\s*,\s*""(Assets/Bundle/[^""]+\.shader)""\s*\}",
        RegexOptions.Compiled);

    /// <summary>`Shader "GloomhavenVR/Name"` — the name a .shader file DECLARES.</summary>
    private static readonly Regex ShaderDecl = new(
        @"^\s*Shader\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>A "GloomhavenVR/Name" string literal in C#.</summary>
    private static readonly Regex NameLiteral = new(
        @"""(GloomhavenVR/[A-Za-z0-9_]+)""", RegexOptions.Compiled);

    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("bundled shaders");

        string src = Path.Combine(repoRoot, "src");
        string helper = Path.Combine(repoRoot, HelperRelPath.Replace('/', Path.DirectorySeparatorChar));

        t.True(Directory.Exists(src), $"src/ exists at '{src}'");
        t.True(File.Exists(helper),
               $"the single bundled-shader lookup lives at {HelperRelPath}. If this file was moved, "
               + "move this test's constant with it — do NOT delete the test; the lookup it guards "
               + "has shipped broken twice.");
        if (!Directory.Exists(src) || !File.Exists(helper))
            return;

        // ---- 1. no bare Shader.Find on a bundled shader, anywhere in src/ ----------------------
        var offenders = new List<string>();
        string[] sources = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
        foreach (string file in sources)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                // COMMENTS ARE EXEMPT, for the same reason check 4 exempts them and for one more
                // that this check learned the hard way: it fired on NetProtocol's OWN BUILD NOTE,
                // which quotes the failing lookup verbatim while explaining it. The build notes are
                // this project's changelog, and a lint that forbids WRITING DOWN the bug it guards
                // against would push the explanation out of the file where the next reader looks.
                // This is not an allow-list — there is nothing here to add a name to, and a real
                // lookup cannot hide behind it because a commented-out lookup does not compile,
                // let alone run.
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal))
                    continue;
                if (lines[i].Contains(BareLookup))
                    offenders.Add($"{Rel(repoRoot, file)}:{i + 1}");
            }
        }
        t.Equal(0, offenders.Count,
                "a bundled shader is looked up with a bare Shader.Find at: "
                + string.Join(", ", offenders)
                + ". Shader.Find sees only LOADED shaders, and a bundled shader that no bundle "
                + "PREFAB material references is never loaded — the lookup returns null with the "
                + "bundle open and the feature silently takes its 'shader missing' branch. This has "
                + "cost two builds (0258fbb, ModBuild 153). Route it through "
                + "Core.BundleShaders.Resolve instead, which loads the shader ASSET out of the "
                + "bundle by path and therefore does not depend on any other subsystem being up.");

        // ---- 2/3. the table's paths exist and declare the name they are filed under ------------
        string helperText = File.ReadAllText(helper);
        var table = new Dictionary<string, string>();
        foreach (Match m in TableRow.Matches(helperText))
            table[m.Groups[1].Value] = m.Groups[2].Value;

        t.True(table.Count >= 5,
               $"BundleShaders.Paths parsed {table.Count} entries; it held 5 when this test was "
               + "written (BoardLit, Overlay, MapUnlit, HexDecalStable, HeadUnlit). A sudden drop "
               + "means the table's literal shape changed and this test is no longer reading it — "
               + "fix the regex, do not delete the assertion.");

        foreach (KeyValuePair<string, string> kv in table)
        {
            string abs = Path.Combine(repoRoot,
                                      UnityAssetsRoot.Replace('/', Path.DirectorySeparatorChar),
                                      kv.Value.Replace('/', Path.DirectorySeparatorChar));
            bool exists = File.Exists(abs);
            t.True(exists,
                   $"BundleShaders.Paths maps '{kv.Key}' to bundle asset '{kv.Value}', but no such "
                   + $"file exists ({UnityAssetsRoot}/{kv.Value}). AssetBundle.LoadAsset matches the "
                   + "path EXACTLY, casing included, so a stale path here is a runtime null that "
                   + "looks identical to 'the bundle never loaded'.");
            if (!exists)
                continue;

            Match decl = ShaderDecl.Match(File.ReadAllText(abs));
            t.True(decl.Success && decl.Groups[1].Value == kv.Key,
                   $"'{kv.Value}' must declare Shader \"{kv.Key}\" — it declares "
                   + $"\"{(decl.Success ? decl.Groups[1].Value : "<no Shader declaration found>")}\". "
                   + "The name and the path are two independent ways to reach one shader and "
                   + "BundleShaders uses both; if they disagree, one of the two mechanisms is dead "
                   + "and nobody finds out until a feature goes quiet on hardware.");
        }

        // ---- 4. every bundled shader named in real code has a table entry ----------------------
        var shipped = new HashSet<string>(StringComparer.Ordinal);
        string bundleRoot = Path.Combine(repoRoot, BundleRelRoot.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(bundleRoot))
            foreach (string sh in Directory.GetFiles(bundleRoot, "*.shader", SearchOption.AllDirectories))
            {
                Match decl = ShaderDecl.Match(File.ReadAllText(sh));
                if (decl.Success)
                    shipped.Add(decl.Groups[1].Value);
            }
        t.True(shipped.Count > 0,
               $"found {shipped.Count} shaders under {BundleRelRoot}; expected the bundle's own "
               + "shader set. Zero means this test stopped looking at the bake and checks 4 is "
               + "vacuous.");

        var untabled = new List<string>();
        foreach (string file in sources)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                // Doc comments quote these names constantly; only real code is evidence of a lookup.
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal))
                    continue;
                foreach (Match m in NameLiteral.Matches(line))
                {
                    string name = m.Groups[1].Value;
                    if (!shipped.Contains(name) || table.ContainsKey(name))
                        continue;
                    untabled.Add($"{Rel(repoRoot, file)}:{i + 1} '{name}'");
                }
            }
        }
        t.Equal(0, untabled.Count,
                "these bundled shaders are named by runtime code but are not in "
                + "BundleShaders.Paths: " + string.Join(", ", untabled)
                + ". Without a table entry the lookup has no asset path to fall back on, so it is a "
                + "bare Shader.Find in all but spelling — the exact failure of ModBuild 153.");
    }

    private static string Rel(string root, string path) =>
        path.StartsWith(root, StringComparison.Ordinal)
            ? path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/')
            : path;
}
