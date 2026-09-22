using System;
using AnimationCurve = UnityEngine.AnimationCurve;
using Transform = UnityEngine.Transform;
using Camera = UnityEngine.Camera;
using Vector3 = UnityEngine.Vector3;
using NotNull = JetBrains.Annotations.NotNullAttribute;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using TweenType = PrimeTween.TweenAnimation.TweenType;

namespace PrimeTween {
    public partial struct Tween {
        /// <summary>Shakes the camera.<br/>
        /// If the camera is perspective, shakes all angles.<br/>
        /// If the camera is orthographic, shakes the z angle and x/y coordinates.<br/>
        /// Reference strengthFactor values - light: 0.2, medium: 0.5, heavy: 1.0.</summary>
        public static Sequence ShakeCamera(
            [NotNull] Camera camera,
            float strengthFactor,
            float duration = 0.5f,
            float frequency = ShakeSettings.kDefaultFrequency,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) {
            var transform = camera.transform;

            if (camera.orthographic) {
                float orthoPosStrength = strengthFactor * camera.orthographicSize * 0.03f;

                return Sequence.Create()
                               .Group(
                                   ShakeLocalPosition(
                                       transform,
                                       new ShakeSettings(
                                           new Vector3(orthoPosStrength, orthoPosStrength),
                                           duration,
                                           frequency,
                                           startDelay: startDelay,
                                           endDelay: endDelay,
                                           useUnscaledTime: useUnscaledTime
                                       )
                                   )
                               )
                               .Group(
                                   ShakeLocalRotation(
                                       transform,
                                       new ShakeSettings(
                                           new Vector3(0, 0, strengthFactor * 0.6f),
                                           duration,
                                           frequency,
                                           startDelay: startDelay,
                                           endDelay: endDelay,
                                           useUnscaledTime: useUnscaledTime
                                       )
                                   )
                               );
            }

            return Sequence.Create()
                           .Group(
                               ShakeLocalRotation(
                                   transform,
                                   new ShakeSettings(
                                       strengthFactor * Vector3.one,
                                       duration,
                                       frequency,
                                       startDelay: startDelay,
                                       endDelay: endDelay,
                                       useUnscaledTime: useUnscaledTime
                                   )
                               )
                           );
        }

        public static Tween ShakeLocalPosition(
            [NotNull] Transform target,
            Vector3 strength,
            float duration,
            float frequency = ShakeSettings.kDefaultFrequency,
            bool enableFalloff = true,
            Ease easeBetweenShakes = Ease.Default,
            float asymmetryFactor = 0f,
            int cycles = 1,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) =>
            ShakeLocalPosition(
                target,
                new ShakeSettings(
                    strength,
                    duration,
                    frequency,
                    enableFalloff,
                    easeBetweenShakes,
                    asymmetryFactor,
                    cycles,
                    startDelay,
                    endDelay,
                    useUnscaledTime
                )
            );

        public static Tween ShakeLocalPosition([NotNull] Transform target, ShakeSettings settings) =>
            ShakeTransform(TweenType.ShakeLocalPosition, target, settings);

        public static Tween PunchLocalPosition(
            [NotNull] Transform target,
            Vector3 strength,
            float duration,
            float frequency = ShakeSettings.kDefaultFrequency,
            bool enableFalloff = true,
            Ease easeBetweenShakes = Ease.Default,
            float asymmetryFactor = 0f,
            int cycles = 1,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) =>
            PunchLocalPosition(
                target,
                new ShakeSettings(
                    strength,
                    duration,
                    frequency,
                    enableFalloff,
                    easeBetweenShakes,
                    asymmetryFactor,
                    cycles,
                    startDelay,
                    endDelay,
                    useUnscaledTime
                )
            );

        public static Tween PunchLocalPosition([NotNull] Transform target, ShakeSettings settings) =>
            ShakeLocalPosition(target, settings.WithPunch());

        public static Tween ShakeLocalRotation(
            [NotNull] Transform target,
            Vector3 strength,
            float duration,
            float frequency = ShakeSettings.kDefaultFrequency,
            bool enableFalloff = true,
            Ease easeBetweenShakes = Ease.Default,
            float asymmetryFactor = 0f,
            int cycles = 1,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) =>
            ShakeLocalRotation(
                target,
                new ShakeSettings(
                    strength,
                    duration,
                    frequency,
                    enableFalloff,
                    easeBetweenShakes,
                    asymmetryFactor,
                    cycles,
                    startDelay,
                    endDelay,
                    useUnscaledTime
                )
            );

        public static Tween ShakeLocalRotation([NotNull] Transform target, ShakeSettings settings) =>
            ShakeTransform(TweenType.ShakeLocalRotation, target, settings);

        public static Tween PunchLocalRotation(
            [NotNull] Transform target,
            Vector3 strength,
            float duration,
            float frequency = ShakeSettings.kDefaultFrequency,
            bool enableFalloff = true,
            Ease easeBetweenShakes = Ease.Default,
            float asymmetryFactor = 0f,
            int cycles = 1,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) =>
            PunchLocalRotation(
                target,
                new ShakeSettings(
                    strength,
                    duration,
                    frequency,
                    enableFalloff,
                    easeBetweenShakes,
                    asymmetryFactor,
                    cycles,
                    startDelay,
                    endDelay,
                    useUnscaledTime
                )
            );

        public static Tween PunchLocalRotation([NotNull] Transform target, ShakeSettings settings) =>
            ShakeLocalRotation(target, settings.WithPunch());

        public static Tween ShakeScale(
            [NotNull] Transform target,
            Vector3 strength,
            float duration,
            float frequency = ShakeSettings.kDefaultFrequency,
            bool enableFalloff = true,
            Ease easeBetweenShakes = Ease.Default,
            float asymmetryFactor = 0f,
            int cycles = 1,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) =>
            ShakeScale(
                target,
                new ShakeSettings(
                    strength,
                    duration,
                    frequency,
                    enableFalloff,
                    easeBetweenShakes,
                    asymmetryFactor,
                    cycles,
                    startDelay,
                    endDelay,
                    useUnscaledTime
                )
            );

        public static Tween ShakeScale([NotNull] Transform target, ShakeSettings settings) =>
            ShakeTransform(TweenType.ShakeScale, target, settings);

        public static Tween PunchScale(
            [NotNull] Transform target,
            Vector3 strength,
            float duration,
            float frequency = ShakeSettings.kDefaultFrequency,
            bool enableFalloff = true,
            Ease easeBetweenShakes = Ease.Default,
            float asymmetryFactor = 0f,
            int cycles = 1,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = PrimeTweenConfig.defaultUseUnscaledTimeForShakes
        ) =>
            PunchScale(
                target,
                new ShakeSettings(
                    strength,
                    duration,
                    frequency,
                    enableFalloff,
                    easeBetweenShakes,
                    asymmetryFactor,
                    cycles,
                    startDelay,
                    endDelay,
                    useUnscaledTime
                )
            );

        public static Tween PunchScale([NotNull] Transform target, ShakeSettings settings) =>
            ShakeScale(target, settings.WithPunch());

        static Tween ShakeTransform(TweenType tweenType, [NotNull] Transform target, ShakeSettings settings) {
            if (PrimeTweenManager.Instance.IsDestroyed) {
                return default;
            }

            var tween = PrimeTweenManager.FetchTween(settings._updateType);
            ref var rt = ref tween.ManagedData;
            ref var d = ref tween.Data;

            PrepareShakeData(settings, ref rt, ref d, target);
            var tweenSettings = settings.TweenSettings;
            tween.Setup(target, ref tweenSettings, true, tweenType, ref rt, ref d);
            return PrimeTweenManager.Animate(ref rt, ref d);
        }

        public static Tween ShakeCustom<T>(
            [NotNull] T target,
            Vector3 startValue,
            ShakeSettings settings,
            [NotNull] Action<T, Vector3> onValueChange
        ) where T : class {
            Assert.IsNotNull(onValueChange);

            if (PrimeTweenManager.Instance.IsDestroyed) {
                return default;
            }

            var tween = PrimeTweenManager.FetchTween(settings._updateType);
            ref var rt = ref tween.ManagedData;
            ref var d = ref tween.Data;

            d.startValue.CopyFrom(ref startValue);
            PrepareShakeData(settings, ref rt, ref d, target);
            tween.customOnValueChange = onValueChange;
            var tweenSettings = settings.TweenSettings;
            tween.Setup(target, ref tweenSettings, false, TweenType.ShakeCustom, ref rt, ref d);

            tween.onValueChange = (ref TweenData rt2, ref UnmanagedTweenData d2) => {
                var customOnValueChange = rt2.cold.customOnValueChange as Action<T, Vector3>;
                Assert.IsNotNull(customOnValueChange);
                var val = d2.startValue.vector3 + GetShakeVal(ref rt2, ref d2);
                customOnValueChange(rt2.target as T, val);
            };

            return PrimeTweenManager.Animate(ref rt, ref d);
        }

        public static Tween PunchCustom<T>(
            [NotNull] T target,
            Vector3 startValue,
            ShakeSettings settings,
            [NotNull] Action<T, Vector3> onValueChange
        ) where T : class =>
            ShakeCustom(target, startValue, settings.WithPunch(), onValueChange);

        private static void PrepareShakeData(
            ShakeSettings settings,
            ref TweenData rt,
            ref UnmanagedTweenData d,
            object target
        ) {
            rt.endValueOrDiff.Reset(); // not used
            rt.cold.shakeData.Setup(settings, ref rt, ref d, target);
        }

        internal static Vector3 GetShakeVal(ref TweenData rt, ref UnmanagedTweenData d) {
            float fadeInOutFactor = CalcFadeInOutFactor(ref rt, ref d);
            return rt.ShakeData.GetNextVal(ref rt, ref d) * fadeInOutFactor;
        }

        private static float CalcFadeInOutFactor(ref TweenData tween, ref UnmanagedTweenData d) {
            float animationDuration = d.animationDuration;
            var elapsedTimeInterpolating = d.easedInterpolationFactor * animationDuration;
            Assert.IsTrue(elapsedTimeInterpolating >= 0f);

            if (animationDuration == 0f) {
                return 0f;
            }

            Assert.IsTrue(animationDuration > 0f);
            float halfDuration = animationDuration * 0.5f;
            var oneShakeDuration = 1f / tween.cold.shakeData.Frequency;

            if (oneShakeDuration > halfDuration) {
                oneShakeDuration = halfDuration;
            }

            float fadeInDuration = oneShakeDuration * 0.5f;

            if (elapsedTimeInterpolating < fadeInDuration) {
                return Mathf.InverseLerp(0f, fadeInDuration, elapsedTimeInterpolating);
            }

            var fadeoutStartTime = animationDuration - oneShakeDuration;
            Assert.IsTrue(fadeoutStartTime > 0f, tween.cold.id);

            if (elapsedTimeInterpolating > fadeoutStartTime) {
                return Mathf.InverseLerp(animationDuration, fadeoutStartTime, elapsedTimeInterpolating);
            }

            return 1f;
        }
    }

#if PRIME_TWEEN_INSPECTOR_DEBUGGING && UNITY_EDITOR
    [Serializable]
#endif
    internal struct ShakeData {
        private float m_T;
        private Vector3 m_From, m_To;
        private float m_SymmetryFactor;
        private int m_FalloffEaseInt;
        private AnimationCurve m_CustomStrengthOverTime;
        private Ease m_EaseBetweenShakes;
        internal Vector3 StrengthPerAxis { get; private set; }
        internal float Frequency { get; private set; }
        private float m_PrevInterpolationFactor;
        private int m_PrevCyclesDone;

        private const int k_DisabledFalloff = -42;
        internal bool IsAlive => Frequency != 0f;

        internal void Setup(ShakeSettings settings, ref TweenData rt, ref UnmanagedTweenData d, object target) {
            d.IsPunch = settings.IsPunch;
            m_SymmetryFactor = Mathf.Clamp01(1 - settings.asymmetry);

            {
                var strength = settings.strength;

                if (strength == default) {
                    Debug.LogError("Shake's strength is (0, 0, 0).");
                }

                StrengthPerAxis = strength;
            }

            {
                var frequency = settings.frequency;

                if (frequency <= 0) {
                    Debug.LogError($"Shake's frequency should be > 0f, but was {frequency}.", target as Object);
                    frequency = ShakeSettings.kDefaultFrequency;
                }

                Frequency = frequency;
            }

            {
                if (settings.enableFalloff) {
                    var falloffEase = settings.falloffEase;
                    var customStrengthOverTime = settings.strengthOverTime;

                    if (falloffEase == Ease.Default) {
                        falloffEase = Ease.Linear;
                    }

                    if (falloffEase == Ease.Custom) {
                        if (customStrengthOverTime == null
                            || !TweenSettings.ValidateCustomCurve(customStrengthOverTime)) {
                            Debug.LogError(
                                $"Shake falloff is Ease.Custom, but {nameof(ShakeSettings.strengthOverTime)} is not configured correctly. Using Ease.Linear instead.",
                                target as Object
                            );

                            falloffEase = Ease.Linear;
                        }
                    }

                    m_FalloffEaseInt = (int)falloffEase;
                    m_CustomStrengthOverTime = customStrengthOverTime;
                } else {
                    m_FalloffEaseInt = k_DisabledFalloff;
                }
            }

            {
                var easeBetweenShakes = settings.easeBetweenShakes;

                if (easeBetweenShakes == Ease.Custom) {
                    Debug.LogError(
                        $"{nameof(ShakeSettings.easeBetweenShakes)} doesn't support Ease.Custom.",
                        target as Object
                    );

                    easeBetweenShakes = Ease.OutQuad;
                }

                if (easeBetweenShakes == Ease.Default) {
                    easeBetweenShakes = PrimeTweenManager.kDefaultShakeEase;
                }

                m_EaseBetweenShakes = easeBetweenShakes;
            }

            OnCycleComplete(ref rt, ref d);
        }

        internal void OnCycleComplete(ref TweenData rt, ref UnmanagedTweenData d) {
            Assert.IsTrue(IsAlive);
            ResetAfterCycle();
            d.ShakeSign = d.IsPunch || PrimeTweenManager.sRandom.NextDouble() < 0.5;
            m_To = GenerateShakePoint(ref d);
        }

        private static int GetMainAxisIndex(Vector3 strengthByAxis) {
            int mainAxisIndex = -1;
            float maxStrength = float.NegativeInfinity;

            for (var i = 0; i < 3; i++) {
                var strength = Mathf.Abs(strengthByAxis[i]);

                if (strength > maxStrength) {
                    maxStrength = strength;
                    mainAxisIndex = i;
                }
            }

            Assert.IsTrue(mainAxisIndex >= 0);
            return mainAxisIndex;
        }

        internal Vector3 GetNextVal(ref TweenData rt, ref UnmanagedTweenData d) {
            var interpolationFactor = d.easedInterpolationFactor;
            Assert.IsTrue(interpolationFactor <= 1);

            int cyclesDiff = d.GetCyclesDone() - m_PrevCyclesDone;
            m_PrevCyclesDone = d.GetCyclesDone();

            if (interpolationFactor == 0f || (cyclesDiff > 0 && d.GetCyclesDone() != d.cyclesTotal)) {
                OnCycleComplete(ref rt, ref d);
                m_PrevInterpolationFactor = interpolationFactor;
            }

            float animationDuration = d.animationDuration;
            var dt = (interpolationFactor - m_PrevInterpolationFactor) * animationDuration;
            m_PrevInterpolationFactor = interpolationFactor;

            var strengthOverTime = CalcStrengthOverTime(interpolationFactor);

            var frequencyFactor =
                Mathf.Clamp01(
                    strengthOverTime * 3f
                ); // handpicked formula that describes the relationship between strength and frequency

            // The initial velocity should twice as big because the first shake starts from zero (twice as short as total range).
            var elapsedTimeInterpolating = d.easedInterpolationFactor * animationDuration;
            var halfShakeDuration = 0.5f / rt.ShakeData.Frequency;
            float iniVelFactor = elapsedTimeInterpolating < halfShakeDuration ? 2f : 1f;

            m_T += Frequency * dt * frequencyFactor * iniVelFactor;

            if (m_T < 0f || m_T >= 1f) {
                d.ShakeSign = !d.ShakeSign;

                if (m_T < 0f) {
                    m_T = 1f;
                    m_To = m_From;
                    m_From = GenerateShakePoint(ref d);
                } else {
                    m_T = 0f;
                    m_From = m_To;
                    m_To = GenerateShakePoint(ref d);
                }
            }

            Vector3 result = default;

            for (int i = 0; i < 3; i++) {
                result[i] = Mathf.Lerp(m_From[i], m_To[i], StandardEasing.Evaluate(m_T, m_EaseBetweenShakes))
                            * strengthOverTime;
            }

            return result;
        }

        private Vector3 GenerateShakePoint(ref UnmanagedTweenData d) {
            var mainAxisIndex = GetMainAxisIndex(StrengthPerAxis);
            Vector3 result = default;
            float signFloat = d.ShakeSign ? 1f : -1f;

            for (int i = 0; i < 3; i++) {
                var strength = StrengthPerAxis[i];

                if (d.IsPunch) {
                    result[i] = ClampBySymmetryFactor(strength * signFloat, strength, m_SymmetryFactor);
                } else {
                    result[i] = i == mainAxisIndex ? CalcMainAxisEndVal(signFloat, strength, m_SymmetryFactor)
                        : CalcNonMainAxisEndVal(strength, m_SymmetryFactor);
                }
            }

            return result;
        }

        private float CalcStrengthOverTime(float interpolationFactor) {
            if (m_FalloffEaseInt == k_DisabledFalloff) {
                return 1;
            }

            var falloffEase = (Ease)m_FalloffEaseInt;

            if (falloffEase != Ease.Custom) {
                return 1 - StandardEasing.Evaluate(interpolationFactor, falloffEase);
            }

            Assert.IsNotNull(m_CustomStrengthOverTime);
            return m_CustomStrengthOverTime.Evaluate(interpolationFactor);
        }

        private static float CalcMainAxisEndVal(float velocity, float strength, float symmetryFactor) {
            float result =
                Mathf.Sign(velocity)
                * strength
                * RandomRange(
                    0.6f,
                    1f
                ); // doesn't matter if we're using strength or its abs because velocity alternates

            return ClampBySymmetryFactor(result, strength, symmetryFactor);
        }

        private static float ClampBySymmetryFactor(float val, float strength, float symmetryFactor) {
            if (strength > 0) {
                return Mathf.Clamp(val, -strength * symmetryFactor, strength);
            }

            return Mathf.Clamp(val, strength, -strength * symmetryFactor);
        }

        private static float CalcNonMainAxisEndVal(float strength, float symmetryFactor) {
            if (strength > 0) {
                return RandomRange(-strength * symmetryFactor, strength);
            }

            return RandomRange(strength, -strength * symmetryFactor);
        }

        private static float RandomRange(float minInclusive, float max) {
            double val = PrimeTweenManager.sRandom.NextDouble();
            return (float)(minInclusive + val * (max - minInclusive));
        }

        internal static bool TryTakeStartValueFromOtherShake(
            ref TweenData newTween,
            ref UnmanagedTweenData newTweenData
        ) {
            if (!newTween.ShakeData.IsAlive) {
                return false;
            }

            var shakeTransform = newTween.target as Transform;

            if (shakeTransform == null) {
                return false;
            }

            var shakes = PrimeTweenManager.Instance.shakes;
            var key = (shakeTransform, newTweenData.tweenType);

            if (!shakes.TryGetValue(key, out var data)) {
                var startValue = Utils.GetAnimatedValue(
                    newTween.target,
                    newTweenData.tweenType,
                    newTween.cold.longParam
                );

                shakes.Add(key, (startValue, 1));
                return false;
            }

            Assert.IsTrue(data.count >= 1);
            newTweenData.startValue = data.startValue;

            // Debug.Log($"tryTakeStartValueFromOtherShake {data.startValue.vector3}");
            data.count++;
            shakes[key] = data;
            return true;
        }

        internal void Reset(object target, TweenType tweenType) {
            Assert.IsTrue(IsAlive);
            var shakeTransform = target as Transform;

            if (shakeTransform != null) {
                var key = (shakeTransform, tweenType);
                var shakes = PrimeTweenManager.Instance.shakes;

                if (shakes.TryGetValue(key, out var data)) {
                    // no key present if it's a ShakeCustom() with Transform target because custom shakes have startFromCurrent == false and aren't added to shakes dict
                    Assert.IsTrue(data.count >= 1);
                    data.count--;

                    if (data.count == 0) {
                        bool isRemoved = shakes.Remove(key);
                        Assert.IsTrue(isRemoved);
                    } else {
                        shakes[key] = data;
                    }
                }
            }

            ResetAfterCycle();
            m_CustomStrengthOverTime = null;
            Frequency = 0f;
            m_PrevInterpolationFactor = 0f;
            m_PrevCyclesDone = 0;
            Assert.IsFalse(IsAlive);
        }

        private void ResetAfterCycle() {
            m_T = 0f;
            m_From = default;
        }
    }
}