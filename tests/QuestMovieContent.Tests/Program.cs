using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR.Quest;
internal static class Program
{
    static int count;
    static void Check(bool value,string label) { count++;if(!value)throw new Exception(label); }
    static string Sha(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static string Manifest(Dictionary<string,byte[]> parts,char producer='a')=>JsonSerializer.Serialize(new {schema=1,target="Android",producerKey=new string(producer,64),files=parts.Select(p=>new {path=p.Key,size=p.Value.LongLength,sha256=Sha(p.Value)})});
    static void Apk(string path,string metadata,Dictionary<string,byte[]> parts)
    {
        using var zip=new ZipArchive(File.Create(path),ZipArchiveMode.Create);
        using(var writer=new StreamWriter(zip.CreateEntry("assets/Quest/movies.json").Open()))writer.Write(metadata);
        foreach(var p in parts)using(var stream=zip.CreateEntry("assets/QuestMovies/"+p.Key).Open())stream.Write(p.Value);
    }
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,label);}
    static int Main(string[] args)
    {
        string work=args[0];Directory.CreateDirectory(work);string apk=Path.Combine(work,"source.apk"),root=Path.Combine(work,"installed");
        var parts=Enumerable.Range(0,22).ToDictionary(i=>"Heroes/Fixture"+i+".sbs-v1.mov",i=>Enumerable.Repeat((byte)i,8192+i).ToArray());
        parts["ghvr-quest-movies.bundle"]="UnityFS-test"u8.ToArray();string metadata=Manifest(parts);Apk(apk,metadata,parts);
        var events=new List<QuestGameContentProgress>();Check(QuestMovieContent.Install(apk,"",root,events.Add),"cold install");
        foreach(var p in parts)Check(File.ReadAllBytes(Path.Combine(root,p.Key)).SequenceEqual(p.Value),"exact movie bytes "+p.Key);
        Check(events.Last().OverallProcessedBytes==events.Last().OverallTotalBytes,"exact total");
        Check(events.Zip(events.Skip(1)).All(p=>p.First.OverallProcessedBytes<=p.Second.OverallProcessedBytes),"monotonic actual byte progress");
        // No original source entries remain: a global producer revision must not
        // reread unchanged files from the APK or the already installed movies.
        metadata=Manifest(parts,'b');Apk(apk,metadata,new());events.Clear();
        Check(QuestMovieContent.Install(apk,"",root,events.Add),"new producer with unchanged files");Check(events.Count==0,"unchanged files never reopened");
        string selected="Heroes/Fixture4.sbs-v1.mov";parts[selected]=Enumerable.Repeat((byte)101,9000).ToArray();metadata=Manifest(parts,'c');
        Apk(apk,metadata,new(){[selected]=parts[selected]});events.Clear();
        Check(QuestMovieContent.Install(apk,"",root,events.Add),"one changed film");Check(events.All(e=>e.File==selected),"only changed film copied");
        string fresh=Path.Combine(work,"interrupted");Apk(apk,metadata,parts);
        Reject(()=>QuestMovieContent.Install(apk,"",fresh,_=>throw new OperationCanceledException()),"interrupt");
        Check(!Directory.EnumerateFiles(fresh,"*.tmp",SearchOption.AllDirectories).Any(),"interrupted temp removed");
        Check(QuestMovieContent.Install(apk,"",fresh,_=>{}),"resume completed files");
        Apk(apk,metadata.Replace(selected,"../outside.mov"),parts);Reject(()=>QuestMovieContent.Install(apk,"",root,_=>{}),"path escape refused");
        Apk(apk,metadata.Replace(selected,"Heroes/Fixture5.sbs-v1.mov"),parts);Reject(()=>QuestMovieContent.Install(apk,"",root,_=>{}),"duplicate refused");
        Console.WriteLine("PASS "+count+" source-bound movie installer/progress/targeted resume checks; Android hardware unverified.");return 0;
    }
}
