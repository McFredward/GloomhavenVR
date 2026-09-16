using System;

namespace UnityEngine
{
    public class Shader
    {
        public string name;
        public Shader(string name) { this.name = name; }
    }
    public class Material
    {
        public Shader? shader;
        public Material(Shader? shader) { this.shader = shader; }
    }
    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color clear => new Color(0, 0, 0, 0);
        public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;
    }
    public class Transform
    {
        public UI.Graphic? Graphic;
        public T? GetComponent<T>() where T : class => Graphic as T;
    }
}
namespace UnityEngine.UI
{
    public class Graphic
    {
        private UnityEngine.Material? _material;
        public int MaterialReads, MaterialWrites;
        public bool ThrowOnRead;
        public static readonly UnityEngine.Material defaultGraphicMaterial = new UnityEngine.Material(new UnityEngine.Shader("UI/Default"));
        public virtual UnityEngine.Material? material
        {
            get
            {
                MaterialReads++;
                if (ThrowOnRead) throw new InvalidOperationException("unrelated graphic failure");
                return _material ?? defaultGraphicMaterial;
            }
            set { MaterialWrites++; _material = value; }
        }
        public UnityEngine.Color color = new UnityEngine.Color(.2f, .3f, .4f, .5f);
        public bool enabled = true;
        public bool raycastTarget = true;
    }
    public sealed class Image : Graphic { }
    public sealed class RawImage : Graphic { }
    public sealed class Text : Graphic { }
}
namespace TMPro
{
    // Reproduce the observed lazy-instantiation contract. The test fails at runtime if
    // production invokes this getter, even when the pooled submesh has no source yet.
    public class TMP_SubMeshUI : UnityEngine.UI.Graphic
    {
        public UnityEngine.Material? sharedMaterial;
        public override UnityEngine.Material? material
        {
            get
            {
                MaterialReads++;
                if (sharedMaterial == null) throw new ArgumentNullException("source", "TMP_SubMeshUI material getter instantiated a null source");
                return new UnityEngine.Material(sharedMaterial.shader);
            }
            set { MaterialWrites++; sharedMaterial = value; }
        }
    }
    public class TMP_Text : UnityEngine.UI.Graphic
    {
        public UnityEngine.Material? fontSharedMaterial;
        public override UnityEngine.Material? material
        {
            get { MaterialReads++; throw new InvalidOperationException("TMP_Text material getter must not be evaluated"); }
            set { MaterialWrites++; fontSharedMaterial = value; }
        }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConfigBool { internal bool Value; }
    internal static class WorldUIConfig { internal static ConfigBool? NeutraliseGrabPassBlur; }
    internal static partial class PanelSupersample
    {
        internal sealed class Entry { internal int GrabPassNeutralised; }
        internal static void Capture(Entry entry, UnityEngine.UI.Graphic? graphic)
            => NeutraliseGrabPassBlur(entry, new UnityEngine.Transform { Graphic = graphic });
    }
}
