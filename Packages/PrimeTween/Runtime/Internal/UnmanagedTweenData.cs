using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace PrimeTween {
    [StructLayout(
        LayoutKind.Explicit,
        Size =
#if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
            128
#else
            64
#endif
    )]
    internal struct UnmanagedTweenData {
        [FieldOffset(0)]
        internal Flags flags;

        [FieldOffset(4)]
        internal float elapsedTimeTotal;

        [FieldOffset(8)]
        internal float timeScale;

        [FieldOffset(12)]
        internal int cyclesDone;

        [FieldOffset(16)]
        internal int cyclesTotal;

        [FieldOffset(20)]
        internal float waitDelay;

        [FieldOffset(24)]
        internal float cycleDuration;

        [FieldOffset(28)]
        internal float startDelay;

        [FieldOffset(32)]
        internal float animationDuration;

        [FieldOffset(36)]
        internal Ease ease;

        [FieldOffset(37)]
        internal CycleMode cycleMode;

        [FieldOffset(38)]
        internal TweenAnimation.TweenType tweenType;

        [FieldOffset(39)]
        internal EUpdateType updateType;

        [FieldOffset(40)]
        internal float easedInterpolationFactor;

        [FieldOffset(48)]
        internal TweenAnimation.ValueWrapper startValue;

#if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
        [FieldOffset(64)]
        internal long id;
#else
        internal long id { get => -1; set { } }
#endif

        private float CalcTFromElapsedTimeTotal(float elapsedTimeTotal, out Flags newState) {
            // key timeline points: 0 | startDelay | duration | 1 | endDelay | onComplete
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (elapsedTimeTotal == float.MaxValue) {
                Assert.AreNotEqual(-1, cyclesTotal);
                Assert.IsTrue(cyclesDone <= cyclesTotal);
                cyclesDone = cyclesTotal;
                newState = Flags.StateAfter;
                return 1f;
            }

            elapsedTimeTotal -= waitDelay; // waitDelay is applied before calculating cycles

            if (elapsedTimeTotal < 0f) {
                cyclesDone = TweenData.kIniCyclesDone;
                newState = Flags.StateBefore;
                return 0f;
            }

            Assert.IsTrue(elapsedTimeTotal >= 0f);
            Assert.AreNotEqual(float.MaxValue, elapsedTimeTotal);

            if (cycleDuration == 0f) {
                if (cyclesTotal == -1) {
                    // add max one cycle per frame
                    if (timeScale > 0f) {
                        if (cyclesDone == TweenData.kIniCyclesDone) {
                            cyclesDone = 1;
                        } else {
                            cyclesDone++;
                        }
                    } else if (timeScale != 0f) {
                        cyclesDone--;

                        if (cyclesDone == TweenData.kIniCyclesDone) {
                            newState = Flags.StateBefore;
                            return 0f;
                        }
                    }

                    newState = Flags.StateRunning;
                    return 1f;
                }

                Assert.AreNotEqual(-1, cyclesTotal);

                if (elapsedTimeTotal == 0f) {
                    cyclesDone = TweenData.kIniCyclesDone;
                    newState = Flags.StateBefore;
                    return 0f;
                }

                Assert.IsTrue(cyclesDone <= cyclesTotal);
                cyclesDone = cyclesTotal;
                newState = Flags.StateAfter;
                return 1f;
            }

            Assert.AreNotEqual(0f, cycleDuration);
            cyclesDone = (int)(elapsedTimeTotal / cycleDuration);

            if (cyclesTotal != -1 && cyclesDone > cyclesTotal) {
                cyclesDone = cyclesTotal;
            }

            if (cyclesTotal != -1 && cyclesDone == cyclesTotal) {
                newState = Flags.StateAfter;
                return 1f;
            }

            float elapsedTimeInCycle = elapsedTimeTotal - cycleDuration * cyclesDone - startDelay;

            if (elapsedTimeInCycle < 0f) {
                newState = Flags.StateBefore;
                return 0f;
            }

            Assert.IsTrue(elapsedTimeInCycle >= 0f);

            if (animationDuration == 0f) {
                newState = Flags.StateAfter;
                return 1f;
            }

            Assert.AreNotEqual(0f, animationDuration);
            float result = elapsedTimeInCycle / animationDuration;

            if (result > 1f) {
                newState = Flags.StateAfter;
                return 1f;
            }

            newState = Flags.StateRunning;
            Assert.IsTrue(result >= 0f);
            return result;
        }

        internal bool CanManipulate() => !IsInSequence || tweenType == TweenAnimation.TweenType.MainSequence;

        internal bool IsMainSequenceRoot() => tweenType == TweenAnimation.TweenType.MainSequence;

        internal bool IsSequenceRoot() =>
            tweenType == TweenAnimation.TweenType.MainSequence || tweenType == TweenAnimation.TweenType.NestedSequence;

        internal bool TrySetPause(bool isPaused) {
            if (IsPaused == isPaused) {
                return false;
            }

            IsPaused = isPaused;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool GetFlag(Flags flag) => (flags & flag) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetFlag(Flags flag, bool value) {
            if (value) {
                flags |= flag;
            } else {
                flags &= ~flag;
            }
        }

        internal bool IsPaused {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsPaused);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsPaused, value);
        }

        internal bool IsInSequence {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsInSequence);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsInSequence, value);
        }

        internal bool UseUnscaledTime {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.UseUnscaledTime);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.UseUnscaledTime, value);
        }

        internal bool IsValueChanged {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsValueChanged);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsValueChanged, value);
        }

        internal bool isDone {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsDone);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsDone, value);
        }

        internal bool StartFromCurrent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.StartFromCurrent);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.StartFromCurrent, value);
        }

        internal bool IsAlive {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsAlive);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsAlive, value);
        }

        internal bool IsUpdatedInJob {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsUpdatedInJob);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsUpdatedInJob, value);
        }

        internal bool HasOnUpdate {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.HasOnUpdate);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.HasOnUpdate, value);
        }

        internal bool ResetOnComplete {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.ResetOnComplete);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.ResetOnComplete, value);
        }

        internal bool StoppedEmergently {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.StoppedEmergently);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.StoppedEmergently, value);
        }

        internal bool IsAdditive {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.Additive);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.Additive, value);
        }

        internal bool WarnEndValueEqualsCurrent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.WarnEndValueEqualsCurrent);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.WarnEndValueEqualsCurrent, value);
        }

        internal bool IsSequenceInverted {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsSequenceInverted);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsSequenceInverted, value);
        }

        internal bool ShakeSign {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.ShakeSign);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.ShakeSign, value);
        }

        internal bool IsPunch {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.ShakePunch);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.ShakePunch, value);
        }

        internal bool IsUpdating {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsUpdating);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsUpdating, value);
        }

        internal bool WarnIgnoredOnCompleteIfTargetDestroyed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.WarnIgnoredOnCompleteIfTargetDestroyed);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.WarnIgnoredOnCompleteIfTargetDestroyed, value);
        }

        internal bool IsCustomEaseSameStartEndValues {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(Flags.IsCustomEaseSameStartEndValues);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(Flags.IsCustomEaseSameStartEndValues, value);
        }

        internal bool IsDone(int cyclesDiff) {
            Assert.IsTrue(cyclesTotal == -1 || cyclesDone <= cyclesTotal);

            if (timeScale > 0f) {
                return cyclesDiff > 0 && cyclesDone == cyclesTotal;
            } else {
                return cyclesDiff < 0 && cyclesDone == TweenData.kIniCyclesDone;
            }
        }

        internal bool CanUpdateData() {
            return IsAlive && !IsInSequence && ease != Ease.Custom && !StartFromCurrent;
        }

        internal float UpdateData(float newElapsedTimeTotal) {
            elapsedTimeTotal = newElapsedTimeTotal;

            int oldCyclesDone = cyclesDone;
            float t = CalcTFromElapsedTimeTotal(elapsedTimeTotal, out Flags newState);
            int cyclesDiff = cyclesDone - oldCyclesDone;
            isDone = IsDone(cyclesDiff);

            bool valueChanged = newState == Flags.StateRunning || (flags & newState) == 0;
            IsValueChanged = valueChanged;

            if (IsValueChanged) {
                // Print($"new state: {flags}/{newState}, cycles: {cyclesDone}/{cyclesTotal} (diff: {cyclesDiff}), elapsedTimeTotal: {elapsedTimeTotal}, t: {t}");
                flags &= ~(Flags.StateAfter | Flags.StateBefore | Flags.StateRunning);
                flags |= newState;
            } else {
                // Print($"same state: {newState}, cycles: {cyclesDone}/{cyclesTotal} (diff: {cyclesDiff}), elapsedTimeTotal: {elapsedTimeTotal}");
            }

            return t;
        }

        [System.Diagnostics.Conditional("PRIME_TWEEN_SAFETY_CHECKS")]
        internal void Print(string msg) {
            Debug.Log($"[{Time.frameCount}] id:{id}  {msg}");
        }

        internal float CalcEasedT(float t) {
            switch (cycleMode) {
                case CycleMode.Restart:
                    return Evaluate(t);
                case CycleMode.Incremental:
                    return Evaluate(t) + ClampCyclesDone(cyclesDone);
                case (CycleMode)ECycleMode.YoyoChildren:
                case CycleMode.Yoyo:
                    return IsForwardCycle(cyclesDone) ? Evaluate(t) : 1 - Evaluate(t);
                case CycleMode.Rewind:
                    return IsForwardCycle(cyclesDone) ? Evaluate(t) : Evaluate(1 - t);
                default:
                    throw new Exception();
            }
        }

        private float Evaluate(float t) {
            return StandardEasing.Evaluate(t, ease);
        }

        internal int ClampCyclesDone(int cyclesDone) {
            if (cyclesDone == TweenData.kIniCyclesDone) {
                return 0;
            }

            if (cyclesDone == cyclesTotal) {
                Assert.AreNotEqual(-1, cyclesTotal);
                return cyclesTotal - 1;
            }

            return cyclesDone;
        }

        internal bool IsForwardCycle(int cycle) => ClampCyclesDone(cycle) % 2 == 0;

        internal int GetCyclesDone() {
            int result = cyclesDone;

            if (result == TweenData.kIniCyclesDone) {
                return 0;
            }

            Assert.IsTrue(result >= 0);
            return result;
        }

        internal float CalcDurationWithWaitDependencies() {
            int cycles = cyclesTotal;

            Assert.IsTrue(
                -1 != cycles,
                id,
                "It's impossible to calculate the duration of an infinite tween (cycles == -1)."
            );

            Assert.AreNotEqual(0, cycles);
            return waitDelay + cycleDuration * cycles;
        }

        internal PropType PropType => Utils.TweenTypeToTweenData(tweenType).Item1;

        internal bool IsDefault() {
            unsafe {
                UnmanagedTweenData defaultValue = default;

                return UnsafeUtility.MemCmp(
                           UnsafeUtility.AddressOf(ref this),
                           UnsafeUtility.AddressOf(ref defaultValue),
                           UnsafeUtility.SizeOf<UnmanagedTweenData>()
                       )
                       == 0;
            }
        }
    }
}