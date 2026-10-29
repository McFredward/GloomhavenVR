using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    // Independent 16-sample area integration of the original native world
    // geometry, fonts and images. No production sampling kernel is reused.
    private static Texture2D SupersampledReference742(Scene scene,string output,int samples)
    {
        RenderTexture original=scene.Head.targetTexture;
        var target=new RenderTexture(original.width*samples,original.height*samples,24,RenderTextureFormat.ARGB32);
        target.Create();Texture2D? high=null;
        try
        {
            scene.Head.targetTexture=target;scene.Head.Render();
            high=Read(target,Path.ChangeExtension(output,"high.png"));
            Color[] pixels=high.GetPixels();var result=new Texture2D(original.width,original.height,TextureFormat.RGBA32,false);
            var reduced=new Color[original.width*original.height];
            for(int y=0;y<original.height;y++)for(int x=0;x<original.width;x++)
            {
                Color sum=Color.clear;
                for(int sy=0;sy<samples;sy++)for(int sx=0;sx<samples;sx++)
                    sum+=pixels[(y*samples+sy)*target.width+x*samples+sx];
                reduced[y*original.width+x]=sum/(samples*samples);
            }
            result.SetPixels(reduced);result.Apply();File.WriteAllBytes(output,result.EncodeToPNG());return result;
        }
        finally
        {
            scene.Head.targetTexture=original;
            if(high!=null)Object.DestroyImmediate(high);
            target.Release();Object.DestroyImmediate(target);
        }
    }

    private sealed class AliasingProof742
    {
        private double _filteredUi,_rawUi,_filteredModel,_rawModel;
        private long _uiPixels,_modelPixels;
        internal void Add(Texture2D filtered,Texture2D raw,Texture2D reference)
        {
            Color32[] actual=filtered.GetPixels32(),old=raw.GetPixels32(),expected=reference.GetPixels32();
            double aa=0,ab=0,ra=0,rb=0;
            for(int p=0;p<actual.Length;p++)
            {
                double a=Math.Max(0,actual[p].g-actual[p].r),r=Math.Max(0,old[p].g-old[p].r),e=Math.Max(0,expected[p].g-expected[p].r);
                aa+=a*a;ab+=a*e;ra+=r*r;rb+=r*e;
            }
            double actualScale=ab/Math.Max(1,aa),oldScale=rb/Math.Max(1,ra);
            int width=filtered.width,height=filtered.height;
            for(int y=1;y<height-1;y++)for(int x=1;x<width-1;x++)
            {
                int p=y*width+x;double u=expected[p].r*expected[p].a/255.0;
                double m=Math.Max(0,expected[p].g-expected[p].r);
                double loUi=255,hiUi=0,loModel=255,hiModel=0;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    Color32 n=expected[p+dy*width+dx];double nu=n.r*n.a/255.0,nm=Math.Max(0,n.g-n.r);
                    loUi=Math.Min(loUi,nu);hiUi=Math.Max(hiUi,nu);
                    loModel=Math.Min(loModel,nm);hiModel=Math.Max(hiModel,nm);
                }
                if(hiUi-loUi>25)
                {
                    _filteredUi+=Math.Pow(actual[p].r-u,2);_rawUi+=Math.Pow(old[p].r*old[p].a/255.0-u,2);_uiPixels++;
                }
                if(hiModel-loModel>25)
                {
                    _filteredModel+=Math.Pow(Math.Max(0,actual[p].g-actual[p].r)*actualScale-m,2);
                    _rawModel+=Math.Pow(Math.Max(0,old[p].g-old[p].r)*oldScale-m,2);_modelPixels++;
                }
            }
        }
        internal void Complete(string output)
        {
            File.WriteAllText(output,"UI filtered="+_filteredUi+";unfiltered="+_rawUi+";pixels="+_uiPixels
                +"\nFigure filtered="+_filteredModel+";unfiltered="+_rawModel+";pixels="+_modelPixels+"\n");
            Check(_uiPixels>1000&&_modelPixels>100,"independent native AA reference contains real UI and model edges throughout motion");
            Check(_filteredUi<_rawUi*.97,"actual final head UI edges reduce alias error against independent supersampled native pixels");
            Check(_filteredModel<_rawModel*.97,"actual final head figure edges reduce alias error against independent supersampled native pixels");
        }
    }
}
