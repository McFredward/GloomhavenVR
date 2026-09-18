using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Require(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    private static AbilityCardUI Card()
    {
        var widget = new AbilityCardUI();
        widget.fullAbilityCard.transform.SetParent(widget.transform, false);
        widget.fullAbilityCard.topActionButton.transform.SetParent(widget.fullAbilityCard.transform, false);
        widget.fullAbilityCard.bottomActionButton.transform.SetParent(widget.fullAbilityCard.transform, false);
        return widget;
    }

    public static void Main()
    {
        for (int repeat = 0; repeat < 12; repeat++)
        {
            var widget = Card();
            Require(NativeCardPoolLifetime.IsIntact(widget), "A native complete card must remain reusable");
            var temporaryDialog = new GameObject();
            // The prior scene-release harness's Detach stub always returned to Owner. The
            // production CardFace instead restores its captured parent, including this dialog.
            widget.fullAbilityCard.transform.SetParent(temporaryDialog.transform, false);
            var manager = new CardsHandManager();
            CardsHandManager.Instance = manager;
            manager.cardHandsUI.Add(new CardsHandUI());
            manager.cardHandsUI[0].cardsUI.Add(widget);
            NativeCardPoolLifetime.ReturnSceneHands();
            UnityEngine.Object.Destroy(temporaryDialog);
            Require(NativeCardPoolLifetime.IsIntact(widget), "Dialog-owned face must survive scene teardown inside its native widget");
            Require(widget.fullAbilityCard.transform.parent == widget.transform, "Native face must return to its pooled owner");
            Require(widget.LossCancellations == 1, "Early native return must preserve the pool's loss-animation cancellation");
            NativeCardPoolLifetime.ReturnSceneHands();
            Require(NativeCardPoolLifetime.IsIntact(widget), "Repeated lifetime return must be harmless");

            var template = Card();
            var healthyCopy = Card();
            var damagedCopy = Card();
            UnityEngine.Object.Destroy(damagedCopy.fullAbilityCard.topActionButton.gameObject);
            var pool = new ObjectPool.CardPool { Instances = new() { template.gameObject, healthyCopy.gameObject, damagedCopy.gameObject } };
            NativeCardPoolLifetime.PruneDamagedCopies(pool);
            Require(pool.Instances.Count == 2 && pool.Instances[0] == template.gameObject,
                "Damaged recycled card must be removed without changing the original template");
            Require(!damagedCopy.gameObject.Alive && healthyCopy.gameObject.Alive,
                "Only the corrupt presentation copy may be destroyed");
            NativeCardPoolLifetime.PruneDamagedCopies(pool);
            Require(pool.Instances.Count == 2, "Healthy pool must remain unchanged on repeated spawning");

            var lostFace = Card();
            UnityEngine.Object.Destroy(lostFace.fullAbilityCard.gameObject);
            pool.Instances.Add(lostFace.gameObject);
            NativeCardPoolLifetime.PruneDamagedCopies(pool);
            Require(pool.Instances.Count == 2 && !lostFace.gameObject.Alive,
                "A destroyed full face must retire its surviving pooled root");

            var foreign = Card();
            var other = Card();
            foreign.fullAbilityCard.topActionButton = other.fullAbilityCard.topActionButton;
            Require(!NativeCardPoolLifetime.IsIntact(foreign), "Serialized action references must not alias another card");
            var lostBottom = Card();
            UnityEngine.Object.Destroy(lostBottom.fullAbilityCard.bottomActionButton.gameObject);
            Require(!NativeCardPoolLifetime.IsIntact(lostBottom), "Ordinary cards require both native action halves");
            lostBottom.fullAbilityCard.isLongRestCard = true;
            Require(!NativeCardPoolLifetime.IsIntact(lostBottom), "Native long-rest widgets also require both action components");

            var invalidTemplate = Card();
            UnityEngine.Object.Destroy(invalidTemplate.fullAbilityCard.topActionButton.gameObject);
            pool.Instances = new() { invalidTemplate.gameObject, foreign.gameObject };
            NativeCardPoolLifetime.PruneDamagedCopies(pool);
            Require(pool.Instances.Count == 2 && foreign.gameObject.Alive,
                "No intact template means the repair must not delete the remaining pool");
            pool.CardType = ObjectPool.ECardType.Item;
            pool.Instances = new() { template.gameObject, foreign.gameObject };
            NativeCardPoolLifetime.PruneDamagedCopies(pool);
            Require(pool.Instances.Count == 2, "Other native pool types must be untouched");
        }
        CardsHandManager.Instance = null;
        NativeCardPoolLifetime.ReturnSceneHands();
        NativeCardPoolLifetime.ReturnToOwner(null);
        NativeCardPoolLifetime.PruneDamagedCopies(null);
        Require(!NativeCardPoolLifetime.IsIntact(null), "Null card is not reusable");
        Console.WriteLine($"Native card pool lifetime: {checks} production assertions passed.");
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Alive = true;
        public static bool operator ==(Object? a, Object? b) =>
            (ReferenceEquals(a, null) || !a.Alive) && (ReferenceEquals(b, null) || !b.Alive) || ReferenceEquals(a,b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            value.Alive = false;
            if (value is GameObject go)
            {
                foreach (Component c in go.Components) c.Alive = false;
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
            }
        }
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public GameObject() { transform = new Transform(this); }
        public T GetComponent<T>() where T : Component => (T)Components.Find(c => c is T)!;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public Component() { gameObject = new GameObject(); gameObject.Components.Add(this); }
        protected Component(GameObject owner) { gameObject = owner; owner.Components.Add(this); }
    }
    public class Transform : Component
    {
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public Transform(GameObject owner) : base(owner) { }
        public void SetParent(Transform owner, bool worldPositionStays)
        { parent?.Children.Remove(this); parent = owner; owner.Children.Add(this); }
        public bool IsChildOf(Transform other)
        { for (Transform? t=this; t != null; t=t.parent) if (ReferenceEquals(t,other)) return true; return false; }
    }
}
public class AbilityCardUI : Component
{
    public Component miniAbilityCard = new();
    public FullAbilityCard fullAbilityCard = new();
    public int LossCancellations;
    public void CancelLostAnimation() { LossCancellations++; }
}
public class FullAbilityCard : Component
{
    public FullAbilityCardAction topActionButton = new(), bottomActionButton = new();
    public bool isLongRestCard;
}
public class FullAbilityCardAction : Component { public Component actionButton = new(); }
public class CardsHandUI : Component { public List<AbilityCardUI> cardsUI = new(); }
public class CardsHandManager : Component
{
    public static CardsHandManager? Instance;
    public List<CardsHandUI> cardHandsUI = new();
}
public class ObjectPool
{
    public enum ECardType { Ability, Item }
    public class CardPool
    {
        public ECardType CardType = ECardType.Ability;
        public List<GameObject> Instances = new();
    }
}
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Warn(string scope, string message) { } } }
