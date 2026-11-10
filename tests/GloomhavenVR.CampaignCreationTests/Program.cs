using System;
using System.Linq;
using FFSNet;
using GloomhavenVR.Compat;
using GloomhavenVR.Core;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;

internal static class Program
{
 private static int _assertions;
 private static void Check(bool condition,string message)
 {
  _assertions++;
  if(!condition)throw new InvalidOperationException(message);
 }
 private static void Reset(int player=2)
 {
  NativeCampaignCreationGuard.Reset();
  AdventureState.MapState=new();FFSNetwork.IsOnline=true;VRSession.IsRunning=true;
  PlayerRegistry.AllPlayers=new(){new(){PlayerID=1},new(){PlayerID=2}};
  PlayerRegistry.MyPlayer=PlayerRegistry.AllPlayers.Single(p=>p.PlayerID==player);
  Debug.Errors.Clear();SceneController.Instance=new();VRLog.Warnings.Clear();
 }
 private static UICharacterCreatorWindow Creator(string id="BruteID")
 {
  var creator=new UICharacterCreatorWindow();
  creator.characterViewInfo.CharacterClass=new CreatorClass(id);
  creator.nameStep.Show(creator.characterViewInfo.CharacterClass);
  return creator;
 }
 private static void Receive(UICharacterCreatorWindow creator,int owner,string id,string name)
 {
  creator.ProxyCreateCharacter(new(){SupplementaryDataIDMax=owner,SupplementaryDataToken=new CampaignCharacterData(id,name)});
 }
 private static CMapCharacter CreateLocal(string classId,string name)
 {
  NativeCampaignCreationGuard.OriginSeam.Prefix(new(){SupplementaryDataIDMax=PlayerRegistry.MyPlayer.PlayerID},out var state);
  try{return CharacterCreatorService.CreateCharacter(classId,name);}
  finally{NativeCampaignCreationGuard.OriginSeam.Finalizer(state);}
 }
 private static bool TryLocalReuse(string classId,string name,out CMapCharacter? character)
 {
  NativeCampaignCreationGuard.OriginSeam.Prefix(new(){SupplementaryDataIDMax=PlayerRegistry.MyPlayer.PlayerID},out var state);
  try{return NativeCampaignCreationGuard.TryReuse(classId,name,out character);}
  finally{NativeCampaignCreationGuard.OriginSeam.Finalizer(state);}
 }
 private static void Origins()
 {
  Reset();var creator=Creator();Receive(creator,2,"BruteID","Shared");
  var own=creator.characterViewInfo.Character;
  Receive(creator,1,"BruteID","Shared");
  var peer=AdventureState.MapState.MapParty.CheckCharacters.Single();
  Check(!ReferenceEquals(own,peer)&&AdventureState.MapState.Rolls==4,"peer same-name command generates its own native object");
  Check(!NativeCampaignCreationGuard.TryReuse("BruteID","Shared",out _),"matching owner record outside native command cannot authorize reuse");
  Check(peer.PossiblePersonalQuests[0].ID=="Q3"&&own.PossiblePersonalQuests[0].ID=="Q1","opposite-owner commands preserve both original quest sequences");
  Reset();creator=Creator();Receive(creator,2,"BruteID","Shared");
  creator.nameStep.Show(new CreatorClass("ScoundrelID"));
  Receive(creator,1,"ScoundrelID","Shared");
  Check(AdventureState.MapState.MapParty.CheckCharacters.Count==1&&AdventureState.MapState.MapParty.CheckCharacters[0].CharacterID=="ScoundrelID","peer wrong-class command retains original observer name validation");
  Reset();creator=Creator();Receive(creator,2,"BruteID","Nested");own=creator.characterViewInfo.Character;
  NativeCampaignCreationGuard.OriginSeam.Prefix(new(){SupplementaryDataIDMax=2},out var outer);
  try
  {
   bool threw=false;
   try{Receive(creator,99,"BruteID","Nested");}
   catch(InvalidOperationException error){threw=error.Message.Contains("Sequence contains no matching element");}
   Check(threw&&NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out var restored)&&ReferenceEquals(restored,own),"nested peer exception restores outer local origin");
   NativeCampaignCreationGuard.OriginSeam.Prefix(new(){SupplementaryDataIDMax=1},out var inner);
   try{Check(!NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _),"nested peer origin cannot use outer local provisional");}
   finally{NativeCampaignCreationGuard.OriginSeam.Finalizer(inner);}
   Check(NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _),"normal nested completion restores outer local origin");
   NativeCampaignCreationGuard.OriginSeam.Finalizer(default);
   Check(NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _),"unentered finalizer cannot clear outer local origin");
   NativeCampaignCreationGuard.OriginSeam.Prefix(null!,out var unknown);
   try{Check(!NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _),"invalid nested command cannot borrow local creation authority");}
   finally{NativeCampaignCreationGuard.OriginSeam.Finalizer(unknown);}
   Check(NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _),"invalid nested completion restores outer local origin");
   bool otherThreadReused=true;
   var thread=new System.Threading.Thread(()=>otherThreadReused=NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _));thread.Start();thread.Join();
   Check(!otherThreadReused,"native origin cannot leak to another thread");
  }
  finally{NativeCampaignCreationGuard.OriginSeam.Finalizer(outer);}
  Check(!NativeCampaignCreationGuard.TryReuse("BruteID","Nested",out _),"normal proxy completion leaves no active origin");
 }
 private sealed record Snapshot(string[] First,string[] Second,int Rolls,int Gold,int Completed,int Errors);
 private static Snapshot Replay(int player)
 {
  Reset(player);var creator=Creator();
  Receive(creator,2,"BruteID","Boomboom");
  var first=player==2?creator.characterViewInfo.Character:AdventureState.MapState.MapParty.CheckCharacters.Single();
  var firstChoices=first.PossiblePersonalQuests.Select(q=>q.ID).ToArray();
  Check(AdventureState.MapState.Rolls==2,"first native creation consumes its original two rolls");
  Check(first.Gold==30,"first native creation grants gold once");
  Check(player!=2||AdventureState.MapState.MapParty.CheckCharacters.Count==0,"owner original character remains provisional outside party");
  // The recorded path: close/cancel/reopen the creator, then name the same class again.
  creator=Creator();Receive(creator,2,"BruteID","Boomboom");
  var same=player==2?creator.characterViewInfo.Character:AdventureState.MapState.MapParty.CheckCharacters.Single();
  Check(ReferenceEquals(same,first),"reopened native creator reuses exact provisional object");
  Check(firstChoices.SequenceEqual(same.PossiblePersonalQuests.Select(q=>q.ID)),"reopened native creator retains exact personal-quest choices");
  Check(AdventureState.MapState.Rolls==2,"repeated native creation consumes no second roll");
  Check(AdventureState.MapState.GoldGifts==1,"repeated native creation grants no second gold or items");
  Check(AdventureState.MapState.ItemUnlocks==1,"repeated native creation repeats no unlock work");
  // Another player's native character creation must see the same next random state.
  creator=Creator("ScoundrelID");Receive(creator,1,"ScoundrelID","Berbel");
  var second=player==1?creator.characterViewInfo.Character:AdventureState.MapState.MapParty.CheckCharacters.Single(c=>c.CharacterID=="ScoundrelID");
  Check(AdventureState.MapState.Rolls==4,"another character consumes only the next original rolls");
  // Original owner's selection adds its provisional character to its own party.
  if(player==2){first.AssignPersonalQuest(first.PossiblePersonalQuests[0]);AdventureState.MapState.MapParty.AddCharacterToCharactersList(first);}
  else{second.AssignPersonalQuest(second.PossiblePersonalQuests[0]);AdventureState.MapState.MapParty.AddCharacterToCharactersList(second);}
  var assembly=new UICampaignAdventurePartyAssemblyWindow();
  foreach(var value in new[]{new CampaignPersonalQuestData{CharacterID="BruteID",CharacterName="Boomboom",PersonalQuestID="Q1",PlayerID=2},new CampaignPersonalQuestData{CharacterID="ScoundrelID",CharacterName="Berbel",PersonalQuestID="Q3",PlayerID=1}})
  {
   var coroutine=assembly.ProxyCreateCampaignCharacterCoroutine(value);
   while(coroutine.MoveNext()){}
  }
  Check(Debug.Errors.Count==0,"original personal-quest coroutine finds each owner's exact quest");
  Check(SceneController.Instance.GlobalErrorMessage.Count==0,"original personal-quest coroutine raises no synchronization error");
  Check(assembly.Completed==2,"original native character completion executes for both players");
  return new(firstChoices,second.PossiblePersonalQuests.Select(q=>q.ID).ToArray(),AdventureState.MapState.Rolls,AdventureState.MapState.GoldGifts,assembly.Completed,Debug.Errors.Count);
 }
 private static void OriginalFailure()
 {
  CharacterCreatorService.UseGuard=false;
  try
  {
   Reset(2);var creator=Creator();Receive(creator,2,"BruteID","Boomboom");
   creator=Creator();Receive(creator,2,"BruteID","Boomboom");
   var active=creator.characterViewInfo.Character;
   string ownerChoice=active.PossiblePersonalQuests[0].ID;
   Check(ownerChoice=="Q3"&&AdventureState.MapState.Rolls==4,"unmodified creator rerolls the repeated provisional identity");
   active.AssignPersonalQuest(active.PossiblePersonalQuests[0]);AdventureState.MapState.MapParty.AddCharacterToCharactersList(active);
   creator=Creator("ScoundrelID");Receive(creator,1,"ScoundrelID","Berbel");
   var ownerMap=AdventureState.MapState;var ownerPlayers=PlayerRegistry.AllPlayers;var ownerPlayer=PlayerRegistry.MyPlayer;
   Reset(1);creator=Creator();Receive(creator,2,"BruteID","Boomboom");
   creator=Creator();Receive(creator,2,"BruteID","Boomboom");
   Check(AdventureState.MapState.Rolls==2,"unmodified observer skips the repeated name already in its party");
   creator=Creator("ScoundrelID");Receive(creator,1,"ScoundrelID","Berbel");
   var hostCharacter=creator.characterViewInfo.Character;string hostChoice=hostCharacter.PossiblePersonalQuests[0].ID;
   hostCharacter.AssignPersonalQuest(hostCharacter.PossiblePersonalQuests[0]);AdventureState.MapState.MapParty.AddCharacterToCharactersList(hostCharacter);
   var hostAssembly=new UICampaignAdventurePartyAssemblyWindow();
   var coroutine=hostAssembly.ProxyCreateCampaignCharacterCoroutine(new(){CharacterID="BruteID",CharacterName="Boomboom",PersonalQuestID=ownerChoice,PlayerID=2});
   while(coroutine.MoveNext()){}
   Check(SceneController.Instance.GlobalErrorMessage.Count==1&&Debug.Errors.Any(t=>t.Contains("Sequence contains no matching element")),"unmodified observer reproduces exact recorded first personal-quest error");
   AdventureState.MapState=ownerMap;PlayerRegistry.AllPlayers=ownerPlayers;PlayerRegistry.MyPlayer=ownerPlayer;
   SceneController.Instance=new();Debug.Errors.Clear();
   var ownerAssembly=new UICampaignAdventurePartyAssemblyWindow();
   coroutine=ownerAssembly.ProxyCreateCampaignCharacterCoroutine(new(){CharacterID="ScoundrelID",CharacterName="Berbel",PersonalQuestID=hostChoice,PlayerID=1});
   while(coroutine.MoveNext()){}
   Check(SceneController.Instance.GlobalErrorMessage.Count==1&&Debug.Errors.Any(t=>t.Contains("Sequence contains no matching element")),"unmodified creator reproduces subsequent peer's personal-quest error");
  }
  finally{CharacterCreatorService.UseGuard=true;}
 }
 private static void Contexts()
 {
  Reset();PlayerRegistry.MyPlayer.CreatingCharacter=new(2,"First");
  var first=CreateLocal("BruteID","First");
  Check(TryLocalReuse("BruteID","First",out var reused)&&ReferenceEquals(first,reused),"same context retains provisional original");
  Check(!TryLocalReuse("ScoundrelID","First",out _),"different class never reuses wrong native character");
  Check(TryLocalReuse("BruteID","first",out reused)&&ReferenceEquals(first,reused),"name identity retains native case-insensitive collision rule");
  first.AssignPersonalQuest(first.PossiblePersonalQuests[0]);
  Check(!TryLocalReuse("BruteID","First",out _),"completed prior character is never reused");
  var committedReplacement=CreateLocal("BruteID","First");
  Check(!ReferenceEquals(first,committedReplacement),"native creation after completed prior character is not substituted");
  PlayerRegistry.MyPlayer=new(){PlayerID=2,CreatingCharacter=new(2,"First")};
  Check(!TryLocalReuse("BruteID","First",out _),"rejoin with same save and numeric player id cannot reuse old session");
  var rejoined=CreateLocal("BruteID","First");
  AdventureState.MapState=new();
  Check(!TryLocalReuse("BruteID","First",out _),"new save never inherits provisional character");
  var fresh=CreateLocal("BruteID","First");
  Check(!ReferenceEquals(rejoined,fresh),"new save executes new native generation");
  FFSNetwork.IsOnline=false;
  Check(!TryLocalReuse("BruteID","First",out _),"offline cannot reuse network provisional objects");
  FFSNetwork.IsOnline=true;
  Check(!TryLocalReuse("BruteID","First",out _),"observed disconnect clears old context even with same player object");
  var next=CreateLocal("BruteID","First");
  VRSession.IsRunning=false;
  Check(!TryLocalReuse("BruteID","First",out _),"disabled VR retains original behavior");
  VRSession.IsRunning=true;AdventureState.MapState.IsCampaign=false;
  Check(!TryLocalReuse("BruteID","First",out _),"Guildmaster retains original behavior");
  AdventureState.MapState.IsCampaign=true;
  Check(!TryLocalReuse("BruteID","First",out _),"mode change invalidates old provisional context");
  _=next;
  Reset(1);var observer=Creator();Receive(observer,2,"BruteID","Peer");
  var observed=AdventureState.MapState.MapParty.CheckCharacters.Single();
  AdventureState.MapState.MapParty.CheckCharacters.Clear();
  PlayerRegistry.MyPlayer.CreatingCharacter=new(1,"Peer");
  Check(!TryLocalReuse("BruteID","Peer",out _),"departed peer cannot leave an observer-side reuse entry");
  var localAfterLeave=CreateLocal("BruteID","Peer");
  Check(!ReferenceEquals(observed,localAfterLeave),"native departed-peer cleanup permits fresh local creation");
 }
 private static void Naming()
 {
  Reset();var creator=Creator();Receive(creator,2,"BruteID","Pending");
  Check(!creator.nameStep.IsNameRepeated("Pending"),"same-class provisional name remains selectable");
  creator.nameStep.Show(new CreatorClass("ScoundrelID"));
  Check(creator.nameStep.IsNameRepeated("Pending"),"changed class retains native repeated-name rejection");
  Check(creator.nameStep.IsNameRepeated("pending"),"changed class collision uses native case-insensitive names");
  Check(!creator.nameStep.IsNameRepeated("Different"),"changed class accepts a new unused name");
  Receive(creator,2,"ScoundrelID","Pending");
  Check(AdventureState.MapState.Rolls==2,"conflicting replay cannot reroll a wrong-class character");
  creator=Creator("ScoundrelID");Receive(creator,2,"ScoundrelID","Different");
  Check(creator.characterViewInfo.Character.CharacterID=="ScoundrelID","canceled creation may create a new class with a new name");
  Check(AdventureState.MapState.Rolls==4,"new class and new name keep original generation");
  creator.nameStep.Show(new CreatorClass("ScoundrelID"),"Rename");
  Check(!creator.nameStep.IsNameRepeated("Pending"),"rename flow is not restricted by creator reservations");
  AdventureState.MapState.MapParty.CheckCharacters.Add(new("BruteID","Existing",0));
  Check(creator.nameStep.IsNameRepeated("Existing"),"native existing-name prohibition is preserved");
  AdventureState.MapState.MapParty.RetiredCharacterRecords.Add(new(){CharacterName="Retired"});
  Check(creator.nameStep.IsNameRepeated("Retired"),"native retired-name prohibition is preserved");
  NativeCampaignCreationGuard.Reset();
  Check(!creator.nameStep.IsNameRepeated("Pending"),"shutdown clears provisional name restrictions");
 }
 public static void Main()
 {
  OriginalFailure();Origins();
  var owner=Replay(2);var host=Replay(1);
  Check(owner.First.SequenceEqual(host.First)&&owner.Second.SequenceEqual(host.Second),"owner and observer quest choices remain identical after repeated native action");
  Check(owner.Rolls==host.Rolls&&owner.Gold==host.Gold,"owner and observer native work stays identical");
  Contexts();Naming();
  Console.WriteLine($"Campaign creation: {_assertions} assertions passed (exact original native generation/proxy/coroutine methods; deterministic domain boundary, no headset claim).");
 }
}
