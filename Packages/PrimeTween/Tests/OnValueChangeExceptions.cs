#if TEST_FRAMEWORK_INSTALLED
using System;
using System.Collections;
using System.Text.RegularExpressions;
using PrimeTween;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

public partial class Tests {
    [UnityTest]
    public IEnumerator ExceptionInTheMiddle() {
        ExpectTweenWasStoppedBecauseException();
        ExpectOnCompleteIgnored();
        var onCompleteCalled = false;

        var tweener = Tween.Custom(m_Transform, 0, 1, 0.1f, delegate { throw new Exception("TEST"); })
                           .OnComplete(() => { onCompleteCalled = true; });

        while (tweener.isAlive) {
            yield return null;
        }

        Assert.IsFalse(onCompleteCalled);
    }

    [UnityTest]
    public IEnumerator ExceptionOnLastFrame() {
        ExpectTweenWasStoppedBecauseException();
        ExpectOnCompleteIgnored();

        var tweener = Tween.Custom(
                               m_Transform,
                               0,
                               1,
                               GetDt() * 4f,
                               (_, val) => {
                                   if (val > 0.99f) {
                                       throw new Exception("TEST");
                                   }
                               }
                           )
                           .OnComplete(Assert.Fail);

        while (tweener.isAlive) {
            yield return null;
        }
    }

    private static void ExpectOnCompleteIgnored() =>
        LogAssert.Expect(LogType.Error, new Regex(Constants.kOnCompleteCallbackIgnored));

    private static void ExpectTweenWasStoppedBecauseException() {
        LogAssert.Expect(LogType.Exception, new Regex(".*"));
        LogAssert.Expect(LogType.Warning, new Regex("Tween was stopped because of exception"));
    }
}
#endif