#if GHVR_QUEST_STARTUP
namespace GloomhavenVR.Quest
{
    /// <summary>A diagnostic action requires a tracked neutral sample after loss/resume.</summary>
    internal sealed class QuestGameButtonGate
    {
        bool neutral, previous;
        internal bool Step(bool valid, bool pressed)
        {
            if (!valid) { Reset(); return false; }
            if (!pressed) neutral = true;
            bool activate = neutral && pressed && !previous;
            previous = pressed;
            return activate;
        }
        internal void Reset() { neutral = previous = false; }
    }
}
#endif
