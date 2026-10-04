using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;
using GloomhavenVR.Quest;
using Attributes=Mono.Cecil.TypeAttributes;
using Methods=Mono.Cecil.MethodAttributes;

int assertions=0;
void Check(bool value,string reason) { assertions++; if(!value)throw new Exception(reason); }
string directory=Path.Combine(Path.GetTempPath(),"quest-storage-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
Exception Capture(Action action) { try { action(); return null; } catch(TargetInvocationException error) { return error.InnerException; } catch(Exception error) { return error; } }
void ResetRecovery()=>typeof(QuestGameSaveStorage).GetField("initialized",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
try
{
    string library=Path.Combine(directory,"QuestGame.Compatibility.dll");
    using(var module=ModuleDefinition.CreateModule("StorageFixture",ModuleKind.Dll))
    using(var compatibility=PathsCompatibility.Create(module))
    {
        MethodDefinition write=StandaloneStorage.EmitWriter(compatibility.MainModule);
        Check(!compatibility.MainModule.AssemblyReferences.Any(reference=>reference.Name.StartsWith("Unity",StringComparison.Ordinal)),"Atomic worker has a Unity/main-thread dependency.");
        Check(write.Body.ExceptionHandlers.Count==2,"Flushed handle/temp lifetimes lost their finally protection.");
        Check(write.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Flush"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Boolean"),"Atomic writer omits durable flush.");
        Check(write.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="System.IO.File"&&m.Name=="Replace")==1,"Live target is not replaced atomically.");
        Check(!write.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="System.IO.File"&&m.Name=="Delete"),"Atomic writer deletes/truncates live target before commit.");
        Check(Capture(()=>StandaloneStorage.EmitWriter(compatibility.MainModule)) is InvalidDataException,"Duplicate adaptation was accepted.");
        compatibility.Write(library);
    }
    var context=new AssemblyLoadContext("storage-fixture",true);
    Assembly generated=context.LoadFromAssemblyPath(library);
    MethodInfo atomic=generated.GetType("QuestGame.Compatibility.SaveFiles").GetMethod("WriteAllBytes");
    void Write(string path,byte[] bytes)=>atomic.Invoke(null,new object[]{path,bytes});
    string file=Path.Combine(directory,"Campaign Ü.dat");
    byte[] original=Enumerable.Range(0,4096).Select(i=>(byte)(i%251)).ToArray();
    byte[] next=Enumerable.Range(0,8192).Select(i=>(byte)(250-i%251)).ToArray();
    Write(file,original);Check(File.ReadAllBytes(file).SequenceEqual(original),"First atomic write changed native serialized bytes.");
    Task.Run(()=>Write(file,next)).GetAwaiter().GetResult();
    Check(File.ReadAllBytes(file).SequenceEqual(next)&&File.ReadAllBytes(file+".ghvr-save-backup").SequenceEqual(original),"Worker replacement lost current or previous native save bytes.");
    Write(file,original);Check(File.ReadAllBytes(file+".ghvr-save-backup").SequenceEqual(next),"Repeated replace did not advance previous-byte backup.");
    Check(!Directory.GetFiles(directory,"*.ghvr-save-write-*.tmp").Any(),"Completed writes leaked unique temp files.");
    Check(Capture(()=>Write(null,original)) is ArgumentNullException&&Capture(()=>Write(file,null)) is ArgumentNullException,"Null path/bytes failure contract changed.");
    Check(Capture(()=>Write("",original)) is ArgumentException,"Empty original save destination was accepted.");
    string blocked=Path.Combine(directory,"existing-directory");Directory.CreateDirectory(blocked);
    Check(Capture(()=>Write(blocked,next)) is IOException,"Failed replacement reported success.");
    Check(Directory.Exists(blocked)&&!Directory.GetFiles(directory,"*.ghvr-save-write-*.tmp").Any(),"Failed replacement removed existing destination or leaked temp.");
    string missing=Path.Combine(directory,"missing-parent","save.dat");
    Check(Capture(()=>Write(missing,next)) is DirectoryNotFoundException&&!Directory.Exists(Path.GetDirectoryName(missing)),"Writer created an unowned parent or hid IO failure.");
    Task.WaitAll(Enumerable.Range(0,24).Select(index=>Task.Run(()=>Write(Path.Combine(directory,"worker-"+index+".dat"),next))).ToArray());
    Check(Enumerable.Range(0,24).All(index=>File.ReadAllBytes(Path.Combine(directory,"worker-"+index+".dat")).SequenceEqual(next)),"Independent native worker paths collide.");

    // Meaningful old-code control: File.WriteAllBytes truncates the current file
    // before a process loss can commit a complete next record and keeps no backup.
    string old=Path.Combine(directory,"old-direct-writer.dat");File.WriteAllBytes(old,original);
    using(var partial=new FileStream(old,FileMode.Create,FileAccess.Write)){partial.Write(next,0,128);partial.Flush(true);}
    Check(File.ReadAllBytes(old).Length==128&&!File.Exists(old+".ghvr-save-backup"),"Direct-write interruption control did not demonstrate original save-loss window.");

    for(int mutation=0;mutation<4;mutation++)
    {
        using(var game=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("NativeStorageFixture",new Version(1,0)),"NativeStorageFixture",ModuleKind.Dll))
        using(var compatibility=PathsCompatibility.Create(game.MainModule))
        {
            var owner=new TypeDefinition("","PlatformFileSystem",Attributes.Public,game.MainModule.TypeSystem.Object);game.MainModule.Types.Add(owner);
            var worker=new TypeDefinition("","NativeAsyncWorker",Attributes.NestedPrivate,game.MainModule.TypeSystem.Object);owner.NestedTypes.Add(worker);
            var native=new MethodReference("WriteAllBytes",game.MainModule.TypeSystem.Void,new TypeReference("System.IO","File",game.MainModule,game.MainModule.TypeSystem.CoreLibrary));
            native.Parameters.Add(new ParameterDefinition(game.MainModule.TypeSystem.String));native.Parameters.Add(new ParameterDefinition(new ArrayType(game.MainModule.TypeSystem.Byte)));
            MethodDefinition Sync(string name,TypeDefinition type,bool writeCall=true)
            {
                var method=new MethodDefinition(name,Methods.Public,game.MainModule.TypeSystem.Void);type.Methods.Add(method);
                method.Parameters.Add(new ParameterDefinition(new ArrayType(game.MainModule.TypeSystem.Byte)));method.Parameters.Add(new ParameterDefinition(game.MainModule.TypeSystem.String));
                if(writeCall){method.Body.GetILProcessor().Emit(OpCodes.Ldarg_2);method.Body.GetILProcessor().Emit(OpCodes.Ldarg_1);method.Body.GetILProcessor().Emit(OpCodes.Call,native);}
                method.Body.GetILProcessor().Emit(OpCodes.Ret);return method;
            }
            MethodDefinition sync=Sync("WriteFile",owner);MethodDefinition async=Sync("WriteFileAsync",owner,false);async.Parameters.Add(new ParameterDefinition(game.MainModule.TypeSystem.Object));
            Sync("OriginalTaskRunWorker",worker);
            var saveOwner=new TypeDefinition("","SaveData",Attributes.Public,game.MainModule.TypeSystem.Object);game.MainModule.Types.Add(saveOwner);Sync("UnrelatedSerializedSave",saveOwner,false);
            string fingerprint=ProtectedTypes.Fingerprint(saveOwner);
            if(mutation==1)native.Parameters[0].ParameterType=game.MainModule.TypeSystem.Int32;
            if(mutation==2)owner.Methods.Remove(async);
            if(mutation==3)Sync("AnotherWriter",owner);
            var changed=new HashSet<string>();var modifications=new List<string>();
            Exception result=Capture(()=>StandaloneStorage.Bind(game,compatibility.MainModule,changed,modifications));
            Check(mutation==0?result==null:result is InvalidDataException,"Native storage ABI control accepted/rejected wrong shape: "+mutation);
            if(mutation==0)
            {
                Check(changed.SetEquals(new[]{owner.FullName,worker.FullName})&&modifications.Count==2,"Storage seam changed unrelated native owners.");
                Check(ProtectedTypes.Fingerprint(saveOwner)==fingerprint,"Storage adapter changed native SaveData/serializer.");
                Check(sync.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="QuestGame.Compatibility.SaveFiles"),"Sync native writer does not reach actual atomic helper.");
            }
        }
    }

    string persistent=Path.Combine(directory,"persistent");Directory.CreateDirectory(persistent);
    string root=Path.Combine(persistent,"GloomSaves");Directory.CreateDirectory(root);
    string campaign=Path.Combine(root,"Campaign.dat");File.WriteAllBytes(campaign+".ghvr-save-backup",original);
    string healthy=Path.Combine(root,"GlobalData.dat");File.WriteAllBytes(healthy,next);File.WriteAllBytes(healthy+".ghvr-save-backup",original);
    File.WriteAllBytes(Path.Combine(root,"bad.dat.ghvr-save-backup"),new byte[32]);
    GloomhavenVR.Core.QuestStandalonePlatform.Enabled=false;ResetRecovery();QuestGameSaveStorage.Initialize(persistent);
    Check(!File.Exists(campaign),"Desktop capability gate mutated saves.");
    GloomhavenVR.Core.QuestStandalonePlatform.Enabled=true;QuestGameSaveStorage.Initialize(persistent);
    Check(File.ReadAllBytes(campaign).SequenceEqual(original)&&File.ReadAllBytes(healthy).SequenceEqual(next),"Recovery changed healthy native bytes or failed missing-file restore.");
    Check(File.ReadAllBytes(campaign+".ghvr-save-backup").SequenceEqual(original)&&!File.Exists(Path.Combine(root,"bad.dat")),"Recovery consumed previous bytes or promoted unusable backup.");
    File.Delete(campaign);QuestGameSaveStorage.Initialize(persistent);Check(!File.Exists(campaign),"Startup recovery repeats during game lifetime.");

    string id="20261005T000000Z-"+Guid.NewGuid().ToString("N");string stage=".quest-save-import-"+Guid.NewGuid().ToString("N");
    string backupRelative="QuestSaveBackups/"+id+"/GloomSaves";string backup=Path.Combine(persistent,backupRelative);Directory.CreateDirectory(Path.GetDirectoryName(backup));Directory.Move(root,backup);
    File.WriteAllText(Path.Combine(persistent,".quest-save-transfer.json"),JsonSerializer.Serialize(new{schema=1,format="gloomhaven-native-dat-snapshot-v1",transferId=id,saveRoot="GloomSaves",staging=stage,backup=backupRelative}));
    ResetRecovery();QuestGameSaveStorage.Initialize(persistent);
    Check(Directory.Exists(root)&&File.ReadAllBytes(healthy).SequenceEqual(next)&&!File.Exists(Path.Combine(persistent,".quest-save-transfer.json")),"Interrupted snapshot root replacement failed rollback.");
    string poisoned=Path.Combine(persistent,".quest-save-transfer.json");File.WriteAllText(poisoned,"{\"schema\":1,\"format\":\"gloomhaven-native-dat-snapshot-v1\",\"saveRoot\":\"../foreign\"}");
    ResetRecovery();Check(Capture(()=>QuestGameSaveStorage.Initialize(persistent)) is InvalidDataException&&File.Exists(healthy),"Escaping recovery journal mutated native saves.");File.Delete(poisoned);
    void Pause(QuestGameSaveLifecycle owner,bool paused)=>typeof(QuestGameSaveLifecycle).GetMethod("OnApplicationPause",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,new object[]{paused});
    SaveData NativeSave()
    {
        FFSNetwork.IsOnline=false;FFSNetwork.IsHost=false;FFSNetwork.IsClient=false;
        PlatformLayer.UserData=new UserData {PlatformAccountID="local",PlatformNetworkAccountPlayerID="network"};
        return SaveData.Instance=new SaveData {Global=new GlobalData {GameMode=EGameMode.Campaign,CurrentAdventureData=new PartyAdventureData {Owner=new SaveOwner {PlatformAccountID="local",PlatformNetworkAccountID="0"}}}};
    }
    SaveData nativeSave=NativeSave();var lifecycle=new QuestGameSaveLifecycle();Pause(lifecycle,false);
    Check(nativeSave.AdventureRequests==0&&nativeSave.GlobalRequests==0,"Unity's initial non-paused lifecycle callback writes a save.");
    nativeSave.DelayAdventure=true;nativeSave.DelayGlobal=true;Pause(lifecycle,true);
    Check(nativeSave.AdventureRequests==1&&nativeSave.GlobalRequests==0,"Pause bypassed original deferred adventure queue.");
    Pause(lifecycle,true);Pause(lifecycle,false);Pause(lifecycle,true);
    Check(nativeSave.AdventureRequests==1,"Repeated/pending pauses enqueue duplicate campaign snapshots.");
    nativeSave.CompleteAdventure();
    Check(nativeSave.GlobalRequests==1&&nativeSave.SaveQueue.IsAnyOperationExecuting,"Global/barrier did not wait for native adventure callback/global queue.");
    int barrierBefore=UnityEngine.Debug.Messages.Count(value=>value.Contains("queue barrier reached"));
    nativeSave.CompleteGlobal();
    Check(!nativeSave.SaveQueue.IsAnyOperationExecuting&&UnityEngine.Debug.Messages.Count(value=>value.Contains("queue barrier reached"))==barrierBefore+1,"Native queue barrier didn't complete/clear original operation exactly once.");
    Pause(lifecycle,false);Pause(lifecycle,true);Check(nativeSave.AdventureRequests==2,"Completed request prevents the next legitimate pause save.");
    NativeSave();SaveData.Instance.Global.CurrentAdventureData.Owner.PlatformAccountID="foreign";
    Pause(new QuestGameSaveLifecycle(),true);
    Check(SaveData.Instance.AdventureRequests==0&&SaveData.Instance.GlobalRequests==1,"Pause overwrites an imported foreign-owned campaign without native local-copy admission.");
    Check(QuestGameSaveLifecycle.IsLocalOwner(new SaveOwner {PlatformAccountID="foreign",PlatformNetworkAccountID="network"})&&!QuestGameSaveLifecycle.IsLocalOwner(new SaveOwner {PlatformAccountID="foreign",PlatformNetworkAccountID="0"}),"Exact native network/account ownership admission changed.");
    NativeSave();FFSNetwork.IsClient=true;FFSNetwork.IsOnline=true;Pause(new QuestGameSaveLifecycle(),true);
    Check(SaveData.Instance.AdventureRequests==0&&SaveData.Instance.GlobalRequests==1,"Multiplayer client pause writes authoritative campaign state.");
    NativeSave();FFSNetwork.IsHost=true;FFSNetwork.IsOnline=true;SaveData.Instance.Global.CurrentAdventureData.Owner.PlatformAccountID="foreign";Pause(new QuestGameSaveLifecycle(),true);
    Check(SaveData.Instance.AdventureRequests==1,"Native authoritative online host pause lost save admission.");
    NativeSave();SaveData.Instance.Global.GameMode=EGameMode.MainMenu;Pause(new QuestGameSaveLifecycle(),true);
    Check(SaveData.Instance.AdventureRequests==0&&SaveData.Instance.GlobalRequests==1,"Main menu pause discarded native preferences or generated campaign state.");
    nativeSave=NativeSave();nativeSave.DelayAdventure=true;lifecycle=new QuestGameSaveLifecycle();Pause(lifecycle,true);
    SaveData replacement=NativeSave();nativeSave.CompleteAdventure();
    Check(nativeSave.GlobalRequests==0&&replacement.GlobalRequests==0,"Old adventure callback wrote a replaced global/save context.");
    NativeSave();SaveData.Instance.SkipAdventure=true;Pause(new QuestGameSaveLifecycle(),true);
    Check(SaveData.Instance.GlobalRequests==1&&UnityEngine.Debug.Messages.Last().Contains("not a durability guarantee"),"Native unsupported map-phase callback was falsely reported as durable campaign success.");
    nativeSave=NativeSave();nativeSave.ThrowAdventure=true;lifecycle=new QuestGameSaveLifecycle();Pause(lifecycle,true);
    Check(nativeSave.GlobalRequests==0&&UnityEngine.Debug.Messages.Last().Contains("request failed"),"Native save exception was hidden or converted to success.");
    nativeSave.ThrowAdventure=false;Pause(lifecycle,false);Pause(lifecycle,true);Check(nativeSave.AdventureRequests==2,"Failed request permanently blocks later native pause saves.");
    NativeSave();GloomhavenVR.Core.QuestStandalonePlatform.Enabled=false;Pause(new QuestGameSaveLifecycle(),true);
    Check(SaveData.Instance.AdventureRequests==0&&SaveData.Instance.GlobalRequests==0,"Desktop capability gate requests native saves.");GloomhavenVR.Core.QuestStandalonePlatform.Enabled=true;
    Console.WriteLine($"Quest local storage: {assertions} assertions passed; real atomic filesystem/worker/ABI/recovery controls. Native Unity/ARM64 campaign continuation remains unverified.");
}
finally{Directory.Delete(directory,true);}
