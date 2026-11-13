// Executable, Unity-free witnesses for the native store/callback ABI. These
// types never replace or certify the real game's permission implementations.
namespace UnityEngine { public class Component { } }

public class UIAdventurePartyAssemblyWindow : UnityEngine.Component
{
    public object? selectedCharacter = "old";
    public bool Select(object next, bool allowed)
    {
        if (!allowed) { ConfirmationWitness.Trace.Add("rejected"); return false; }
        try { selectedCharacter = next; ConfirmationWitness.Trace.Add("stored:" + selectedCharacter); return true; }
        finally { ConfirmationWitness.Trace.Add("finally"); }
    }
}
public class UIPartyItemInventoryDisplay : UnityEngine.Component
{
    public int slotNum = 3;
    public void Display(int next) { slotNum = next; ConfirmationWitness.Trace.Add("slot:" + slotNum); }
    public virtual void Hide() { ConfirmationWitness.Trace.Add("hide"); }
}
public class UILevelUpWindow : UnityEngine.Component { public virtual void Show() { ConfirmationWitness.Trace.Add("show"); } }
public class NewPartyDisplayUI : UnityEngine.Component
{
    public void Level(UILevelUpWindow window) { window.Show(); }
    public void Close(UIPartyItemInventoryDisplay window) { window.Hide(); }
}
public class DeferredWitness : UnityEngine.Component
{
    public sealed class Callback { public DeferredWitness? Owner; }
    public Callback Create() { var callback = new Callback(); callback.Owner = this; return callback; }
}
public static class ConfirmationWitness
{
    public static readonly List<string> Trace = new();
    public static void BeforeIdentityWrite(UnityEngine.Component owner, object next, string field)
    {
        object? before = owner.GetType().GetField(field)!.GetValue(owner);
        Trace.Add(field + ":" + before + ">" + next);
    }
    public static void NoopIdentity(UnityEngine.Component owner, object next, string field) { }
    public static void BeforeInventoryClose(UnityEngine.Component owner) { Trace.Add("before-hide:" + owner.GetType().Name); }
    public static void BeforePartyPage(UnityEngine.Component owner, string page, bool selected) { Trace.Add(page + ":" + selected + ":" + owner.GetType().Name); }
    public static void CaptureDeferredOrigin(object callback, UnityEngine.Component owner)
    {
        if (!ReferenceEquals(((DeferredWitness.Callback)callback).Owner, owner)) throw new InvalidOperationException("capture-before-store");
        Trace.Add("captured");
    }
}
