using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The approved alpha-textured front closes a solid relief with a polygonal
/// rear and side walls. Its front sits at the existing logo plane, with the
/// thickness entirely behind it inside the menu's existing optical clearance.
/// </summary>
internal sealed class FrosthavenWordmark
{
    private const string Outline = "GloomhavenVR.Assets.MenuPromotion.FrosthavenVR_logo.fwm";
    private const uint Signature = 0x314D5746; // FWM1
    private const int MaxQuads = 8192;
    private const float DepthFraction = .085f;
    private static bool _reportedFailure;

    private readonly Mesh _mesh;
    private readonly Material _body;
    private readonly Material _face;
    internal readonly Transform Transform;

    private FrosthavenWordmark(Transform transform, Mesh mesh, Material body, Material face)
    {
        Transform = transform;
        _mesh = mesh;
        _body = body;
        _face = face;
    }

    internal static FrosthavenWordmark? Create(Transform parent, Texture2D wordmark, Shader shader)
    {
        Mesh? mesh = null;
        Material? body = null, face = null;
        GameObject? node = null;
        try
        {
            mesh = ReadMesh();
            body = new Material(shader)
            {
                name = "FrosthavenVR wordmark stone",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.AlphaTest
            };
            body.SetFloat("_ZWrite", 1f);
            body.SetFloat("_Cull", (int)CullMode.Back);
            body.SetFloat("_SrcBlend", (int)BlendMode.One);
            body.SetFloat("_DstBlend", (int)BlendMode.Zero);

            face = new Material(shader)
            {
                name = "FrosthavenVR wordmark ink",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = wordmark
            };
            face.SetFloat("_Cull", (int)CullMode.Back);

            node = new GameObject("FrosthavenVR raised logo", typeof(MeshFilter), typeof(MeshRenderer));
            node.transform.SetParent(parent, false);
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { body, face };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new FrosthavenWordmark(node.transform, mesh, body, face);
        }
        catch (Exception error)
        {
            Object.Destroy(node);
            Object.Destroy(mesh);
            Object.Destroy(body);
            Object.Destroy(face);
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                VRLog.Warn("WorldUI", $"FrosthavenVR wordmark volume unavailable ({error.GetType().Name}); showing the original wordmark.");
            }
            return null;
        }
    }

    internal void Place(Vector2 faceSize, float seatWidth, Vector3 offset)
    {
        Vector3 scale = new(faceSize.x, faceSize.y, seatWidth * DepthFraction);
        if (Transform.localScale != scale) Transform.localScale = scale;
        if (Transform.localPosition != offset) Transform.localPosition = offset;
    }

    internal void Release()
    {
        Object.Destroy(_mesh);
        Object.Destroy(_body);
        Object.Destroy(_face);
    }

    private static Mesh ReadMesh()
    {
        using Stream stream = typeof(FrosthavenWordmark).Assembly.GetManifestResourceStream(Outline)
            ?? throw new InvalidDataException("Missing FrosthavenVR outline.");
        using var input = new BinaryReader(stream);
        if (input.ReadUInt32() != Signature)
            throw new InvalidDataException("Unknown FrosthavenVR outline format.");
        int width = input.ReadUInt16(), height = input.ReadUInt16();
        int rearQuads = input.ReadUInt16(), wallQuads = input.ReadUInt16();
        if (width == 0 || height == 0 || width > 1024 || height > 512
            || rearQuads == 0 || wallQuads == 0 || rearQuads + wallQuads > MaxQuads
            || stream.Length - stream.Position != (rearQuads + wallQuads) * 8)
            throw new InvalidDataException("Invalid FrosthavenVR outline dimensions.");

        var vertices = new List<Vector3>((rearQuads + wallQuads + 1) * 4);
        var uv = new List<Vector2>(vertices.Capacity);
        var colors = new List<Color32>(vertices.Capacity);
        var body = new List<int>((rearQuads + wallQuads) * 6);
        var rear = new Color32(60, 96, 113, 255);
        for (int i = 0; i < rearQuads + wallQuads; i++)
        {
            Vector2 a = ReadPoint(input, width, height);
            Vector2 b = ReadPoint(input, width, height);
            if (i < rearQuads)
            {
                if (a.x >= b.x || a.y >= b.y)
                    throw new InvalidDataException("Invalid FrosthavenVR rear cap.");
                Quad(body, new Vector3(a.x, a.y, 1), new Vector3(b.x, a.y, 1),
                    new Vector3(b.x, b.y, 1), new Vector3(a.x, b.y, 1), rear, rear);
            }
            else
            {
                if ((a.x == b.x) == (a.y == b.y))
                    throw new InvalidDataException("Invalid FrosthavenVR wall edge.");
                Vector2 edge = (b - a).normalized;
                Vector2 outward = new(edge.y, -edge.x);
                float light = .75f + .25f * Vector2.Dot(outward, new Vector2(-.6f, .8f));
                Color32 rim = new((byte)(134 * light), (byte)(193 * light), (byte)(214 * light), 255);
                Color32 baseTint = new((byte)(62 * light), (byte)(101 * light), (byte)(122 * light), 255);
                Quad(body, new Vector3(a.x, a.y, 0), new Vector3(b.x, b.y, 0),
                    new Vector3(b.x, b.y, 1), new Vector3(a.x, a.y, 1), rim, baseTint);
            }
        }

        // The original image is the front cap, including subpixel alpha beyond
        // the opaque outline. The front remains translucent at the outer edge.
        var face = new List<int>(6);
        var white = new Color32(255, 255, 255, 255);
        Quad(face, new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0),
            new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0), white, white);

        var mesh = new Mesh { name = "FrosthavenVR extruded wordmark", hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(body, 0, false);
        mesh.SetTriangles(face, 1, false);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(markNoLongerReadable: true);
        return mesh;

        void Quad(List<int> target, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 near, Color32 far)
        {
            int start = vertices.Count;
            Add(a, near);
            Add(b, near);
            Add(c, far);
            Add(d, far);
            target.Add(start);
            target.Add(start + 1);
            target.Add(start + 2);
            target.Add(start);
            target.Add(start + 2);
            target.Add(start + 3);
        }

        void Add(Vector3 vertex, Color32 tint)
        {
            vertices.Add(vertex);
            colors.Add(tint);
            uv.Add(new Vector2(vertex.x + .5f, vertex.y + .5f));
        }
    }

    private static Vector2 ReadPoint(BinaryReader input, int width, int height)
    {
        int x = input.ReadUInt16(), y = input.ReadUInt16();
        if (x > width || y > height)
            throw new InvalidDataException("FrosthavenVR outline leaves its texture bounds.");
        return new Vector2(x / (float)width - .5f, y / (float)height - .5f);
    }
}
