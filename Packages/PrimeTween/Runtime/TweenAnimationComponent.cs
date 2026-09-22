#if TEXT_MESH_PRO_INSTALLED
using TMPro;
#endif
#if UNITY_UGUI_INSTALLED
using UnityEngine.UI;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;

namespace PrimeTween {
    /// <summary>A component that exposes <see cref="TweenAnimation"/> to the scene. Consider using it if you need to reference the animation from other scripts or animations in the scene.<br/>
    /// - Allows authoring and previewing complex animation sequences in the Inspector without writing any code.<br/>
    /// - Allows an animation to be referenced in a scene and played from other MonoBehaviours or UnityEvents.<br/>
    /// - Can play an animation in response to Unity messages like OnEnable and OnDisable.<br/></summary>
    [PublicAPI, SelectionBase]
    public class TweenAnimationComponent : MonoBehaviour {
        [SerializeField]
        public new TweenAnimation animation = new();

        [SerializeField]
        public UnityMessageAction onEnable;

        [SerializeField]
        public UnityMessageAction onDisable;

        /// <summary>Defines the action to perform when a Unity message like OnEnable or OnDisable is invoked.</summary>
        public enum UnityMessageAction {
            None,
            Trigger,
            SetStateTrue,
            SetStateFalse,
            Stop,
            Reset,
            Complete
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___Complete() => GetAnimation()?.Complete();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___SetPaused(bool isPaused) {
            if (GetAnimation() is TweenAnimation a)
                a.isPaused = isPaused;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___SetProgressTotal(float value) {
            if (GetAnimation() is TweenAnimation a)
                a.progressTotal = value;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___SetState(bool newState) {
            if (GetAnimation() is TweenAnimation a)
                a.state = newState;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___Stop() => GetAnimation()?.Stop();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___ToggleState() => GetAnimation()?.ToggleState();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ___Trigger() => GetAnimation()?.Trigger();

        [CanBeNull]
        private TweenAnimation GetAnimation() {
            if (animation == null) {
                Debug.LogError($"{nameof(animation)} is null.", this);
            }

            return animation;
        }

        public const string kSelfReferenceError = "It's not allowed to reference '"
                                                  + nameof(TweenAnimationComponent)
                                                  + "' from itself or have a circular reference in the hierarchy.";

        private void OnEnable() => ApplyBehaviour(onEnable);

        private void OnDisable() {
            bool warnTweenOnDisabledTarget = PrimeTweenConfig.warnTweenOnDisabledTarget;
            PrimeTweenConfig.warnTweenOnDisabledTarget = false;
            ApplyBehaviour(onDisable);
            PrimeTweenConfig.warnTweenOnDisabledTarget = warnTweenOnDisabledTarget;
        }

        private void ApplyBehaviour(UnityMessageAction behaviour) {
#if UNITY_EDITOR
            if (PrimeTweenManager.sPlayModeState == UnityEditor.PlayModeStateChange.ExitingPlayMode) {
                return;
            }
#endif
            var a = GetAnimation();

            if (a == null) {
                return;
            }

            switch (behaviour) {
                case UnityMessageAction.None:
                    break;
                case UnityMessageAction.Trigger:
                    a.Trigger();
                    break;
                case UnityMessageAction.SetStateTrue:
                    a.state = true;
                    break;
                case UnityMessageAction.SetStateFalse:
                    a.state = false;
                    break;
                case UnityMessageAction.Stop:
                    a.Stop();
                    break;
                case UnityMessageAction.Reset:
                    a.Reset();
                    break;
                case UnityMessageAction.Complete:
                    a.Complete();
                    break;
                default:
                    Debug.LogError($"Invalid {nameof(UnityMessageAction)}: {behaviour}.", this);
                    break;
            }
        }
    }

    /// <summary>A serializable animation which allows authoring and previewing complex animation sequences in the Inspector without writing any code.<br/>
    /// - Allows creating animations right where you need them with no need to set up references in a scene.<br/>
    /// - If animation is reversible, allows you to change the direction of the animation by setting 'state'. No need to keep track of the state yourself, simply choose a new desired direction at any time.<br/>
    /// - Provides ways to tweak how an animation responds to interruptions with <see cref="InterruptionMode"/>.<br/>
    /// <example>
    /// <code>
    /// // Add TweenAnimation to your script, then set the animation up in the Inspector.
    /// [SerializeField] TweenAnimation doorAnimation = new TweenAnimation();
    ///
    /// void Update() {
    ///     if (Input.GetKeyDown(KeyCode.Space)) {
    ///         // Play the animation in response to gameplay events.
    ///         doorAnimation.Trigger();
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [Serializable, PublicAPI]
    public partial class TweenAnimation : ISerializationCallbackReceiver { // p1 todo add OnComplete?
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        [SerializeField]
        public List<Data> animations = new();

        [Tooltip(
            "Use for toggle animations (e.g., open/close or show/hide).\n\n"
            + "Set 'state = true' to play forward and 'state = false' to reverse — no need for a separate direction variable. Or use 'Trigger()' to toggle the current state."
        )]
        [SerializeField]
        public bool isReversible;

        /// <summary>See <see cref="Trigger"/> method's documentation to know when the <see cref="InterruptionMode"/> is applied.</summary>
        [Tooltip("Controls how the animation behaves when interrupted.")]
        [SerializeField]
        public InterruptionMode interruptionMode;

        [Tooltip(Constants.kCyclesTooltip)]
        [SerializeField]
        public int cycles = 1;

        [Tooltip("Controls how the animation behaves with multiple cycles or multiple successive plays.")]
        [SerializeField]
        public Sequence.SequenceCycleMode
            cycleMode; // p2 todo if sequenceEase is Linear or Default, Yoyo and Rewind are the same. I can collapse these options into one enum in this case

        [Tooltip("If enabled, triggering the animation again while it's already playing will be ignored.")]
        [SerializeField]
        public bool ignoreDuplicateTrigger;

        [Tooltip("Instantly resets the animation to the beginning upon completion.")]
        [SerializeField]
        public bool resetOnCompletion;

        [Tooltip(
            "Easing curve of the entire '"
            + nameof(TweenAnimation)
            + "'. Different easing curves produce a different animation 'feeling'.\n\n"
            + "Easing is clamped to [0:1] range and is applied to the whole animation's timeline."
        )]
        [SerializeField]
        public Ease sequenceEase = Ease.Linear;

        [Tooltip(Constants.kUnscaledTimeTooltip)]
        [SerializeField]
        public bool useUnscaledTime;

        [Tooltip(Constants.kUpdateTypeTooltip)]
        [SerializeField]
        internal EUpdateType _updateType;

        [Tooltip("Custom timescale.")]
        [SerializeField]
        internal float _timeScale = 1f;

        [SerializeField]
        internal UnityEngine.Object context;

#if UNITY_EDITOR
        [NonSerialized]
        private ValueWrapper?[] m_IniValues;
        
        [NonSerialized] 
        internal HeaderData[] headers;

        /// p2 todo headers should display "---" when multi-editing different animations
        /// p2 todo: add custom tween target name and property name; add material property name
        internal struct HeaderData {
            internal Operation operation;
            internal TweenType tweenType;
            internal string targetName;
            internal GUIContent guiContent;
            internal float insertionTime;
            internal float duration;
            internal int cycles;
            internal string eventNames;
        }
#endif

        /// <summary>Controls the Unity event function that updates the animation. The default is MonoBehaviour.Update().</summary>
        public UpdateType updateType {
            get => new UpdateType(_updateType);
            set => _updateType = value.enumValue;
        }

        [NonSerialized]
        private bool m_State;

        /// <summary>The logical state of the animation. Instead of keeping track of the animation state manually, you can store the state in the animation directly.<br/>
        /// State means different things depending on the animation type. Changing state applies <see cref="interruptionMode"/>.<br/><br/>
        /// Simple animations: returns <c>true</c> if an animation is currently playing.<br/>
        /// Infinite animations: returns <c>true</c> if an animation is currently playing and NOT being interrupted by <see cref="interruptionMode"/>.<br/>
        /// Reversible animations (<see cref="isReversible"/> == true): returns <c>true</c> if the animation is moving forward OR already at the end. Changing state changes animation direction.<br/>
        ///
        /// <example>
        /// <code>
        /// [SerializeField] TweenAnimation doorAnimation = new TweenAnimation();
        /// [SerializeField] TweenAnimation infiniteGlowAnimation = new TweenAnimation();
        ///
        /// public void SetDoorState(bool isOpen) {
        ///     // Apply the animation state with no additional checks. TweenAnimation will only play when the state changes
        ///     doorAnimation.state = isOpen;
        /// }
        ///
        /// public void EnableGlow(bool isEnabled) {
        ///     // Using InterruptionMode.Default will seamlessly transition the infinite animation between active and inactive states
        ///     infiniteGlowAnimation.state = isEnabled;
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public bool state {
            get {
                if (isReversible
                    || (IsInfinite() && isAlive)
                   ) {
                    return m_State;
                }

                return isAlive;
            }
            set {
                if (!TryManipulate()) {
                    return;
                }

                if (state != value) {
                    if (isAlive) {
                        Interrupt(value);
                    } else {
                        CreateSequenceAndApplyDirection(value);
                    }

                    SetState(value);
                    Assert.AreEqual(state, value);
                }
            }
        }

        private void Interrupt(bool newState) {
            Assert.IsTrue(isAlive);
            Assert.IsTrue(CanManipulate());
            SanitiseInterruptionMode(isReversible, cycles, ref interruptionMode);

            if (IsInfinite()) {
                if (newState) {
                    SetRemainingCycles(-1);
                    SetState(true);
                } else {
                    if (interruptionMode == InterruptionMode.Reset) {
                        Reset();
                    } else {
                        // StopAtStart
                        if (cycleMode == Sequence.SequenceCycleMode.Restart) {
                            SetRemainingCycles(1);
                        } else {
                            SetRemainingCycles(false);
                        }

                        SetState(false);
                    }
                }
            } else if (isReversible) {
                if (interruptionMode == InterruptionMode.Complete) {
                    Stop();
                    CreateSequenceAndApplyDirection(newState);
                    SetState(newState);
                    Complete();
                } else {
                    // Rewind
                    sequence.timeScale *= -1f;
                    SetState(!state);
                }
            } else { // simple
                if (newState) {
                    Debug.LogError(
                        "Simple animation is in an invalid state. It was alive while setting 'state = true'. Please file a bug report: https://github.com/KyryloKuzyk/PrimeTween/issues"
                    );
                } else {
                    switch (interruptionMode) {
                        case InterruptionMode.Complete:
                            Complete();
                            break;
                        default:
                            Reset();
                            break;
                    }
                }
            }
        }

        [System.Diagnostics.Conditional("_")]
        private void Print(string msg) {
            // Debug.Log($"[{Time.frameCount}] {msg}  {(context != null ? context.name : string.Empty)}", context);
        }

        internal void SetState(bool value) {
            if (m_State == value) {
                return;
            }

            // print($"SetState:{value}");
            m_State = value;

            // Set recursively on all nested reversible animations
            if (animations != null) {
                foreach (var data in animations) {
                    if (data.tweenType == TweenType.TweenAnimationComponent
                        && data.operation != Operation.Disabled
                        && data.targets != null
                        && data.targets.Count > 0
                        && data.targets[0] is TweenAnimationComponent component
                        && component != null
                        && component.animation is TweenAnimation nestedAnimation
                        && nestedAnimation != this
                        && nestedAnimation.isReversible
                       ) {
                        nestedAnimation.SetState(value);
                    }
                }
            }
        }

        internal const float kMinTimeScale = 0.01f;
        internal Sequence sequence;
#if UNITY_EDITOR
        [NonSerialized]
        private bool m_IsStartValueBackedUp;

        internal static bool
            sIsPreviewing; // this should be a static field. For example, when GameObject has multiple TweenAnimations, if we're previewing one animation, other animations should not save start values and should be disabled in Inspector

        internal static (int id, int keyboardControl, TweenAnimation animation, string propertyPath, Data? iniData)
            sEndValueHighlightData;

        internal static void ResetHighlightData() {
            if (sEndValueHighlightData.keyboardControl == GUIUtility.keyboardControl) {
                GUIUtility.keyboardControl = 0; // reset the currently highlighted inspector property
            }

            if (sEndValueHighlightData.iniData.HasValue && sEndValueHighlightData.animation != null) {
                var iniData = sEndValueHighlightData.iniData.Value;

                for (int i = 0; i < iniData.targets.Count; i++) {
                    // Debug.Log($"restore ini val after endValue preview {iniData.targets[i]?.name} {iniData.startValue}");
                    string error = string.Empty;
                    var tween = iniData.StartTween(ref error, sEndValueHighlightData.animation, i);

                    if (tween.isAlive) {
                        tween.elapsedTime = 0f;
                        tween.Stop();
                    } else {
                        Debug.LogError(error);
                    }
                }
            }

            sEndValueHighlightData = default;
        }
#endif

        /// <summary>An animation is 'alive' when it has been played and has not stopped and has not completed yet. A paused animation is also considered 'alive'.</summary>
        public bool isAlive => sequence.isAlive;

        /// <summary>Stops the animation.</summary>
        public void Stop() {
            // print("Stop");
            sequence.Stop();

            if (!isReversible) {
                SetState(false);
            }
        }

        /// <summary>Immediately completes the animation. See also <see cref="Tween.Complete"/>.</summary>
        public void Complete() {
            // print("Complete");
            sequence.Complete();
        }

        private const string k_SetRemainingCyclesNotSupportedWithReversible =
            "Reversible animations don't support " + nameof(SetRemainingCycles);

        /// <summary>See also <see cref="Tween.SetRemainingCycles(int)"/>.</summary>
        public void SetRemainingCycles(int cycles) {
            if (isReversible) {
                Debug.LogError(k_SetRemainingCyclesNotSupportedWithReversible);
                return;
            }

            sequence.SetRemainingCycles(cycles);
        }

        /// <summary>See also <see cref="Tween.SetRemainingCycles(bool)"/>.</summary>
        public void SetRemainingCycles(bool stopAtEndValue) {
            if (isReversible) {
                Debug.LogError(k_SetRemainingCyclesNotSupportedWithReversible);
                return;
            }

            sequence.SetRemainingCycles(stopAtEndValue);
        }

        /// <summary>The number of completed cycles.</summary>
        public int cyclesDone => sequence.cyclesDone;

        /// <summary>The total number of cycles. Returns -1 to indicate an infinite number of cycles.</summary>
        public int cyclesTotal => sequence.cyclesTotal;

        [NonSerialized]
        private bool m_IsPaused;

        /// <summary>Gets or sets whether the animation is paused.</summary>
        public bool isPaused {
            get => m_IsPaused;
            set {
                m_IsPaused = value;

                if (sequence.isAlive) {
                    sequence.isPaused = value;
                }
            }
        }

        /// <summary>Custom timeScale.</summary>
        public float timeScale {
            // p2 todo add Tween.TweenTimeScale(TweenAnimation)? Or Custom animation can be used instead?
            get => _timeScale;
            set {
                if (value < kMinTimeScale) {
                    Log(LogType.Error, $"{nameof(timeScale)} can't be less than {kMinTimeScale}.");
                    value = kMinTimeScale;
                }

                if (float.IsNaN(value) || float.IsInfinity(value)) {
                    throw new ArgumentException($"Invalid {nameof(timeScale)}: {value}.");
                }

                _timeScale = value;

                if (sequence.isAlive) {
                    sequence.timeScale = value;
                }
            }
        }

        /// <summary>The duration of one cycle.</summary>
        public float duration => sequence.duration;

        /// <summary>The duration of all cycles. If the <see cref="cycles"/> == -1, returns <see cref="float.PositiveInfinity"/>.</summary>
        public float durationTotal => sequence.durationTotal;

        /// <summary>Elapsed time of the current cycle.</summary>
        public float elapsedTime {
            get => sequence.elapsedTime;
            set => sequence.elapsedTime = value;
        }

        /// <summary>Elapsed time of all cycles.</summary>
        public float elapsedTimeTotal {
            get => sequence.elapsedTimeTotal;
            set => sequence.elapsedTimeTotal = value;
        }

        /// <summary>Normalized progress of the current cycle expressed in the 0..1 range.</summary>
        public float progress {
            get {
                if (!sequence.root.ValidateIsAlive()) {
                    Debug.LogError(Constants.kUseProgressTotalInstead);
                    return 0f;
                }

                float res = sequence.progress;
                return sequence.root.tween.Data.IsSequenceInverted ? 1f - res : res;
            }
            set {
                if (!sequence.root.ValidateIsAlive()) {
                    Debug.LogError(Constants.kUseProgressTotalInstead);
                    return;
                }

                if (sequence.root.tween.Data.IsSequenceInverted) {
                    sequence.progress = 1f - value;
                } else {
                    sequence.progress = value;
                }
            }
        }

        /// <summary>Normalized progress of all cycles expressed in the 0..1 range. Can be accessed and set even if animation is not <see cref="isAlive"/>.</summary>
        public float progressTotal {
            get {
                if (!isAlive) {
                    return state ? 1f : 0f;
                }

                float totalProgress = sequence.progressTotal;
                return sequence.root.tween.Data.IsSequenceInverted ? 1f - totalProgress : totalProgress;
            }
            set => SetProgressTotal(value, true);
        }

        internal void SetProgressTotal(float value, bool autoStop) {
            if (!isAlive) {
                if (value >= 1f && state) {
                    return;
                }

                if (value <= 0f && !state) {
                    return;
                }

                Trigger();
            }

            sequence.progressTotal = sequence.root.tween.Data.IsSequenceInverted ? 1f - value : value;

            if (isAlive) {
                if (value >= 1f) {
                    SetState(isReversible);

                    if (autoStop) {
                        Stop();
                    }
                } else if (value <= 0f) {
                    SetState(false);

                    if (autoStop) {
                        Stop();
                    }
                }
            }
        }

        /// <summary>Use this method to wait for the animation in coroutines.</summary>
        public IEnumerator ToYieldInstruction() => sequence.ToYieldInstruction();

        #pragma warning disable CS0618 // Type or member is obsolete
        public Tween.TweenAwaiter GetAwaiter() => sequence.GetAwaiter();
        #pragma warning restore CS0618

        /// <summary>Toggles animation state. Equivalent to calling 'animation.state = !animation.state'.</summary>
        public void ToggleState() => state = !state;

        /// <summary>Triggers the animation. The resulting animation state depends on the animation type (simple, reversible, or infinite). If the animation is already playing, applies <see cref="interruptionMode"/>.
        /// Simple and infinite animations: plays the animation from the beginning.<br/>
        /// Reversible animations: changes the direction.<br/></summary>
        public void Trigger() {
            if (!TryManipulate()) {
                return;
            }

            if (isAlive) {
                if (ignoreDuplicateTrigger) {
                    Print("ignoreDuplicateTrigger");
                } else {
                    if (IsSimple() && interruptionMode == InterruptionMode.Default) {
                        // Restore transform shake startValue
                        foreach (var data in animations) {
                            switch (data.tweenType) {
                                case TweenType.ShakeLocalPosition:
                                case TweenType.ShakeLocalRotation:
                                case TweenType.ShakeScale:
                                    foreach (var target in data.targets) {
                                        if (target is Transform shakeTransform) {
                                            if (PrimeTweenManager.Instance.shakes.TryGetValue(
                                                    (shakeTransform, data.tweenType),
                                                    out (ValueWrapper startValue, int count) shakeStartValueData
                                                )) {
                                                ValueWrapper startValue = shakeStartValueData.startValue;

                                                switch (data.tweenType) {
                                                    case TweenType.ShakeLocalPosition:
                                                        shakeTransform.localPosition = startValue.vector3;
                                                        break;
                                                    case TweenType.ShakeLocalRotation:
                                                        shakeTransform.localRotation = startValue.quaternion;
                                                        break;
                                                    case TweenType.ShakeScale:
                                                        shakeTransform.localScale = startValue.vector3;
                                                        break;
                                                }
                                            }
                                        }
                                    }

                                    break;
                            }
                        }

                        Stop();
                        CreateSequenceAndApplyDirection(true);
                        SetState(true);
                    } else {
                        Interrupt(!state);
                    }
                }
            } else {
                ToggleState();
            }
        }

        internal Sequence CreateSequenceAndBackupStartValues(bool restoreIniValues) {
#if UNITY_EDITOR
            if (Application.isPlaying && !m_IsStartValueBackedUp && animations != null) {
                m_IsStartValueBackedUp = true;
                var backups = PrimeTweenManager.sInstance.startValuesBackup;

                foreach (Data data in animations) {
                    if (data.targets != null) {
                        foreach (var target in data.targets) {
                            if (target is Material material && UnityEditor.EditorUtility.IsPersistent(material)) {
                                Assert.IsTrue(Utils.IsMaterialAnimation(data.tweenType));
                                TryAddDataToBackup();

                                void TryAddDataToBackup() {
                                    foreach (var backup in backups) {
                                        if (backup.target == material) {
                                            return;
                                        }
                                    }

                                    // Debug.Log($"save {data.tweenType} {target.name}", target);
                                    backups.Add(
                                        new PrimeTweenManager.StartValueBackupData {
                                            target = material, startValue = data.startValue, tweenType = data.tweenType,
                                            propertyName = data.stringParam
                                        }
                                    );
                                }
                            }
                        }
                    }
                }
            }

            if (!Application.isPlaying && animations != null && m_IniValues == null) {
                m_IniValues = new ValueWrapper?[animations.Count];

                for (int i = 0; i < animations.Count; i++) {
                    var data = animations[i];
                    var targets = data.targets;

                    if (targets != null) {
                        foreach (var target in targets) {
                            var iniVal = GetCurrentValue(data.operation, target, data.tweenType, data.stringParam);

                            // Log(LogType.Log, $"save ini val {target?.name} {iniVal}");
                            if (iniVal.HasValue) {
                                m_IniValues[i] = iniVal;
                                break;
                            }
                        }
                    }
                }
            }
#endif

            return CreateSequence(restoreIniValues);
        }

        internal void CreateSequenceAndApplyDirection(bool newState) {
            // print($"CreateSequenceAndApplyDirection {newState}");
            Assert.IsTrue(CanManipulate());
            Assert.IsFalse(isAlive);

#if UNITY_EDITOR
            sIsPreviewing = true;
            ResetHighlightData();
            PrimeTweenManager.Instance.TryAddCurrentTweenAnimation(this);
#endif

            sequence = CreateSequenceAndBackupStartValues(false);

            bool isBackward = isReversible && !newState;
            Print($"isBackward {isBackward}");

            if (isBackward) {
                var root = sequence.root.tween;
                ref var d = ref root.Data;

                (d.startValue, root.ManagedData.endValueOrDiff) =
                    (root.ManagedData.endValueOrDiff,
                     d.startValue); // sequence uses startValue and endValue as bounds for time calculation. Swapping them makes the sequence go in the opposite direction.

                d.IsSequenceInverted = true;
                TweenData.CacheDiff(ref d, ref root.ManagedData);

                SetSequenceToEnd(sequence.root.tween);

                void SetSequenceToEnd(ColdData seq) {
                    ref var seqData = ref seq.Data;
                    Assert.IsTrue(seqData.IsAlive);

                    foreach (var child in new Sequence(seq).GetSelfChildren()) {
                        ref var childData = ref child.Data;

                        int tweenCyclesTotal = childData.cyclesTotal;
                        Assert.AreNotEqual(-1, tweenCyclesTotal); // nested animations can't be infinite

                        childData.cyclesDone =
                            tweenCyclesTotal; // setting the cyclesDone to tweenCyclesTotal prevents callback from firing because isDone requires 'cyclesDiff > 0'

                        if (childData.IsSequenceRoot()) {
                            SetSequenceToEnd(child);
                        } else {
                            childData.SetFlag(Flags.StateAfter, false);

                            bool isValueChanged = child.ManagedData.UpdateAndSetElapsedTimeTotal(
                                float.MaxValue,
                                out _,
                                false,
                                ref childData
                            );

                            Assert.IsTrue(isValueChanged);

                            if (!seqData.IsAlive) {
                                return;
                            }

                            Assert.AreNotEqual(0, (int)(childData.flags & Flags.StateAfter));
                        }
                    }
                }
            } else {
                TweenData.ResetSequence(sequence.root.tween);

                if (resetOnCompletion && CanResetOnComplete()) {
                    sequence.root.ResetOnCompletion();
                }
            }

            if (resetOnCompletion && CanResetOnComplete()) {
                sequence.OnComplete(this, x => x.SetState(false));
            }
        }

        internal bool CanManipulate() {
            if (sequence.isAlive && !sequence.root.tween.Data.CanManipulate()) {
                return false;
            }

            return true;
        }

        private bool TryManipulate() {
            if (!CanManipulate()) {
                Log(LogType.Error, Constants.kCantManipulateNested);
                return false;
            }

            return true;
        }

        internal static bool SanitiseCycles(bool isReversible, ref int cycles) {
            if (isReversible) {
                if (cycles < 1) {
                    if (!Application.isPlaying) {
                        Debug.LogWarning("Reversible animations can't be infinite.");
                    }

                    cycles = 1;
                    return true;
                }

                if (cycles % 2 == 0) {
                    cycles--;
                    return true;
                }
            } else if (cycles < -1) {
                cycles = -1;
                return true;
            }

            return false;
        }

        internal static bool SanitiseCycleMode(bool isReversible, ref Sequence.SequenceCycleMode cycleMode) {
            if (isReversible && cycleMode == Sequence.SequenceCycleMode.Restart) {
                cycleMode = Sequence.SequenceCycleMode.YoyoChildren;
                return true;
            }

            return false;
        }

        internal static bool SanitiseInterruptionMode(bool isReversible, int cycles, ref InterruptionMode mode) {
            if (cycles == -1) {
                if (mode == InterruptionMode.Complete) {
                    mode = InterruptionMode.Reset;
                    return true;
                }
            } else if (isReversible) {
                if (mode == InterruptionMode.Reset) {
                    mode = InterruptionMode.Complete;
                    return true;
                }
            }

            return false;
        }

        private Sequence CreateSequence(bool restoreIniValues) {
            var anims = animations;

            if (anims == null) {
                Log(LogType.Error, $"'{nameof(anims)}' is null.");
                return default;
            }
#if UNITY_EDITOR
            if (restoreIniValues) {
                if (m_IniValues != null) {
                    if (m_IniValues.Length == anims.Count) {
                        anims = anims.ToList();

                        for (int i = 0; i < m_IniValues.Length; i++) {
                            var iniVal = m_IniValues[i];

                            if (iniVal.HasValue) {
                                Data data = anims[i];
                                data.startValue = iniVal.Value;
                                anims[i] = data;

                                // Log(LogType.Log, $"restore ini val {iniVal.Value}");
                            }
                        }

                        m_IniValues = null;
                    } else {
                        Log(LogType.Error, $"Invalid number of _iniValues: {m_IniValues.Length} != {anims.Count}");
                    }
                } else {
                    // Log(LogType.Log, "_iniValues is null"); // can be null if scrubbed to the beginning manually, then deselected
                    return default;
                }
            }
#endif

            SanitiseCycles(isReversible, ref cycles);
            SanitiseCycleMode(isReversible, ref cycleMode);
            var seq = Sequence.Create(cycles, cycleMode, sequenceEase, useUnscaledTime, updateType);
            seq.timeScale = Mathf.Max(kMinTimeScale, _timeScale);
            seq.isPaused = m_IsPaused;

            for (int i = 0; i < anims.Count; i++) {
                Data data = anims[i];

                if (data.operation == Operation.Disabled || data.tweenType == TweenType.Disabled) {
                    continue;
                }

                void AddSequence(Sequence otherSequence) {
#if UNITY_EDITOR
                    otherSequence.root.tween.indexInTweenAnimation = i;
#endif
                    switch (data.operation) {
                        case Operation.Insert:
                            seq.Insert(data.startTime, otherSequence);
                            break;
                        case Operation.Chain:
                            seq.Chain(otherSequence);
                            break;
                        case Operation.Group:
                            seq.Group(otherSequence);
                            break;
                        default:
                            LogError($"Invalid {nameof(Operation)}: {data.operation}.");
                            break;
                    }
                }

                if (data.tweenType == TweenType.TweenAnimationComponent) {
                    string error = string.Empty;

                    if (!data.GetUnityTarget(out TweenAnimationComponent target, ref error, 0)) {
                        LogError(error);
                        continue;
                    }

                    var otherAnimation = target.animation;

                    if (otherAnimation == null) {
                        LogError("Target's TweenAnimationComponent animation is null.");
                        continue;
                    }

                    if (target == context || otherAnimation == this) {
                        LogError(TweenAnimationComponent.kSelfReferenceError);
                        continue;
                    }

                    if (otherAnimation.IsInfinite()) {
                        LogError(Constants.kInfiniteTweenInSequenceError);
                        continue;
                    }

                    if (otherAnimation.isAlive) {
                        otherAnimation
                            .Reset(); // we should reset the nested animation so that it doesn't overwrite the newly created parent animation
                    }

                    var nestedSequence = otherAnimation.CreateSequenceAndBackupStartValues(restoreIniValues);

                    if (!nestedSequence.isAlive) {
                        LogError(null);
                        continue;
                    }

                    AddSequence(nestedSequence);
                } else if (data.tweenType == TweenType.ShakeCamera) {
                    string error = string.Empty;

                    if (!data.GetUnityTarget(out Camera target, ref error, 0)) {
                        LogError(error);
                        continue;
                    }

                    AddSequence(
                        Tween.ShakeCamera(target, data.shakeCameraStrengthFactor, data.duration, data.shakeFrequency)
                    );
                } else {
                    int count = Math.Max(1, data.targets?.Count ?? 0);

                    for (int targetIndex = 0; targetIndex < count; targetIndex++) {
                        string error = string.Empty;
                        Tween tween = data.StartTween(ref error, this, targetIndex);

                        if (!tween.isAlive) {
                            LogError(error);
                            continue;
                        }
#if UNITY_EDITOR
                        tween.tween.indexInTweenAnimation = i;
#endif
                        if (targetIndex == 0) {
                            switch (data.operation) {
                                case Operation.Insert:
                                    seq.Insert(data.startTime, tween);
                                    break;
                                case Operation.Chain:
                                    seq.Chain(tween);
                                    break;
                                case Operation.Group:
                                    seq.Group(tween);
                                    break;
                                default:
                                    LogError($"Invalid {nameof(Operation)}: {data.operation}.");
                                    continue;
                            }
                        } else {
                            // Tweens on the first target should adhere to the actual operation, while all later tweens should be grouped to the first one
                            seq.Group(tween);
                        }
                    }
                }

                void LogError(string msg) {
                    if (!string.IsNullOrEmpty(msg)) {
                        Log(LogType.Error, $"{nameof(TweenAnimation)} is not set up correctly at index {i}. {msg}");
                    }
                }
            }

            return seq;
        }

        private void Log(LogType type, string msg) {
            object log = $"{(context != null ? context.name : string.Empty)} {msg}";
            Debug.unityLogger.Log(type, log, context);
        }

        internal bool CanResetOnComplete() =>
            cycles >= 1 && (cycleMode == Sequence.SequenceCycleMode.Restart || cycles % 2 != 0) && !isReversible;

        internal bool IsInfinite() => cycles == -1;
        private bool IsSimple() => !IsInfinite() && !isReversible;

        /// <summary>Defines how an animation is added to a sequence.</summary>
        /// <seealso cref="Sequence.Chain(Tween)"/> <seealso cref="Sequence.Group(Tween)"/> <seealso cref="Sequence.Insert(float, Tween)"/>
        public enum Operation : sbyte {
            Disabled = -1,
            Chain,
            Group,
            Insert
        }

        /// <summary>Controls how <see cref="TweenAnimation"/> behaves when interrupted.</summary>
        /// <seealso cref="state"/> <seealso cref="TweenAnimation.interruptionMode"/>
        public enum InterruptionMode {
            [Tooltip(
                "Simple animations: restart the animation from the beginning.\n\n"
                + "Reversible animations: seamlessly change the direction.\n\n"
                + "Infinite animations: continue playing the current loop until its end. Use this mode to prevent abrupt changes and smoothly stop infinite animations."
            )]
            Default,

            [Tooltip("Completes the animation.")]
            Complete,

            [Tooltip("Instantly resets the animation to the beginning.")]
            Reset
        }

        internal void PlayIfNotAlive() {
            if (!isAlive) {
                Trigger();
            }
        }

        /// <summary>Instantly resets the animation to the beginning.</summary>
        public void Reset() {
            if (!TryManipulate()) {
                return;
            }

            // print("Reset");
            if (Application.isPlaying) {
                if (!isAlive) {
                    if (animations == null) {
                        Log(LogType.Error, $"'{nameof(animations)}' is null.");
                        return;
                    }

                    sequence = CreateSequence(false);
                }

                TweenData.ResetSequence(sequence.root.tween);
                sequence.Stop();
            }
#if UNITY_EDITOR
            else {
                if (isAlive) {
                    sequence.Stop();
                }

                if (animations == null) {
                    Log(LogType.Error, $"'{nameof(animations)}' is null.");
                    return;
                }

                sequence = CreateSequence(true);

                if (isAlive) {
                    TweenData.ResetSequence(sequence.root.tween);
                    sequence.Stop();
                }
            }
#endif

            SetState(false);

#if UNITY_EDITOR
            sIsPreviewing = false;
            ResetHighlightData();

            if (PrimeTweenManager.EnteredEditMode) {
                isPaused = false;
                ResetComponentsInEditor(0);
            }
#endif
        }

        private void ResetComponentsInEditor(int depth) {
            if (depth > 32) {
                Debug.LogError(TweenAnimationComponent.kSelfReferenceError);
                return;
            }

            if (animations != null) {
                foreach (var data in animations) {
                    if (data.targets != null) {
                        foreach (var target in data.targets) {
                            if (target is Renderer renderer && renderer) {
                                // Debug.Log($"reset PropertyBlock {renderer.name}", renderer);
                                renderer.SetPropertyBlock(null);
                            }
#if TEXT_MESH_PRO_INSTALLED
                            else if (target is TMP_Text text && text) {
                                // Debug.Log($"reset maxVisibleCharacters {text.name}", text);
                                text.maxVisibleCharacters = 99999;
                            }
#endif
                            else if ((target as TweenAnimationComponent)?.animation is TweenAnimation nested) {
                                if (this == nested) {
                                    Debug.LogError(TweenAnimationComponent.kSelfReferenceError, target);
                                } else {
                                    nested.ResetComponentsInEditor(depth + 1);
                                }
                            }
                        }
                    }
                }
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize() {
            if (animations != null) {
                for (var i = 0; i < animations.Count; i++) {
                    Data data = animations[i];

                    if (data.customData is Data.Custom customData) {
                        var tweenType = data.tweenType;

                        if (tweenType != TweenType.CustomFloat) {
                            customData.unityEventFloat = null;
                        }

                        if (tweenType != TweenType.CustomColor) {
                            customData.unityEventColor = null;
                        }

                        if (tweenType != TweenType.CustomVector2) {
                            customData.unityEventVector2 = null;
                        }

                        if (tweenType != TweenType.CustomVector3 && tweenType != TweenType.ShakeCustom) {
                            customData.unityEventVector3 = null;
                        }

                        if (tweenType != TweenType.CustomVector4) {
                            customData.unityEventVector4 = null;
                        }

                        if (tweenType != TweenType.CustomRect) {
                            customData.unityEventRect = null;
                        }

                        if (tweenType != TweenType.Callback) {
                            customData.callback = null;
                        }

                        data.customData = customData;
                        animations[i] = data;
                    }
                }
            }
        }

        internal static ValueWrapper? GetCurrentValue(
            Operation operation,
            [CanBeNull] UnityEngine.Object target,
            TweenType tweenType,
            string stringParam
        ) {
            if (operation == Operation.Disabled) {
                return null;
            }

            if (Data.IsCustomTweenType(tweenType)
                || tweenType == TweenType.Disabled
                || Utils.IsShake(tweenType)
                || tweenType == TweenType.TweenAnimationComponent
               ) {
                return null;
            }

            Type targetType;

            try {
                targetType = Utils.TweenTypeToTweenData(tweenType).Item2;
            } catch {
                return null;
            }

            if (tweenType != TweenType.GlobalTimeScale) {
                if (target == null || targetType == null || !targetType.IsAssignableFrom(target.GetType())) {
                    return null;
                }
            }

            TweenType? remapped = RemapToQuaternionType(tweenType);

            TweenType? RemapToQuaternionType(TweenType t) {
                switch (t) {
                    case TweenType.Rotation:
                        return TweenType.RotationQuaternion;
                    case TweenType.LocalRotation:
                        return TweenType.LocalRotationQuaternion;
                    case TweenType.RigidbodyMoveRotation:
                        return TweenType.RigidbodyMoveRotationQuaternion;
                    default:
                        return null;
                }
            }

            if (remapped.HasValue) {
                return Utils.GetAnimatedValue(target, remapped.Value, 0).quaternion.eulerAngles.ToContainer();
            }

            if (tweenType == TweenType.ScaleUniform) {
                return Utils.GetAnimatedValue(target, TweenType.ScaleX, 0);
            }

            int propId = Shader.PropertyToID(stringParam);

            if (((targetType == typeof(Material) && target is Material)
                 || (targetType == typeof(Renderer) && target is Renderer))
                && Utils.IsMaterialPropertyAnimation(tweenType)
                && !Utils.IsValidMaterialProperty(tweenType, target, propId)
               ) {
                return null;
            }

            return Utils.GetAnimatedValue(target, tweenType, propId);
        }

        /// <summary>Internal animation data. Typically, it should not be modified directly. Use only for advanced use cases.</summary>
        [Serializable, EditorBrowsable(EditorBrowsableState.Advanced)]
        public struct Data {
            [SerializeField]
            public Operation operation;

            [SerializeField]
            public float startTime;

            [SerializeField]
            public TweenType tweenType;

            [SerializeField]
            public List<UnityEngine.Object> targets;

            [SerializeField]
            public string stringParam; // stores the name of the Material property to animate

            [SerializeField]
            internal Custom[] _customData;

            [SerializeField]
            internal bool boolParam; // hasStartValue / callbackWarnIfTargetDestroyed / shakeEnableFalloff

            [SerializeField,
             Tooltip(
                 "Start value of an animation. Enable to set the start value manually.\n\n"
                 + "Disable to use the current value (only works in Edit Mode)."
             )]
            public ValueWrapper startValue; // Or 'xyz': shakeCustomStartValue | 'w': shakeFrequency

            [SerializeField, Tooltip("End value of an animation")]
            public ValueWrapper
                endValue; // Or 'x': shakeCameraStrengthFactor / 'xyz': shakeStrength | 'w': shakeAsymmetry

            [SerializeField]
            public float duration;

            [SerializeField, Tooltip(Constants.kEaseTooltip)]
            public Ease ease; // Or ShakeSettings.falloffEase

            [SerializeField]
            public AnimationCurve customEase;

            [SerializeField, Tooltip(Constants.kCyclesTooltip)]
            public int cycles; // p1 todo allow having multiple cycles for TweenAnimationComponent?

            [SerializeField, Tooltip(Constants.kCycleModeTooltip)]
            public CycleMode cycleMode;

            [Serializable]
            public struct Custom {
                [SerializeField]
                public UnityEvent callback;

                [SerializeField]
                public UnityEventFloat unityEventFloat;

                [SerializeField]
                public UnityEventColor unityEventColor;

                [SerializeField]
                public UnityEventVector2 unityEventVector2;

                [SerializeField]
                public UnityEventVector3 unityEventVector3;

                [SerializeField]
                public UnityEventVector4 unityEventVector4;

                [SerializeField]
                public UnityEventRect unityEventRect;

                [Serializable]
                public class UnityEventFloat : UnityEvent<float> { }

                [Serializable]
                public class UnityEventColor : UnityEvent<Color> { }

                [Serializable]
                public class UnityEventVector2 : UnityEvent<Vector2> { }

                [Serializable]
                public class UnityEventVector3 : UnityEvent<Vector3> { }

                [Serializable]
                public class UnityEventVector4 : UnityEvent<Vector4> { }

                [Serializable]
                public class UnityEventRect : UnityEvent<Rect> { }
            }

            public Custom? customData {
                get => _customData != null && _customData.Length == 1 ? _customData[0] : (Custom?)null;
                set {
                    if (value.HasValue) {
                        if (_customData != null && _customData.Length == 1) {
                            _customData[0] = value.Value;
                        } else {
                            _customData = new[] { value.Value };
                        }
                    } else {
                        _customData = null;
                    }
                }
            }

            public bool hasStartValue {
                get => boolParam;
                set => boolParam = value;
            }

            public bool callbackWarnIfTargetDestroyed {
                get => boolParam;
                set => boolParam = value;
            }

            public bool shakeEnableFalloff {
                get => boolParam;
                set => boolParam = value;
            }

            public Vector3 shakeCustomStartValue {
                get => startValue.vector3;
                set => startValue.vector3 = value;
            }

            public float shakeFrequency {
                get => startValue.w;
                set => startValue.w = value;
            }

            public float shakeCameraStrengthFactor {
                get => endValue.x;
                set => endValue.x = value;
            }

            public Vector3 shakeStrength {
                get => endValue.vector3;
                set => endValue.vector3 = value;
            }

            public float shakeAsymmetry {
                get => endValue.w;
                set => endValue.w = value;
            }

            private TweenSettings<int> SettingsInt =>
                CreateSettings(Mathf.RoundToInt(startValue.single), Mathf.RoundToInt(endValue.single));

            private TweenSettings<float> SettingsFloat => CreateSettings(startValue.single, endValue.single);
            private TweenSettings<Color> SettingsColor => CreateSettings(startValue.color, endValue.color);
            private TweenSettings<Vector2> SettingsVector2 => CreateSettings(startValue.vector2, endValue.vector2);
            private TweenSettings<Vector3> SettingsVector3 => CreateSettings(startValue.vector3, endValue.vector3);
            private TweenSettings<Vector4> SettingsVector4 => CreateSettings(startValue.vector4, endValue.vector4);
            private TweenSettings<Quaternion> SettingsQuaternion => CreateSettings(startValue.quaternion, endValue.quaternion);
            private TweenSettings<Rect> SettingsRect => CreateSettings(startValue.rect, endValue.rect);

            private TweenSettings<T> CreateSettings<T>(T start, T end) where T : struct {
                if (cycles < 0) {
                    Debug.LogError(Constants.kInfiniteTweenInSequenceError);
                    cycles = 1;
                }

                TweenSettings settings = new TweenSettings(
                    duration,
                    ease,
                    ease == Ease.Custom ? customEase : (Easing?)null,
                    cycles,
                    cycleMode
                );

                return new TweenSettings<T>(start, end, settings);
            }

            internal static bool IsCustomTweenType(TweenType type) {
                switch (type) {
                    case TweenType.CustomFloat:
                    case TweenType.CustomColor:
                    case TweenType.CustomVector2:
                    case TweenType.CustomVector3:
                    case TweenType.CustomVector4:
                    case TweenType.CustomQuaternion:
                    case TweenType.CustomRect:
                        return true;
                    default:
                        return false;
                }
            }

            internal bool GetUnityTarget<T>(out T tweenTarget, ref string error, int targetIndex)
                where T : UnityEngine.Object {
                var target = targets[targetIndex];

                if (target == null) {
                    tweenTarget = null;

                    if (Application.isPlaying && !PrimeTweenManager.HasPrefabStage()) {
                        error = $"'target' ({typeof(T)}) is null or destroyed.";
                    }

                    return false;
                }

                tweenTarget = target as T;

                if (tweenTarget == null) {
                    error = $"'target' type mismatch: expected ({typeof(T)}), but was {target.GetType()}.";
                    return false;
                }

                return true;
            }

            internal bool GetCustomData(out Custom result, ref string error) {
                Custom? data = customData;

                if (!data.HasValue) {
                    error = $"'{nameof(customData)}' is null or empty.";
                    result = default;
                    return false;
                }

                result = data.Value;
                return true;
            }

            private bool CheckEvent(UnityEventBase evt, ref string error, TweenAnimation context) {
                int eventCount = evt.GetPersistentEventCount();

                if (eventCount != 0) {
                    // return true if there are no persistent events as there can be dynamic C# events added via AddListener()
                    for (int i = 0; i < eventCount; i++) {
                        var persistentTarget = evt.GetPersistentTarget(i);

                        if (persistentTarget == null) {
                            error = $"UnityEvent target is null or destroyed at index {i}.";
                            return false;
                        }

                        if (persistentTarget is TweenAnimationComponent animationComponent
                            && animationComponent.animation == context) {
                            error = TweenAnimationComponent.kSelfReferenceError;
                            return false;
                        }

                        if (string.IsNullOrEmpty(evt.GetPersistentMethodName(i))) {
                            error = $"UnityEvent method name is invalid at index {i}.";
                            return false;
                        }
                    }
                }

                return true;
            }

            private bool GetPropName([CanBeNull] out string propName, ref string err) {
                if (string.IsNullOrEmpty(stringParam)) {
                    err = "Material property name is empty.";
                    propName = null;
                    return false;
                }

                propName = stringParam;
                return true;
            }

            private bool GetPropId(out int propId, ref string err, int targetIndex) {
                if (!GetPropName(out string propName, ref err)) {
                    propId = -1;
                    return false;
                }

                propId = Shader.PropertyToID(propName);
                var target = targets[targetIndex];

                if (target is Material material && !Utils.IsValidMaterialProperty(tweenType, target, propId)) {
                    Debug.LogWarning(
                        $"Material doesn't have a property with id '{propId}', propName: '{propName}'.",
                        material
                    );
                }

                if (target is Renderer renderer && !Utils.IsValidMaterialProperty(tweenType, target, propId)) {
                    Debug.LogWarning(
                        $"Renderer's material doesn't have a property with id '{propId}', propName: '{propName}'.",
                        renderer
                    );
                }

                return true;
            }

            private ShakeSettings GetShakeSettings() {
                Ease? falloff = shakeEnableFalloff ? ease : (Ease?)null;
                const AnimationCurve strengthOverTime = null;

                return new ShakeSettings(
                    shakeStrength,
                    duration,
                    shakeFrequency,
                    falloff,
                    strengthOverTime,
                    Ease.OutQuad,
                    shakeAsymmetry,
                    1,
                    0f,
                    0f,
                    false,
                    UpdateType.Default
                );
            }

            internal Tween StartTween(ref string err, TweenAnimation ctx, int i) {
                switch (tweenType) {
                    case TweenType.Disabled:
                    case TweenType.TweenAnimationComponent:
                    case TweenType.ShakeCamera:
                        // Handled by TweenAnimation
                        err = $"Invalid TweenType: {tweenType}";
                        return default;
                    case TweenType.MainSequence:
                    case TweenType.NestedSequence:
                    case TweenType.TweenTimeScale:
                    case TweenType.TweenTimeScaleSequence:
                    case TweenType.VisualElementLayout:
                    case TweenType.VisualElementPosition:
                    case TweenType.VisualElementRotationQuaternion:
                    case TweenType.VisualElementScale:
                    case TweenType.VisualElementSize:
                    case TweenType.VisualElementTopLeft:
                    case TweenType.VisualElementColor:
                    case TweenType.VisualElementBackgroundColor:
                    case TweenType.VisualElementOpacity:
#if PRIME_TWEEN_EXPERIMENTAL
                    case TweenType.CustomDouble:
#endif
                        err = $"Unsupported TweenType: {tweenType}.";
                        return default;

                    case TweenType.Delay:
                        return Tween.Delay(duration);

                    case TweenType.ShakeLocalPosition: {
                        return GetUnityTarget(out Transform t, ref err, i)
                            ? Tween.ShakeLocalPosition(t, GetShakeSettings()) : default;
                    }

                    case TweenType.ShakeLocalRotation: {
                        return GetUnityTarget(out Transform t, ref err, i)
                            ? Tween.ShakeLocalRotation(t, GetShakeSettings()) : default;
                    }

                    case TweenType.ShakeScale: {
                        return GetUnityTarget(out Transform t, ref err, i) ? Tween.ShakeScale(t, GetShakeSettings())
                            : default;
                    }

                    case TweenType.ShakeCustom: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventVector3 is Custom.UnityEventVector3 t
                               && CheckEvent(t, ref err, ctx) ? Tween.ShakeCustom(
                            t,
                            shakeCustomStartValue,
                            GetShakeSettings(),
                            (x, val) => x.Invoke(val)
                        ) : default;
                    }

                    case TweenType.CustomFloat: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventFloat is Custom.UnityEventFloat t
                               && CheckEvent(t, ref err, ctx) ? Tween.Custom(
                            t,
                            SettingsFloat,
                            (x, val) => x.Invoke(val)
                        ) : default;
                    }

                    case TweenType.CustomColor: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventColor is Custom.UnityEventColor t
                               && CheckEvent(t, ref err, ctx) ? Tween.Custom(
                            t,
                            SettingsColor,
                            (x, val) => x.Invoke(val)
                        ) : default;
                    }

                    case TweenType.CustomVector2: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventVector2 is Custom.UnityEventVector2 t
                               && CheckEvent(t, ref err, ctx) ? Tween.Custom(
                            t,
                            SettingsVector2,
                            (x, val) => x.Invoke(val)
                        ) : default;
                    }

                    case TweenType.CustomVector3: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventVector3 is Custom.UnityEventVector3 t
                               && CheckEvent(t, ref err, ctx) ? Tween.Custom(
                            t,
                            SettingsVector3,
                            (x, val) => x.Invoke(val)
                        ) : default;
                    }

                    case TweenType.CustomVector4: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventVector4 is Custom.UnityEventVector4 t
                               && CheckEvent(t, ref err, ctx) ? Tween.Custom(
                            t,
                            SettingsVector4,
                            (x, val) => x.Invoke(val)
                        ) : default;
                    }

                    case TweenType.CustomRect: {
                        return GetCustomData(out var custom, ref err)
                               && custom.unityEventRect is Custom.UnityEventRect t
                               && CheckEvent(t, ref err, ctx) ? Tween.Custom(t, SettingsRect, (x, val) => x.Invoke(val))
                            : default;
                    }

                    case TweenType.Callback: {
                        return GetCustomData(out var custom, ref err)
                               && custom.callback is UnityEvent t
                               && CheckEvent(t, ref err, ctx) ? Sequence.CreateCallback(
                                                                    t,
                                                                    x => x.Invoke(),
                                                                    callbackWarnIfTargetDestroyed
                                                                )
                                                                ?? default : default;
                    }

                    // Comment out this check because IsValidMaterialProperty is not 100% reliable. For example, when a material has a Vector with an unmodified value, the HasVector() will fail.
                    /*if (!Utils.IsValidMaterialProperty(tweenType, tweenTarget, propId)) {
                        err = $"Material doesn't have the '{stringParam}' property.";
                        return default;
                    }*/
                    case TweenType.MaterialColorProperty: {
                        return GetUnityTarget(out Material m, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialColor(m, propId, SettingsColor) : default;
                    }

                    case TweenType.MaterialProperty: {
                        return GetUnityTarget(out Material m, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialProperty(m, propId, SettingsFloat) : default;
                    }

                    case TweenType.MaterialAlphaProperty: {
                        return GetUnityTarget(out Material m, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialAlpha(m, propId, SettingsFloat) : default;
                    }

                    case TweenType.MaterialTextureOffset: {
                        return GetUnityTarget(out Material m, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialTextureOffset(m, propId, SettingsVector2) : default;
                    }

                    case TweenType.MaterialTextureScale: {
                        return GetUnityTarget(out Material m, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialTextureScale(m, propId, SettingsVector2) : default;
                    }

                    case TweenType.MaterialPropertyVector4: {
                        return GetUnityTarget(out Material m, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialProperty(m, propId, SettingsVector4) : default;
                    }

                    case TweenType.EulerAngles: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.EulerAngles(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.LocalEulerAngles: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalEulerAngles(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.GlobalTimeScale: {
                        if (!ctx.useUnscaledTime) {
                            Debug.LogError(
                                $"Please enable {nameof(TweenAnimation)}.{nameof(ctx.useUnscaledTime)} to animate Time.timeScale correctly."
                            );
                        }

                        var settings = SettingsFloat;
                        settings.settings.useUnscaledTime = true;
                        return Tween.GlobalTimeScale(settings);
                    }

                    case TweenType.MaterialPropertyBlockColorProperty: {
                        return GetUnityTarget(out Renderer r, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialPropertyBlockColor(r, propId, SettingsColor) : default;
                    }

                    case TweenType.MaterialPropertyBlockAlphaProperty: {
                        return GetUnityTarget(out Renderer r, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialPropertyBlockAlpha(r, propId, SettingsFloat) : default;
                    }

                    case TweenType.MaterialPropertyBlockProperty: {
                        return GetUnityTarget(out Renderer r, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialPropertyBlockProperty(r, propId, SettingsFloat) : default;
                    }

                    case TweenType.MaterialPropertyBlockPropertyVector4: {
                        return GetUnityTarget(out Renderer r, ref err, i) && GetPropId(out int propId, ref err, i)
                            ? Tween.MaterialPropertyBlockProperty(r, propId, SettingsVector4) : default;
                    }

                    case TweenType.MaterialPropertyBlockTextureScale: {
                        return GetUnityTarget(out Renderer r, ref err, i) && GetPropName(out string propName, ref err)
                            ? Tween.MaterialPropertyBlockTextureScale(r, propName, SettingsVector2) : default;
                    }

                    case TweenType.MaterialPropertyBlockTextureOffset: {
                        return GetUnityTarget(out Renderer r, ref err, i) && GetPropName(out string propName, ref err)
                            ? Tween.MaterialPropertyBlockTextureOffset(r, propName, SettingsVector2) : default;
                    }

#if TEXT_MESH_PRO_INSTALLED
                    case TweenType.TextMaxVisibleCharactersNormalized: {
                        return GetUnityTarget<TMP_Text>(out var tweenTarget, ref err, i)
                            ? Tween.TextMaxVisibleCharactersNormalized(tweenTarget, SettingsFloat) : default;
                    }
#endif

                    // CODE GENERATOR BEGIN
                    case TweenType.LightRange: {
                        return GetUnityTarget<Light>(out var tweenTarget, ref err, i)
                            ? Tween.LightRange(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.LightShadowStrength: {
                        return GetUnityTarget<Light>(out var tweenTarget, ref err, i)
                            ? Tween.LightShadowStrength(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.LightIntensity: {
                        return GetUnityTarget<Light>(out var tweenTarget, ref err, i)
                            ? Tween.LightIntensity(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.LightColor: {
                        return GetUnityTarget<Light>(out var tweenTarget, ref err, i)
                            ? Tween.LightColor(tweenTarget, SettingsColor) : default;
                    }

                    case TweenType.CameraOrthographicSize: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraOrthographicSize(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.CameraBackgroundColor: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraBackgroundColor(tweenTarget, SettingsColor) : default;
                    }

                    case TweenType.CameraAspect: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraAspect(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.CameraFarClipPlane: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraFarClipPlane(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.CameraFieldOfView: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraFieldOfView(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.CameraNearClipPlane: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraNearClipPlane(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.CameraPixelRect: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraPixelRect(tweenTarget, SettingsRect) : default;
                    }

                    case TweenType.CameraRect: {
                        return GetUnityTarget<Camera>(out var tweenTarget, ref err, i)
                            ? Tween.CameraRect(tweenTarget, SettingsRect) : default;
                    }

                    case TweenType.LocalRotation: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalRotation(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.ScaleUniform: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.Scale(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.Rotation: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.Rotation(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.Position: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.Position(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.PositionX: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.PositionX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.PositionY: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.PositionY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.PositionZ: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.PositionZ(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.LocalPosition: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalPosition(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.LocalPositionX: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalPositionX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.LocalPositionY: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalPositionY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.LocalPositionZ: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalPositionZ(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.RotationQuaternion: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.Rotation(tweenTarget, SettingsQuaternion) : default;
                    }

                    case TweenType.LocalRotationQuaternion: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.LocalRotation(tweenTarget, SettingsQuaternion) : default;
                    }

                    case TweenType.Scale: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.Scale(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.ScaleX: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.ScaleX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.ScaleY: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.ScaleY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.ScaleZ: {
                        return GetUnityTarget<Transform>(out var tweenTarget, ref err, i)
                            ? Tween.ScaleZ(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.ColorSpriteRenderer: {
                        return GetUnityTarget<SpriteRenderer>(out var tweenTarget, ref err, i)
                            ? Tween.Color(tweenTarget, SettingsColor) : default;
                    }

                    case TweenType.AlphaSpriteRenderer: {
                        return GetUnityTarget<SpriteRenderer>(out var tweenTarget, ref err, i)
                            ? Tween.Alpha(tweenTarget, SettingsFloat) : default;
                    }
#if UNITY_UGUI_INSTALLED
                    case TweenType.UISliderValue: {
                        return GetUnityTarget<Slider>(out var tweenTarget, ref err, i)
                            ? Tween.UISliderValue(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UINormalizedPosition: {
                        return GetUnityTarget<ScrollRect>(out var tweenTarget, ref err, i)
                            ? Tween.UINormalizedPosition(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIHorizontalNormalizedPosition: {
                        return GetUnityTarget<ScrollRect>(out var tweenTarget, ref err, i)
                            ? Tween.UIHorizontalNormalizedPosition(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIVerticalNormalizedPosition: {
                        return GetUnityTarget<ScrollRect>(out var tweenTarget, ref err, i)
                            ? Tween.UIVerticalNormalizedPosition(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIPivotX: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIPivotX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIPivotY: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIPivotY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIPivot: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIPivot(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIAnchorMax: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchorMax(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIAnchorMin: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchorMin(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIAnchoredPosition3D: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPosition3D(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.UIAnchoredPosition3DX: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPosition3DX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIAnchoredPosition3DY: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPosition3DY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIAnchoredPosition3DZ: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPosition3DZ(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIEffectDistance: {
                        return GetUnityTarget<Shadow>(out var tweenTarget, ref err, i)
                            ? Tween.UIEffectDistance(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIAlphaShadow: {
                        return GetUnityTarget<Shadow>(out var tweenTarget, ref err, i)
                            ? Tween.Alpha(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIColorShadow: {
                        return GetUnityTarget<Shadow>(out var tweenTarget, ref err, i)
                            ? Tween.Color(tweenTarget, SettingsColor) : default;
                    }

                    case TweenType.UIPreferredSize: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIPreferredSize(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIPreferredWidth: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIPreferredWidth(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIPreferredHeight: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIPreferredHeight(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIFlexibleSize: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIFlexibleSize(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIFlexibleWidth: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIFlexibleWidth(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIFlexibleHeight: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIFlexibleHeight(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIMinSize: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIMinSize(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIMinWidth: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIMinWidth(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIMinHeight: {
                        return GetUnityTarget<LayoutElement>(out var tweenTarget, ref err, i)
                            ? Tween.UIMinHeight(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIColorGraphic: {
                        return GetUnityTarget<Graphic>(out var tweenTarget, ref err, i)
                            ? Tween.Color(tweenTarget, SettingsColor) : default;
                    }

                    case TweenType.UIAnchoredPosition: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPosition(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIAnchoredPositionX: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPositionX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIAnchoredPositionY: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIAnchoredPositionY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UISizeDelta: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UISizeDelta(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIAlphaCanvasGroup: {
                        return GetUnityTarget<CanvasGroup>(out var tweenTarget, ref err, i)
                            ? Tween.Alpha(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIAlphaGraphic: {
                        return GetUnityTarget<Graphic>(out var tweenTarget, ref err, i)
                            ? Tween.Alpha(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIFillAmount: {
                        return GetUnityTarget<Image>(out var tweenTarget, ref err, i)
                            ? Tween.UIFillAmount(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIOffsetMin: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIOffsetMin(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIOffsetMinX: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIOffsetMinX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIOffsetMinY: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIOffsetMinY(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIOffsetMax: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIOffsetMax(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.UIOffsetMaxX: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIOffsetMaxX(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.UIOffsetMaxY: {
                        return GetUnityTarget<RectTransform>(out var tweenTarget, ref err, i)
                            ? Tween.UIOffsetMaxY(tweenTarget, SettingsFloat) : default;
                    }
#endif
#if PHYSICS_MODULE_INSTALLED
                    case TweenType.RigidbodyMoveRotation: {
                        return GetUnityTarget<Rigidbody>(out var tweenTarget, ref err, i)
                            ? Tween.RigidbodyMoveRotation(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.RigidbodyMovePosition: {
                        return GetUnityTarget<Rigidbody>(out var tweenTarget, ref err, i)
                            ? Tween.RigidbodyMovePosition(tweenTarget, SettingsVector3) : default;
                    }

                    case TweenType.RigidbodyMoveRotationQuaternion: {
                        return GetUnityTarget<Rigidbody>(out var tweenTarget, ref err, i)
                            ? Tween.RigidbodyMoveRotation(tweenTarget, SettingsQuaternion) : default;
                    }
#endif
#if PHYSICS2D_MODULE_INSTALLED
                    case TweenType.RigidbodyMovePosition2D: {
                        return GetUnityTarget<Rigidbody2D>(out var tweenTarget, ref err, i)
                            ? Tween.RigidbodyMovePosition(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.RigidbodyMoveRotation2D: {
                        return GetUnityTarget<Rigidbody2D>(out var tweenTarget, ref err, i)
                            ? Tween.RigidbodyMoveRotation(tweenTarget, SettingsFloat) : default;
                    }
#endif
                    case TweenType.MaterialColor: {
                        return GetUnityTarget<Material>(out var tweenTarget, ref err, i)
                            ? Tween.MaterialColor(tweenTarget, SettingsColor) : default;
                    }

                    case TweenType.MaterialAlpha: {
                        return GetUnityTarget<Material>(out var tweenTarget, ref err, i)
                            ? Tween.MaterialAlpha(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.MaterialMainTextureOffset: {
                        return GetUnityTarget<Material>(out var tweenTarget, ref err, i)
                            ? Tween.MaterialMainTextureOffset(tweenTarget, SettingsVector2) : default;
                    }

                    case TweenType.MaterialMainTextureScale: {
                        return GetUnityTarget<Material>(out var tweenTarget, ref err, i)
                            ? Tween.MaterialMainTextureScale(tweenTarget, SettingsVector2) : default;
                    }
#if AUDIO_MODULE_INSTALLED
                    case TweenType.AudioVolume: {
                        return GetUnityTarget<AudioSource>(out var tweenTarget, ref err, i)
                            ? Tween.AudioVolume(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.AudioPitch: {
                        return GetUnityTarget<AudioSource>(out var tweenTarget, ref err, i)
                            ? Tween.AudioPitch(tweenTarget, SettingsFloat) : default;
                    }

                    case TweenType.AudioPanStereo: {
                        return GetUnityTarget<AudioSource>(out var tweenTarget, ref err, i)
                            ? Tween.AudioPanStereo(tweenTarget, SettingsFloat) : default;
                    }
#endif

#if TEXT_MESH_PRO_INSTALLED
                    case TweenType.TextMaxVisibleCharacters: {
                        return GetUnityTarget<TMP_Text>(out var tweenTarget, ref err, i)
                            ? Tween.TextMaxVisibleCharacters(tweenTarget, SettingsInt) : default;
                    }

                    case TweenType.TextFontSize: {
                        return GetUnityTarget<TMP_Text>(out var tweenTarget, ref err, i)
                            ? Tween.TextFontSize(tweenTarget, SettingsFloat) : default;
                    }
#endif
                    default:
                        throw new Exception(
                            $"Unsupported tween type: {tweenType}. Please install necessary packages (TextMeshPro, UGUI, etc.) or use a newer version of Unity."
                        );
                }
            }
        }
    }
}