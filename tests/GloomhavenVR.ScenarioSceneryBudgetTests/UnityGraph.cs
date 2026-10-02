using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine
{
    internal class Object { }
    internal class Component : Object
    {
        internal GameObject gameObject=null!;
        internal Transform transform=>gameObject.transform;
        internal string name=>gameObject.name;
        internal T GetComponent<T>() where T:Component=>gameObject.Components.OfType<T>().FirstOrDefault()!;
        internal T[] GetComponents<T>() where T:Component=>gameObject.Components.OfType<T>().ToArray();
        internal T[] GetComponentsInChildren<T>(bool includeInactive=false) where T:Component
        {
            var all=new List<T>(); Walk(transform); return all.ToArray();
            void Walk(Transform node)
            {
                if(includeInactive||node.gameObject.activeInHierarchy)all.AddRange(node.GetComponents<T>());
                foreach(var child in node.Children)Walk(child);
            }
        }
        internal T? GetComponentInParent<T>() where T:Component
        {
            for(Transform? node=transform;node!=null;node=node.parent)
                if(node.gameObject.activeInHierarchy && node.GetComponent<T>() is T component)return component;
            return null;
        }
    }
    internal class MonoBehaviour:Component { }
    internal class Transform:Component
    {
        internal Transform? parent;
        internal readonly List<Transform> Children=new();
        internal void SetParent(Transform target,bool worldPositionStays=false){parent?.Children.Remove(this);parent=target;target.Children.Add(this);}
        internal bool IsChildOf(Transform root){for(Transform? n=this;n!=null;n=n.parent)if(ReferenceEquals(n,root))return true;return false;}
    }
    internal class GameObject:Object
    {
        internal string name;
        internal bool activeSelf=true;
        internal bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
        internal Transform transform;
        internal readonly List<Component> Components=new();
        internal SceneManagement.Scene scene=new();
        internal GameObject(string text){name=text;transform=AddComponent<Transform>();}
        internal T AddComponent<T>() where T:Component,new(){var value=new T {gameObject=this};Components.Add(value);return value;}
        internal T GetComponent<T>() where T:Component=>transform.GetComponent<T>();
    }
    internal struct Vector3
    {
        internal float x,y,z; internal Vector3(float a,float b,float c){x=a;y=b;z=c;}
        internal float sqrMagnitude=>x*x+y*y+z*z;
    }
    internal struct Bounds { internal Vector3 size; }
    internal class Mesh:Object { internal Bounds bounds; internal string name=""; }
    internal class MeshFilter:Component { internal Mesh? sharedMesh; }
    internal class Shader:Object { internal string name=""; }
    internal class Material:Object { internal Shader shader=null!; }
    internal class Renderer:Component
    {
        internal bool Forced; internal int Writes; internal bool enabled=true;
        internal bool forceRenderingOff {get=>Forced;set{Forced=value;Writes++;}}
    }
    internal class MeshRenderer:Renderer { internal Material[] sharedMaterials=Array.Empty<Material>(); }
    internal class SkinnedMeshRenderer:Renderer { }
    internal class Rigidbody:Component { }
    internal class Collider:Component { internal bool enabled=true; internal bool isTrigger; internal Rigidbody? attachedRigidbody; }
    internal class MeshCollider:Collider { internal Mesh? sharedMesh; }
    internal class Light:Component { }
    internal class Animator:Component { }
    internal class ParticleSystem:Component { }
    internal class Canvas:Component { }
}
namespace UnityEngine.SceneManagement
{
    internal class Scene
    {
        internal bool isLoaded=true;
        internal readonly List<UnityEngine.GameObject> Roots=new();
        internal bool IsValid()=>true;
        internal UnityEngine.GameObject[] GetRootGameObjects()=>Roots.ToArray();
    }
}
internal class ProceduralBase:UnityEngine.MonoBehaviour { }
internal class ProceduralMapTile:ProceduralBase { }
internal class ProceduralScenario:UnityEngine.MonoBehaviour { }
internal class ProceduralProp:ProceduralBase { }
internal class ProceduralWall:ProceduralBase { }
internal class ProceduralDoorway:UnityEngine.MonoBehaviour { }
internal class UnityGameEditorDoorProp:UnityEngine.MonoBehaviour { }
internal class UnityGameEditorObject:UnityEngine.MonoBehaviour { internal object? PropObject; }
internal class CInteractable:UnityEngine.MonoBehaviour { }
internal class CInteractableTile:CInteractable { }
internal class CInteractableActor:CInteractable { }
internal class ActorBehaviour:UnityEngine.MonoBehaviour { }
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldProps
    {
        internal static UnityEngine.Transform? Held;
        internal static bool OwnsRendererOf(UnityEngine.Transform leaf)=>Held!=null&&leaf.IsChildOf(Held);
    }
}
