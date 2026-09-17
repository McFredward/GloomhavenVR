using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>MR geometry reads native text vertices and image drawing dimensions, not text layout
/// boxes. Unity 2021 has no CanvasRenderer.GetMesh: never regenerate a native Graphic or invoke
/// OnPopulateMesh to obtain its picture. Missing text meshes can be retried by the normal sampler.</summary>
internal static class MrBackingPaintedBounds
{
    private static readonly List<Vector3> Vertices = new(128);
    private static readonly List<Color32> Colors = new(128);

    internal static bool TryMeasure(RectTransform host, Graphic? graphic, out Rect bounds)
    {
        bounds = default;
        if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
            return false;
        Canvas? canvas = graphic.canvas;
        CanvasRenderer? renderer = graphic.canvasRenderer;
        if (canvas == null || !canvas.isActiveAndEnabled || renderer == null || renderer.cull)
            return false;
        if (graphic.color.a * renderer.GetInheritedAlpha() < CanvasConversion.FitMinAlpha)
            return false;
        Mask? mask = graphic.GetComponent<Mask>();
        if (mask != null && mask.enabled && !mask.showMaskGraphic)
            return false; // the caller retains stencil clipping and still visits its children

        Matrix4x4 toHost = host.worldToLocalMatrix * graphic.transform.localToWorldMatrix;
        if (graphic is TMP_Text text)
            return MeshBounds(text.mesh, toHost, out bounds);
        if (graphic is TMP_SubMeshUI subMesh)
            return MeshBounds(subMesh.mesh, toHost, out bounds);
        if (graphic is Text legacy)
            return LegacyTextBounds(legacy, toHost, out bounds);

        Rect draw = graphic.GetPixelAdjustedRect();
        if (graphic is Image image && !ImageRect(image, ref draw))
            return false;
        // RawImage paints its complete adjusted rectangle. Sliced/tiled images and unknown
        // custom Graphics retain a conservative own rectangle; never substitute the panel host.
        return RectBounds(draw, toHost, out bounds);
    }

    private static bool MeshBounds(Mesh? mesh, Matrix4x4 toHost, out Rect bounds)
    {
        bounds = default;
        if (mesh == null)
            return false;
        Vertices.Clear(); Colors.Clear();
        mesh.GetVertices(Vertices); mesh.GetColors(Colors);
        bool colorsPresent = Colors.Count == Vertices.Count;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (int i = 0; i < Vertices.Count; i++)
        {
            // TMP keeps unused transparent vertices; they are not painted origin points.
            if (colorsPresent && Colors[i].a == 0)
                continue;
            if (!Include(toHost.MultiplyPoint3x4(Vertices[i]), ref minX, ref minY, ref maxX, ref maxY))
                return false;
        }
        return Finish(minX,minY,maxX,maxY,out bounds);
    }

    private static bool LegacyTextBounds(Text text, Matrix4x4 toHost, out Rect bounds)
    {
        bounds = default;
        IList<UIVertex> vertices = text.cachedTextGenerator.verts;
        if (vertices.Count == 0 || text.pixelsPerUnit <= 0f)
            return false;
        float units = 1f / text.pixelsPerUnit;
        Vector2 first = new(vertices[0].position.x * units, vertices[0].position.y * units);
        Vector2 adjusted = text.PixelAdjustPoint(first);
        float offsetX = adjusted.x - first.x, offsetY = adjusted.y - first.y;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        // The bundled Unity 2021.3.5 uGUI Text.OnPopulateMesh uses every complete quad;
        // older versions subtracted four sentinel vertices, which would drop a real final glyph here.
        int count = vertices.Count - vertices.Count % 4;
        for (int quad = 0; quad < count; quad += 4)
        {
            bool hasArea = false;
            Vector3 a = vertices[quad].position;
            for (int corner = 1; corner < 3; corner++)
            {
                Vector3 b = vertices[quad+corner].position, c = vertices[quad+corner+1].position;
                if ((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x) != 0f) hasArea = true;
            }
            if (!hasArea) continue; // a collapsed generator quad paints no pixels at its origin
            for (int i = quad; i < quad+4; i++)
            {
                if (vertices[i].color.a == 0) continue;
                Vector3 p = vertices[i].position;
                if (!Include(toHost.MultiplyPoint3x4(new Vector3(p.x*units+offsetX,p.y*units+offsetY,p.z*units)),
                        ref minX,ref minY,ref maxX,ref maxY)) return false;
            }
        }
        return Finish(minX,minY,maxX,maxY,out bounds);
    }

    private static bool ImageRect(Image image, ref Rect draw)
    {
        Sprite? sprite = image.overrideSprite;
        // With no active sprite uGUI calls Graphic.OnPopulateMesh: its own adjusted quad,
        // irrespective of Image.type or fillAmount. This also covers original solid UI panels.
        if (sprite == null) return true;
        bool filled = image.type == Image.Type.Filled;
        if (filled && image.fillAmount < 0.001f) return false;
        // uGUI Image.GetDrawingDimensions: preserve the active sprite's aspect and packed padding.
        // Reference: Unity-Technologies/uGUI, Runtime/UGUI/UI/Core/Image.cs. No native setters run.
        if ((image.type == Image.Type.Simple || filled) && sprite != null)
        {
            float width = sprite.rect.width, height = sprite.rect.height;
            if (width > 0f && height > 0f)
            {
                if (image.preserveAspect && draw.width > 0f && draw.height > 0f)
                {
                    float aspect = width/height;
                    if (aspect > draw.width/draw.height)
                    {
                        float old = draw.height;
                        draw.height = draw.width/aspect;
                        draw.y += (old-draw.height)*image.rectTransform.pivot.y;
                    }
                    else
                    {
                        float old = draw.width;
                        draw.width = draw.height*aspect;
                        draw.x += (old-draw.width)*image.rectTransform.pivot.x;
                    }
                }
                Vector4 pad = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
                float pixelWidth = Mathf.Max(1,Mathf.RoundToInt(width));
                float pixelHeight = Mathf.Max(1,Mathf.RoundToInt(height));
                draw = Rect.MinMaxRect(draw.xMin+draw.width*pad.x/pixelWidth,
                    draw.yMin+draw.height*pad.y/pixelHeight,
                    draw.xMax-draw.width*pad.z/pixelWidth,draw.yMax-draw.height*pad.w/pixelHeight);
            }
        }
        if (filled)
        {
            if (image.fillMethod == Image.FillMethod.Horizontal)
            {
                float length = draw.width*image.fillAmount;
                draw = image.fillOrigin == 1 ? Rect.MinMaxRect(draw.xMax-length,draw.yMin,draw.xMax,draw.yMax)
                    : Rect.MinMaxRect(draw.xMin,draw.yMin,draw.xMin+length,draw.yMax);
            }
            else if (image.fillMethod == Image.FillMethod.Vertical)
            {
                float length = draw.height*image.fillAmount;
                draw = image.fillOrigin == 1 ? Rect.MinMaxRect(draw.xMin,draw.yMax-length,draw.xMax,draw.yMax)
                    : Rect.MinMaxRect(draw.xMin,draw.yMin,draw.xMax,draw.yMin+length);
            }
            // Partial radial fills conservatively keep their own drawing rectangle.
        }
        return true;
    }

    private static bool RectBounds(Rect draw, Matrix4x4 toHost, out Rect bounds)
    {
        bounds=default;
        if(draw.width<=0f || draw.height<=0f) return false;
        float minX=float.PositiveInfinity,minY=float.PositiveInfinity;
        float maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
        for(int i=0;i<4;i++)
            if(!Include(toHost.MultiplyPoint3x4(new Vector3((i&1)==0 ? draw.xMin : draw.xMax,
                (i&2)==0 ? draw.yMin : draw.yMax,0)),ref minX,ref minY,ref maxX,ref maxY)) return false;
        return Finish(minX,minY,maxX,maxY,out bounds);
    }

    private static bool Include(Vector3 point, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        if(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsInfinity(point.x)||float.IsInfinity(point.y)) return false;
        minX=Mathf.Min(minX,point.x);minY=Mathf.Min(minY,point.y);
        maxX=Mathf.Max(maxX,point.x);maxY=Mathf.Max(maxY,point.y);
        return true;
    }

    private static bool Finish(float minX,float minY,float maxX,float maxY,out Rect bounds)
    {
        bounds=default;
        if(maxX<=minX || maxY<=minY) return false;
        bounds=Rect.MinMaxRect(minX,minY,maxX,maxY);
        return true;
    }
}
