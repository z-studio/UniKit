#if PRIME_TWEEN_SAFETY_CHECKS && UNITY_ASSERTIONS
#define SAFETY_CHECKS
#endif
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using TweenType = PrimeTween.TweenAnimation.TweenType;

namespace PrimeTween {
    internal partial class ColdData {
        internal TweenArray tweenArray;
        internal int index;

        internal ColdData sequence;
        internal ColdData prev;
        internal ColdData next;
        internal ColdData prevSibling;
        internal ColdData nextSibling;

        [CanBeNull]
        internal Action<TweenData> onComplete;

        [CanBeNull]
        internal object onCompleteCallback;

        [CanBeNull]
        internal object onCompleteTarget;

        internal OnValueChangeDelegate onValueChange;
        internal object customOnValueChange;

        internal AnimationCurve customEase;
        internal ParametricEase parametricEase;
        internal float parametricEaseStrength;
        internal float parametricEasePeriod;

        internal long longParam;
        internal TweenAnimation.ValueWrapper prevVal;
        internal ShakeData shakeData;

        [CanBeNull]
        internal object onUpdateTarget;

        internal object onUpdateCallback;
        internal Action<TweenData> onUpdate;

        internal long id = -1;

#if UNITY_EDITOR
        internal string debugDescription;
        internal int indexInTweenAnimation;
#endif

        internal bool HasData => tweenArray != null && tweenArray.GetData().Length > index;

        internal unsafe ref UnmanagedTweenData Data {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                Assert.IsTrue(HasData, null, nameof(HasData));
                return ref *(tweenArray.DataPtr + index);
            }
        }

        internal ref TweenData ManagedData {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                Assert.IsTrue(HasData);
                return ref tweenArray[index];
            }
        }

        internal int IntParam {
            get => (int)longParam;
            set => longParam = value;
        }

        /// _getter is null for custom tweens
        internal void Setup(
            [CanBeNull] object target,
            ref TweenSettings settings,
            bool startFromCurrent,
            TweenType tweenType,
            ref TweenData rt,
            ref UnmanagedTweenData d
        ) {
            Assert.IsTrue(HasData);
            Assert.AreNotEqual(0, d.id);
            Assert.AreEqual(d.id, id);
            Assert.IsTrue(settings.cycles >= -1);
            Assert.AreNotEqual(TweenType.Disabled, tweenType);

            var manager = PrimeTweenManager.Instance;
            var propType = Utils.TweenTypeToTweenData(tweenType).Item1;
            Assert.AreNotEqual(PropType.None, propType);
            d.tweenType = tweenType;

            if (settings.ease == Ease.Default) {
                settings.ease = manager.defaultEase;
            } else if (settings.ease == Ease.Custom && settings.parametricEase == ParametricEase.None) {
                AnimationCurve curve = settings.customEase;

                if (curve != null && TweenSettings.ValidateCustomCurveKeyframes(curve)) {
                    var startKey = curve[0];
                    var endKey = curve[curve.length - 1];
                    d.IsCustomEaseSameStartEndValues = Mathf.Approximately(startKey.value, endKey.value);
                } else {
                    Debug.LogError(
                        $"Ease type is Ease.Custom, but {nameof(TweenSettings.customEase)} is not configured correctly.",
                        target as UnityEngine.Object
                    );

                    settings.ease = manager.defaultEase;
                }
            }

            Revive(ref d);

            d.flags |= Flags.WarnIgnoredOnCompleteIfTargetDestroyed;
            d.flags &= ~(Flags.StateAfter | Flags.StateRunning);
            d.flags |= Flags.StateBefore;
            d.easedInterpolationFactor = float.MinValue;
            d.cyclesDone = TweenData.kIniCyclesDone;
            d.timeScale = 1f;

            settings.SetValidValues();
            d.animationDuration = settings.duration;
            d.ease = settings.ease;
            d.cyclesTotal = settings.cycles;
            d.cycleMode = settings.cycleMode;
            d.startDelay = settings.startDelay;
            d.UseUnscaledTime = settings.useUnscaledTime;
            d.StartFromCurrent = startFromCurrent;

            customEase = settings.customEase;
            parametricEase = settings.parametricEase;
            parametricEaseStrength = settings.parametricEaseStrength;
            parametricEasePeriod = settings.parametricEasePeriod;
            TweenData.CalculateCycleDuration(settings.endDelay, ref d);
            Assert.IsTrue(d.cycleDuration >= 0);

            if (propType == PropType.Quaternion) {
                // Quaternion.identity
                prevVal.x = prevVal.y = prevVal.z = 0f;
                prevVal.w = 1f;
            } else {
                prevVal.Reset();
            }

            d.WarnEndValueEqualsCurrent = manager.warnEndValueEqualsCurrent;

            if (!startFromCurrent) {
                TweenData.CacheDiff(ref d, ref rt);
            }

            rt.target = target;
        }

        private void Revive(ref UnmanagedTweenData d) {
            // managedData.print("revive");
            Assert.IsFalse(d.IsAlive);
            d.IsAlive = true;
#if UNITY_EDITOR
            debugDescription = null;
            indexInTweenAnimation = -1;
#endif
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct TweenData : IEquatable<TweenData> {
        [FieldOffset(0)]
        internal TweenAnimation.ValueWrapper endValueOrDiff;

        /// Holds a reference to tween's target. If the target is UnityEngine.Object, the tween will gracefully stop when the target is destroyed. That is, destroying an object with running tweens is perfectly ok.
        /// Keep in mind: when animating plain C# objects (not derived from UnityEngine.Object), the plugin will hold a strong reference to the object for the entire tween duration.
        ///     If the plain C# target holds a reference to UnityEngine.Object and animates its properties, then it's the user's responsibility to ensure that UnityEngine.Object still exists.
        [FieldOffset(16)]
        [CanBeNull]
        internal object target;

        /// Item can be null if the list is accessed from the <see cref="UpdateAndCheckIfRunning"/> via onValueChange() or onComplete()
        [FieldOffset(24)] /*[CanBeNull]*/ internal ColdData cold;

        internal const float kNegativeElapsedTime = -1000f;
        private static readonly System.Text.StringBuilder s_Sb = new();

        internal ref ColdData Sequence => ref cold.sequence;
        internal ref ShakeData ShakeData => ref cold.shakeData;

#if UNITY_EDITOR
        internal ref string DebugDescription => ref cold.debugDescription;
#endif
        internal ref long Id => ref cold.id;

        internal const int kIniCyclesDone = -1;

        internal int IntParam => (int)cold.longParam;

        internal bool UpdateAndCheckIfRunning(float dt, ref UnmanagedTweenData d) {
            Assert.IsFalse(d.IsUpdating);

            if (!d.IsAlive) {
                return d.IsInSequence; // don't release a tween until sequence.ReleaseTweens()
            }

            if (!d.IsPaused) {
                return
                    SetElapsedTimeTotal(
                        d.elapsedTimeTotal + dt * d.timeScale,
                        true,
                        ref d
                    ); // p2 todo move this calculation inside. But I should redesign SetElapsedTimeTotal for that because it's called from other places too
            }

            if (IsUnityTargetDestroyed()) {
                EmergencyStop(true, ref d);
                return false;
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            return d.IsAlive;
        }

        internal bool SetElapsedTimeTotal(
            float newElapsedTimeTotal,
            bool earlyExitSequenceIfPaused,
            ref UnmanagedTweenData d
        ) {
            if (d.IsUpdatedInJob) {
                d.IsUpdatedInJob = false;
                bool isDone = d.isDone;

                if (d.IsValueChanged) {
                    bool res = ReportOnValueChange(ref d);

                    if (!res) {
                        return false;
                    }
                }

                Assert.IsFalse(d.StoppedEmergently);

                if (isDone && d.IsAlive) {
                    // tween
                    if (!d.IsPaused) {
                        Kill(ref d);
                    }

                    ReportOnComplete(ref d); // tween
                    return false;
                }

                return true;
            }

            if (d.IsInSequence) {
                Assert.IsTrue(Sequence.Data.IsAlive, Id);

                if (d.IsMainSequenceRoot()) {
                    Assert.IsTrue(Sequence == cold, Id);
                    UpdateSequence(newElapsedTimeTotal, false, earlyExitSequenceIfPaused, true, false, ref d);
                }
            } else {
                UpdateAndSetElapsedTimeTotal(newElapsedTimeTotal, out int cyclesDiff, false, ref d);

                if (!d.StoppedEmergently && d.IsAlive && d.IsDone(cyclesDiff)) {
                    // tween
                    if (!d.IsPaused) {
                        Kill(ref d);
                    }

                    ReportOnComplete(ref d); // tween
                }
            }

            return d.IsAlive;
        }

        internal void UpdateSequence(
            float elapsedTimeTotal,
            bool isRestart,
            bool earlyExitSequenceIfPaused,
            bool allowSkipChildrenUpdate,
            bool invertEase,
            ref UnmanagedTweenData d
        ) {
            Assert.IsTrue(d.IsSequenceRoot());

            bool isRootBackwardCycle = (d.ClampCyclesDone(d.cyclesDone) % 2 != 0) ^ d.IsSequenceInverted;
            float prevElapsedTime = FloatVal(d.startValue, d.easedInterpolationFactor, endValueOrDiff);

            bool invertRootEase = (d.cyclesTotal == 1 && isRootBackwardCycle && d.cycleMode == CycleMode.Rewind)
                                  ^ invertEase;

            if (!UpdateAndSetElapsedTimeTotal(elapsedTimeTotal, out int cyclesDiff, invertRootEase, ref d)
                && allowSkipChildrenUpdate) { // update sequence root
                return;
            }

            if (d.cycleMode == (CycleMode)ECycleMode.YoyoChildren && isRootBackwardCycle) {
                invertEase = !invertEase;
            }

            bool isRestartToBeginning = isRestart && cyclesDiff < 0;
            Assert.IsTrue(!isRestartToBeginning || d.cyclesDone == 0 || d.cyclesDone == kIniCyclesDone);

            if (cyclesDiff != 0 && !isRestartToBeginning) {
                // print($"           sequence cyclesDiff: {cyclesDiff}");
                if (isRestart) {
                    Assert.IsTrue(cyclesDiff > 0 && d.cyclesDone == d.cyclesTotal);
                    cyclesDiff = 1;
                }

                int cyclesDiffAbs = Mathf.Abs(cyclesDiff);
                int newCyclesDone = d.cyclesDone;
                d.cyclesDone -= cyclesDiff;
                int cyclesDelta = cyclesDiff > 0 ? 1 : -1;
                float interpolationFactor = cyclesDelta > 0 ? 1f : 0f;

                for (int i = 0; i < cyclesDiffAbs; i++) {
                    Assert.IsTrue(!isRestart || i == 0);

                    if (d.cyclesDone == d.cyclesTotal || d.cyclesDone == kIniCyclesDone) {
                        // do nothing when moving backward from the last cycle or forward from the -1 cycle
                        d.cyclesDone += cyclesDelta;
                        continue;
                    }

                    float easedT = CalcEasedT(interpolationFactor, d.cyclesDone, false, ref d);
                    bool isForwardCycle = (easedT > 0.5f) ^ d.IsSequenceInverted;

                    // complete the previous cycles by forcing all children tweens to 0f or 1f
                    // print($" (i:{i}) force to pos: {isForwardCycle}");
                    float forceChildrenToPosElapsedTime = isForwardCycle ? float.MaxValue : kNegativeElapsedTime;

                    foreach (var tween in GetSequenceSelfChildren(isForwardCycle)) {
                        tween.ManagedData.UpdateSequenceChild(
                            forceChildrenToPosElapsedTime,
                            isRestart,
                            invertEase,
                            ref tween.Data
                        );

                        if (isEarlyExitAfterChildUpdate(ref d)) {
                            return;
                        }
                    }

                    d.cyclesDone += cyclesDelta;
                    var sequenceCycleMode = d.cycleMode;

                    if (sequenceCycleMode == CycleMode.Restart
                        && d.cyclesDone != d.cyclesTotal
                        && d.cyclesDone != kIniCyclesDone) {
                        // '&& cyclesDone != 0' check is wrong because we should do the restart when moving from 1 to 0 cyclesDone
                        // print($"restartChildren to pos: {!isForwardCycle}");
                        float restartChildrenElapsedTime = !isForwardCycle ? float.MaxValue : kNegativeElapsedTime;
                        prevElapsedTime = restartChildrenElapsedTime;

                        foreach (var tween in GetSequenceSelfChildren(!isForwardCycle)) {
                            tween.ManagedData.UpdateSequenceChild(
                                restartChildrenElapsedTime,
                                true,
                                invertEase,
                                ref tween.Data
                            );

                            if (isEarlyExitAfterChildUpdate(ref d)) {
                                return;
                            }

                            Assert.IsTrue(isForwardCycle || tween.Data.cyclesDone == tween.Data.cyclesTotal, Id);
                            Assert.IsTrue(!isForwardCycle || tween.Data.cyclesDone <= 0, Id);
                            Assert.IsTrue(isForwardCycle || tween.Data.GetFlag(Flags.StateAfter), Id);
                            Assert.IsTrue(!isForwardCycle || tween.Data.GetFlag(Flags.StateBefore), Id);
                        }
                    }
                }

                Assert.IsTrue(newCyclesDone == d.cyclesDone, Id);

                if (d.IsDone(cyclesDiff)) { // sequence
                    if (d.ResetOnComplete && d.IsMainSequenceRoot()) {
                        ResetSequence(Sequence);
                    }

                    if (d.IsMainSequenceRoot() && !d.IsPaused) {
                        new Sequence(Sequence).ReleaseTweens();
                    }

                    ReportOnComplete(ref d, false); // sequence
                    return;
                }
            }

            float sequenceElapsedTime = Mathf.Clamp(
                FloatVal(d.startValue, d.easedInterpolationFactor, endValueOrDiff),
                0f,
                d.cycleDuration
            );

            bool isForward = sequenceElapsedTime > prevElapsedTime;

            foreach (var t in GetSequenceSelfChildren(isForward)) {
                t.ManagedData.UpdateSequenceChild(sequenceElapsedTime, isRestart, invertEase, ref t.Data);

                if (isEarlyExitAfterChildUpdate(ref d)) {
                    return;
                }
            }

            bool isEarlyExitAfterChildUpdate(ref UnmanagedTweenData d2) {
                if (!d2.IsAlive) {
                    return true;
                }

                return
                    earlyExitSequenceIfPaused
                    && d2.IsPaused; // access isPaused via root tween to bypass the cantManipulateNested check
            }
        }

        internal static void ResetSequence(ColdData seq) {
            ref var seqData = ref seq.Data;
            Assert.IsTrue(seqData.IsAlive);

            foreach (var child in new Sequence(seq).GetSelfChildren(false)) {
                ref var childData = ref child.Data;

                if (childData.IsSequenceRoot()) {
                    ResetSequence(child);
                } else {
                    childData.SetFlag(Flags.StateBefore, false);

                    bool isValueChanged = child.ManagedData.UpdateAndSetElapsedTimeTotal(
                        kNegativeElapsedTime,
                        out _,
                        false,
                        ref childData
                    );

                    Assert.IsTrue(isValueChanged);

                    if (!seqData.IsAlive) {
                        return;
                    }

                    Assert.AreNotEqual(0, (int)(childData.flags & Flags.StateBefore));
                }
            }
        }

        private Sequence.SequenceDirectEnumerator GetSequenceSelfChildren(bool isForward) {
            Assert.IsTrue(Sequence.Data.IsAlive, Id);
            return new Sequence(Sequence).GetSelfChildren(isForward);
        }

        private void UpdateSequenceChild(
            float encompassingElapsedTime,
            bool isRestart,
            bool invertEase,
            ref UnmanagedTweenData d
        ) {
            if (d.IsSequenceRoot()) {
                UpdateSequence(encompassingElapsedTime, isRestart, true, true, invertEase, ref d);
            } else {
                UpdateAndSetElapsedTimeTotal(encompassingElapsedTime, out int cyclesDiff, invertEase, ref d);

                if (!d.StoppedEmergently && d.IsAlive && d.IsDone(cyclesDiff)) { // sequence child
                    ReportOnComplete(ref d); // sequence child
                }
            }
        }

        internal bool UpdateAndSetElapsedTimeTotal(
            float newElapsedTimeTotal,
            out int cyclesDiff,
            bool invertEase,
            ref UnmanagedTweenData d
        ) {
            int oldCyclesDone = d.cyclesDone;
            float t = d.UpdateData(newElapsedTimeTotal);
            cyclesDiff = d.cyclesDone - oldCyclesDone;

            if (d.IsValueChanged) {
                d.easedInterpolationFactor = CalcEasedT(t, d.cyclesDone, invertEase, ref d);

                TryCacheDiff(ref d);
                ReportOnValueChange(ref d);
                return true;
            }

            return false;
        }

#if BURST_INSTALLED
        [Unity.Burst.BurstCompile]
#endif
        internal struct UpdateTweensJob : IJobParallelFor {
            internal float deltaTime;
            internal float unscaledDeltaTime;

            [NativeDisableUnsafePtrRestriction]
            internal unsafe UnmanagedTweenData* dataPtr;

            public unsafe void Execute(int i) {
                ref var d = ref UnsafeUtility.ArrayElementAsRef<UnmanagedTweenData>(dataPtr, i);

                if (!d.IsPaused && d.CanUpdateData()) {
                    d.IsUpdatedInJob = true;
                    float dt = d.UseUnscaledTime ? unscaledDeltaTime : deltaTime;
                    float newElapsedTimeTotal = d.elapsedTimeTotal + dt * d.timeScale;
                    float t = d.UpdateData(newElapsedTimeTotal);
                    d.easedInterpolationFactor = d.CalcEasedT(t);
                }
            }
        }

        [System.Diagnostics.Conditional("PRIME_TWEEN_SAFETY_CHECKS")]
        internal void Print(string msg) {
            // Debug.Log($"[{Time.frameCount}] id:{id}  {msg}  {GetDescription()}", target as UnityEngine.Object);
        }

        internal void Reset(ref UnmanagedTweenData d) {
            Assert.IsFalse(d.IsUpdating);
            Assert.IsFalse(d.IsAlive);
            Assert.IsNull(cold.sequence);
            Assert.IsNull(cold.prev);
            Assert.IsNull(cold.next);
            Assert.IsNull(cold.prevSibling);
            Assert.IsNull(cold.nextSibling);
            Assert.IsFalse(d.IsInSequence);

            if (cold.shakeData.IsAlive) {
                cold.shakeData.Reset(target, d.tweenType);
            }
#if UNITY_EDITOR
            DebugDescription = null;
#endif
            target = null;
            cold.customEase = null;
            cold.customOnValueChange = null;
            cold.onValueChange = null;
            cold.onComplete = null;
            cold.onCompleteCallback = null;
            cold.onCompleteTarget = null;
            ClearOnUpdate(ref d);

            Assert.IsTrue(cold.HasData);
            Assert.AreEqual(Id, d.id);
            d = default; // reset the data before returning tween to pool
            Id = -1;
        }

        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        internal void OnComplete([CanBeNull] Action onComplete, bool? warnIfTargetDestroyed) {
            if (onComplete == null) {
                return;
            }

            ValidateOnCompleteAssignment();

            cold.Data.WarnIgnoredOnCompleteIfTargetDestroyed =
                warnIfTargetDestroyed ?? PrimeTweenManager.Instance.warnIfTargetDestroyed;

            cold.onCompleteCallback = onComplete;

            cold.onComplete = tween => {
                var callback = tween.cold.onCompleteCallback as Action;
                Assert.IsNotNull(callback);

                try {
                    callback();
                } catch (Exception e) {
                    tween.HandleOnCompleteException(e);
                }
            };
        }

        internal void OnComplete<T>(
            [CanBeNull] T target,
            [CanBeNull] Action<T> onComplete,
            bool? warnIfTargetDestroyed
        ) where T : class {
            if (target == null || IsDestroyedUnityObject(target)) {
                Debug.LogError(
                    $"{nameof(target)} is null or has been destroyed. {Constants.kOnCompleteCallbackIgnored}"
                );

                return;
            }

            if (onComplete == null) {
                return;
            }

            ValidateOnCompleteAssignment();

            cold.Data.WarnIgnoredOnCompleteIfTargetDestroyed =
                warnIfTargetDestroyed ?? PrimeTweenManager.Instance.warnIfTargetDestroyed;

            cold.onCompleteTarget = target;
            cold.onCompleteCallback = onComplete;

            cold.onComplete = tween => {
                var callback = tween.cold.onCompleteCallback as Action<T>;
                Assert.IsNotNull(callback);
                var onCompleteTarget = tween.cold.onCompleteTarget as T;

                if (IsDestroyedUnityObject(onCompleteTarget)) {
                    tween.WarnOnCompleteIgnored(true);
                    return;
                }

                try {
                    callback(onCompleteTarget);
                } catch (Exception e) {
                    tween.HandleOnCompleteException(e);
                }
            };
        }

        private void HandleOnCompleteException(Exception e) {
            // Design decision: if a tween is inside a Sequence and the user's tween.OnComplete() throws an exception, the Sequence should continue
            LogErrorWithStackTrace($"Tween's onComplete callback raised exception, tween: {GetDescription()}");
            Debug.LogException(e, target as UnityEngine.Object);
        }

        internal void LogErrorWithStackTrace(string msg) => Assert.LogErrorWithStackTrace(msg, cold.id, target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsDestroyedUnityObject<T>(T obj) where T : class =>
            obj is UnityEngine.Object unityObject && unityObject == null;

        private void ValidateOnCompleteAssignment() {
            const string msg = "Tween already has an onComplete callback. Adding more callbacks is not allowed.\n"
                               + "Workaround: wrap a tween in a Sequence by calling Sequence.Create(tween) and use multiple ChainCallback().\n";

            Assert.IsNull(cold.onCompleteTarget, msg);
            Assert.IsNull(cold.onCompleteCallback, msg);
            Assert.IsNull(cold.onComplete, msg);
        }

        internal bool ReportOnValueChange(ref UnmanagedTweenData d) {
            // print($"ReportOnValueChange {d.easedInterpolationFactor}, {Vector4Val(d.startValue, d.easedInterpolationFactor, endValueOrDiff)}, {d.startValue}, {endValueOrDiff}");
            Assert.IsFalse(d.StartFromCurrent);
            bool hasOnUpdate = d.HasOnUpdate;

            try {
                // The value setter can fail even if the Unity target is not destroyed. For example, ScrollRect.SetNormalizedPosition can throw null ref if m_Content is not populated: https://github.com/needle-mirror/com.unity.ugui/blob/a601a2bf30161c47959231b627a8c40f64d69a68/Runtime/UI/Core/ScrollRect.cs#L1031.
                // Also, this try-catch catches exceptions in user-provided setter callbacks in Custom tweens.
                if (!Utils.SetAnimatedValue(ref this, ref d)) {
                    return false;
                }

                if (hasOnUpdate && !d.StoppedEmergently && d.IsAlive) {
                    d.IsUpdating = true;
                    cold.onUpdate?.Invoke(this);
                    d.IsUpdating = false;
                }
            } catch (Exception e) {
                Debug.LogException(e, target as UnityEngine.Object);

                Assert.LogWarningWithStackTrace(
                    $"Tween was stopped because of exception in '{nameof(ColdData.onValueChange)}', tween: {GetDescription()}\n",
                    Id,
                    target
                );

                EmergencyStop(false, ref d);
                return false;
            }

            return true;
        }

        private void TryCacheDiff(ref UnmanagedTweenData d) {
            if (d.StartFromCurrent) {
                d.StartFromCurrent = false;

                if (!ShakeData.TryTakeStartValueFromOtherShake(ref this, ref d)) {
                    if (!IsUnityTargetDestroyed()) {
                        d.startValue = Utils.GetAnimatedValue(
                            target,
                            d.tweenType,
                            cold.IntParam
                        ); // p2 todo getter can potentially throw even if Unity target is not destroyed. For example, this is the case for ScrollRect.SetNormalizedPosition
                    }
                }

                if (d.startValue.vector4 == endValueOrDiff.vector4
                    && d.WarnEndValueEqualsCurrent
                    && !ShakeData.IsAlive) {
                    Assert.LogWarningWithStackTrace(
                        $"Tween's 'endValue' equals to the current animated value: {d.startValue.vector4}, tween: {GetDescription()}.\n"
                        + $"{Constants.BuildWarningCanBeDisabledMessage(nameof(PrimeTweenConfig.warnEndValueEqualsCurrent))}\n",
                        Id,
                        target
                    );
                }

                CacheDiff(ref d, ref this);
            }
        }

        private void ReportOnComplete(ref UnmanagedTweenData d, bool canResetOnComplete = true) {
            // print($"ReportOnComplete() {easedInterpolationFactor}");
            Assert.IsFalse(d.StartFromCurrent);
            Assert.IsTrue(d.timeScale < 0 || d.cyclesDone == d.cyclesTotal);
            Assert.IsTrue(d.timeScale >= 0 || d.cyclesDone == kIniCyclesDone);

            if (canResetOnComplete && d.ResetOnComplete && !d.IsInSequence) {
                // reset Tween
                UpdateAndSetElapsedTimeTotal(kNegativeElapsedTime, out _, false, ref d);
            }

            cold.onComplete?.Invoke(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsUnityTargetDestroyed() {
            // must use target here instead of unityTarget
            // unityTarget has the SerializeField attribute, so if ReferenceEquals(unityTarget, null), then Unity will populate the field with non-null UnityEngine.Object when a new scene is loaded in the Editor
            // https://github.com/KyryloKuzyk/PrimeTween/issues/32
            return IsDestroyedUnityObject(target);
        }

        internal bool HasOnComplete => cold.onComplete != null;

        [NotNull]
        internal string GetDescription() {
            s_Sb.Clear();
            var d = cold.Data;

            if (!d.IsAlive) {
                s_Sb.Append(" - ");
            }

            // _sb.Append(id).Append(" ");

            if (Sequence != null) {
                var currentSequence = Sequence;

                while (true) {
                    if (Id != currentSequence.id) {
                        s_Sb.Append(
                            " · "
                        ); // p2 todo animations are ordered by creation time, not by sequence nesting depth
                    }

                    var _prev = currentSequence.prev;

                    if (_prev == null) {
                        break;
                    }

                    var parent = _prev.sequence;

                    if (parent == null) {
                        break;
                    }

                    currentSequence = parent;
                }
            }

            float duration = d.animationDuration;
            bool isCallback = false;

            if (d.tweenType == TweenType.Delay) {
                if (duration == 0f && cold.onComplete != null) {
                    isCallback = true;
                    s_Sb.Append("Callback");
                } else {
                    s_Sb.Append("Delay");
                }
            } else {
                if (d.tweenType == TweenType.MainSequence || d.tweenType == TweenType.NestedSequence) {
                    s_Sb.Append("Sequence ");
                } else {
                    s_Sb.Append(d.tweenType);
                }
            }

            const string separator = "  /  ";

            if (target != PrimeTweenManager.sDummyTarget) {
                s_Sb.Append(separator);

                s_Sb.Append(
                    target is UnityEngine.Object unityObject && unityObject != null ? unityObject.name
                        : target?.GetType().Name
                );
            }

            if (!isCallback) {
                s_Sb.Append(separator).AppendFormat("{0:0.0#}s", duration);
            }

            return s_Sb.ToString();
        }

        internal static void CalculateCycleDuration(float endDelay, ref UnmanagedTweenData d) {
            Assert.IsTrue(endDelay >= 0f);
            d.cycleDuration = d.startDelay + d.animationDuration + endDelay;
        }

        internal static double DoubleVal(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            startValue.DoubleVal + delta.DoubleVal * t;

        internal static float FloatVal(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            startValue.single + delta.single * t;

        internal static Color ColorVal(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            startValue.color + delta.color * t;

        internal static Vector2 Vector2Val(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            startValue.vector2 + delta.vector2 * t;

        internal static Vector3 Vector3Val(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            startValue.vector3 + delta.vector3 * t;

        internal static Vector4 Vector4Val(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            startValue.vector4 + delta.vector4 * t;

        internal static Rect RectVal(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            new Rect(
                startValue.x + delta.x * t,
                startValue.y + delta.y * t,
                startValue.z + delta.z * t,
                startValue.w + delta.w * t
            );

        internal static Quaternion QuaternionVal(
            TweenAnimation.ValueWrapper startValue,
            float t,
            TweenAnimation.ValueWrapper delta
        ) =>
            Quaternion.SlerpUnclamped(startValue.quaternion, delta.quaternion, t);

        private float CalcEasedT(float t, int cyclesDone, bool invertEase, ref UnmanagedTweenData d) {
            if (invertEase) {
                float oneMinusT = CalcEasedTInternal(1f - t, cyclesDone, ref d);
                return d.IsCustomEaseSameStartEndValues ? oneMinusT : 1f - oneMinusT;
            } else {
                return CalcEasedTInternal(t, cyclesDone, ref d);
            }
        }

        private float CalcEasedTInternal(float t, int cyclesDone, ref UnmanagedTweenData d) {
            switch (d.cycleMode) {
                case CycleMode.Restart:
                    return Evaluate(t, ref d);
                case CycleMode.Incremental:
                    return Evaluate(t, ref d) + d.ClampCyclesDone(cyclesDone);
                case (CycleMode)ECycleMode.YoyoChildren:
                case CycleMode.Yoyo:
                    return d.IsForwardCycle(cyclesDone) ? Evaluate(t, ref d) : 1 - Evaluate(t, ref d);
                case CycleMode.Rewind:
                    return d.IsForwardCycle(cyclesDone) ? Evaluate(t, ref d) : Evaluate(1 - t, ref d);
                default:
                    throw new Exception();
            }
        }

        private float Evaluate(float t, ref UnmanagedTweenData d) {
            if (d.ease == Ease.Custom) {
                if (cold.parametricEase != ParametricEase.None) {
                    return Easing.EvaluateParametricEase(t, ref this, ref d);
                }

                return cold.customEase.Evaluate(t);
            }

            return StandardEasing.Evaluate(t, d.ease);
        }

        internal static void CacheDiff(ref UnmanagedTweenData d, ref TweenData rt) {
            // print($"CacheDiff, startValue: {d.startValue}, endValue: {endValue}");
            Assert.IsFalse(d.StartFromCurrent);
            var propType = d.PropType;
            Assert.AreNotEqual(PropType.None, propType);

            switch (propType) {
                case PropType.Quaternion:
                    d.startValue.QuaternionNormalize();
                    rt.endValueOrDiff.QuaternionNormalize();
                    break;
                case PropType.Double:
                    rt.endValueOrDiff.DoubleVal -= d.startValue.DoubleVal;
                    rt.endValueOrDiff.z = 0f;
                    rt.endValueOrDiff.w = 0f;
                    break;
                default:
                    rt.endValueOrDiff.vector4 -= d.startValue.vector4;
                    break;
            }

            // rt.print($"CacheDiff, diff: {rt.endValueOrDiff}");
        }

        internal void ForceComplete(ref UnmanagedTweenData d) {
            Assert.IsNull(Sequence);
            Kill(ref d); // protects from recursive call
            int cyclesTotal;

            if (d.timeScale > 0f) {
                cyclesTotal = d.cyclesTotal;

                if (cyclesTotal == -1) {
                    // same as SetRemainingCycles(1)
                    cyclesTotal = d.GetCyclesDone() + 1;
                    d.cyclesTotal = cyclesTotal;
                }
            } else {
                cyclesTotal = kIniCyclesDone;
            }

            d.cyclesDone = cyclesTotal;
            d.easedInterpolationFactor = CalcEasedT(1f, cyclesTotal, false, ref d);

            TryCacheDiff(ref d);
            ReportOnValueChange(ref d);

            if (d.StoppedEmergently) {
                return;
            }

            ReportOnComplete(ref d);
            Assert.IsFalse(d.IsAlive);
        }

        internal void WarnOnCompleteIgnored(bool isTargetDestroyed) {
            if (HasOnComplete && cold.Data.WarnIgnoredOnCompleteIfTargetDestroyed) {
                cold.onComplete = null;
                var msg = $"{Constants.kOnCompleteCallbackIgnored} Tween: {GetDescription()}.\n";

                if (isTargetDestroyed) {
                    msg +=
                        "\nIf you use tween.OnComplete(), Tween.Delay(), or sequence.ChainDelay() only for cosmetic purposes, you can turn off this error by passing 'warnIfTargetDestroyed: false' parameter to the method.\n"
                        + "More info: https://github.com/KyryloKuzyk/PrimeTween/discussions/4\n\n"
                        + "Not recommended: it's also possible to disable this setting globally with '"
                        + nameof(PrimeTweenConfig)
                        + "."
                        + nameof(PrimeTweenConfig.warnIfTargetDestroyed)
                        + " = false', but doing so will silent potential logic errors and might introduce subtle hard-to-debug issues to your project.\n";
                }

                Assert.LogErrorWithStackTrace(msg, Id, target ?? cold.onCompleteTarget);
            }
        }

        internal void EmergencyStop(bool isTargetDestroyed, ref UnmanagedTweenData d) {
            if (Sequence != null) {
                var mainSequence = Sequence;

                while (true) {
                    var prev = mainSequence.prev;

                    if (prev == null) {
                        break;
                    }

                    var parent = prev.sequence;

                    if (parent == null) {
                        break;
                    }

                    mainSequence = parent;
                }

                Assert.IsTrue(mainSequence.Data.IsAlive);
                Assert.IsTrue(mainSequence.Data.IsMainSequenceRoot());
                new Sequence(mainSequence).EmergencyStop();
            } else if (d.IsAlive) {
                // EmergencyStop() can be called after ForceComplete() and a caught exception in Tween.Custom()
                Kill(ref d);
            }

            d.StoppedEmergently = true;
            WarnOnCompleteIgnored(isTargetDestroyed);
            Assert.IsFalse(d.IsAlive);
            Assert.IsNull(Sequence);
        }

        internal void Kill(ref UnmanagedTweenData d) {
            // print($"kill {GetDescription()}");
            Assert.IsTrue(d.IsAlive);
            d.IsAlive = false;
#if UNITY_EDITOR
            DebugDescription = null;
#endif
        }

        internal void SetOnUpdate<T>(T target, [NotNull] Action<T, Tween> onUpdate) where T : class {
            Assert.IsNull(cold.onUpdate, "Only one OnUpdate() is allowed for one tween.");
            Assert.IsNotNull(onUpdate, nameof(onUpdate) + " is null!");
            cold.onUpdateTarget = target;
            cold.onUpdateCallback = onUpdate;
            cold.onUpdate = reusableTween => reusableTween.InvokeOnUpdate<T>();
            cold.Data.HasOnUpdate = true;
        }

        private void InvokeOnUpdate<T>() where T : class {
            var callback = cold.onUpdateCallback as Action<T, Tween>;
            Assert.IsNotNull(callback);
            var onUpdateTarget = cold.onUpdateTarget as T;

            if (IsDestroyedUnityObject(onUpdateTarget)) {
                LogErrorWithStackTrace(
                    $"OnUpdate() will not be called again because OnUpdate()'s target has been destroyed, tween: {GetDescription()}"
                );

                ClearOnUpdate(ref cold.Data);
                return;
            }

            try {
                callback(onUpdateTarget, new Tween(cold));
            } catch (Exception e) {
                LogErrorWithStackTrace(
                    $"OnUpdate() will not be called again because it thrown exception, tween: {GetDescription()}"
                );

                Debug.LogException(e, onUpdateTarget as UnityEngine.Object);
                ClearOnUpdate(ref cold.Data);
            }
        }

        private void ClearOnUpdate(ref UnmanagedTweenData d) {
            cold.onUpdateTarget = null;
            cold.onUpdateCallback = null;
            cold.onUpdate = null;
            d.HasOnUpdate = false;
        }

        public override string ToString() {
            return GetDescription();
        }

        public bool Equals(TweenData other) {
            return Equals(cold, other.cold);
        }

        public override bool Equals(object obj) {
            return obj is TweenData other && Equals(other);
        }

        public override int GetHashCode() {
            return (cold != null ? cold.GetHashCode() : 0);
        }
    }

    [Flags]
    internal enum Flags {
        Additive = 1 << 0,
        ShakeSign = 1 << 1,
        ShakePunch = 1 << 2,
        WarnEndValueEqualsCurrent = 1 << 3,
        WarnIgnoredOnCompleteIfTargetDestroyed = 1 << 4,
        ResetOnComplete = 1 << 5,
        IsUpdating = 1 << 6,
        StoppedEmergently = 1 << 7,
        IsAlive = 1 << 8,
        StateBefore = 1 << 9,
        StateRunning = 1 << 10,
        StateAfter = 1 << 11,
        StartFromCurrent = 1 << 12,
        IsDone = 1 << 13,
        IsValueChanged = 1 << 14,
        UseUnscaledTime = 1 << 15,
        IsInSequence = 1 << 16,
        IsPaused = 1 << 17,
        IsUpdatedInJob = 1 << 18,
        HasOnUpdate = 1 << 19,

        // unused = 1 << 20,
        IsSequenceInverted = 1 << 21,
        IsCustomEaseSameStartEndValues = 1 << 22,
    }

    internal enum ECycleMode : byte {
        Restart = 0,
        Yoyo = 1,
        Incremental = 2,
        Rewind = 3,
        YoyoChildren = 4
    }

    internal delegate void OnValueChangeDelegate(ref TweenData rt, ref UnmanagedTweenData d);
}