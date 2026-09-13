using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// The harness exercises the production decision with Unity's destroyed-object null behavior.
// It does not simulate coroutines, rendering, scene discovery or native animation.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object? value) => value != null;
        public static bool operator ==(Object? left, Object? right)
        {
            bool leftNull = ReferenceEquals(left, null);
            bool rightNull = ReferenceEquals(right, null);
            if (leftNull && rightNull) return true;
            if (leftNull) return right!.Destroyed;
            if (rightNull) return left!.Destroyed;
            return ReferenceEquals(left, right);
        }
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? other) => other is Object value && this == value;
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    public class MonoBehaviour : Object { }

    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Object> _components = new();
        public bool activeSelf = true;
        public T AddComponent<T>() where T : Object, new()
        {
            var component = new T();
            _components[typeof(T)] = component;
            return component;
        }
        public T? GetComponent<T>() where T : Object
            => _components.TryGetValue(typeof(T), out Object? component) ? (T)component : null;
    }
}

public sealed class UIManager : UnityEngine.MonoBehaviour
{
    public static UIManager? Instance;
    public HashSet<UnityEngine.GameObject> elementsLockUI = new();
}

public sealed class CardsHandUI : UnityEngine.MonoBehaviour
{
    public bool AnimatingLostCards;
}
