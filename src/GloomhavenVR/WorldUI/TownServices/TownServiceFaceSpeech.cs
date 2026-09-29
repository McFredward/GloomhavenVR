using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Presentation-only adapter contract. No adapter means a closed, silent mouth.
/// A cue identifies one exact bundled language/voice/curve. Generation changes per utterance.
/// Observers receive the elected author ID; changing authority invalidates their old audio.</summary>
internal static class TownServiceFaceSpeech
{
    internal delegate bool SampleDelegate(byte service, out ushort cue, out uint generation, out float age, out Vector3 mouth);
    internal delegate Vector3 CurveDelegate(byte service, ushort cue, float age);
    internal delegate void ObserveDelegate(byte service, int author, ushort cue, uint generation, float age, Transform head);
    internal static SampleDelegate? Sampler { get; set; }
    internal static CurveDelegate? Curve { get; set; }
    internal static ObserveDelegate? Observer { get; set; }
    internal static System.Action? ResetObserver { get; set; }
}
