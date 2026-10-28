# Quest keyboard admission

B619's Quest capture reports an unavailable Oculus overlay keyboard and a visible
native key layout whose characters do not reach the field. The owned TMP's
`InPlaceEditing` rejects `Append(char)` before validation on Android when the
software keyboard is supported and both authored hide flags are false.

The existing VR keyboard acquires a reversible `shouldHideSoftKeyboard` lease
only through `QuestStandalonePlatform.Enabled`. Native `ProcessEvent`, validation,
character limits, `onValueChanged`, Enter and Escape remain in the existing path.
The lease restores the original flag on field switching, dismissal, window close
and teardown, with native changes taking precedence. Desktop acquires no lease.

Run `python3 scripts/quest-keyboard-input-tests.py`. Optional
`--game-managed PATH --unity-managed PATH` compiles the production lease against
the owned TMP API and inspects the original CIL call relationships.

The managed fixture runs the full production `VRKeyboard`, using narrow scene,
event and TMP admission seams. It checks native callback routing through that
seam, not actual Unity text rendering or the Android software keyboard. Eight
mutation controls prove the checks reject missing admission, desktop leakage,
failed restoration, a second writer and bypassed native events. The SDK probe
constructs no engine objects and calls no engine or gameplay callbacks.

Hardware still needs to confirm visible insertion and campaign/invite submission.
No overlay-keyboard manifest feature is required: the existing in-game keyboard
provides the input, and TMP stops requesting another software keyboard while owned.
