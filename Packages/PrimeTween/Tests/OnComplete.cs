#if TEST_FRAMEWORK_INSTALLED
using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

public partial class Tests {
    [Test]
    public void OnCompleteIsCalledImmediatelyAfterCallingComplete() {
        var onCompleteIsCalled = false;
        var t = CreateTween().OnComplete(() => onCompleteIsCalled = true);
        Assert.IsFalse(onCompleteIsCalled);
        t.Complete();
        Assert.IsTrue(onCompleteIsCalled);
    }

    [Test]
    public void OnCompleteDuplicationThrows() {
        var t = CreateTween().OnComplete(() => { });

        try {
            t.OnComplete(() => { });
        } catch (Exception e) {
            Assert.IsTrue(e.Message.Contains("Tween already has an onComplete callback"));
            return;
        }

        Assert.Fail();
    }

    [Test]
    public void AddingOnCompleteToInfiniteTween() {
        int numCompleted = 0;
        CreateInfiniteTween().OnComplete(() => numCompleted++).Complete();
        Assert.AreEqual(1, numCompleted);
    }

    private Tween CreateInfiniteTween() {
        return Tween.Custom(this, 0, 1, 0.01f, cycles: -1, onValueChange: delegate { });
    }

    [Test]
    public void AddingOnCompleteOnDeadTweenDisplaysError() {
        var t = CreateTween();
        Assert.IsTrue(t.isAlive);
        t.Complete();
        Assert.IsFalse(t.isAlive);
        ExpectIsDeadError();
        t.OnComplete(delegate { });
        ExpectIsDeadError();
        t.OnComplete(this, delegate { });
    }

    [Test]
    public async Task OnCompleteTargetDestructionWhileTweenRunning() {
        ExpectOnCompleteIgnored();
        LogAssert.NoUnexpectedReceived();
        var target = new GameObject();

        await Tween.Custom(0, 1, 0.001f, _ => { Object.DestroyImmediate(target); })
                   .OnComplete(target, _ => Assert.Fail());
    }

    [Test]
    public void PassingNullToOnComplete() {
        ExpectOnCompleteIgnored();
        Tween.Delay(k_MinDuration).OnComplete<GameObject>(null, _ => Assert.Fail());
    }

    [UnityTest]
    public IEnumerator PassingDestroyedObjectToOnComplete() {
        var target = new GameObject();
        Object.DestroyImmediate(target);
        ExpectOnCompleteIgnored();
        yield return Tween.Delay(k_MinDuration).OnComplete(target, _ => Assert.Fail()).ToYieldInstruction();
        LogAssert.NoUnexpectedReceived();
    }
}
#endif