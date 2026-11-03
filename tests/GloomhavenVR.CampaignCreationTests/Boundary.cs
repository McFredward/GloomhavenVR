#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Compat;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;

namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type,string method){} }
 [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
 [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
}
namespace GloomhavenVR.Core
{
 internal static class VRSession { internal static bool IsRunning=true; }
 internal static class VRLog { internal static readonly List<string> Warnings=new(); internal static void Warn(string scope,string message)=>Warnings.Add(message); }
}
namespace FFSNet
{
 public sealed class NetworkPlayer
 {
  public int PlayerID; public CreatingCharacter CreatingCharacter { get; set; }
  public bool IsCreatingCharacter=>CreatingCharacter!=null;
  public void ToggleCharacterCreationScreen(bool value){if(!value)CreatingCharacter=null;}
 }
 public sealed class CreatingCharacter { public string CharacterName; public CreatingCharacter(int id,string name)=>CharacterName=name; }
 public sealed class GameAction { public object SupplementaryDataToken; public int SupplementaryDataIDMax; }
}
public static class FFSNetwork { public static bool IsOnline=true; public static bool IsUGCEnabled=true; }
namespace FFSNet
{
public static class PlayerRegistry
{
 public static FFSNet.NetworkPlayer MyPlayer;
 public static List<FFSNet.NetworkPlayer> AllPlayers=new();
}
}
namespace MapRuleLibrary.Adventure
{
 public static class AdventureState { public static MapState MapState=new(); }
 public sealed class MapState
 {
  public bool IsCampaign=true; public Party MapParty=new(); public Headquarters HeadquartersState=new();
  public List<string> DebugNextPersonalQuests=new(); public int Rolls; public int GoldGifts; public int ItemUnlocks;
 }
 public sealed class Headquarters { public int CurrentStartingPerksAmount; }
 public sealed class Party
 {
  public List<CMapCharacter> CheckCharacters=new(); public List<CPlayerRecords> RetiredCharacterRecords=new();
  public void AddCharacterToCharactersList(CMapCharacter value)=>CheckCharacters.Add(value);
  public void UnlockNewProsperityItems()=>AdventureState.MapState.ItemUnlocks++;
 }
 public sealed class CPlayerRecords { public string CharacterName; }
}
namespace MapRuleLibrary.Party
{
 public sealed class CPersonalQuestState { public string ID; public CPersonalQuestState(string id)=>ID=id; }
 public sealed class CMapCharacter
 {
  public string CharacterID; public string CharacterName; public string DisplayCharacterName;
  public CPersonalQuestState PersonalQuest; public List<CPersonalQuestState> PossiblePersonalQuests=new();
  public CreatorData CharacterYMLData=new(); public int Gold;
  public CMapCharacter(string classId,string name,int perks){CharacterID=classId;CharacterName=name;}
  public void DrawPossiblePersonalQuests()
  {
   PossiblePersonalQuests=new(){new("Q"+(++AdventureState.MapState.Rolls)),new("Q"+(++AdventureState.MapState.Rolls))};
  }
  public void GiveCreateCharacterGoldAndItems(){Gold+=30;AdventureState.MapState.GoldGifts++;}
  public void AssignPersonalQuest(CPersonalQuestState quest)=>PersonalQuest=quest;
 }
}
public sealed class CreatorData { public string LocKey="class"; }
public interface ICharacterCreatorClass { string ID{get;} }
public sealed class CreatorClass : ICharacterCreatorClass { public string ID{get;} public CreatorClass(string id)=>ID=id; }
public sealed class CampaignCharacterData
{
 public string CharacterID;public string CharacterName;
 public CampaignCharacterData(string id,string name){CharacterID=id;CharacterName=name;}
}
public sealed class CampaignPersonalQuestData
{
 public string CharacterID; public string CharacterName; public string PersonalQuestID;
 public int SlotIndex; public int PlayerID; public bool SelectCharacter;
}
public static class LocalizationManager { public static string GetTranslation(string key)=>key; }
public static class UnityGameEditorRuntime { public static void ErrorHandlingUnloadSceneAndLoadMainMenu(){} }
public static class Debug { public static List<string> Errors=new(); public static void LogError(object error)=>Errors.Add(error.ToString()); }
public sealed class SceneController
{
 public static SceneController Instance=new();public ErrorBox GlobalErrorMessage=new();
 public sealed class ErrorBox { public int Count;public void ShowMessageDefaultTitle(string key,string button,string stack,Action callback,string reason)=>Count++; }
}
public sealed class Singleton<T> where T:new() { public static T Instance=new(); }
public sealed class UIMapMultiplayerController { public void RefreshWaitingNotifications(){} }
public partial class CharacterCreatorService
{
 public static bool UseGuard=true;
 public CMapCharacter Create(string classId,string characterName)=>CreateCharacter(classId,characterName);
 public static CMapCharacter CreateCharacter(string classId,string characterName)
 {
  CMapCharacter result=null;
  if(!UseGuard||NativeCampaignCreationGuard.CreateSeam.Prefix(classId,characterName,ref result))
   result=CreateCharacterOriginal(classId,characterName);
  if(UseGuard)NativeCampaignCreationGuard.CreateSeam.Postfix(classId,characterName,result);
  return result;
 }
}
public partial class UICharacterCreatorNameStep
{
 public UICharacterCreatorNameStep Show(ICharacterCreatorClass character,string currentName=null)
 {
  if(CharacterCreatorService.UseGuard)NativeCampaignCreationGuard.NameClassSeam.Prefix(this,character,currentName);
  return this;
 }
 public bool IsNameRepeated(string characterName)
 {
  bool result=IsNameRepeatedOriginal(characterName);
  if(CharacterCreatorService.UseGuard)NativeCampaignCreationGuard.ReservedNameSeam.Postfix(this,characterName,ref result);
  return result;
 }
 public void Hide(){} public void IsValid(){}
 public void Done(Action<string> next,Action back){}
}
public partial class UICharacterCreatorWindow
{
 public sealed class CharacterViewInfo
 {
  public CMapCharacter Character;public ICharacterCreatorClass CharacterClass=new CreatorClass("BruteID");
 }
 public CharacterViewInfo characterViewInfo=new();public UICharacterCreatorNameStep nameStep=new();
 public CharacterCreatorService creationService=new();public int QuestOpened;
 public void OpenPersonalQuest()=>QuestOpened++;
 public void OnSelectedName(string name){}public void GoBackToClass(){}
}
public partial class UICampaignAdventurePartyAssemblyWindow
{
 public int Completed;
 public void OnCreatedNewCharacter(CMapCharacter character,bool networkIfOnline,int slotID,int playerID,bool selectCharacter)=>Completed++;
}
