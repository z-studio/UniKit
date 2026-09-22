#if TEST_FRAMEWORK_INSTALLED
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PrimeTween;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

public partial class Tests {
    private Transform m_Transform;

    private static TweenSettings<Vector3> SettingsVector3 => new(Vector3.zero, Vector3.one, GetDt() * 2f);

    private static bool s_SetTweenCapacityBeforeSplashScreenLoggedError;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void BeforeSplashScreen() {
        Application.logMessageReceived += OnLogMessageReceived;
        PrimeTweenConfig.SetTweensCapacity(k_CapacityForTest);
        Application.logMessageReceived -= OnLogMessageReceived;

        void OnLogMessageReceived(string condition, string stacktrace, LogType type) =>
            s_SetTweenCapacityBeforeSplashScreenLoggedError = true;
    }

    private bool m_ResetCapacityBetweenTests;

    [OneTimeSetUp]
    public void OneTimeSetup() {
        Assert.IsFalse(s_SetTweenCapacityBeforeSplashScreenLoggedError, "s_SetTweenCapacityBeforeSplashScreenLoggedError");
        m_Transform = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        PrimeTweenConfig.SetTweensCapacity(k_CapacityForTest);

#if UNITY_EDITOR
        var gameView = Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()
                                .FirstOrDefault(w => w.GetType().Name == "GameView");

        Assert.IsNotNull(gameView);
        UnityEditor.EditorWindow.GetWindow(gameView.GetType());
#endif
    }

    private const int k_TargetFrameRate = -1;

    [SetUp]
    public void SetUp() {
        Application.targetFrameRate = k_TargetFrameRate;

        if (m_ResetCapacityBetweenTests) {
            PrimeTweenConfig.SetTweensCapacity(1);
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        if (m_ResetCapacityBetweenTests && tweensCount > 0) {
            Tween.StopAll();
            yield return null;
            Assert.AreEqual(0, tweensCount);
        }
    }

    private static int tweensCount => PrimeTweenManager.Instance.TweensCount;

    [Test]
    public void CreatingTweenWithDestroyedTargetReturnsTweenToPool() {
        if (tweensCount != 0) {
            Tween.StopAll();
        }

        Assert.AreEqual(0, tweensCount);
        var target = new GameObject();
        var targetTr = target.transform;
        Object.DestroyImmediate(target);

        var pool = PrimeTweenManager.Instance.pool;
        var poolCount = pool.Count;

        {
            ExpectTargetIsNull();
            var t = Tween.Delay(target, 0.0001f);
            Assert.IsFalse(t.isAlive);
            Assert.AreEqual(poolCount, pool.Count);
        }

        {
            ExpectTargetIsNull();
            var t = Tween.Custom(target, 0, 0, 1, delegate { });
            Assert.IsFalse(t.isAlive);
            Assert.AreEqual(poolCount, pool.Count);
        }

        {
            ExpectTargetIsNull();
            Assert.IsTrue(targetTr == null);
            var t = Tween.Position(targetTr, default, 1);
            Assert.IsFalse(t.isAlive);
            Assert.AreEqual(poolCount, pool.Count);
        }

        Assert.AreEqual(0, tweensCount);
    }

    [UnityTest]
    public IEnumerator TweenTargetDestroyedInSequenceWithCallbacks() {
        ExpectOnCompleteIgnored();
        var target = new GameObject("t1");
        var duration = GetDt();

        yield return Sequence.Create()
                             .Group(Tween.Custom(target, 0, 1, duration, delegate { }))
                             .Chain(
                                 Tween.Custom(target, 0, 1, duration, (_target, _) => Object.DestroyImmediate(_target))
                                      .OnComplete(() => { })
                             )
                             .Chain(Tween.Delay(target, duration))
                             .ToYieldInstruction();

        yield return null;
        Assert.AreEqual(0, tweensCount, "TweenCoroutineEnumerator should not check the target's destruction.");
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator TweenTargetDestroyedInSequence() {
        var target = new GameObject("t1");
        const float duration = 0.03f;

        yield return Sequence.Create()
                             .Group(Tween.Custom(target, 0, 1, duration, delegate { }))
                             .Chain(
                                 Tween.Custom(target, 0, 1, duration, (_target, _) => Object.DestroyImmediate(_target))
                             )
                             .Chain(Tween.Delay(target, duration))
                             .ToYieldInstruction();

        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void SequenceTargetDestroyedBeforeCallingStop() {
        var tweener = CreateTweenAndDestroyTargetImmediately(false);
        Sequence.Create(tweener).Stop();
    }

    [Test]
    public void SequenceTargetDestroyedBeforeCallingComplete() {
        var tweener = CreateTweenAndDestroyTargetImmediately().OnComplete(Assert.Fail);
        Sequence.Create(tweener).Complete();
    }

    [UnityTest]
    public IEnumerator TargetDestroyedBeforeCallingCompleteAll() {
        CreateTweenAndDestroyTargetImmediately().OnComplete(Assert.Fail);
        Tween.CompleteAll();
        Tween.SetPausedAll(true);
        Tween.SetPausedAll(false);
        Assert.AreEqual(0, GetCurrentTweensCount());
        yield break;
    }

    private static int GetCurrentTweensCount() => tweensCount;

    [UnityTest]
    public IEnumerator TargetDestroyedBeforeCallingCompleteByTarget() {
        var tweener = CreateTweenAndDestroyTargetImmediately().OnComplete(Assert.Fail);
        Assert.AreEqual(Tween.CompleteAll(tweener.tween.ManagedData.target), 1);
        Tween.SetPausedAll(true);
        Tween.SetPausedAll(false);
        Assert.AreEqual(1, GetCurrentTweensCount());
        yield return tweener;
        Assert.AreEqual(0, tweensCount);
    }

    [Test]
    public void TargetDestroyedBeforeCallingComplete() {
        CreateTweenAndDestroyTargetImmediately().OnComplete(Assert.Fail).Complete();
    }

    [UnityTest]
    public IEnumerator TargetDestroyedBeforeAddingOnComplete1() {
        yield return CreateTweenAndDestroyTargetImmediately()
                     .OnComplete(delegate { })
                     .ToYieldInstruction();
    }

    [UnityTest]
    public IEnumerator TargetDestroyedBeforeAddingOnComplete2() {
        yield return CreateTweenAndDestroyTargetImmediately()
                     .OnComplete(this, delegate { })
                     .ToYieldInstruction();
    }

    [UnityTest]
    public IEnumerator TargetDestroyedSetIsPaused() {
        var t = CreateTweenAndDestroyTargetImmediately().OnComplete(Assert.Fail);
        t.isPaused = true; // changing isPaused is ok
        t.isPaused = false;
        yield return t.ToYieldInstruction();
    }

    private static Tween CreateTweenAndDestroyTargetImmediately(bool expectOnCompleteIgnoredWarning = true) {
        if (expectOnCompleteIgnoredWarning) {
            ExpectOnCompleteIgnored();
        }

        var tempTransform = new GameObject().transform;
        var tweener = Tween.LocalPosition(tempTransform, SettingsVector3);
        Object.DestroyImmediate(tempTransform.gameObject);
        Assert.IsTrue(tweener.isAlive);
        return tweener;
    }

    [UnityTest]
    public IEnumerator OnCompleteIsNotCalledIfTargetDestroyed() {
        ExpectOnCompleteIgnored();
        var tempTransform = new GameObject().transform;
        var tweener = Tween.LocalPosition(tempTransform, SettingsVector3).OnComplete(Assert.Fail);
        Object.DestroyImmediate(tempTransform.gameObject);

        while (tweener.isAlive) {
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator SettingCyclesOnDestroyedTween() {
        var t = CreateTweenAndDestroyTargetImmediately().OnComplete(Assert.Fail);
        t.SetRemainingCycles(2);
        yield return t;
    }

    [Test]
    public void IgnoreIfOnCompleteTargetDestroyed() {
        {
            var target = new GameObject(nameof(IgnoreIfOnCompleteTargetDestroyed));
            var t = Tween.Delay(1f).OnComplete(target, _ => Assert.Fail());
            Object.DestroyImmediate(target);
            ExpectOnCompleteIgnored();
            t.Complete();
        }

        {
            var target = new GameObject(nameof(IgnoreIfOnCompleteTargetDestroyed));
            var t = Tween.Delay(1f).OnComplete(target, _ => Assert.Fail(), false);
            Object.DestroyImmediate(target);
            t.Complete();
        }

        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public async Task TargetDestructionAsync() {
        {
            ExpectOnCompleteIgnored();
            var target = new GameObject(nameof(TargetDestructionAsync));

            await Tween.Custom(target, 0, 1, 1, delegate { Object.DestroyImmediate(target); })
                       .OnComplete(Assert.Fail);
        }

        {
            var target = new GameObject(nameof(TargetDestructionAsync));

            await Tween.Custom(target, 0, 1, 1, delegate { Object.DestroyImmediate(target); })
                       .OnComplete(Assert.Fail, false);
        }

        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator TargetDestructionInCoroutine() {
        if (tweensCount > 0) {
            Tween.StopAll();
        }

        Assert.AreEqual(0, tweensCount);

        {
            PrimeTweenConfig.warnStructBoxingAllocationInCoroutine = true;
            ExpectCoroutineBoxingWarning();
            ExpectOnCompleteIgnored();
            var target = new GameObject(nameof(TargetDestructionInCoroutine));

            yield return Tween.Custom(target, 0, 1, 1, delegate { Object.DestroyImmediate(target); })
                              .OnComplete(Assert.Fail);
        }

        {
            var target = new GameObject(nameof(TargetDestructionInCoroutine));

            yield return Tween.Custom(target, 0, 1, 1, delegate { Object.DestroyImmediate(target); })
                              .OnComplete(Assert.Fail, false)
                              .ToYieldInstruction();
        }

        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator TargetDestructionWhilePaused() {
        var target = new GameObject(nameof(TargetDestructionWhilePaused));
        var tween = Tween.Delay(target, 0.05f);
        tween.isPaused = true;
        Object.DestroyImmediate(target);
        yield return null;
        Assert.IsFalse(tween.isAlive);
    }
}
#endif