using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal static class Probe { public static int Queries; }
    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x*b.x, a.y*b.y, a.z*b.z);
        public static Vector3 Divide(Vector3 a, Vector3 b) => new(a.x/b.x, a.y/b.y, a.z/b.z);
        public static float Distance(Vector3 a, Vector3 b) => System.Numerics.Vector3.Distance(new(a.x,a.y,a.z),new(b.x,b.y,b.z));
        public static bool operator ==(Vector3 a, Vector3 b) => Distance(a,b)<.000001f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a==b);
        public override bool Equals(object? o) => o is Vector3 v && this==v;
        public override int GetHashCode() => HashCode.Combine(x,y,z);
    }
    internal struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; }
        System.Numerics.Quaternion Q => new(x,y,z,w);
        static Quaternion From(System.Numerics.Quaternion q) => new(q.X,q.Y,q.Z,q.W);
        public static Quaternion identity => new(0,0,0,1);
        public static Quaternion Inverse(Quaternion q) => From(System.Numerics.Quaternion.Inverse(q.Q));
        public static Quaternion operator *(Quaternion a,Quaternion b) => From(a.Q*b.Q);
        public static Vector3 operator *(Quaternion q,Vector3 p)
        { var v=System.Numerics.Vector3.Transform(new(p.x,p.y,p.z),q.Q);return new(v.X,v.Y,v.Z); }
        public static float Angle(Quaternion a,Quaternion b) => 2*MathF.Acos(MathF.Min(1,MathF.Abs(System.Numerics.Quaternion.Dot(a.Q,b.Q))))*180/MathF.PI;
        public static bool operator ==(Quaternion a,Quaternion b) => a.Q==b.Q;
        public static bool operator !=(Quaternion a,Quaternion b) => !(a==b);
        public override bool Equals(object? o) => o is Quaternion q && this==q;
        public override int GetHashCode() => HashCode.Combine(x,y,z,w);
    }
    internal sealed class Transform
    {
        public Transform? parent;
        public Vector3 localPosition;
        public Quaternion localRotation=Quaternion.identity;
        public Vector3 localScale=Vector3.one;
        public Vector3 position { get { Probe.Queries++;return parent==null?localPosition:parent.TransformPoint(localPosition); } }
        public Quaternion rotation { get { Probe.Queries++;return parent==null?localRotation:parent.rotation*localRotation; } }
        public Vector3 lossyScale { get { Probe.Queries++;return parent==null?localScale:Vector3.Scale(parent.lossyScale,localScale); } }
        public Vector3 TransformPoint(Vector3 value) => position+rotation*Vector3.Scale(value,lossyScale);
        public Vector3 InverseTransformPoint(Vector3 value) => Vector3.Divide(Quaternion.Inverse(rotation)*(value-position),lossyScale);
    }
    internal sealed class GameObject { public readonly Transform transform=new(); }
    internal enum StereoTargetEyeMask { None,Left,Right,Both }
    internal sealed class Camera
    {
        public enum StereoscopicEye { Left,Right }
        public readonly Transform transform=new();
        public bool stereoEnabled;
        public StereoTargetEyeMask stereoTargetEye;
        public float nearClipPlane=.05f,farClipPlane=1000f;
        public Matrix4x4 projectionMatrix=Matrix4x4.identity,worldToCameraMatrix=Matrix4x4.identity;
        public Matrix4x4 LeftProjection=Matrix4x4.identity,RightProjection=Matrix4x4.identity;
        public Matrix4x4 LeftView=Matrix4x4.identity,RightView=Matrix4x4.identity;
        public Matrix4x4 GetStereoProjectionMatrix(StereoscopicEye eye) { Probe.Queries++;return eye==StereoscopicEye.Left?LeftProjection:RightProjection; }
        public Matrix4x4 GetStereoViewMatrix(StereoscopicEye eye) { Probe.Queries++;return eye==StereoscopicEye.Left?LeftView:RightView; }
        public static event Action<Camera>? onPreCull;
        public static int Subscribers => onPreCull?.GetInvocationList().Length??0;
        public static void Render(Camera camera) => onPreCull?.Invoke(camera);
    }
    internal struct Matrix4x4
    {
        float m0,m1,m2,m3,m4,m5,m6,m7,m8,m9,m10,m11v,m12v,m13,m14,m15;
        public static Matrix4x4 identity { get { var v=new Matrix4x4();for(int i=0;i<4;i++)v[i,i]=1;return v; } }
        public float this[int row,int column]
        {
            get => (row*4+column) switch {0=>m0,1=>m1,2=>m2,3=>m3,4=>m4,5=>m5,6=>m6,7=>m7,8=>m8,9=>m9,10=>m10,11=>m11v,12=>m12v,13=>m13,14=>m14,15=>m15,_=>throw new IndexOutOfRangeException()};
            set { switch(row*4+column){case 0:m0=value;break;case 1:m1=value;break;case 2:m2=value;break;case 3:m3=value;break;case 4:m4=value;break;case 5:m5=value;break;case 6:m6=value;break;case 7:m7=value;break;case 8:m8=value;break;case 9:m9=value;break;case 10:m10=value;break;case 11:m11v=value;break;case 12:m12v=value;break;case 13:m13=value;break;case 14:m14=value;break;case 15:m15=value;break;default:throw new IndexOutOfRangeException();} }
        }
        public float m00 {get=>this[0,0];set=>this[0,0]=value;} public float m11 {get=>this[1,1];set=>this[1,1]=value;}
        public float m02 {get=>this[0,2];set=>this[0,2]=value;} public float m12 {get=>this[1,2];set=>this[1,2]=value;}
        public float m32 {get=>this[3,2];set=>this[3,2]=value;} public float m33 {get=>this[3,3];set=>this[3,3]=value;}
    }
    internal static class Mathf { public static float Abs(float value)=>MathF.Abs(value); }
    internal static class Time
    { public static float Now; public static float realtimeSinceStartup {get {Probe.Queries++;return Now;}} public static int frameCount=>1; }
    internal static class Debug { public static readonly List<string> Messages=new();public static void Log(string message)=>Messages.Add(message); }
}
namespace UnityEngine.XR
{
    internal enum XRNode { Head,LeftHand,RightHand }
    internal readonly struct InputFeatureUsage<T> { public readonly string Name;public InputFeatureUsage(string name)=>Name=name; }
    internal static class CommonUsages
    { public static InputFeatureUsage<bool> isTracked=new("tracked");public static InputFeatureUsage<UnityEngine.Vector3> devicePosition=new("position");public static InputFeatureUsage<UnityEngine.Quaternion> deviceRotation=new("rotation"); }
    internal sealed class DeviceState
    { public bool Valid=true,Tracked=true,PositionAvailable=true,RotationAvailable=true;public UnityEngine.Vector3 Position;public UnityEngine.Quaternion Rotation; }
    internal readonly struct InputDevice
    {
        readonly DeviceState? state;
        public InputDevice(DeviceState? state)=>this.state=state;
        public bool isValid=>state!=null&&state.Valid;
        public bool TryGetFeatureValue(InputFeatureUsage<bool> usage,out bool value){value=state?.Tracked??false;return state!=null;}
        public bool TryGetFeatureValue(InputFeatureUsage<UnityEngine.Vector3> usage,out UnityEngine.Vector3 value){value=state?.Position??default;return state?.PositionAvailable??false;}
        public bool TryGetFeatureValue(InputFeatureUsage<UnityEngine.Quaternion> usage,out UnityEngine.Quaternion value){value=state?.Rotation??default;return state?.RotationAvailable??false;}
    }
    internal static class InputDevices
    {
        public static readonly Dictionary<XRNode,DeviceState> States=new();public static bool ThrowOnRead;
        public static InputDevice GetDeviceAtXRNode(XRNode node){UnityEngine.Probe.Queries++;if(ThrowOnRead)throw new InvalidOperationException("device probe");return new(States.GetValueOrDefault(node));}
        public static void Set(XRNode node,UnityEngine.Vector3 p,UnityEngine.Quaternion q)=>States[node]=new(){Position=p,Rotation=q};
    }
    internal static class XRSettings
    { public enum StereoRenderingMode{MultiPass,SinglePassInstanced}public static StereoRenderingMode stereoRenderingMode=StereoRenderingMode.SinglePassInstanced;public static float renderViewportScale,eyeTextureResolutionScale; }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform {public static bool Enabled;}
    internal enum VRLogLevel {Debug}
    internal static class VRLog {public static bool DebugEnabled;public static bool Wants(VRLogLevel level)=>DebugEnabled;}
}
namespace GloomhavenVR.Hands
{
    internal sealed class HandRig {public UnityEngine.Transform Root=new();}
    internal sealed class VRHand {public readonly UnityEngine.Transform transform=new();public bool IsTracked;public HandRig Rig=new();}
    internal static class VRHands {public static VRHand? Left,Right;}
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver
    {
        UnityEngine.Camera? _camera;UnityEngine.GameObject? _rigRoot;
        public VRRigDriver(UnityEngine.Camera camera,UnityEngine.GameObject rig){_camera=camera;_rigRoot=rig;}
        public void Start()=>StartQuestTrackingEvidence();public void Stop()=>StopQuestTrackingEvidence();public void Scene(string scene)=>NoteQuestTrackingScene(scene);
    }
}
