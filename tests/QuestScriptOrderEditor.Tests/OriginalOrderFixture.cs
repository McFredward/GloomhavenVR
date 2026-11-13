using UnityEngine;

namespace OriginalOrderFixture
{
    public sealed class Authored : MonoBehaviour { }
    [DefaultExecutionOrder(120)] public sealed class ExplicitZero : MonoBehaviour { }
    public sealed class Late : MonoBehaviour { }
    public static class Utility { }
}
