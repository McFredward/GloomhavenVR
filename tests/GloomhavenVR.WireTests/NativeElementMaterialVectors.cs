using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

/// <summary>Native clone material provenance checks. These protect inactive-branch playback and
/// source replacement; they cannot establish Unity shader output without a hardware test.</summary>
internal static class NativeElementMaterialVectors
{
    internal static void Run(Harness t, string root)
    {
        string source = Regex.Replace(File.ReadAllText(Path.Combine(root,
            "src/GloomhavenVR/Net/Remote/RemoteNativeElements.cs")), @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        t.Case("native-elements/owner-activation-does-not-depend-on-viewer-clone-material");
        t.True(OriginalBindings(source), "both graphics and effects validate and instantiate from their original graphic");
        t.True(!OriginalBindings(source.Replace("ValidateMaterial(_source.Effects[i]", "ValidateMaterial(_effects[i]")),
            "negative control: inactive clone effect material cannot gate the entire board");
        t.True(!OriginalBindings(source.Replace("Material original = EffectMaterial(source);", "Material original = target.material;")),
            "negative control: an old/default clone material cannot seed an owner effect");
        t.True(SourceReplacement(source), "original material replacement invalidates and disposes the preceding clone-owned material");
        t.True(!SourceReplacement(source.Replace("!ReferenceEquals(previous, original)", "false")),
            "negative control: a reused graphic must not retain an earlier original material variant");
        t.True(!SourceReplacement(source.Replace("Object.Destroy(material);", "")),
            "negative control: material replacement must release the preceding owned instance");
        t.True(source.Contains("material.SetFloat(\"_FXAnim\"") && !source.Contains("original.SetFloat("),
            "animation output writes only the clone-owned material");
        t.True(source.Contains("throw new InvalidOperationException(\"owner element effect has no original material: element=\"")
            && source.Contains("original.name") && source.Contains("material.shader.name"),
            "a genuinely unavailable original remains explicit and names its element, graphic and shader");
    }
    private static bool OriginalBindings(string source) => source.Contains("ValidateMaterial(_source.Graphics[i]")
        && source.Contains("ValidateMaterial(_source.Effects[i]")
        && source.Contains("Graphic(_graphics[i], _source.Graphics[i]")
        && source.Contains("Graphic(_effects[i], _source.Effects[i]")
        && source.Contains("Material original = EffectMaterial(source);")
        && !source.Contains("Material original = target.material;");
    private static bool SourceReplacement(string source) => source.Contains("!ReferenceEquals(previous, original)")
        && source.Contains("Material replacement = new Material(original);")
        && source.Contains("if (material != null) Object.Destroy(material);")
        && source.Contains("_materialSources[target] = original;") && source.Contains("_materialSources.Clear();");
}
