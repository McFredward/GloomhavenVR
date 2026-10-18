using System;
using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;

namespace GloomhavenVR.Cards;

/// <summary>Passive, change-deduplicated guide evidence in the existing startup-state export.
/// Native selection counts, occupancy and actual cue inputs distinguish permission from a
/// missing draw. Never lights a slot, changes native selection or samples textures.</summary>
internal static class QuestCardGuideEvidence
{
    private static readonly List<string> Reports = new(8);
    private static (int owner, int hand, int mask, int first, int second, int held,
        int mode, int remaining, int handCards, int roundCards, int fields, int locked,
        bool blocked, string box, string help, int pickWanted, int renderVersion) _prior;
    private static bool _havePrior;
    private static string? _lastPair;

    internal static void Observe(CardsHandUI? hand, PlayTray tray, int fields, int locked, bool blocked)
    {
        // In particular: no original hand/message/renderer reads or formatting
        // in desktop play or at the normal log level.
        if (!QuestStandalonePlatform.Enabled || !QuestStandalonePlatform.DebugLogging) return;
        try
        {
            VRCard? first = tray.Occupant(0), second = tray.Occupant(1);
            var actor = hand != null ? hand.PlayerActor : null;
            var character = actor != null ? actor.CharacterClass : null;
            LevelMessagesUIHandler? messages = LevelMessagesUIHandler.s_Instance;
            string box = messages != null ? messages.CurrentlyDisplayedBoxMessage?.MessageName ?? "none" : "none";
            string help = messages != null ? messages.CurrentlyDisplayedHelpTextMessage?.MessageName ?? "none" : "none";
            var mode = hand != null ? CardsGameApi.Mode(hand) : default;
            int pickWanted = hand != null && CardsGameApi.IsPickMode(mode) ? CardsGameApi.PickCardsWanted() : -1;
            var current = (tray.QuestGuideOwnerId, hand != null ? hand.GetInstanceID() : 0,
                tray.WantedSlotMask, first != null ? first.GetInstanceID() : 0, second != null ? second.GetInstanceID() : 0,
                (first != null && first.IsHeld ? 1 : 0) | (second != null && second.IsHeld ? 2 : 0),
                hand != null ? (int)mode : -1,
                hand != null && mode == CardHandMode.CardsSelection ? CardsGameApi.SelectionCardsStillWanted(hand) : -1,
                character != null ? character.HandAbilityCards.Count : -1,
                character != null ? character.RoundAbilityCards.Count : -1, fields, locked, blocked, box, help, pickWanted,
                tray.QuestGuideRenderVersion);
            if (_havePrior && current.Equals(_prior)) return;
            _havePrior = true;
            _prior = current;
            Add("frame=" + UnityEngine.Time.frameCount + " owner=" + current.Item1 + " hand=" + current.Item2
                + " mode=" + (hand != null ? mode.ToString() : "none")
                + " wanted=" + current.Item3 + " occupied=" + (first != null ? "1" : "0") + (second != null ? "1" : "0")
                + " held=" + current.Item6 + " selectionRemaining=" + current.Item8 + " pickWanted=" + pickWanted
                + " handCards=" + current.Item9 + " roundCards=" + current.Item10
                + " picks=" + fields + " locked=" + locked + " blocked=" + blocked
                + " box=" + Short(box) + " help=" + Short(help)
                + " left={" + tray.QuestGuideDrawState(0) + "} right={" + tray.QuestGuideDrawState(1) + "}",
                current.Item3 == 3);
        }
        catch (Exception error)
        {
            // Retain one concise failure, without interrupting the native update.
            string failure = "guide-observation-unavailable=" + error.GetType().Name;
            if (Reports.Count == 0 || Reports[Reports.Count - 1] != failure) Add(failure);
        }
    }

    private static string Short(string text) => text.Length <= 32 ? text : text.Substring(0, 32);

    private static void Add(string facts, bool pair = false)
    {
        // Bounded independently of potentially long native names and repeat visits.
        while (Encoding.UTF8.GetByteCount(facts) > 1024) facts = facts.Substring(0, facts.Length - 1);
        if (pair) _lastPair = facts;
        Reports.Add(facts);
        // Late action/lose-card transitions evicted every wanted=3 activation
        // from B691's capture. Preserve the last actual pair, including its
        // head-submission update, inside the same four-KiB budget. This is not a
        // statement that either fragment passed its final depth/blend test.
        int bytes = _lastPair != null && _lastPair != facts ? Encoding.UTF8.GetByteCount(_lastPair) : 0;
        for (int i = Reports.Count - 1; i >= 0; i--)
        {
            bytes += Encoding.UTF8.GetByteCount(Reports[i]);
            if (bytes > 4096 || Reports.Count - i > (_lastPair != null && _lastPair != facts ? 7 : 8)) { Reports.RemoveRange(0, i + 1); break; }
        }
        var snapshot = new List<string>(Reports);
        if (_lastPair != null && !snapshot.Contains(_lastPair)) snapshot.Insert(0, _lastPair);
        QuestStandalonePlatform.CardGuideDiagnostics = snapshot.ToArray();
    }
}
