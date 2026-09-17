using System;
namespace UnityEngine;

public class Object
{
    public string name = string.Empty;
    public HideFlags hideFlags;
    public bool Destroyed;
    public static void Destroy(Object? obj) { if (!ReferenceEquals(obj, null)) obj.Destroyed = true; }
    public static bool operator ==(Object? a, Object? b)
    {
        if (ReferenceEquals(a, null)) return ReferenceEquals(b, null) || b.Destroyed;
        if (ReferenceEquals(b, null)) return a.Destroyed;
        return ReferenceEquals(a, b);
    }
    public static bool operator !=(Object? a, Object? b) => !(a == b);
    public override bool Equals(object? obj) => this == obj as Object;
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
public enum HideFlags { HideAndDontSave }
public sealed class Material : Object { }
public sealed class Transform : Object
{
    public MeshFilter? Filter;
    public Renderer? Renderer;
    public T? GetComponent<T>() where T : Object => (Filter as T) ?? Renderer as T;
}
public sealed class MeshFilter : Object
{
    private Mesh? _mesh;
    public int MeshWrites;
    public Mesh? sharedMesh { get => _mesh; set { _mesh = value; MeshWrites++; } }
}
public sealed class Renderer : Object
{
    private Material? _material;
    public int MaterialWrites;
    public Material? sharedMaterial { get => _material; set { _material = value; MaterialWrites++; } }
    public bool enabled = true;
}
public sealed class Mesh : Object
{
    public static int Created;
    public Mesh() { Created++; }
    public Vector3[] vertices = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>();
    public Vector2[] uv = Array.Empty<Vector2>();
    public int[] triangles = Array.Empty<int>();
    private Color[] _colors = Array.Empty<Color>();
    public int ColorWrites;
    public Color[] colors { get => _colors; set { _colors = value; ColorWrites++; } }
    public Bounds bounds;
    public bool Dynamic;
    public void MarkDynamic() => Dynamic = true;
}
public readonly struct Bounds(Vector3 center, Vector3 size) { public readonly Vector3 center = center, size = size; }
public readonly struct Color(float r, float g, float b, float a) { public readonly float r = r, g = g, b = b, a = a; }
public readonly struct Vector3(float x, float y, float z)
{
    public readonly float x = x, y = y, z = z;
    public static Vector3 zero => new(0, 0, 0);
    public static Vector3 back => new(0, 0, -1);
}
public readonly struct Vector2(float x, float y)
{
    public readonly float x = x, y = y;
    public Vector2 normalized { get { float n = MathF.Sqrt(x * x + y * y); return new(x / n, y / n); } }
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    public static Vector2 operator *(Vector2 a, float s) => new(a.x * s, a.y * s);
    public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
}
public readonly struct Rect(float x, float y, float width, float height)
{
    public readonly float xMin = x, yMin = y, width = width, height = height;
}
public static class Mathf
{
    public static float Floor(float value) => MathF.Floor(value);
    public static float Abs(float value) => MathF.Abs(value);
    public static float Max(float a, float b) => MathF.Max(a, b);
    public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
}
