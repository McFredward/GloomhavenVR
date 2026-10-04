using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using SpriteMemoryManagement;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Prepare original scenario card resources before releasing the loading gate. Build616
/// recorded 172.37ms inside Cards.Driver on the first Summoner hand switch, including new
/// class strips and ConsumeDark mips. The existing adopted-face queue starts AFTER that
/// synchronous work. This fills the existing shared mip/pin cache and reserves inert VR
/// backings for the whole real party, without selecting a character, creating a native
/// hand, invoking ShowCard/Awake, or assigning anything to an original Image.
///
/// The coordinator owns Begin/Tick/Reset and its spinner. One Tick starts/observes one
/// reference, bakes one sprite, or builds one backing. Failed/late art is terminal for this
/// preparation pass, not a gameplay gate: ordinary native loaders and lazy fallbacks stay
/// available. Pin handles retain their existing map/scenario teardown owner. Reset releases
/// only this job and unused backing reservations, never a shared/local/remote live face.
/// </summary>
internal static class ScenarioCardPreparation
{
    private const float WaitSeconds = 30f;
    private const int MaxBackings = 64;
    private const int MaxResources = 2048;
    private static readonly Queue<Sprite> Sprites = new();
    private static readonly Queue<ReferenceToSprite> References = new();
    private static readonly HashSet<int> SeenSprites = new();
    private static readonly HashSet<ReferenceToSprite> StartedReferences = new();
    private static readonly HashSet<ReferenceToSprite> SeenReferences = new();
    private static readonly HashSet<AbilityCardUISkin> SeenSkins = new();
    private static readonly Dictionary<Type, FieldInfo[]> SpriteFields = new();
    private static VRCardFactory? s_factory;
    private static bool s_started;
    private static bool s_collected;
    private static float s_deadline;

    internal static bool IsReady { get; private set; } = true;
    internal static int Classes => SeenSkins.Count;
    internal static int SpritesPrepared { get; private set; }
    internal static int SpritesTotal => SeenSprites.Count;
    internal static int ReferencesPending => References.Count;
    internal static int BackingsPrepared => s_factory?.PreparedBlankCount ?? 0;
    internal static int BackingsTarget { get; private set; }
    internal static int Failures { get; private set; }

    internal static void Begin()
    {
        Reset();
        s_started = true;
        IsReady = false;
        s_deadline = Time.realtimeSinceStartup + WaitSeconds;
    }

    internal static void Reset()
    {
        s_factory?.ClearPreparedBlanks();
        s_factory = null;
        Sprites.Clear();
        References.Clear();
        SeenSprites.Clear();
        SeenReferences.Clear();
        StartedReferences.Clear();
        SeenSkins.Clear();
        s_started = false;
        s_collected = false;
        IsReady = true;
        SpritesPrepared = 0;
        BackingsTarget = 0;
        Failures = 0;
    }

    internal static void Tick()
    {
        if (!s_started || IsReady) return;
        using var scope = PerfMonitor.Scope("Cards.ScenarioPreparation");
        try
        {
            if (!s_collected)
            {
                UIInfoTools? tools = UIInfoTools.Instance;
                List<CPlayerActor>? players = ScenarioManager.Scenario?.PlayerActors;
                s_factory = VRCardFactory.PreparationFactory;
                if (tools == null || players == null || players.Count == 0 || s_factory == null)
                {
                    if (Time.realtimeSinceStartup >= s_deadline) Finish(unavailable: true);
                    return;
                }
                CollectRoster(players, tools);
                CollectSpriteFields(tools, includeReferences: false);
                s_collected = true;
                // Bound only asynchronous waiting, not the amount of legitimate loading work.
                s_deadline = Time.realtimeSinceStartup + WaitSeconds;
                return;
            }
            if (!ReferenceEquals(s_factory, VRCardFactory.PreparationFactory))
            {
                Finish(unavailable: true);
                return;
            }
            // Start all addressable references before spending time on synchronous mip copies.
            // Revisit incomplete references at most once per frame, after ready sprites/backings.
            if (References.Count > 0 && StartedReferences.Count < SeenReferences.Count)
            {
                PrepareReference();
                return;
            }
            if (Sprites.Count > 0)
            {
                Sprite sprite = Sprites.Dequeue();
                CardFaceMipBake.WarmSprite(sprite);
                SpritesPrepared++;
                PerfMonitor.Count("Cards.PreparedSprites");
                return;
            }
            if (s_factory != null && s_factory.PreparedBlankCount < BackingsTarget)
            {
                s_factory.PrepareOneBlank();
                return;
            }
            if (References.Count > 0)
            {
                PrepareReference();
                return;
            }
            Finish(unavailable: false);
        }
        catch (Exception error)
        {
            // A resource/preparation failure cannot trap the player behind the spinner.
            Failures++;
            VRLog.Warn("Cards", $"SCENARIO CARD PREPARATION failed ({error.GetType().Name}: {error.Message}); ordinary native card loading remains available.");
            Finish(unavailable: false);
        }
    }

    private static void CollectRoster(List<CPlayerActor> players, UIInfoTools tools)
    {
        using var scope = PerfMonitor.Scope("Cards.PrepareRoster");
        int backings = 0;
        foreach (CPlayerActor player in players)
        {
            CCharacterClass? character = player?.CharacterClass;
            if (character == null) continue;
            List<CAbilityCard>? selected = character.SelectedAbilityCards;
            List<CAbilityCard>? hand = character.HandAbilityCards;
            int count = Math.Max(selected?.Count ?? 0, hand?.Count ?? 0);
            backings = Math.Min(MaxBackings, backings + count);
            // A transferred/supply ability can have another class skin. Read all native pools;
            // do not force focus or synthesize a hand to discover it.
            CollectCards(character.AbilityCardsPool, tools);
            CollectCards(selected, tools);
            CollectCards(hand, tools);
        }
        BackingsTarget = Math.Max(0, backings - (s_factory?.All.Count ?? 0));
    }

    private static void CollectCards(List<CAbilityCard>? cards, UIInfoTools tools)
    {
        if (cards == null) return;
        foreach (CAbilityCard card in cards)
        {
            if (card == null) continue;
            AbilityCardUISkin? skin = tools.GetCardSkin(card.ClassModel, card.ClassCharacterConfig);
            if (skin != null && SeenSkins.Add(skin)) CollectSpriteFields(skin, includeReferences: true);
        }
    }

    /// <summary>Read serialized native sprite fields only. Reflection also includes private
    /// shared action/XP icon arrays. No getters, object hierarchy discovery, or lifecycle call
    /// is used. Element/condition configs are the native card-icon sources, not fabricated art.</summary>
    private static void CollectSpriteFields(object owner, bool includeReferences)
    {
        Type type = owner.GetType();
        if (!SpriteFields.TryGetValue(type, out FieldInfo[] fields))
        {
            var selected = new List<FieldInfo>();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (field.FieldType == typeof(Sprite) || field.FieldType == typeof(Sprite[])
                    || field.FieldType == typeof(ReferenceToSprite)
                    || field.FieldType == typeof(ElementConfigUI) || field.FieldType == typeof(UIInfoTools.EffectInfo))
                    selected.Add(field);
            fields = selected.ToArray();
            SpriteFields[type] = fields;
        }
        foreach (FieldInfo field in fields)
        {
            object? value = field.GetValue(owner);
            if (value is Sprite sprite) AddSprite(sprite);
            else if (value is Sprite[] sprites)
            {
                foreach (Sprite entry in sprites) AddSprite(entry);
            }
            else if (includeReferences && value is ReferenceToSprite reference
                && SeenReferences.Count < MaxResources && SeenReferences.Add(reference)) References.Enqueue(reference);
            else if (value is ElementConfigUI element) CollectSpriteFields(element, includeReferences: false);
            else if (value is UIInfoTools.EffectInfo info) CollectSpriteFields(info, includeReferences: false);
        }
    }

    private static void AddSprite(Sprite? sprite)
    {
        if (sprite == null || SeenSprites.Count >= MaxResources) return;
        Sprite original = CardFaceMipBake.OriginalFor(sprite);
        if (SeenSprites.Add(original.GetInstanceID())) Sprites.Enqueue(original);
    }

    private static void PrepareReference()
    {
        ReferenceToSprite reference = References.Dequeue();
        if (StartedReferences.Add(reference)) CardArtPin.PinReference(reference);
        Sprite? sprite = CardArtPin.PreparedSprite(reference, out bool pending);
        if (sprite != null) AddSprite(sprite);
        else if (pending && Time.realtimeSinceStartup < s_deadline) References.Enqueue(reference);
        else Failures++;
    }

    private static void Finish(bool unavailable)
    {
        if (unavailable) Failures++;
        IsReady = true;
        References.Clear();
        if (VRLog.WantsDebug)
            VRLog.Debug("Cards", $"SCENARIO CARD PREPARATION ready: classes={Classes}, sprites={SpritesPrepared}/{SpritesTotal}, backings={BackingsPrepared}/{BackingsTarget}, failures={Failures}, unavailable={unavailable}; original widget state and shared card caches retained.");
    }
}
