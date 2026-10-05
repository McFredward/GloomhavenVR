using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.Core {
 internal static class PerfConfig { internal static bool SharedUiWindowReadsOn=true; }
 internal static class VRLog {
  internal static int Errors;
  internal static void Info(string s,string m) {} internal static void Note(string s,string m) {}
  internal static void Warn(string s,string m) { Errors++; } internal static void Alert(string s,string m) {Errors++;}
 }
 internal static class PerfMonitor { internal readonly struct Token:IDisposable {public void Dispose() {}} internal static Token Scope(string s)=>new(); internal static void Count(string s,int n) {} }
}
namespace GloomhavenVR.Cards {
 internal static class CardFaceMipBake {
  internal static readonly Dictionary<Sprite,Sprite> Copies=new();
  internal static bool IsBakedSprite(Sprite s)=>Copies.ContainsValue(s);
  internal static Sprite? ReplacementFor(Sprite s) {
   if(Copies.TryGetValue(s,out Sprite copy))return copy;
   copy=Sprite.Create(s.texture,s.rect,s.pivot/s.rect.size,s.pixelsPerUnit); Copies[s]=copy;return copy;
  }
  internal static Texture2D? BakedTextureFor(Texture2D t)=>t;
  internal static string BudgetSummary=>"fixture";
  internal static void RestoreSprites(Component root) { foreach(Image image in root.GetComponentsInChildren<Image>(true))foreach(var pair in Copies)if(image.sprite==pair.Value)image.sprite=pair.Key; }
 }
}
namespace GloomhavenVR.WorldUI {
 internal sealed class Entry<T> { internal T Value; internal Entry(T v){Value=v;} }
 internal static class WorldUIConfig {internal static Entry<bool> PanelMipBake=new(true); internal static Entry<float> InitiativeDepthMaxSpreadPx=new(10);}
 internal sealed class ConvertedPanel {
  internal GameObject HostGo=null!;internal RectTransform Target=null!;internal bool IsAlive=true;
 }
 internal static partial class CanvasConversion {internal static readonly List<ConvertedPanel> ActivePanels=new(); private static List<ConvertedPanel> Active=>ActivePanels; private static readonly Dictionary<ConvertedPanel,HiddenWindowVeilState> HiddenWindowVeils=new();}
 internal sealed class HiddenWindowVeilState {
  public UiHierarchyInventory? Inventory;
  public HashSet<UIWindow>? SharedRegistry; public int SharedRegistryVersion;
  public readonly List<UIWindow> Windows=new(); public readonly HashSet<UIWindow> WindowSet=new();
 }
 internal sealed class InitiativeTrackSurface {
  private readonly Dictionary<Transform,float> _rawDepth=new(); private static InitiativeTrackSurface? _depthSource;
  private const float DepthEpsilonPixels=.5f;
  private struct DepthNode {public Transform T; public float Raw;}
  private readonly List<DepthNode> _depthScratch=new(); private UiHierarchyInventory? _depthInventory;
  private int _depthRevision=-1; private float _depthCap=float.NaN;
  internal void Tick()=>NormalizeDepth(); internal void Restore()=>RestoreDepth();
 }
}
internal sealed class InitiativeTrack {internal static InitiativeTrack Instance=null!;internal Transform initiativeTrackHolder=null!;}
internal sealed class UIWindow:MonoBehaviour {
 private static readonly HashSet<UIWindow> Windows=new();internal static HashSet<UIWindow> GetWindows()=>Windows;
 private void OnEnable()=>Windows.Add(this);private void OnDisable()=>Windows.Remove(this);
}
