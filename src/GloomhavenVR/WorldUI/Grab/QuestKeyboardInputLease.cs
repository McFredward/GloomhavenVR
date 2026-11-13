using GloomhavenVR.Core;
using TMPro;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// While the existing VR keyboard owns a Quest field, use TMP's own in-place editing path.
/// The B619 headset capture reports an unavailable Oculus overlay keyboard and a visible
/// native key layout that cannot insert text. The owned game's TMP Append(char) returns
/// before validation when TouchScreenKeyboard is supported and both authored hide flags
/// are false. Hiding the software keyboard admits the existing ProcessEvent path and
/// prevents ActivateInputFieldInternal from requesting a second, platform keyboard.
/// No characters, callbacks, validation rules or focus are replaced. Desktop never leases.
/// </summary>
internal sealed class QuestKeyboardInputLease
{
    private TMP_InputField? _field;
    private readonly bool _previousHideSoftKeyboard;

    private QuestKeyboardInputLease(TMP_InputField field)
    {
        _field = field;
        _previousHideSoftKeyboard = field.shouldHideSoftKeyboard;
        if (!_previousHideSoftKeyboard)
            field.shouldHideSoftKeyboard = true;
    }

    internal static QuestKeyboardInputLease? TryAcquire(TMP_InputField field)
        => QuestStandalonePlatform.Enabled ? new QuestKeyboardInputLease(field) : null;

    /// <summary>Restore only our change, once; a native change made meanwhile keeps precedence.</summary>
    internal void Release()
    {
        TMP_InputField? field = _field;
        _field = null;
        if (field != null && !_previousHideSoftKeyboard && field.shouldHideSoftKeyboard)
            field.shouldHideSoftKeyboard = false;
    }
}
