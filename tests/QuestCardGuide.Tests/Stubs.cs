using System;
using System.Collections.Generic;
namespace UnityEngine { static class Time { internal static int frameCount=0; } }
namespace GloomhavenVR.Core { static class QuestStandalonePlatform { internal static bool Enabled=true,DebugLogging=true; internal static string[] CardGuideDiagnostics=Array.Empty<string>(); } }
class CardsHandUI { internal int Id=1;internal Character? PlayerActor=new();internal int GetInstanceID()=>Id; }
class Character { internal CharacterClass? CharacterClass=new(); }
class CharacterClass { internal List<int> HandAbilityCards=new(){1,2}; internal List<int> RoundAbilityCards=new(); }
class LevelMessagesUIHandler { internal static LevelMessagesUIHandler? s_Instance=new();internal Message? CurrentlyDisplayedBoxMessage=null;internal Message? CurrentlyDisplayedHelpTextMessage; }
class Message { internal string MessageName="TB_select"; }
namespace GloomhavenVR.Cards
{
 enum CardHandMode { CardsSelection,DiscardCard }
 static class CardsGameApi { internal static int NativeReads;internal static int PickWanted=2;internal static bool IsPickMode(CardHandMode mode)=>mode==CardHandMode.DiscardCard;internal static int PickCardsWanted()=>PickWanted;internal static CardHandMode CurrentMode=CardHandMode.CardsSelection;internal static CardHandMode Mode(CardsHandUI h){NativeReads++;return CurrentMode;}internal static int SelectionCardsStillWanted(CardsHandUI h){NativeReads++;return h.PlayerActor==null?0:Math.Max(0,2-h.PlayerActor.CharacterClass!.RoundAbilityCards.Count);} }
 class VRCard { internal bool IsHeld;internal int Id=2; internal int GetInstanceID()=>Id; }
 class PlayTray
 {
  internal int NativeReads,DrawReads;internal bool Throw;internal int Mask=3,Owner=9; internal string? LongFacts;
  internal VRCard?[] Occupants=new VRCard?[2];internal VRCard? Occupant(int slot){NativeReads++;return Occupants[slot];}
  internal int QuestGuideOwnerId=>Owner;internal int WantedSlotMask=>Mask;
  internal string QuestGuideDrawState(int slot){DrawReads++;if(Throw)throw new InvalidOperationException();return LongFacts??("active="+((Mask&(1<<slot))!=0)+",renderer=True,mesh=4/1,color=True,material="+(slot+10)+",shader=GloomhavenVR/Overlay,state=0/4/0/1/1,det=1.00");}
 }
}
