#if TEST_FRAMEWORK_INSTALLED
using System.Collections;
using PrimeTween;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public class FramePacingTest : MonoBehaviour {
    private int m_Frame;
    private int m_NumRunning;

    private void Update() {
        if (m_Frame == 0) {
            StartCoroutine(DelayCor());
            StartCoroutine(ZeroFramesAnimationCor());
            StartCoroutine(DelaysInSequenceDurationTest());

            for (var i = 2; i <= 10; i++) {
                StartCoroutine(TestMult(i));
            }

            Assert.AreEqual(m_NumRunning, 12);
        }

        if (m_NumRunning == 0) {
            Assert.AreEqual(0, Tween.StopAll(this));
            Destroy(gameObject);
        }

        m_Frame++;
    }

    private IEnumerator DelaysInSequenceDurationTest() {
        for (var i = 0; i < 1; i++) {
            yield return test();
        }

        IEnumerator test() {
            m_NumRunning++;
            int frameStart = Time.frameCount;
            int numFrames = Random.Range(1, 3);
            float duration = numFrames * (1f / Application.targetFrameRate);

            yield return Tween.Delay(duration)
                              .Chain(Tween.Delay(duration))
                              .ChainCallback(assert)
                              .ToYieldInstruction();

            assert();
            m_NumRunning--;

            void assert() {
                Validate(numFrames * 2, frameStart);
            }
        }
    }

    private IEnumerator DelayCor() {
        m_NumRunning++;
        int frameStart = Time.frameCount;
        float deltaTime = 1f / Application.targetFrameRate;

        yield return Tween.Delay(this, deltaTime)
                          .OnComplete(() => Validate(1, frameStart))
                          .ToYieldInstruction();

        Validate(1, frameStart);
        m_NumRunning--;
    }

    private static void Validate(int expected, int frameStart) {
        var frameDelta = Time.frameCount - frameStart;
        var diff = frameDelta - expected;
        
        /*if (diff != 0) {
            Debug.LogWarning($"frameDelta: {frameDelta}, expected: {expected}");
        } else {
            Debug.Log("ok");
        }*/
        
        Assert.IsTrue(diff == 0 || diff == 1, $"frameDelta: {frameDelta}, expected: {expected}");
    }

    private IEnumerator ZeroFramesAnimationCor() {
        m_NumRunning++;
        int frameStart = Time.frameCount;
        float duration = 1f / Application.targetFrameRate;

        // print($"zeroFramesAnimationCor {duration}");
        yield return Tween.Custom(
                              this,
                              0f,
                              0f,
                              duration,
                              delegate {
                                  // Assert.AreEqual(Time.frameCount, frameStart + 1);
                              }
                          )
                          .OnComplete(() => Validate(1, frameStart))
                          .ToYieldInstruction();

        Validate(1, frameStart);
        m_NumRunning--;
    }

    private IEnumerator TestMult(int numFrames) {
        m_NumRunning++;
        int frameStart = Time.frameCount;
        float duration = 1f / Application.targetFrameRate * numFrames;

        // print($"multipleFrameAnimationCor {numFrames}, {duration}");
        yield return Tween.Custom(
                              this,
                              0f,
                              0f,
                              duration,
                              delegate {
                                  // print(Time.deltaTime);
                              }
                          )
                          .OnComplete(() => Validate(numFrames, frameStart))
                          .ToYieldInstruction();

        Validate(numFrames, frameStart);

        // print($"multipleFrameAnimationCor done {numFrames}");
        m_NumRunning--;
    }
}
#endif