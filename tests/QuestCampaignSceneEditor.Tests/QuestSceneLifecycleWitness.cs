using UnityEngine;

// Original scene-open callbacks are measured, not inferred from preview status.
[ExecuteInEditMode]
public sealed class QuestSceneLifecycleWitness : MonoBehaviour
{
    public static int awakes, enables, disables;
    public Object retainedReference;
    private void Awake() { awakes++; }
    private void OnEnable() { enables++; }
    private void OnDisable() { disables++; }
    public static void Reset() { awakes = enables = disables = 0; }
}
