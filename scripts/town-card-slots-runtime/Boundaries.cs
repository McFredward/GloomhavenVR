global using GloomhavenVR.Net;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class SlotsClock { internal static float Now; }
internal sealed class UIEnhanceCardPoint : MonoBehaviour { }
internal sealed class UIEnhanceCardSlot : MonoBehaviour { internal readonly List<UIEnhanceCardPoint> assignedPoints=new(); }
internal sealed class NativePointController : MonoBehaviour
{
    internal static int Enabled;
    private void OnEnable()=>Enabled++;
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class TownServiceEnhancementHandoff
    {
        internal object? Card;
        internal UIEnhanceCardSlot? NativeSlot;
        internal Transform Seat=null!;
    }
}
namespace GloomhavenVR.Net
{
    // Fitting/rasterization/transport are tested by the existing native mirror suites.
    // This boundary records exact source ownership and cadence, implements a tiny
    // presentation-only image copy, and uses the REAL production Neutralize below.
    // It cannot establish pixel parity of the full RemoteWidgetMirror renderer.
    internal sealed partial class RemoteWidgetMirror
    {
        internal enum LayoutOwner { Source, CloneAtBoardOwnersWidth }
        internal static readonly List<RemoteWidgetMirror> Live=new();
        internal static int Creates,Destroys;
        internal int Refreshes,Ticks;
        private readonly Transform _mount;
        private Transform? _source,_clone;
        private int _nodes;
        private readonly Dictionary<Transform,Transform> _pairs=new();
        internal RemoteWidgetMirror(string name,Transform mount,float width,float height,Vector2 grow,bool mrBacking)
        {if(mrBacking)throw new Exception("slot icons must not create backing geometry");_mount=mount;Live.Add(this);Creates++;}
        internal Transform? CloneOf(Transform source)=>_pairs.TryGetValue(source,out var copy)?copy:null;
        internal bool Refresh(Transform source)
        {
            Refreshes++;
            if(source==null)return false;
            var nodes=source.GetComponentsInChildren<Transform>(true);
            if(source!=_source||nodes.Length!=_nodes)
            {
                if(_clone!=null)UnityEngine.Object.DestroyImmediate(_clone.gameObject);
                var nursery=new GameObject("Inactive point nursery");nursery.SetActive(false);nursery.transform.SetParent(_mount,false);
                _clone=UnityEngine.Object.Instantiate(source.gameObject,nursery.transform).transform;
                Neutralize(_clone.gameObject,LayoutOwner.Source,null);
                _clone.SetParent(_mount,false);UnityEngine.Object.DestroyImmediate(nursery);
                _source=source;_nodes=nodes.Length;_pairs.Clear();
                var copies=_clone.GetComponentsInChildren<Transform>(true);
                for(int i=0;i<nodes.Length;i++)_pairs[nodes[i]]=copies[i];
            }
            TickLive();return true;
        }
        internal void TickLive()
        {
            Ticks++;
            foreach(var pair in _pairs)
            {
                if(pair.Key==null||pair.Value==null)continue;
                var source=pair.Key.GetComponent<Image>();var clone=pair.Value.GetComponent<Image>();
                if(source!=null&&clone!=null){clone.enabled=source.enabled;clone.color=source.color;}
                pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
            }
        }
        internal void Destroy()
        {
            if(!Live.Remove(this))return;
            Destroys++;_pairs.Clear();if(_clone!=null)UnityEngine.Object.DestroyImmediate(_clone.gameObject);
        }
    }
}
