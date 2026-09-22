#if UNITY_EDITOR && TEST_FRAMEWORK_INSTALLED
using System;
using PrimeTween;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

[ExecuteInEditMode]
public partial class EditModeTest : MonoBehaviour {
    [SerializeField]
    private TweenSettings m_Settings = CreateSettings();

    private static TweenSettings CreateSettings() {
        TweenSettings res = default;

        if (!PrimeTweenManager.HasInstance) {
            ExpectException(() => res = new TweenSettings(1, AnimationCurve.Linear(0, 0, 1, 1)));
        }

        return res;
    }

    private static Tween TestWithPossibleException() {
        if (PrimeTweenManager.HasInstance) {
            Tween.StopAll();
            return Test();
        }

        ExpectException(() => Tween.StopAll());
        ExpectException(() => Sequence.Create());
        ExpectException(() => PrimeTweenConfig.SetTweensCapacity(PrimeTweenManager.Instance.CurrentPoolCapacity + 1));
        ExpectException(() => PrimeTweenConfig.warnZeroDuration = !PrimeTweenConfig.warnZeroDuration);
        ExpectException(() => Tween.GetTweensCount());

        ExpectException(() => {
                Sequence.Create()
                        .ChainCallback(() => { })
                        .InsertCallback(0f, delegate { })
                        .Group(StartTween())
                        .Chain(StartTween())
                        .Insert(0f, Sequence.Create())
                        .Insert(0, StartTween());
            }
        );

        ExpectException(() => Tween.Delay(new object(), 1f, () => { }));
        ExpectException(() => Tween.Delay(new object(), 1f, _ => { }));
        ExpectException(() => Tween.Delay(1f, () => { }));
        ExpectException(() => Tween.Custom(0, 1, 1, delegate { }));
        return default;
    }

    private static void ExpectException(Action action) {
        try {
            action();
        } catch (Exception e) {
            string message = e.Message;
            Assert.IsTrue(message.Contains("is not allowed to be called from a MonoBehaviour constructor"), message);
        }
    }

    private static Tween Test() {
        PrimeTweenConfig.SetTweensCapacity(PrimeTweenManager.Instance.CurrentPoolCapacity + 1);
        Assert.DoesNotThrow(() => PrimeTweenConfig.warnZeroDuration = false);
        
        Tween.GetTweensCount();

        Sequence.Create()
                .ChainCallback(() => { })
                .InsertCallback(0f, delegate { })
                .Group(StartTween())
                .Chain(StartTween())
                .Insert(0f, Sequence.Create())
                .Insert(0, StartTween());

        Tween.Delay(new object(), 1f, () => { });
        Tween.Delay(new object(), 1f, _ => { });
        Tween.Delay(1f, () => { });
        return Tween.Custom(0, 1, 1, delegate { });
    }

    private static Tween StartTween() => Tween.Custom(0f, 1f, 1f, delegate { });

    private void Awake() => TestWithPossibleException();
    private void OnValidate() => TestWithPossibleException();
    private void Reset() => TestWithPossibleException();
    private void OnEnable() => TestWithPossibleException();
    private void OnDisable() => TestWithPossibleException();
    private void OnDestroy() => Test();
}
#endif