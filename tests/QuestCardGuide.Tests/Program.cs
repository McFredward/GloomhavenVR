using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;

static class Program
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Reset()
    {
        ((IList)typeof(QuestCardGuideEvidence).GetField("Reports",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Clear();
        typeof(QuestCardGuideEvidence).GetField("_havePrior",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,false);
        typeof(QuestCardGuideEvidence).GetField("_lastPair",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,null);
        QuestStandalonePlatform.Enabled=QuestStandalonePlatform.DebugLogging=true;
        QuestStandalonePlatform.CardGuideDiagnostics=Array.Empty<string>();
        LevelMessagesUIHandler.s_Instance=new();CardsGameApi.NativeReads=0;CardsGameApi.CurrentMode=CardHandMode.CardsSelection;CardsGameApi.PickWanted=2;
    }
    static int Main()
    {
        for(int gate=0;gate<2;gate++)
        {
            Reset();var tray=new PlayTray{Throw=true};var hand=new CardsHandUI();
            if(gate==0)QuestStandalonePlatform.Enabled=false;else QuestStandalonePlatform.DebugLogging=false;
            QuestCardGuideEvidence.Observe(hand,tray,0,0,false);
            Check(tray.NativeReads==0&&tray.DrawReads==0&&CardsGameApi.NativeReads==0&&QuestStandalonePlatform.CardGuideDiagnostics.Length==0,"disabled evidence reads or formats native state");
        }
        Reset();var board=new PlayTray();var native=new CardsHandUI();LevelMessagesUIHandler.s_Instance!.CurrentlyDisplayedHelpTextMessage=new();
        QuestCardGuideEvidence.Observe(native,board,0,0,false);
        string first=QuestStandalonePlatform.CardGuideDiagnostics.Single();
        foreach(string expected in new[]{"mode=CardsSelection","wanted=3","occupied=00","selectionRemaining=2","handCards=2","roundCards=0","help=TB_select","left={active=True","right={active=True"})Check(first.Contains(expected),"actual guide facts missing "+expected);
        for(int n=0;n<100;n++)QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(board.DrawReads==2&&QuestStandalonePlatform.CardGuideDiagnostics.Length==1,"unchanged selection repeatedly formats renderer facts");
        board.QuestGuideRenderVersion=3;QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(board.DrawReads==4&&QuestStandalonePlatform.CardGuideDiagnostics.Length==2,"observed head submission is lost behind the earlier inactive-culling snapshot");
        board.Occupants[0]=new();board.Mask=2;native.PlayerActor!.CharacterClass!.RoundAbilityCards.Add(1);
        QuestCardGuideEvidence.Observe(native,board,0,0,false);
        string remaining=QuestStandalonePlatform.CardGuideDiagnostics.Last();
        Check(remaining.Contains("wanted=2 occupied=10")&&remaining.Contains("selectionRemaining=1")&&remaining.Contains("left={active=False")&&remaining.Contains("right={active=True"),"native single remaining slot is not distinguished from a lost renderer");
        Check(board.Mask==2&&native.PlayerActor.CharacterClass.RoundAbilityCards.Count==1,"evidence changed native eligibility or cue mask");
        board.Occupants[0]!.IsHeld=true;QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Last().Contains("held=1"),"held occupancy transition was deduplicated");
        LevelMessagesUIHandler.s_Instance.CurrentlyDisplayedHelpTextMessage!.MessageName="TB_next";
        QuestCardGuideEvidence.Observe(native,board,1,0,true);
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Last().Contains("picks=1 locked=0 blocked=True")&&QuestStandalonePlatform.CardGuideDiagnostics.Last().Contains("help=TB_next"),"native tutorial step or pick/gate change was lost");
        for(int n=0;n<50;n++){board.Owner++;QuestCardGuideEvidence.Observe(native,board,n,0,false);}
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Any(s=>s.Contains("wanted=3 occupied=00")),"actual both-wanted activation was evicted by later non-guide actions");
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Length<=8&&QuestStandalonePlatform.CardGuideDiagnostics.Length>0,"guide history exceeds eight observations");
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Sum(Encoding.UTF8.GetByteCount)<=4096,"guide history exceeds four KiB");
        board.LongFacts=new string('\u754c',3000);board.Owner++;QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(QuestStandalonePlatform.CardGuideDiagnostics.All(s=>Encoding.UTF8.GetByteCount(s)<=1024)&&QuestStandalonePlatform.CardGuideDiagnostics.Sum(Encoding.UTF8.GetByteCount)<=4096&&QuestStandalonePlatform.CardGuideDiagnostics.Any(s=>s.Contains("owner="+board.Owner+" ")),"native Unicode text bypasses guide byte bounds");
        CardsGameApi.CurrentMode=CardHandMode.DiscardCard;CardsGameApi.PickWanted=2;board.LongFacts=null;
        QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Last().Contains("selectionRemaining=-1 pickWanted=2"),"pick demand is confused with ordinary card selection");
        CardsGameApi.PickWanted=1;QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Last().Contains("pickWanted=1"),"native pick-demand change was deduplicated");
        Reset();board=new PlayTray{Throw=true};QuestCardGuideEvidence.Observe(native,board,0,0,false);
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Single()=="guide-observation-unavailable=InvalidOperationException","optional observation exception escaped native update");
        for(int n=0;n<20;n++){board.Owner++;QuestCardGuideEvidence.Observe(native,board,0,0,false);}
        Check(QuestStandalonePlatform.CardGuideDiagnostics.Length==1,"repeated observation failure grew the snapshot");
        Console.WriteLine("PASS Quest card guide evidence: "+checks+" assertions; no headset visibility claim");return 0;
    }
}
