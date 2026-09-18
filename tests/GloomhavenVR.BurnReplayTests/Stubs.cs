using System.Collections;
using System.Runtime.CompilerServices;
using GloomhavenVR.Cards;
namespace HarmonyLib { static class AccessTools {internal static List<System.Reflection.MethodInfo> GetDeclaredMethods(Type t)=>t.GetMethods().ToList();}  [AttributeUsage(AttributeTargets.Class)] class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type type,string name){} } }
namespace ScenarioRuleLibrary {
 class CBaseCard { internal enum ECardPile { None,Hand,Round,Discarded,Lost,PermanentlyLost,Activated } }
 class CPlayerActor {internal CCharacterClass CharacterClass=new();}
 class CCharacterClass { internal readonly List<CAbilityCard> HandAbilityCards=new(),DiscardedAbilityCards=new(),RoundAbilityCards=new(),LostAbilityCards=new(),PermanentlyLostAbilityCards=new(),ActivatedCards=new(); }
 class CAbilityCard { internal CBaseCard.ECardPile CurrentCardPile; }
}
class AbilityCardUI { internal FullAbilityCard fullAbilityCard=null!; internal ScenarioRuleLibrary.CAbilityCard AbilityCard=null!; internal ScenarioRuleLibrary.CPlayerActor? PlayerActor=null; internal void Init(){} }
class FullAbilityCard {
 internal ScenarioRuleLibrary.CAbilityCard? AbilityCard;
 internal ScenarioRuleLibrary.CPlayerActor? playerActor=null;
 internal AbilityCardUI? Parent=null;
 internal T? GetComponentInParent<T>() where T:class => Parent as T;
}
class CardEffects {
 internal enum FXTask { BurnCard,LostMode,DiscardMode }
 internal readonly HashSet<FXTask> toggledEffects=new();
 internal FullAbilityCard Full=new();
 internal float Paint;
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
 internal void RestoreCard(){if(!BurnArtwork.RestoreCard_PreservePlayback_Patch.Prefix(this))return; if(ThrowReset)throw new InvalidOperationException("reset unavailable"); Resets++;Live=null;Paint=0;toggledEffects.Clear();}
 internal IEnumerator BurnCardTimeline(bool burnAnim,bool playOnDisabled=false){IEnumerator result=Native(burnAnim);BurnArtwork.BurnCardTimeline_PreserveSpentStart_Patch.Postfix(this,burnAnim,ref result);return result;}
 IEnumerator Native(bool animate){if(Disabled)yield break;if(!animate){Paint=1;yield break;}Starts++;while(Paint<1){Paint+=.1f;yield return this;}}
}
namespace GloomhavenVR.Core { static class VRLog { internal static void Warn(string scope,string text){} } }
namespace GloomhavenVR.Net { static class CardAppearanceSampler { internal static int Starts; internal static void ObserveNativeBurnStart(CardEffects fx)=>Starts++; } }
namespace GloomhavenVR.Cards {
 static class CardFace {internal static AbilityCardUI? Owner;internal static AbilityCardUI? OwnerOf(FullAbilityCard? full)=>Owner!=null&&ReferenceEquals(Owner.fullAbilityCard,full)?Owner:null;}
 partial class BurnArtwork {
 internal sealed class Start {internal bool HasFloor=true;}
 internal static readonly ConditionalWeakTable<CardEffects,Start> BurnStarts=new();
 internal static readonly ConditionalWeakTable<CardEffects,NativeBurnEnumerator> BurnTimelines=new();
 internal const float FinishedGreyOut=.98f;
 internal static bool SettledBurnPainted(CardEffects fx,out float grey){grey=fx.Paint;return grey>=.5f;}
 internal static void Forget(CardEffects fx){}
 internal static void RestoreNativeBurnChannels(CardEffects fx){}
 internal static void ClearRecoveredSpentBurnStart(CardEffects fx,FullAbilityCard? full){}
 internal static void PreserveSpentBurnStart(CardEffects fx,ScenarioRuleLibrary.CAbilityCard? card,object? owner,bool beforeReset,bool nativeStep=false){}
 }
}

namespace UnityEngine { class WaitForEndOfFrame {} }

class CardsHandUI { internal ScenarioRuleLibrary.CAbilityCard? ShortRestedCard; }
class CardsHandManager { internal static CardsHandManager? Instance; internal CardsHandUI Hand=new(); internal CardsHandUI GetHand(ScenarioRuleLibrary.CPlayerActor owner)=>Hand; }
