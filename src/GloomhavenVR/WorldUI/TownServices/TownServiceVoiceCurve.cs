using System;
using System.Globalization;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Offline sound-derived mouth shapes with short coarticulation, never word guessing.</summary>
internal sealed class TownServiceVoiceCurve
{
    [Serializable] public sealed class Document
    {
        public int schema = 0, cue = 0, service = 0;
        public string language = string.Empty;
        public float duration = 0f;
        public string packed = string.Empty;
        public Interval[] mouthCues = null!;
    }
    [Serializable] public sealed class Interval
    {
        public float start = 0f, end = 0f;
        public string value = string.Empty;
    }
    private readonly Document _data;
    internal float Duration => _data.duration;
    internal Interval[] Intervals => _data.mouthCues;
    private TownServiceVoiceCurve(Document data) { _data = data; }

    internal static TownServiceVoiceCurve? Parse(string json, ushort cue)
    {
        Document data = JsonUtility.FromJson<Document>(json);
        // Unity 2021's JsonUtility can deserialize the scalar cue metadata from
        // this TextAsset but silently leaves a nested Interval[] empty when that
        // class is loaded from the mod DLL in the Editor/Mono player. The offline
        // exporter therefore also writes a compact, culture-independent interval
        // column. It is validated here before any face animation can use it.
        if (data == null || String.IsNullOrEmpty(data.packed)) return null;
        string[] pieces = data.packed.Split(';');
        if (pieces.Length == 0 || pieces.Length > 1024) return null;
        var intervals = new Interval[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            string[] fields = pieces[i].Split(',');
            if (fields.Length != 3 || !float.TryParse(fields[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float start)
                || !float.TryParse(fields[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float end)) return null;
            intervals[i] = new Interval { start = start, end = end, value = fields[2] };
        }
        data.mouthCues = intervals;
        if (data == null || data.schema != 1 || data.cue != cue ||
            data.service != TownServiceVoice.ServiceForCue(cue) || data.language != "en" ||
            !TownServiceVoiceSchedule.Finite(data.duration) ||
            data.duration <= 0f || data.duration > 30f || data.mouthCues == null ||
            data.mouthCues.Length == 0 || data.mouthCues.Length > 1024) return null;
        float previous = 0f;
        foreach (Interval item in data.mouthCues)
        {
            if (item == null || !TownServiceVoiceSchedule.Finite(item.start) || !TownServiceVoiceSchedule.Finite(item.end)
                || item.start < previous - .011f || item.end <= item.start || item.end > data.duration + .011f
                || item.value == null || item.value.Length != 1 || "ABCDEFGHX".IndexOf(item.value[0]) < 0) return null;
            previous = item.end;
        }
        return new TownServiceVoiceCurve(data);
    }

    internal Vector3 At(float age)
    {
        if (!TownServiceVoiceSchedule.Finite(age) || age < 0f || age >= Duration) return Vector3.zero;
        Interval[] cues = _data.mouthCues;
        for (int i = 0; i < cues.Length; i++)
        {
            Interval cue = cues[i];
            if (age < cue.start) return Vector3.zero;
            if (age >= cue.end) continue;
            Vector3 current = Shape(cue.value[0]);
            // Coarticulate around both phoneme boundaries on the shared cue
            // timeline. The old 35 ms leading blend followed by a hard held pose
            // caused visible jaw steps, especially with short Rhubarb intervals.
            // Keep A/X closures genuine in the middle of their interval.
            float left = Mathf.Min(.07f, (cue.end - cue.start) * .3f);
            float right = left;
            bool joinedLeft = i > 0 && cues[i - 1].end >= cue.start - .011f;
            bool joinedRight = i + 1 < cues.Length && cues[i + 1].start <= cue.end + .011f;
            Vector3 previous = joinedLeft ? Shape(cues[i - 1].value[0]) : Vector3.zero;
            Vector3 next = joinedRight ? Shape(cues[i + 1].value[0]) : Vector3.zero;
            if (age < cue.start + left)
                return Vector3.Lerp(Vector3.Lerp(previous, current, joinedLeft ? .5f : 0f), current,
                    Mathf.SmoothStep(0f, 1f, (age - cue.start) / left));
            if (age >= cue.end - right)
                return Vector3.Lerp(current,
                    Vector3.Lerp(current, next, joinedRight ? .5f : 1f),
                    Mathf.SmoothStep(0f, 1f, (age - (cue.end - right)) / right));
            return current;
        }
        return Vector3.zero;
    }

    private static Vector3 Shape(char shape)
    {
        switch (shape)
        {
            case 'B': return new Vector3(.12f, .25f, 0f);
            case 'C': return new Vector3(.35f, .65f, 0f);
            case 'D': return new Vector3(.7f, .3f, 0f);
            case 'E': return new Vector3(.4f, 0f, .75f);
            case 'F': return new Vector3(.15f, 0f, .95f);
            case 'G': return new Vector3(.05f, .15f, 0f);
            case 'H': return new Vector3(.25f, .3f, .05f);
            default: return Vector3.zero;
        }
    }
}
