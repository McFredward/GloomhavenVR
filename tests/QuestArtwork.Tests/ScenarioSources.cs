using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static void ResetArtworkRecords()
    {
        ((IDictionary)typeof(QuestArtworkEvidence).GetField("Seen", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Clear();
        typeof(QuestArtworkEvidence).GetField("_records", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0);
    }

    private static void QualifyScenarioSources()
    {
        ResetArtworkRecords();
        var records = new List<string>();
        Application.LogCallback observe = (message, trace, type) => {
            if (message.StartsWith("[Quest startup] artwork ", StringComparison.Ordinal)) records.Add(message);
        };
        Application.logMessageReceived += observe;
        bool debug = VRLog.WantsDebug;
        try
        {
            VRLog.WantsDebug = true;
            QuestArtworkEvidence.ScenarioSources();
            Check(records.Count == 3 && records[0].Contains("unavailable=image")
                && records[0].Contains("caller=scenario-census"),
                "scenario callback reports missing native sources without an adoption callback");
            var liveRoot = Keep(new GameObject("live-native-face"));
            var face = liveRoot.AddComponent<FullAbilityCard>();
            var headerRoot = Keep(new GameObject("live-native-header", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            headerRoot.transform.SetParent(liveRoot.transform, false);
            face.headerImage = headerRoot.GetComponent<Image>();
            var liveTexture = Pattern("live-native-card", 53);
            face.headerImage.sprite = SpriteOf(liveTexture, new Rect(0, 0, 32, 24));
            face.headerImage.enabled = false;

            var poolRoot = Keep(new GameObject("parked-native-template"));
            var pooled = poolRoot.AddComponent<FullAbilityCard>();
            var poolImageRoot = Keep(new GameObject("pooled-template-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            poolImageRoot.transform.SetParent(poolRoot.transform, false);
            pooled.headerImage = poolImageRoot.GetComponent<Image>();
            pooled.headerImage.sprite = SpriteOf(Pattern("wrong-parked-template", 71), new Rect(0, 0, 32, 24));
            poolRoot.SetActive(false);

            var trackRoot = Keep(new GameObject("actual-native-track"));
            InitiativeTrack.Instance = trackRoot.AddComponent<InitiativeTrack>();
            var avatarRoot = Keep(new GameObject("native-avatar", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)));
            avatarRoot.transform.SetParent(trackRoot.transform, false);
            var avatar = avatarRoot.AddComponent<InitiativeTrackActorAvatar>();
            var raw = avatarRoot.GetComponent<RawImage>(); raw.texture = Pattern("actual-avatar", 22);
            raw.uvRect = new Rect(.125f, .25f, .5f, .5f);
            var frameRoot = Keep(new GameObject("native-selection-frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            frameRoot.transform.SetParent(avatarRoot.transform, false);
            avatar.selectionImage = frameRoot.GetComponent<Image>();
            avatar.selectionImage.sprite = SpriteOf(Pattern("actual-native-frame", 17), new Rect(0, 0, 32, 24));

            QuestArtworkEvidence.ScenarioSources();
            Check(records.Count == 6 && records[3].Contains("source=live-native-card")
                && records[3].Contains("enabled=False") && records[3].Contains("caller=scenario-census"),
                "scenario census reaches actual native header without adoption callback");
            Check(records[4].Contains("source=actual-avatar") && records[4].Contains("uv=0.125,0.250,0.500,0.500")
                && records[5].Contains("source=actual-native-frame"),
                "scenario census observes exact native avatar crop and frame consumer independently");
            Check(!face.headerImage.enabled && liveRoot.activeSelf && !poolRoot.activeSelf
                && face.headerImage.sprite.texture == liveTexture && raw.uvRect == new Rect(.125f, .25f, .5f, .5f),
                "scenario source discovery never changes native activation, sprite or portrait crop");
            QuestArtworkEvidence.ScenarioSources();
            Check(records.Count == 6, "unchanged fallback sources share consumer deduplication");
            var statRoot = Keep(new GameObject("native-stat-panel"));
            var stat = statRoot.AddComponent<ActorStatPanel>();
            stat.characterPortrait = face.headerImage;
            Singleton<ActorStatPanel>.Instance = stat;
            QuestArtworkEvidence.ScenarioSources();
            Check(records.Count == 7 && records[6].Contains("role=stat-portrait"),
                "initialized native stat panel remains reachable without converted-watch state");
            face.headerImage.enabled = true;
            QuestArtworkEvidence.ScenarioSources();
            Check(records.Count == 8, "fallback and direct consumers retain the same eight-record hard ceiling");
            foreach (string record in records)
                Check(System.Text.Encoding.UTF8.GetByteCount(record) <= 1000, "fallback source payload fits Android logcat");
        }
        finally
        {
            InitiativeTrack.Instance = null;
            Singleton<ActorStatPanel>.Instance = null!;
            Application.logMessageReceived -= observe;
            VRLog.WantsDebug = debug;
            ResetArtworkRecords();
        }
    }
}
