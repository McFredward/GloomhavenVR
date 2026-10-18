using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;
using GloomhavenVR.Quest;
using GloomhavenVR.Quest.Editor;

public static class ContentBuildWitness
{
    static int assertions;
    const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string NextKey = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string Manifest = "Assets/Quest/Resources/quest-startup-content.json";
    [DllImport("libc", SetLastError=true)] static extern int symlink(string source, string destination);
    static string root, mono;
    static string Full(string path) { return Path.Combine(root,path); }
    static void Assert(bool condition,string message) { ++assertions; if(!condition)throw new Exception(message); }
    static string Hash(string file) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant(); }
    static void Write(string file,string text) { Directory.CreateDirectory(Path.GetDirectoryName(Full(file)));File.WriteAllText(Full(file),text); }
    static void Load(string project) {
        root=Path.GetFullPath(project);Directory.SetCurrentDirectory(root);Application.dataPath=Full("Assets");
        UnityEngine.AddressableAssets.Addressables.BuildPath=Full(QuestCampaignContentBuild.NativePath);
        AssetDatabase.onRefresh=AssetDatabase.onImport=null;
    }
    static void NewProject(string directory) {
        Directory.CreateDirectory(directory);Load(directory);
        Write(QuestCampaignContentBuild.ArchivePath,"bounded original content bank");
        Write(QuestCampaignContentBuild.ArchivePath+".meta","guid: 12121212121212121212121212121212\n");
        Write(QuestCampaignContentBuild.NativeLink,"<linker><assembly fullname=\"OriginalGame\" preserve=\"all\"/></linker>\n");
        Write(QuestCampaignContentBuild.NativePath+"/nested/native.bundle","original native bundle bytes");
        Write(QuestCampaignContentBuild.NativePath+"/settings.json","original settings");
        SetManifest(Key);
    }
    static void SetManifest(string key) {
        Write(Manifest,JsonUtility.ToJson(new QuestGameContentManifest{schema=1,inputKey=key,archive="quest-startup-content.zip",
            archiveSha256=Hash(Full(QuestCampaignContentBuild.ArchivePath)),files=new QuestGameContentFile[0]},true));
        Write("Assets/Quest/Resources/quest-mod-content.json",JsonUtility.ToJson(new QuestGameContentManifest{
            schema=1,inputKey=key,archive="quest-mod-content.zip",archiveSha256=new string('c',64),
            files=new[]{new QuestGameContentFile{path="StreamingAssets/current-mod.bundle",sha256=new string('d',64),size=17}}},true));
    }
    sealed class InstallationView { public int schema; public string inputKey; public QuestGameContentManifest game,mod; }
    static QuestCampaignContentBuild Begin(string key=Key) { return new QuestCampaignContentBuild(Full("Output/game.apk"),key); }
    static QuestCampaignContentBuild.Journal Journal() {return JsonUtility.FromJson<QuestCampaignContentBuild.Journal>(File.ReadAllText(Full(QuestCampaignContentBuild.JournalPath)));}
    static void Saved(QuestCampaignContentBuild.Journal value){Write(QuestCampaignContentBuild.JournalPath,JsonUtility.ToJson(value,true));}
    static void Sources(bool expected) {
        Assert(File.Exists(Full(QuestCampaignContentBuild.ArchivePath))==expected,"ZIP source visibility");
        Assert(File.Exists(Full(QuestCampaignContentBuild.ArchivePath+".meta"))==expected,"ZIP meta visibility");
        Assert(Directory.Exists(Full(QuestCampaignContentBuild.NativePath))==expected,"Native aa source visibility");
    }
    static void Reject(Action operation,string message) {bool rejected=false;try{operation();}catch(Exception){rejected=true;}Assert(rejected,message);}
    public static int Main(string[] args) {
        mono=args[0];
        if(args.Length>1&&args[1]=="die") {Load(args[2]);Begin(args[3]);Environment.Exit(0);return 0;}
        string baseDirectory=Path.GetFullPath(args[1]);Directory.CreateDirectory(baseDirectory);
        NewProject(Path.Combine(baseDirectory,"normal"));
        var normal=Begin();Sources(false);
        Assert(File.ReadAllText(Full(QuestCampaignContentBuild.StableLink))==File.ReadAllText(Full(QuestCampaignContentBuild.NativeTemporary+"/AddressablesLink/link.xml")),"Native linker bytes preserved in Assets");
        Assert(File.Exists(Full("Output/GloomhavenVR-Quest-content.zip")),"External bank copied");
        Assert(Hash(Full("Output/GloomhavenVR-Quest-content.zip"))==Journal().moves[0].sha256,"External bank exact actualSHA");
        Assert(Journal().state=="excluded","Excluded journal completed");
        var inventory=JsonUtility.FromJson<InstallationView>(File.ReadAllText(Full(QuestCampaignContentBuild.InstallationPath)));
        Assert(inventory.schema==1 && inventory.inputKey==Key,"Signed installation inventory matches current input");
        Assert(inventory.game.externalDelivery && inventory.game.archiveSha256==Journal().moves[0].sha256,
            "Installation inventory uses actual final external native bank");
        Assert(inventory.mod.inputKey==Key && inventory.mod.files.Length==1 && inventory.mod.files[0].size==17,
            "Installation inventory preserves exact current mod files");
        normal.Dispose();Sources(true);normal.Dispose();
        Assert(Journal().state=="restored","Normal dispose durable restored state");
        using(Begin())Sources(false);Sources(true);

        // A newer input and linker are legitimate after an owned prior build.
        Write(QuestCampaignContentBuild.NativeLink,"<linker><assembly fullname=\"NewMod\" preserve=\"all\"/></linker>\n");
        Write(QuestCampaignContentBuild.ArchivePath,"updated native content");
        SetManifest(NextKey);
        string oldLink=Hash(Full(QuestCampaignContentBuild.StableLink));
        using(Begin(NextKey)) {
            Sources(false);
            Assert(Journal().inputKey==NextKey,"New input journal replaces only restored previous transaction");
            Assert(Journal().nativeLink.previousSha256==oldLink,"Old owned link hash retained");
            Assert(Hash(Full(QuestCampaignContentBuild.StableLink))==Journal().nativeLink.sha256,"Updated native linker supplied to IL2CPP");
        }
        Sources(true);

        Directory.Delete(Full(QuestCampaignContentBuild.NativePath),true);
        QuestCampaignContentBuild.RestoreExcludedContent(root,NextKey);
        Assert(!Directory.Exists(Full(QuestCampaignContentBuild.NativePath)),"Restored journal allows failed rebuild to leave native AA absent");
        File.Move(Full(QuestCampaignContentBuild.ArchivePath),Full(QuestCampaignContentBuild.ArchivePath+".outer-recovery"));
        QuestCampaignContentBuild.RestoreExcludedContent(root,NextKey);
        Assert(!File.Exists(Full(QuestCampaignContentBuild.ArchivePath)),"Restored journal allows outer transaction to recover its archive later");
        File.Move(Full(QuestCampaignContentBuild.ArchivePath+".outer-recovery"),Full(QuestCampaignContentBuild.ArchivePath));
        Write(QuestCampaignContentBuild.NativeLink,"<linker>regenerated native declarations</linker>");
        Write(QuestCampaignContentBuild.NativePath+"/settings.json","regenerated native settings");
        using(Begin(NextKey)){};Sources(true);

        NewProject(Path.Combine(baseDirectory,"rollback"));
        byte[] manifestBefore=File.ReadAllBytes(Full(Manifest));
        Write("Assets/StreamingAssets/Quest/content-delivery.json","original delivery");
        Write(QuestCampaignContentBuild.InstallationPath,"original installation inventory");
        var initiating=new InvalidOperationException("initiating refresh failure");
        AssetDatabase.onRefresh=()=>{throw initiating;};
        bool same=false;try{Begin();}catch(Exception error){same=Object.ReferenceEquals(error,initiating);}
        Assert(same,"Constructor retains initiating exception identity");Sources(true);
        Assert(Convert.ToBase64String(File.ReadAllBytes(Full(Manifest)))==Convert.ToBase64String(manifestBefore),"Constructor restores exact manifest bytes");
        Assert(File.ReadAllText(Full("Assets/StreamingAssets/Quest/content-delivery.json"))=="original delivery","Original delivery bytes restored");
        Assert(File.ReadAllText(Full(QuestCampaignContentBuild.InstallationPath))=="original installation inventory",
            "Original installation inventory restored after interrupted build");
        Assert(!File.Exists(Full(QuestCampaignContentBuild.StableLink)),"Created linker rolled back");
        AssetDatabase.onRefresh=null;using(Begin()){};Sources(true);
        string previousLink=File.ReadAllText(Full(QuestCampaignContentBuild.StableLink));
        Write(QuestCampaignContentBuild.NativeLink,"<linker>newer original declarations</linker>");SetManifest(NextKey);
        AssetDatabase.onRefresh=()=>{throw initiating;};
        same=false;try{Begin(NextKey);}catch(Exception error){same=Object.ReferenceEquals(error,initiating);}
        Assert(same,"Updated-link failure preserves initiating error");
        Sources(true);Assert(File.ReadAllText(Full(QuestCampaignContentBuild.StableLink))==previousLink,"Previous owned linker restored after new-input failure");
        AssetDatabase.onRefresh=null;using(Begin(NextKey)){};Sources(true);

        NewProject(Path.Combine(baseDirectory,"collision"));Write(QuestCampaignContentBuild.StableLink,"unowned original");
        Reject(()=>Begin(),"Original linker collision rejected");Sources(true);
        Assert(File.ReadAllText(Full(QuestCampaignContentBuild.StableLink))=="unowned original","Original linker collision never overwritten");

        NewProject(Path.Combine(baseDirectory,"harddeath"));
        var process=Process.Start(new ProcessStartInfo{FileName=mono,Arguments="\""+Assembly.GetExecutingAssembly().Location+"\" \""+mono+"\" die \""+root+"\" "+Key,UseShellExecute=false});
        process.WaitForExit();Assert(process.ExitCode==0,"Independent child exits without Dispose");Sources(false);
        Write(QuestCampaignContentBuild.JournalPath+".quest-content-pending","partial journal");
        Write(Manifest+".quest-content-pending","partial owned manifest");
        Write(QuestCampaignContentBuild.StableLink+".quest-content-pending","partial owned linker");
        QuestCampaignContentBuild.RestoreExcludedContent(root,Key);Sources(true);
        Assert(Journal().state=="restored","Hard-death next-process journal recovery");
        Assert(!File.Exists(Full(QuestCampaignContentBuild.JournalPath+".quest-content-pending")),"Interrupted owned journal pending removed after recovery");

        // Simulate death before and after a changed native linker replacement.
        string beforeNewLink=File.ReadAllText(Full(QuestCampaignContentBuild.StableLink));
        Write(QuestCampaignContentBuild.NativeLink,"<linker>changed mod native link</linker>");SetManifest(NextKey);
        var changed=Begin(NextKey);Sources(false);
        Write(QuestCampaignContentBuild.StableLink,beforeNewLink);
        QuestCampaignContentBuild.RestoreExcludedContent(root,NextKey);Sources(true);
        Assert(Hash(Full(QuestCampaignContentBuild.StableLink))==Journal().nativeLink.previousSha256,"Recovery permits pre-replace old owned linker");
        changed.Dispose();using(Begin(NextKey)){};Sources(true);
        Assert(Hash(Full(QuestCampaignContentBuild.StableLink))==Hash(Full(QuestCampaignContentBuild.NativeLink)),"Future constructor applies current native linker");

        NewProject(Path.Combine(baseDirectory,"conflict"));var conflict=Begin();Sources(false);
        Directory.CreateDirectory(Full(QuestCampaignContentBuild.NativePath));
        Reject(()=>QuestCampaignContentBuild.RestoreExcludedContent(root,Key),"Both source+temporary native directories reject");
        Assert(!File.Exists(Full(QuestCampaignContentBuild.ArchivePath)),"Conflict preflight does not partially restore ZIP");
        Directory.Delete(Full(QuestCampaignContentBuild.NativePath));conflict.Dispose();Sources(true);

        NewProject(Path.Combine(baseDirectory,"drift"));var drift=Begin();var journal=Journal();
        journal.moves[1].source="../escape";Saved(journal);
        Reject(()=>QuestCampaignContentBuild.RestoreExcludedContent(root,Key),"Unknown journal paths rejected");Sources(false);
        journal.moves[1].source=QuestCampaignContentBuild.ArchivePath+".meta";Saved(journal);
        Reject(()=>QuestCampaignContentBuild.RestoreExcludedContent(root,NextKey),"Wrong outstanding input key rejected");Sources(false);
        drift.Dispose();Sources(true);

        NewProject(Path.Combine(baseDirectory,"symlink"));
        Assert(symlink(Full("Output"),Full(QuestCampaignContentBuild.NativePath+"/escape-link"))==0,"Native symlink negative created");
        Reject(()=>Begin(),"Native AA symlink rejected");Sources(true);
        File.Delete(Full(QuestCampaignContentBuild.NativePath+"/escape-link"));

        // Pin the exact public API without launching Unity.
        Assert(typeof(QuestCampaignContentBuild).GetMethod("RestoreExcludedContent",BindingFlags.Public|BindingFlags.Static)!=null,"Public confined recovery API available");
        Console.WriteLine("PASS real C# filesystem exclusion/recovery/rollback/ownership controls: "+assertions);
        return 0;
    }
}
