using System;
using Debug = UnityEngine.Debug;
using SerializeField = UnityEngine.SerializeField;
using HideInInspector = UnityEngine.HideInInspector;
using Tooltip = UnityEngine.TooltipAttribute;
using AnimationCurve = UnityEngine.AnimationCurve;
using CanBeNull = JetBrains.Annotations.CanBeNullAttribute;
using NotNull = JetBrains.Annotations.NotNullAttribute;
using FormerlySerializedAs = UnityEngine.Serialization.FormerlySerializedAsAttribute;

namespace PrimeTween {
    /// <summary>TweenSettings contains animation properties (duration, ease, delay, etc.). Can be serialized and tweaked from the Inspector.<br/>
    /// Use this struct when the 'start' and 'end' values of an animation are NOT known in advance and determined at runtime.<br/>
    /// When the 'start' and 'end' values ARE known, consider using <see cref="TweenSettings{T}"/> instead.</summary>
    /// <example>
    /// Tweak an animation settings from the Inspector, then pass the settings to the Tween method:
    /// <code>
    /// [SerializeField] TweenSettings animationSettings;
    /// public void AnimatePositionX(float targetPosX) {
    ///     Tween.PositionX(transform, targetPosX, animationSettings);
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public struct TweenSettings {
        public float duration;

        [Tooltip(Constants.kEaseTooltip)]
        public Ease ease;

        [Tooltip("A custom Animation Curve that will work as an easing curve.")]
        [CanBeNull]
        public AnimationCurve customEase;

        [Tooltip(Constants.kCyclesTooltip)]
        public int cycles;

        [Tooltip(Constants.kCycleModeTooltip)]
        public CycleMode cycleMode;

        [Tooltip(Constants.kStartDelayTooltip)]
        public float startDelay;

        [Tooltip(Constants.kEndDelayTooltip)]
        public float endDelay;

        [Tooltip(Constants.kUnscaledTimeTooltip)]
        public bool useUnscaledTime;

        [SerializeField, FormerlySerializedAs("useFixedUpdate")]
        [HideInInspector]
        private bool m_UseFixedUpdate;

        public UpdateType updateType {
            get => m_UseFixedUpdate ? UpdateType.FixedUpdate : new UpdateType(_updateType);
            set {
                _updateType = value.enumValue;
                m_UseFixedUpdate = value == UpdateType.FixedUpdate;
            }
        }

        [SerializeField, Tooltip(Constants.kUpdateTypeTooltip)]
        internal EUpdateType _updateType;

        [NonSerialized]
        internal ParametricEase parametricEase;

        [NonSerialized]
        internal float parametricEaseStrength;

        [NonSerialized]
        internal float parametricEasePeriod;

        internal TweenSettings(
            float duration,
            Ease ease,
            Easing? customEasing,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false,
            UpdateType updateType = default
        ) {
            this.duration = duration;
            var curve = customEasing?.curve;

            if (ease == Ease.Custom && customEasing?.parametricEase == ParametricEase.None) {
                if (curve == null || !ValidateCustomCurveKeyframes(curve)) {
                    Debug.LogError(Constants.kCustomAnimationCurveInvalidError);
                    ease = Ease.Default;
                }
            }

            this.ease = ease;
            customEase = ease == Ease.Custom ? curve : null;
            this.cycles = cycles;
            this.cycleMode = cycleMode;
            this.startDelay = startDelay;
            this.endDelay = endDelay;
            this.useUnscaledTime = useUnscaledTime;
            parametricEase = customEasing?.parametricEase ?? ParametricEase.None;
            parametricEaseStrength = customEasing?.parametricEaseStrength ?? float.NaN;
            parametricEasePeriod = customEasing?.parametricEasePeriod ?? float.NaN;
            m_UseFixedUpdate = updateType == UpdateType.FixedUpdate;
            _updateType = updateType.enumValue;
        }

        public TweenSettings(
            float duration,
            Ease ease = Ease.Default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false,
            UpdateType updateType = default
        )
            : this(duration, ease, null, cycles, cycleMode, startDelay, endDelay, useUnscaledTime, updateType) { }

        public TweenSettings(
            float duration,
            Easing easing,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false,
            UpdateType updateType = default
        )
            : this(
                duration,
                easing.ease,
                easing,
                cycles,
                cycleMode,
                startDelay,
                endDelay,
                useUnscaledTime,
                updateType
            ) { }

        public TweenSettings(
            float duration,
            AnimationCurve ease,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false,
            UpdateType updateType = default
        )
            : this(
                duration,
                Ease.Custom,
                Easing.Curve(ease),
                cycles,
                cycleMode,
                startDelay,
                endDelay,
                useUnscaledTime,
                updateType
            ) { }

        internal static void SetCyclesTo1If0(ref int cycles) {
            if (cycles == 0) {
                cycles = 1;
            }
        }

        internal const float kMinDuration = 0.0001f;

        internal void SetValidValues() {
            ValidateFiniteDuration(ref duration);
            ValidateFiniteDuration(ref startDelay);
            ValidateFiniteDuration(ref endDelay);
            SetCyclesTo1If0(ref cycles);

            if (duration != 0f) {
#if UNITY_EDITOR && PRIME_TWEEN_SAFETY_CHECKS
                if (duration < kMinDuration) {
                    Debug.LogError("duration = Mathf.Max(minDuration, duration);");
                }
#endif
                duration = Mathf.Max(kMinDuration, duration);
            }

            startDelay = Mathf.Max(0f, startDelay);
            endDelay = Mathf.Max(0f, endDelay);
        }

        internal static void ValidateFiniteDuration(ref float f) {
            if (float.IsNaN(f) || float.IsInfinity(f)) {
                Debug.LogError(Constants.kDurationInvalidError);
                f = 0f;
            }
        }

        internal static bool ValidateCustomCurve([NotNull] AnimationCurve curve) {
#if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
            if (curve.length < 2) {
                Debug.LogError(
                    "Custom animation curve should have at least 2 keyframes, please edit the curve in Inspector."
                );

                return false;
            }

            return true;
#else
            return true;
#endif
        }

        internal static bool ValidateCustomCurveKeyframes([NotNull] AnimationCurve curve) {
#if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
            if (!ValidateCustomCurve(curve)) {
                return false;
            }

            var instance = PrimeTweenManager.Instance;

            if (instance == null || instance.validateCustomCurves) {
                var error = GetError();

                if (error != null) {
                    Debug.LogWarning(
                        $"Custom animation curve is not configured correctly which may have unexpected results: {error}. "
                        + Constants.BuildWarningCanBeDisabledMessage(nameof(PrimeTweenConfig.validateCustomCurves))
                    );
                }

                string GetError() {
                    var start = curve[0];

                    if (!Mathf.Approximately(start.time, 0)) {
                        return "start time is not 0";
                    }

                    if (!Mathf.Approximately(start.value, 0) && !Mathf.Approximately(start.value, 1)) {
                        return "start value is not 0 or 1";
                    }

                    var end = curve[curve.length - 1];

                    if (!Mathf.Approximately(end.time, 1)) {
                        return "end time is not 1";
                    }

                    if (!Mathf.Approximately(end.value, 0) && !Mathf.Approximately(end.value, 1)) {
                        return "end value is not 0 or 1";
                    }

                    return null;
                }
            }

            return true;
#else
            return true;
#endif
        }
    }

    [Serializable]
    public struct UpdateType : IEquatable<UpdateType> {
        /// Uses <see cref="PrimeTweenConfig.defaultUpdateType"/> to control the default Unity's event function, which updates the animation.
        public static readonly UpdateType Default = new(EUpdateType.Default);

        /// Updates the animation in MonoBehaviour.Update().<br/>
        /// If the animation has 'startValue' and doesn't have a start delay, the 'startValue' is applied in <see cref="PrimeTweenManager.LateUpdate"/>.
        /// This ensures the animation is rendered at the 'startValue' in the same frame it's created.
        public static readonly UpdateType Update = new(EUpdateType.Update);

        /// Updates the animation in MonoBehaviour.LateUpdate().<br/>
        /// If the animation has 'startValue' and doesn't have a start delay, the 'startValue' is applied in <see cref="PrimeTweenManager.LateUpdate"/>.
        /// This ensures the animation is rendered at the 'startValue' in the same frame it's created.
        public static readonly UpdateType LateUpdate = new(EUpdateType.LateUpdate);

        /// Updates the animation in MonoBehaviour.FixedUpdate().<br/>
        /// Unlike Update and LateUpdate animations, FixedUpdate animations don't apply the 'startValue' before the first frame is rendered.
        /// They receive their first update in the first FixedUpdate() after creation.
        public static readonly UpdateType FixedUpdate = new(EUpdateType.FixedUpdate);
#if PRIME_TWEEN_EXPERIMENTAL
        public static readonly UpdateType Manual = new(EUpdateType.Manual);
#endif

        [SerializeField]
        internal EUpdateType enumValue;

        internal UpdateType(EUpdateType enumValue) {
            this.enumValue = enumValue;
        }

        public static bool operator ==(UpdateType lhs, UpdateType rhs) => lhs.enumValue == rhs.enumValue;
        public static bool operator !=(UpdateType lhs, UpdateType rhs) => lhs.enumValue != rhs.enumValue;
        public bool Equals(UpdateType other) => enumValue == other.enumValue;
        public override bool Equals(object obj) => obj is UpdateType other && Equals(other);
        public override int GetHashCode() => ((int)enumValue).GetHashCode();
    }

    internal enum EUpdateType : byte {
        [Tooltip(
            "Uses 'PrimeTweenConfig.defaultUpdateType' to control the default Unity's event function, which updates the animation."
        )]
        Default,

        [Tooltip(
            "Updates the animation in MonoBehaviour.Update().\n\n"
            + "If the animation has 'startValue' and doesn't have a start delay, the 'startValue' is applied in 'PrimeTweenManager.LateUpdate'. This ensures the animation is rendered at the 'startValue' in the same frame it's created."
        )]
        Update,

        [Tooltip(
            "Updates the animation in MonoBehaviour.LateUpdate().\n\n"
            + "If the animation has 'startValue' and doesn't have a start delay, the 'startValue' is applied in 'PrimeTweenManager.LateUpdate'. This ensures the animation is rendered at the 'startValue' in the same frame it's created."
        )]
        LateUpdate,

        [Tooltip(
            "Updates the animation in 'MonoBehaviour.FixedUpdate()'.\n\n"
            + "Unlike Update and LateUpdate animations, FixedUpdate animations don't apply the 'startValue' before the first frame is rendered. They receive their first update in the first FixedUpdate() after creation."
        )]
        FixedUpdate,
#if PRIME_TWEEN_EXPERIMENTAL
        Manual
#endif
    }

    /// <summary>The standard animation easing types. Different easing curves produce a different animation 'feeling'.<br/>
    /// Play around with different ease types to choose one that suites you the best.
    /// You can also provide a custom AnimationCurve as an ease function or parametrize eases with the Easing.Overshoot/Elastic/BounceExact(...) methods.</summary>
    public enum Ease : sbyte {
        Custom = -1,
        Default = 0,
        Linear = 1,
        InSine,
        OutSine,
        InOutSine,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        InQuart,
        OutQuart,
        InOutQuart,
        InQuint,
        OutQuint,
        InOutQuint,
        InExpo,
        OutExpo,
        InOutExpo,
        InCirc,
        OutCirc,
        InOutCirc,
        InElastic,
        OutElastic,
        InOutElastic,
        InBack,
        OutBack,
        InOutBack,
        InBounce,
        OutBounce,
        InOutBounce
    }

    /// <summary>Controls the behavior of subsequent cycles when a tween has more than one cycle.</summary>
    public enum CycleMode : byte {
        [Tooltip(Constants.kCycleModeRestartTooltip)]
        Restart = ECycleMode.Restart,

        [Tooltip(Constants.kCycleModeYoyoTooltip)]
        Yoyo = ECycleMode.Yoyo,

        [Tooltip(Constants.kCycleModeIncrementalTooltip)]
        Incremental = ECycleMode.Incremental,

        [Tooltip(Constants.kCycleModeRewindTooltip)]
        Rewind = ECycleMode.Rewind
    }
}