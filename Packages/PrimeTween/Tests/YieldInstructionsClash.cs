using System.Collections;
using System.Diagnostics;
using PrimeTween;
using UnityEngine;
using Assert = UnityEngine.Assertions.Assert;
using Debug = UnityEngine.Debug;

internal class YieldInstructionsClash : MonoBehaviour {
    private int m_Frame;

    private void Update() {
        Log($"{Time.frameCount} Update()");

        switch (m_Frame) {
            case 0:
                StartCoroutine(Cor());
                break;
            case 1:
                Tween.Delay(TweenSettings.kMinDuration).ToYieldInstruction();
                break;
        }

        m_Frame++;
    }

    private IEnumerator Cor() {
        Log($"{Time.frameCount} cor start");
        int frameStart = Time.frameCount;
        var t = Tween.Delay(TweenSettings.kMinDuration);
        var enumerator = t.ToYieldInstruction();

        while (enumerator.MoveNext()) {
            var coroutineEnumerator = enumerator as CoroutineIterator;
            Assert.AreEqual(t.id, coroutineEnumerator.tween.id);
            yield return enumerator.Current;
        }

        Destroy(gameObject);
        var diff = Time.frameCount - frameStart;
        Assert.AreEqual(1, diff);
        Log($"{Time.frameCount} cor DONE");
    }

    [Conditional("_")]
    private static void Log(string msg) {
        Debug.Log(msg);
    }
}