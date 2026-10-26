using System;
using UnityEngine;
using UnityEngine.Events;

namespace ZStudio.UniKit.LuckyWheel {
    public enum SpinDirection {
        Counter,
        ClockWise
    }

    public enum WheelState {
        Idle,
        Spinning,
        Stopping,
        Starting,
        Settling
    }

    /// <summary>
    /// 等分转盘的表现组件。调用方决定结果；组件只负责旋转、对齐、指针反馈和完成通知。
    /// 所有角度均为 Reel 的本地 Z 角度，编号方向与旋转方向相互独立。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class WheelController : MonoBehaviour {
        [SerializeField]
        private Transform m_Reel;

        [SerializeField]
        private PointerController m_PointerController;

        [Min(1), SerializeField]
        private int m_SectorsCount = 6;

        [Tooltip("0 号扇区中心对准指针时，Reel 的本地 Z 角度。")]
        [SerializeField]
        private float m_ZeroAngle;

        [Tooltip("美术上的扇区编号方向，与转动方向无关。索引从 0 开始。")]
        [SerializeField]
        private SpinDirection m_SectorDirection = SpinDirection.ClockWise;

        [SerializeField]
        private SpinDirection m_SpinDirection = SpinDirection.Counter;

        [Tooltip("主旋转阶段的时长，不包含开始延迟、起转预摆和停转回摆。等待模式下从结果锁定且起转完成后计时。")]
        [Min(0.1f), SerializeField]
        private float m_MainRotationTime = 3f;

        [Tooltip("到目标位置之前至少再转多少个完整圈。等待模式可能为保证平滑减速增加圈数。")]
        [Min(0), SerializeField]
        private int m_MinimumTurns = 3;

        [Min(0), SerializeField]
        private float m_SpinStartDelay;

        [Tooltip("等待结果时的最高转速，单位：度/秒。")]
        [Min(1), SerializeField]
        private float m_WaitingSpeed = 720f;

        [Min(0), SerializeField]
        private float m_AccelerationTime = 0.4f;

        [Tooltip("扇区内的随机落点范围，以一格宽度为单位；0 为中心，最大 ±0.49，避免停在线上。")]
        [Range(0, 0.49f), SerializeField]
        private float m_LandingJitter;

        [SerializeField]
        private bool m_UseUnscaledTime;

        [Tooltip("主旋转前反向预摆的时长；0 表示跳过此阶段。")]
        [Min(0), SerializeField]
        private float m_InRotationTime = 0.2f;

        [Tooltip("沿主旋转反方向预摆的角度。")]
        [Min(0), SerializeField]
        private float m_InRotationAngle = 5f;

        [Tooltip("起转进度曲线，从 (0,0) 到 (1,1)，进度限制在 0～1。")]
        [SerializeField]
        private AnimationCurve m_InRotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("主旋转越过目标后回摆归位的时长；0 表示直接停到目标。")]
        [Min(0), SerializeField]
        private float m_OutRotationTime = 0.2f;

        [Tooltip("主旋转沿转动方向越过目标的角度；停转阶段反向回到目标。")]
        [Min(0), SerializeField]
        private float m_OutRotationAngle = 5f;

        [Tooltip("停转回摆进度曲线，从 (0,0) 到 (1,1)，进度限制在 0～1。")]
        [SerializeField]
        private AnimationCurve m_OutRotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [SerializeField]
        private AudioClip m_SpinSound;

        [SerializeField]
        private AudioClip m_HitTargetSound;

        [SerializeField]
        private UnityEvent m_StartedEvent = new();

        [SerializeField]
        private UnityEvent<int> m_ResultEvent = new();

        [SerializeField]
        private UnityEvent m_CompleteEvent = new();

        [SerializeField]
        private UnityEvent m_CancelledEvent = new();

        public WheelState State { get; private set; }
        public bool IsSpinning => State != WheelState.Idle;

        public bool CanSubmitResult => State is WheelState.Starting or WheelState.Spinning && TargetSectorIndex < 0;

        public int WinSectorIndex { get; private set; } = -1;
        public int TargetSectorIndex { get; private set; } = -1;
        public int SectorsCount => m_SectorsCount;
        public float ZeroAngle => m_ZeroAngle;
        public SpinDirection SectorDirection => m_SectorDirection;
        public Transform Reel => m_Reel;
        public bool UseUnscaledTime => m_UseUnscaledTime;
        public event Action<int> Completed;
        public event Action Cancelled;

        private AudioSource m_AudioSource;
        private Action m_OnComplete;
        private float m_DelayRemaining;
        private float m_Elapsed;
        private float m_Speed;
        private double m_Angle;
        private double m_StopStartAngle;
        private double m_StopDistance;
        private float m_StopStartSpeed;
        private float m_StopDuration;
        private float m_LandingAngle;
        private float m_ActiveDirection;
        private double m_StageStartAngle;
        private double m_MainEndAngle;

        private static float Direction(SpinDirection direction) => direction == SpinDirection.ClockWise ? -1f : 1f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void Awake() => m_AudioSource = GetComponent<AudioSource>();
        private void Reset() => m_Reel = transform;

        private void OnValidate() {
            m_SectorsCount = Mathf.Max(1, m_SectorsCount);
            m_MainRotationTime = IsFinite(m_MainRotationTime) ? Mathf.Max(0.1f, m_MainRotationTime) : 3f;
            m_MinimumTurns = Mathf.Clamp(m_MinimumTurns, 0, 100);
            m_SpinStartDelay = IsFinite(m_SpinStartDelay) ? Mathf.Max(0, m_SpinStartDelay) : 0;
            m_WaitingSpeed = IsFinite(m_WaitingSpeed) ? Mathf.Clamp(m_WaitingSpeed, 1, 100000) : 720f;
            m_AccelerationTime = IsFinite(m_AccelerationTime) ? Mathf.Max(0, m_AccelerationTime) : 0.4f;
            m_LandingJitter = IsFinite(m_LandingJitter) ? Mathf.Clamp(m_LandingJitter, 0, 0.49f) : 0;

            if (!IsFinite(m_ZeroAngle)) {
                m_ZeroAngle = 0;
            }

            m_InRotationTime = SanitizeNonNegative(m_InRotationTime);
            m_OutRotationTime = SanitizeNonNegative(m_OutRotationTime);
            m_InRotationAngle = SanitizeNonNegative(m_InRotationAngle);
            m_OutRotationAngle = SanitizeNonNegative(m_OutRotationAngle);

            if (!IsValidStageCurve(m_InRotationCurve)) {
                m_InRotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            }

            if (!IsValidStageCurve(m_OutRotationCurve)) {
                m_OutRotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            }
        }

        /// <summary>配置布局，不改变当前视觉角度。正在转动时不允许更换布局。</summary>
        public void Configure(Transform reel, int sectorsCount, float zeroAngle = 0,
            SpinDirection sectorDirection = SpinDirection.ClockWise) {
            RequireIdle();

            if (!reel) {
                throw new ArgumentNullException(nameof(reel));
            }

            ValidateLayout(sectorsCount, zeroAngle);
            m_Reel = reel;
            m_SectorsCount = sectorsCount;
            m_ZeroAngle = zeroAngle;
            m_SectorDirection = sectorDirection;
            WinSectorIndex = -1;
        }

        /// <summary>Inspector 已配置布局时无需调用；此方法供运行时更新扇区数量。</summary>
        public void Initialize(int sectorsCount) {
            RequireIdle();
            ValidateLayout(sectorsCount, m_ZeroAngle);
            m_SectorsCount = sectorsCount;
            WinSectorIndex = -1;
        }

        /// <summary>运行时配置动画。旋转期间先取消，再改变参数，避免本次轨迹被中途改写。</summary>
        public void ConfigureMotion(float duration, int minimumTurns, SpinDirection direction,
            float waitingSpeed = 720, float accelerationTime = 0.4f,
            float startDelay = 0, float landingJitter = 0, bool useUnscaledTime = false) {
            RequireIdle();

            if (!IsFinite(duration) || duration < 0.1f) {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            if (minimumTurns is < 0 or > 100) {
                throw new ArgumentOutOfRangeException(nameof(minimumTurns));
            }

            if (!IsFinite(waitingSpeed) || waitingSpeed < 1 || waitingSpeed > 100000) {
                throw new ArgumentOutOfRangeException(nameof(waitingSpeed));
            }

            if (!IsFinite(accelerationTime) || accelerationTime < 0) {
                throw new ArgumentOutOfRangeException(nameof(accelerationTime));
            }

            if (!IsFinite(startDelay) || startDelay < 0) {
                throw new ArgumentOutOfRangeException(nameof(startDelay));
            }

            if (!IsFinite(landingJitter) || landingJitter < 0 || landingJitter > 0.49f) {
                throw new ArgumentOutOfRangeException(nameof(landingJitter));
            }

            m_MainRotationTime = duration;
            m_MinimumTurns = minimumTurns;
            m_SpinDirection = direction;
            m_WaitingSpeed = waitingSpeed;
            m_AccelerationTime = accelerationTime;
            m_SpinStartDelay = startDelay;
            m_LandingJitter = landingJitter;
            m_UseUnscaledTime = useUnscaledTime;
        }

        /// <summary>独立配置起转预摆和停转回摆。曲线进度为 0～1，时长为 0 时跳过对应阶段。</summary>
        public void ConfigureStages(float inDuration, float inAngle, float outDuration, float outAngle,
            AnimationCurve inCurve = null, AnimationCurve outCurve = null) {
            RequireIdle();

            if (!IsFinite(inDuration) || inDuration < 0) {
                throw new ArgumentOutOfRangeException(nameof(inDuration));
            }

            if (!IsFinite(inAngle) || inAngle < 0) {
                throw new ArgumentOutOfRangeException(nameof(inAngle));
            }

            if (!IsFinite(outDuration) || outDuration < 0) {
                throw new ArgumentOutOfRangeException(nameof(outDuration));
            }

            if (!IsFinite(outAngle) || outAngle < 0) {
                throw new ArgumentOutOfRangeException(nameof(outAngle));
            }

            if (inCurve != null && !IsValidStageCurve(inCurve)) {
                throw new ArgumentException("Curve must start at (0,0) and end at (1,1).", nameof(inCurve));
            }

            if (outCurve != null && !IsValidStageCurve(outCurve)) {
                throw new ArgumentException("Curve must start at (0,0) and end at (1,1).", nameof(outCurve));
            }

            m_InRotationTime = inDuration;
            m_InRotationAngle = inAngle;
            m_OutRotationTime = outDuration;
            m_OutRotationAngle = outAngle;
            m_InRotationCurve = inCurve == null ? AnimationCurve.EaseInOut(0, 0, 1, 1)
                : new AnimationCurve(inCurve.keys);
            m_OutRotationCurve = outCurve == null ? AnimationCurve.EaseInOut(0, 0, 1, 1)
                : new AnimationCurve(outCurve.keys);
        }

        private static float SanitizeNonNegative(float value) => IsFinite(value) ? Mathf.Max(0, value) : 0;

        private static bool IsValidStageCurve(AnimationCurve curve) {
            if (curve == null || curve.length < 2) {
                return false;
            }

            foreach (var key in curve.keys) {
                if (!IsFinite(key.time) || !IsFinite(key.value)) {
                    return false;
                }
            }

            return Mathf.Abs(curve[0].time) < 0.0001f && Mathf.Abs(curve[curve.length - 1].time - 1) < 0.0001f
                                                      && Mathf.Abs(curve.Evaluate(0)) < 0.0001f
                                                      && Mathf.Abs(curve.Evaluate(1) - 1) < 0.0001f;
        }

        private void RequireIdle() {
            if (IsSpinning) {
                throw new InvalidOperationException("Cancel the current spin before changing the layout.");
            }
        }

        private static void ValidateLayout(int count, float zero) {
            if (count < 1) {
                throw new ArgumentOutOfRangeException(nameof(count), "Sector count must be positive.");
            }

            if (!IsFinite(zero)) {
                throw new ArgumentOutOfRangeException(nameof(zero));
            }
        }

        /// <summary>指定扇区对准指针时的绝对本地 Z 角。offset 为一格的比例，正值沿编号方向偏向格内一侧。</summary>
        public float GetSectorAngle(int sectorIndex, float offset = 0) {
            ValidateLayout(m_SectorsCount, m_ZeroAngle);

            if (sectorIndex < 0 || sectorIndex >= m_SectorsCount) {
                throw new ArgumentOutOfRangeException(nameof(sectorIndex));
            }

            if (!IsFinite(offset) || Mathf.Abs(offset) > 0.49f) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return Mathf.Repeat(
                m_ZeroAngle - Direction(m_SectorDirection) * (sectorIndex + offset) * (360f / m_SectorsCount), 360f);
        }

        /// <summary>编辑器对齐和运行时复位入口。仅空闲时生效，不触发中奖通知。</summary>
        public bool SnapToSector(int sectorIndex) {
            var angle = GetSectorAngle(sectorIndex);

            if (IsSpinning || !m_Reel) {
                return false;
            }

            ApplyAngle(angle, false);
            WinSectorIndex = -1;
            return true;
        }

        /// <summary>兼容简单调用。需要判断忙碌/不可用时，使用 TryStartSpin。</summary>
        public void StartSpin(int winSectorIndex, Action onComplete = null) => TryStartSpin(winSectorIndex, onComplete);

        public bool TryStartSpin(int winSectorIndex, Action onComplete = null) =>
            TryStartSpin(winSectorIndex, UnityEngine.Random.Range(-m_LandingJitter, m_LandingJitter), onComplete);

        public bool TryStartSpin(int winSectorIndex, float offset, Action onComplete = null) {
            var angle = GetSectorAngle(winSectorIndex, offset);

            if (!CanStart()) {
                return false;
            }

            PrepareSpin();
            LockResult(winSectorIndex, angle, onComplete);
            BeginMotion();
            m_StartedEvent.Invoke();
            return true;
        }

        /// <summary>先持续转动，等待业务方调用 StopAt；失败或超时由业务方调用 CancelSpin。</summary>
        public bool BeginSpin() {
            if (!CanStart()) {
                return false;
            }

            PrepareSpin();
            BeginMotion();
            m_StartedEvent.Invoke();
            return true;
        }

        public bool StopAt(int sectorIndex, Action onComplete = null) =>
            StopAt(sectorIndex, UnityEngine.Random.Range(-m_LandingJitter, m_LandingJitter), onComplete);

        /// <summary>等待模式指定结果。显式 offset 可用于可复现测试；结果一旦锁定，不接受第二次停止请求。</summary>
        public bool StopAt(int sectorIndex, float offset, Action onComplete = null) {
            var angle = GetSectorAngle(sectorIndex, offset);

            if (!CanSubmitResult) {
                return false;
            }

            LockResult(sectorIndex, angle, onComplete);

            if (State == WheelState.Spinning) {
                PlanMainStop();
            }

            return true;
        }

        private bool CanStart() {
            if (IsSpinning || !isActiveAndEnabled) {
                return false;
            }

            if (!m_Reel) {
                Debug.LogError("LuckyWheel: assign Reel before starting a spin.", this);
                return false;
            }

            ValidateLayout(m_SectorsCount, m_ZeroAngle);
            OnValidate();
            return true;
        }

        private void PrepareSpin() {
            m_Angle = m_Reel.localEulerAngles.z;
            m_ActiveDirection = Direction(m_SpinDirection);
            m_Elapsed = 0;
            m_Speed = 0;
            m_DelayRemaining = m_SpinStartDelay;
            WinSectorIndex = TargetSectorIndex = -1;

            if (m_PointerController) {
                m_PointerController.CancelFlick();
            }

            if (!m_AudioSource) {
                m_AudioSource = GetComponent<AudioSource>();
            }

            m_AudioSource.Stop();

            if (m_SpinSound) {
                m_AudioSource.clip = m_SpinSound;
                m_AudioSource.loop = true;
                m_AudioSource.Play();
            }
        }

        private void LockResult(int target, float angle, Action callback) {
            TargetSectorIndex = target;
            m_LandingAngle = angle;
            m_OnComplete = callback;
        }

        private void BeginMotion() {
            m_Elapsed = 0;
            m_StageStartAngle = m_Angle;

            if (m_InRotationTime > 0) {
                State = WheelState.Starting;
            } else {
                FinishStarting();
            }
        }

        private void FinishStarting() {
            m_Elapsed = 0;

            if (TargetSectorIndex >= 0) {
                PlanMainStop();
            } else {
                State = WheelState.Spinning;
            }
        }

        private void PlanMainStop() {
            m_StopStartAngle = m_Angle;
            m_StopStartSpeed = m_Speed;
            m_StopDuration = m_MainRotationTime;
            var current = (float)(m_Angle % 360);
            var overshoot = m_OutRotationTime > 0 ? m_OutRotationAngle : 0;
            m_StopDistance = Mathf.Repeat((m_LandingAngle - current) * m_ActiveDirection, 360f) + overshoot
                + 360.0 * m_MinimumTurns;
            // Hermite 起始切线不超过平均速度的三倍，保证减速轨迹单调，不会先越过目标再倒转。
            var minimumDistance = m_StopStartSpeed * m_StopDuration / 3.0;

            if (m_StopDistance < minimumDistance) {
                m_StopDistance += Math.Ceiling((minimumDistance - m_StopDistance) / 360.0) * 360.0;
            }

            m_MainEndAngle = m_StopStartAngle + m_StopDistance * m_ActiveDirection;
            m_Elapsed = 0;
            State = WheelState.Stopping;
        }

        private void Update() => Advance(m_UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        // 单一时间推进入口，便于覆盖延迟、取消、连续转动和大帧间隔。
        private void Advance(float deltaTime) {
            if (!IsSpinning || deltaTime <= 0) {
                return;
            }

            if (!m_Reel) {
                CancelSpin();
                return;
            }

            if (m_DelayRemaining > 0) {
                var consumed = Mathf.Min(deltaTime, m_DelayRemaining);
                m_DelayRemaining -= consumed;
                deltaTime -= consumed;

                if (deltaTime <= 0) {
                    return;
                }
            }

            // 同一帧可跨越多个阶段，剩余时间继续传递；完成回调启动的新一轮不消耗旧轮剩余时间。
            while (deltaTime > 0) {
                if (State == WheelState.Spinning) {
                    var previousTime = m_Elapsed;
                    m_Elapsed += deltaTime;
                    var distance = WaitingDistance(m_Elapsed) - WaitingDistance(previousTime);
                    m_Speed = m_WaitingSpeed
                              * (m_AccelerationTime <= 0 ? 1 : Mathf.Clamp01(m_Elapsed / m_AccelerationTime));
                    ApplyAngle(m_Angle + distance * m_ActiveDirection, true);
                    return;
                }

                var duration = State == WheelState.Starting
                    ? m_InRotationTime
                    : State == WheelState.Settling
                        ? m_OutRotationTime
                        : m_StopDuration;
                var consumed = Mathf.Min(deltaTime, Mathf.Max(0, duration - m_Elapsed));
                m_Elapsed += consumed;
                deltaTime -= consumed;
                var t = duration <= 0 ? 1 : Mathf.Clamp01(m_Elapsed / duration);

                if (State == WheelState.Starting) {
                    var progress = t >= 1 ? 1 : Mathf.Clamp01(m_InRotationCurve.Evaluate(t));
                    ApplyAngle(m_StageStartAngle - m_InRotationAngle * m_ActiveDirection * progress, true);

                    if (t < 1) {
                        return;
                    }

                    FinishStarting();
                } else if (State == WheelState.Settling) {
                    var progress = t >= 1 ? 1 : Mathf.Clamp01(m_OutRotationCurve.Evaluate(t));
                    ApplyAngle(m_StageStartAngle - m_OutRotationAngle * m_ActiveDirection * progress, true);

                    if (t < 1) {
                        return;
                    }

                    CompleteSpin();
                    return;
                } else {
                    var t2 = t * t;
                    var t3 = t2 * t;
                    var distanceToTarget = (-2 * t3 + 3 * t2) * m_StopDistance
                                           + (t3 - 2 * t2 + t) * m_StopStartSpeed * m_StopDuration;
                    ApplyAngle(t >= 1 ? m_MainEndAngle : m_StopStartAngle + distanceToTarget * m_ActiveDirection, true);

                    if (t < 1) {
                        return;
                    }

                    if (m_OutRotationTime <= 0) {
                        CompleteSpin();
                        return;
                    }

                    m_StageStartAngle = m_MainEndAngle;
                    m_Elapsed = 0;
                    State = WheelState.Settling;
                }
            }
        }

        private double WaitingDistance(float time) {
            if (m_AccelerationTime <= 0) {
                return (double)m_WaitingSpeed * time;
            }

            var accelerating = Math.Min(time, m_AccelerationTime);
            return m_WaitingSpeed * (accelerating * accelerating / (2 * m_AccelerationTime)
                                     + Math.Max(0, time - m_AccelerationTime));
        }

        private void ApplyAngle(double angle, bool flick) {
            if (flick && m_PointerController) {
                var step = 360.0 / m_SectorsCount;
                var previousLine = Math.Floor((m_Angle - m_ZeroAngle) / step + 0.5);
                var nextLine = Math.Floor((angle - m_ZeroAngle) / step + 0.5);

                // 一帧跨越多格只拨动一次，避免卡顿恢复时挤出大量音效。
                if (previousLine != nextLine) {
                    m_PointerController.Flick(angle < m_Angle, m_UseUnscaledTime);
                }
            }

            m_Angle = angle;
            var euler = m_Reel.localEulerAngles;
            euler.z = (float)(angle % 360.0);
            m_Reel.localEulerAngles = euler;
        }

        private void CompleteSpin() {
            ApplyAngle(m_LandingAngle, false);
            var result = TargetSectorIndex;
            var callback = m_OnComplete;
            m_OnComplete = null;
            State = WheelState.Idle;
            WinSectorIndex = result;
            m_AudioSource.Stop();
            m_AudioSource.loop = false;

            if (m_PointerController) {
                m_PointerController.CancelFlick();
            }

            if (m_HitTargetSound) {
                m_AudioSource.PlayOneShot(m_HitTargetSound);
            }

            // 所有状态和资源先收尾，回调内可安全发起下一轮。结果参数属于本次完成，不读取下一轮状态。
            m_ResultEvent.Invoke(result);
            m_CompleteEvent.Invoke();
            Completed?.Invoke(result);
            callback?.Invoke();
        }

        /// <summary>保留当前转盘角度，停止声音并恢复指针；丢弃完成回调，不产生中奖结果。</summary>
        public void CancelSpin() {
            var wasSpinning = IsSpinning;
            State = WheelState.Idle;
            m_OnComplete = null;
            TargetSectorIndex = -1;

            if (wasSpinning) {
                WinSectorIndex = -1;
            }

            if (m_AudioSource) {
                m_AudioSource.Stop();
                m_AudioSource.loop = false;
            }

            if (m_PointerController) {
                m_PointerController.CancelFlick();
            }

            if (!wasSpinning) {
                return;
            }

            m_CancelledEvent.Invoke();
            Cancelled?.Invoke();
        }

        private void OnDisable() => CancelSpin();
        private void OnDestroy() => CancelSpin();
    }
}