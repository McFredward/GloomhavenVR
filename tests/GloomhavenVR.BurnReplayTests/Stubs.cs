using System.Collections;
using System.Runtime.CompilerServices;
using GloomhavenVR.Cards;
namespace HarmonyLib { static class AccessTools {internal static List<System.Reflection.MethodInfo> GetDeclaredMethods(Type t)=>t.GetMethods().ToList();}  [AttributeUsage(AttributeTargets.Class)] class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type type,string name){} } }
namespace ScenarioRuleLibrary {
 class CBaseCard { internal enum ECardPile { None,Hand,Round,Discarded,Lost,PermanentlyLost,Activated } }
 class CPlayerActor {internal CCharacterClass CharacterClass=new();}
 class CCharacterClass { internal readonly List<CAbilityCard> HandAbilityCards=new(),DiscardedAbilityCards=new(),RoundAbilityCards=new(),LostAbilityCards=new(),PermanentlyLostAbilityCards=new(),ActivatedCards=new(); }
 class CAbilityCard { internal string Name="fixture"; internal CBaseCard.ECardPile CurrentCardPile; }
}
class AbilityCardUI { internal FullAbilityCard fullAbilityCard=null!; internal ScenarioRuleLibrary.CAbilityCard AbilityCard=null!; internal ScenarioRuleLibrary.CPlayerActor? PlayerActor=null; internal void Init(){} }
class FullAbilityCard {
 internal ScenarioRuleLibrary.CAbilityCard? AbilityCard;
 internal ScenarioRuleLibrary.CPlayerActor? playerActor=null;
 internal AbilityCardUI? Parent=null;
 internal T? GetComponentInParent<T>() where T:class => Parent as T;
}
class CardEffects {
 internal int GetInstanceID()=>RuntimeHelpers.GetHashCode(this);
 internal bool HasEffect(FXTask task)=>toggledEffects.Contains(task);
 internal UnityEngine.UI.Image? _headerImage, _uiFxOverlay;
 internal enum FXTask { BurnCard,LostMode,DiscardMode }
 internal readonly HashSet<FXTask> toggledEffects=new();
 internal FullAbilityCard Full=new();
 internal float Paint;
 internal UnityEngine.UI.Image[]? imgComp;
 internal readonly UnityEngine.GameObject gameObject=new();
 internal object? coroutine;
 internal float[]? RawSteps;
 internal void WriteRaw(float value){Paint=value;if(imgComp!=null)foreach(var image in imgComp){image.material.SetFloat(UnityEngine.Shader.PropertyToID("_GreyOut"),value);image.material.SetFloat(UnityEngine.Shader.PropertyToID("_Flow"),value);image.material.SetFloat(UnityEngine.Shader.PropertyToID("_Dissolve"),value*.646f);}}
 internal int Starts,Resets,Discards;
 internal bool Disabled,ThrowOwner,ThrowReset;
 internal NativeBurnEnumerator? Live;
 internal T? GetComponent<T>() where T:class => ThrowOwner?throw new InvalidOperationException():Full as T;
 internal T? GetComponentInParent<T>() where T:class => Full as T;
 internal void ToggleEffect(bool active,FXTask effect) {
  if(!BurnArtwork.ToggleEffect_PreserveSpentStart_Patch.Prefix(this,active,effect))return;
  RestoreCard(); ToggleAdditiveEffect(active,effect);
 }
 internal void ToggleAdditiveEffect(bool active,FXTask effect){
  if(!BurnArtwork.ToggleAdditiveEffect_PreservePlayback_Patch.Prefix(this,active,effect))return;
  Live=null;
  if(active)toggledEffects.Add(effect);else toggledEffects.Remove(effect);
  if(effect==FXTask.DiscardMode){Discards++;return;}
  var iterator=BurnCardTimeline(active); iterator.MoveNext(); Live=(NativeBurnEnumerator)iterator;
 }
 internal void RestoreCard(){if(!BurnArtwork.RestoreCard_PreservePlayback_Patch.Prefix(this))return; if(ThrowReset)throw new InvalidOperationException("reset unavailable"); Resets++;Live=null;coroutine=null;WriteRaw(0);toggledEffects.Clear();}
 internal IEnumerator BurnCardTimeline(bool burnAnim,bool playOnDisabled=false){BurnArtwork.BurnCardTimeline_PreserveSpentStart_Patch.Prefix(this,burnAnim,ref playOnDisabled);IEnumerator result=Native(burnAnim,playOnDisabled);BurnArtwork.BurnCardTimeline_PreserveSpentStart_Patch.Postfix(this,burnAnim,ref result);return result;}
 IEnumerator Native(bool animate,bool playOnDisabled){if((Disabled||!gameObject.activeInHierarchy)&&!playOnDisabled){coroutine=null;yield break;}if(!animate){WriteRaw(1);coroutine=null;yield break;}Starts++;coroutine=new();if(RawSteps!=null){foreach(float raw in RawSteps){WriteRaw(raw);yield return this;}}else{while(Paint<1){WriteRaw(Paint+.1f);yield return this;}}coroutine=null;}
}
namespace GloomhavenVR.Core { static class VRLog { internal static bool WantsDebug; internal static readonly List<string> Lines=new(); internal static void Info(string scope,string text)=>Lines.Add(text); internal static void Warn(string scope,string text){} } }
namespace GloomhavenVR.Net { static class CardAppearanceSampler { internal static int Starts; internal static void ObserveNativeBurnStart(CardEffects fx)=>Starts++; } }
namespace GloomhavenVR.Cards {
 static class CardFace {internal static AbilityCardUI? Owner;internal static AbilityCardUI? OwnerOf(FullAbilityCard? full)=>Owner!=null&&ReferenceEquals(Owner.fullAbilityCard,full)?Owner:null;}
 partial class BurnArtwork {
 internal const float FinishedGreyOut=.98f;
 internal static float PaintProgress(CardEffects fx)=>fx.Paint;
 internal static bool SettledBurnPainted(CardEffects fx,out float grey){grey=fx.Paint;return grey>=.5f;}
 internal static void Forget(CardEffects fx){}
 internal static bool HandleIsABailedTimeline(CardEffects fx)=>false;
 internal static bool Latched(CardEffects fx)=>fx.toggledEffects.Contains(CardEffects.FXTask.BurnCard)||fx.toggledEffects.Contains(CardEffects.FXTask.LostMode);
 }
}

namespace UnityEngine {
 static class Time { internal static float unscaledTime=0; internal static int frameCount=0; }
 class WaitForEndOfFrame {}
 class GameObject { internal bool activeInHierarchy=true; }
 static class Shader { static readonly Dictionary<string,int> ids=new();internal static int PropertyToID(string name){if(!ids.TryGetValue(name,out int id))ids[name]=id=ids.Count;return id;} }
 class Material { internal int renderQueue=3000; internal int GetInstanceID()=>RuntimeHelpers.GetHashCode(this); internal bool HasProperty(string p)=>true; internal float GetFloat(string p)=>GetFloat(Shader.PropertyToID(p)); readonly Dictionary<int,float> values=new();internal bool HasProperty(int id)=>true;internal float GetFloat(int id)=>values.TryGetValue(id,out var value)?value:0;internal void SetFloat(int id,float value)=>values[id]=value; }
}
namespace UnityEngine.UI { class Graphic { internal UnityEngine.Material material=new(); internal CanvasRenderer canvasRenderer=new(); } class Image:Graphic {} class CanvasRenderer { internal UnityEngine.Material? Bound; internal int materialCount=>Bound==null?0:1; internal UnityEngine.Material? GetMaterial()=>Bound; } }

class CardsHandUI { internal ScenarioRuleLibrary.CAbilityCard? ShortRestedCard; }
class CardsHandManager { internal static CardsHandManager? Instance; internal CardsHandUI Hand=new(); internal CardsHandUI GetHand(ScenarioRuleLibrary.CPlayerActor owner)=>Hand; }

namespace Chronos { class Timekeeper { internal static Timekeeper? instance=null; internal Clock m_GlobalClock=new(); } class Clock { internal float time=0,deltaTime=0; } }
