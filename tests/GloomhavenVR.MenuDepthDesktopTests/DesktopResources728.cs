using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator DesktopResources728(string output,Action<int> complete)
    {
        _count=0;const long MiB=1024L*1024;
        var d=new RenderTextureDescriptor(1024,1024){graphicsFormat=GraphicsFormat.R8G8B8A8_UNorm,depthStencilFormat=GraphicsFormat.D24_UNorm_S8_UInt,msaaSamples=1,volumeDepth=1};
        Check(MenuDepthDesktopResources.ExtraBytes(d,1024,1024,false)==8*MiB,"only the actual extra right eye is charged beside the existing native UI basis");
        Check(MenuDepthDesktopResources.ExtraBytes(d,1024,1024,true)==16*MiB,"an expanded pair includes both real colour and depth allocations");
        d.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;
        Check(MenuDepthDesktopResources.ExtraBytes(d,1024,1024,true)==24*MiB,"native HDR format bytes are accounted rather than assuming four-byte colour");
        d.graphicsFormat=GraphicsFormat.R8G8B8A8_UNorm;d.msaaSamples=4;
        Check(MenuDepthDesktopResources.ExtraBytes(d,1024,1024,true)==72*MiB,"MSAA includes both sampled resolves and multisampled colour/depth storage");
        d.msaaSamples=1;d.useMipMap=true;d.mipCount=0;d.depthStencilFormat=GraphicsFormat.D16_UNorm;
        Check(MenuDepthDesktopResources.ExtraBytes(d,256,128,false)==240300,"mip colour levels and actual sixteen-bit depth use their real dimensions");
        d.useMipMap=false;d.depthStencilFormat=GraphicsFormat.D24_UNorm_S8_UInt;
        Check(MenuDepthDesktopResources.Fits(d,10240,1024,true),"a complete pair exactly at the fixed 160MiB ceiling retains native density");
        Check(!MenuDepthDesktopResources.Fits(d,16384,8192,true),"extreme native eye allocations are rejected before creating either additional target");
        Check(!MenuDepthDesktopResources.Fits(d,int.MaxValue,int.MaxValue,true),"overflowing descriptor estimates cannot bypass memory admission");
        Rect current=new Rect(-.5f,0,2,1),small=new Rect(-.45f,0,1.9f,1);float since=-1;
        Check(MenuDepthDesktopResources.RetainFrame(current,small,0,ref since)==current,"small bounds initially reuse the current native-density capture");
        Check(MenuDepthDesktopResources.RetainFrame(current,small,3,ref since)==current,"small silhouette jitter never reallocates even after the old two-second timeout");
        Rect large=new Rect(0,0,1,1);since=-1;
        Check(MenuDepthDesktopResources.RetainFrame(current,large,5,ref since)==current,"a substantial shrink first waits for its sustained interval");
        Check(MenuDepthDesktopResources.RetainFrame(current,large,6.99f,ref since)==current,"substantial bounds never shrink early");
        Check(MenuDepthDesktopResources.RetainFrame(current,large,7,ref since)==large,"a sustained substantial shrink retires excess targets after two seconds");
        Rect moved=new Rect(-.7f,0,2,1);since=0;Rect grown=MenuDepthDesktopResources.RetainFrame(current,moved,10,ref since);
        Check(grown.xMin<=moved.xMin&&grown.xMax>=current.xMax&&since<0,"growth and recentering conservatively retain every current foreground pixel immediately");
        File.WriteAllText(Path.Combine(output,"budget.txt"),"cap="+MenuDepthDesktopResources.MaxOwnedBytes+"; extremePair="+MenuDepthDesktopResources.ExtraBytes(d,16384,8192,true)+"\n");
        complete(_count);yield break;
    }
}
