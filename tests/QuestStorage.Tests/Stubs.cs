using System.Text.Json;

namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled=true; internal static bool DebugLogging=false; }
}
namespace QuestGame.Compatibility
{
    public static class Paths { public static string Persistent; public static string get_persistentDataPath()=>Persistent; }
}
namespace GloomhavenVR.Quest
{
    internal static class QuestGameSaveValidation { internal static int Invocations; internal static void VerifyOriginalRootAndOwner()=>Invocations++; }
}
namespace UnityEngine
{
    public class MonoBehaviour { protected static void DontDestroyOnLoad(GameObject value) { } }
    public class GameObject
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T:new()=>new T();
    }
    internal static class JsonUtility
    {
        internal static T FromJson<T>(string raw)=>JsonSerializer.Deserialize<T>(raw,new JsonSerializerOptions { IncludeFields=true });
    }
    internal static class Debug
    {
        internal static readonly List<string> Messages=new();
        internal static void Log(object value)=>Messages.Add(value.ToString());
        internal static void LogWarning(object value)=>Messages.Add(value.ToString());
        internal static void LogError(object value)=>Messages.Add(value.ToString());
    }
}

public enum EGameMode { MainMenu,Campaign,Guildmaster }
public class SaveOwner
{
    public string PlatformNetworkAccountID,PlatformAccountID;
}
public class UserData
{
    public string PlatformNetworkAccountPlayerID,PlatformAccountID;
}
public static class PlatformLayer { public static UserData UserData; }
public static class FFSNetwork { public static bool IsClient,IsHost,IsOnline; }
public class PartyAdventureData { public SaveOwner Owner; }
public class GlobalData { public EGameMode GameMode;public PartyAdventureData CurrentAdventureData; }
public class SaveQueue
{
    readonly Queue<Action<Action>> operations=new();
    bool executing;
    public bool IsAnyOperationExecuting=>executing;
    public void Enqueue(Action<Action> operation) { operations.Enqueue(operation);Process(); }
    void Process()
    {
        if(executing||operations.Count==0)return;
        executing=true;
        operations.Dequeue()(()=>{executing=false;Process();});
    }
}
public class SaveData
{
    public static SaveData Instance;
    public GlobalData Global;
    public bool GameBootedForAutoTests;
    public readonly SaveQueue SaveQueue=new();
    public int AdventureRequests,GlobalRequests;
    public Action CompleteAdventure,CompleteGlobal;
    public bool DelayAdventure,DelayGlobal,ThrowAdventure,SkipAdventure;
    public void SaveCurrentAdventureData(Action onDone)
    {
        AdventureRequests++;
        if(ThrowAdventure)throw new IOException("native request failure");
        if(SkipAdventure){onDone();return;}
        SaveQueue.Enqueue(done=>
        {
            CompleteAdventure=()=>{onDone();done();};
            if(!DelayAdventure)CompleteAdventure();
        });
    }
    public void SaveGlobalData()
    {
        GlobalRequests++;
        SaveQueue.Enqueue(done=>{CompleteGlobal=done;if(!DelayGlobal)done();});
    }
    public void PerformCustomFileOperation(Action<Action> operation)=>SaveQueue.Enqueue(operation);
}
