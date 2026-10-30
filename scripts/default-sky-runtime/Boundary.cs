using System.Collections.Generic;
using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLayers { internal const int ModLayer=26;internal static void Apply(GameObject go)=>go.layer=ModLayer; }
    internal static class VRLog
    {
        internal static readonly List<string> Lines=new();
        internal static void Info(string area,string value)=>Lines.Add(value);
        internal static void Warn(string area,string value)=>Lines.Add(value);
    }
    internal static class MixedReality { internal static void Tick(){} }
    internal static class SkyAlternative { internal static float MinFarWorldUnits(float scale)=>0f; }
    internal static class VRCameraPolicy { }
}
namespace GloomhavenVR.Core.Events
{
    // Explicit port for the native menu/recenter lifecycle owned by MenuWorldContext.
    internal enum VRMode{Menu2D,TableIdle}
    internal static class VRModeStateMachine{internal static VRMode CurrentMode=VRMode.Menu2D;}
    internal static class MenuWorldContext
    {
        internal static bool IsReady=true;
        internal static bool AllowsSurroundings=>IsReady;
    }
}
namespace GloomhavenVR.Cards
{
    internal static class PlayTray { internal static Shader? OverlayShader()=>Shader.Find("GloomhavenVR/Overlay"); }
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver
    {
        internal static Transform? RigRoot;
        internal static Camera? HeadCamera;
        internal static float BaseWorldScale=1f;
        internal static VRRigDriver? Instance;
        private readonly Camera _camera;
        private readonly GameObject _rigRoot;
        private readonly float _baseFarClip,_buildScale;
        internal VRRigDriver(Camera head,Transform rig,float baseFar,float buildScale)
        {_camera=head;_rigRoot=rig.gameObject;_baseFarClip=baseFar;_buildScale=buildScale;Instance=this;}
        internal void TickClipProbe()=>TickClipPlanes();
    }
}

namespace GloomhavenVR.Hands
{ internal static class VRHands { internal static object? Left=null,Right=null; } }
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig
    {
        internal sealed class ValuePort { internal float Value=1.6f; }
        internal static readonly ValuePort ScreenDistance=new();
    }
    internal sealed partial class FlatScreen
    {
        private Material? _screenMaterial,_coverageMaterial;
        private Shader? _coverageShader;
        private GameObject? _worldMenuHost;
        private Shader? _worldMenuOriginalShader,_worldMenuBackdropShader;
        private int _worldMenuOriginalQueue=0;
        private bool _worldMenuBackdropDepth=false;
        private GameObject? _quad,_worldMenuRear;
        private MeshRenderer? _worldMenuRearRenderer;
        private Mesh? _worldMenuRearMesh;
        private float _worldMenuUnit=1f;
        private const float BackplaneGapMeters=.06f;
        private bool _coverageMaterialOpaque=false;
        private int _screenMaterialQueueDefault=-1;
        private bool _handsDiagLogged=false,_visible=true;
        private GameObject? _backQuad=null;
        private Renderer? _backRenderer=null,_quadRenderer;
        private RenderTexture? _rt;
        private static FlatScreen? _coverageOwner;
        private static bool IsPreMenuScene()=>false;
        private void LogHandsVisibilityDiagnostic(){}
        internal void TickBackdrop()=>TickBackdropDepth();
        internal string MaterialState=>_screenMaterial!.shader.name+"/q"+_screenMaterial.renderQueue;
        internal Material ProbeMaterial=>_screenMaterial!;
        internal Renderer? RearProbe=>_worldMenuRearRenderer;
        internal Mesh? RearMeshProbe=>_worldMenuRearMesh;
        internal void SplitProbe(GameObject original,GameObject background)
        {_backQuad=background;_backRenderer=background.GetComponent<Renderer>();_backRenderer.sharedMaterial=_screenMaterial;_quadRenderer=original.GetComponent<Renderer>();SyncWorldMenuBack();}
        internal void DestroyProbe(){RestoreWorldMenuBackdrop();if(_worldMenuRear!=null)UnityEngine.Object.Destroy(_worldMenuRear);if(_worldMenuRearMesh!=null)UnityEngine.Object.Destroy(_worldMenuRearMesh);if(_screenMaterial!=null)UnityEngine.Object.Destroy(_screenMaterial);_screenMaterial=null;_coverageOwner=null;}
    }
}
