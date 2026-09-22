using JetBrains.Annotations;
using PrimeTween;
using UnityEngine;

public class DebugInspectorTest : MonoBehaviour {
    [UsedImplicitly]
    private string m_Debug;

    private void Update() {
        if (Time.frameCount == 2) {
            Test();
        }

        UpdateDebug();
    }

    [ContextMenu(nameof(UpdateDebug))]
    private void UpdateDebug() {
        throw new System.Exception();
    }

    private void Test() {
        Sequence.Create().Chain(Tween.Delay(0.5f)).Chain(Tween.Delay(0.5f));
    }
}