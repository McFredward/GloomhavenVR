using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace GloomhavenVR.WorldUI
{
    // Allocation, the outer admission loop and the layer-pool lease are explicit
    // fixed resources. Real production admission/camera/projection/blur/resolve/
    // display kernels are linked by the runner and render the final eye pixels.
    internal static partial class PanelSupersample
    {
        private const string Scope="WorldUI";
        private static bool _mipBiasWriteWarned=false;
        private sealed class TargetLife {internal int TargetId=0,CreatedFrame=0,CompletedCaptures=0;}
        private sealed class Entry
        {
            internal ConvertedPanel Panel=null!;internal string Window="confirmation738";
            internal Camera Cam=null!;internal GameObject CamGo=null!,DisplayGo=null!;
            internal RectTransform DisplayRect=null!;internal Canvas DisplayCanvas=null!;internal RawImage DisplayImage=null!;
            internal RenderTexture Rt=null!,MipRt=null!;internal Rect Frame;internal int Layer=27;
            internal int ResolveSizeMismatch=0,MipCount=0,MotionResolves=0,StillResolves=0,GrabPassNeutralised=0;
            internal readonly TargetLife Life=new();
        }
        private static TargetLife LifeOf(Entry entry)=>entry.Life;
        private static RenderTexture DisplayTexture(Entry entry)=>entry.MipRt;
        private static bool IsMoving(Entry entry)=>true;

        internal sealed class ConfirmationCapture:IDisposable
        {
            private readonly Entry? _entry;
            private readonly List<(Transform Node,int Layer)> _layers=new();
            internal bool Active=>_entry!=null;
            internal int Captures {get;private set;}
            internal ConfirmationCapture(ConvertedPanel panel)
            {
                if(!Eligible(panel))return;
                var entry=new Entry {Panel=panel,Frame=panel.HostRect.rect};_entry=entry;
                entry.Rt=new RenderTexture(2048,1152,24,RenderTextureFormat.ARGB32)
                    {antiAliasing=1,useMipMap=false,filterMode=FilterMode.Bilinear};entry.Rt.Create();
                entry.MipRt=new RenderTexture(2048,1152,0,RenderTextureFormat.ARGB32)
                    {antiAliasing=1,useMipMap=true,autoGenerateMips=false,filterMode=FilterMode.Trilinear,anisoLevel=8};entry.MipRt.Create();
                BuildCamera(entry,panel);entry.Cam.enabled=false;
                foreach(Transform node in panel.HostRect.GetComponentsInChildren<Transform>(true))
                {
                    _layers.Add((node,node.gameObject.layer));node.gameObject.layer=entry.Layer;
                    NeutraliseGrabPassBlur(entry,node);
                }
                BuildDisplay(entry,panel);entry.DisplayRect.sizeDelta=entry.Frame.size;
                if(panel.HostGo.scene.name=="DontDestroyOnLoad")Object.DontDestroyOnLoad(entry.DisplayGo);
                else SceneManager.MoveGameObjectToScene(entry.DisplayGo,panel.HostGo.scene);
                SyncProjection(entry);SyncDisplayPose(entry);
            }
            internal void Draw()
            {
                if(_entry==null)return;
                SyncProjection(_entry);SyncDisplayPose(_entry);Canvas.ForceUpdateCanvases();
                _entry.Cam.Render();ResolveAndMip(_entry);Captures++;
            }
            public void Dispose()
            {
                foreach(var item in _layers)if(item.Node!=null)item.Node.gameObject.layer=item.Layer;
                if(_entry==null)return;
                Object.DestroyImmediate(_entry.DisplayGo);Object.DestroyImmediate(_entry.CamGo);
                _entry.Rt.Release();_entry.MipRt.Release();Object.DestroyImmediate(_entry.Rt);Object.DestroyImmediate(_entry.MipRt);
            }
        }
    }
}
