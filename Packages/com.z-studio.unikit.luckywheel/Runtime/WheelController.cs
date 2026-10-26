using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ZStudio.UniKit.LuckyWheel {
    public enum SpinDirection {
        Counter,
        ClockWise
    }

    public enum WheelLandingMode {
        Center,
        Fixed,
        Random
    }

    public enum WheelState {
        Idle,
        Spinning,
        Stopping,
        Starting,
        Settling,
        Accelerating,
        Decelerating
    }

    /// <summary>
    /// 转盘的表现组件。调用方决定结果；组件只负责旋转、对齐、指针反馈和完成通知。
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

        [SerializeField]
        private bool m_CustomSectors;

        [SerializeField]
        private List<float> m_SectorWidths = new();

        [SerializeField]
        private WheelLandingMode m_LandingMode;

        [Range(-0.49f, 0.49f), SerializeField]
        private float m_LandingOffset;

        [SerializeField]
        private Vector2 m_LandingRange = new(-0.25f, 0.25f);

        [Tooltip("列表顺序对应 0、1、2……号奖格。配置后奖格数量自动跟随列表。空列表仍可只按编号转动。")]
        [SerializeField]
        private List<WheelReward> m_Rewards = new();

        [Tooltip("0 号扇区中心对准指针时，Reel 的本地 Z 角度。")]
        [SerializeField]
        private float m_ZeroAngle;

        [Tooltip("美术上的扇区编号方向，与转动方向无关。索引从 0 开始。")]
        [SerializeField]
        private SpinDirection m_SectorDirection = SpinDirection.ClockWise;

        [SerializeField]
        private SpinDirection m_SpinDirection = SpinDirection.Counter;

        [InspectorName("Minimum Cruise Time")]
        [Tooltip("本轮至少匀速转动的时间，已等待的匀速时间计入。对齐目标或满足最少圈数时可能延长，不通过突然改变转速来凑时长。")]
        [Min(0), SerializeField]
        private float m_MainRotationTime = 3f;

        [Tooltip("本轮正向转动至少经过的完整圈数，包含加速、等待和减速；预摆和回摆不计入。")]
        [Min(0), SerializeField]
        private int m_MinimumTurns = 3;

        [Min(0), SerializeField]
        private float m_SpinStartDelay;

        [InspectorName("Cruise Speed")]
        [Tooltip("两种结果流程共用的稳定转速，单位：度/秒。收到结果后不会突然加速或改变旋转方向。")]
        [Min(1), SerializeField]
        private float m_WaitingSpeed = 720f;

        [Tooltip("预摆结束后，从静止平滑加速到稳定转速的时长，两种结果流程均生效。0 表示跳过加速，会立即达到稳定转速。")]
        [Min(0), SerializeField]
        private float m_AccelerationTime = 0.4f;

        [Tooltip("从稳定转速平滑减速到静止的时长；回摆在此后单独执行。")]
        [Min(0.05f), SerializeField]
        private float m_DecelerationTime = 2f;

        [SerializeField]
        private bool m_CustomAccelerationRhythm;

        [Tooltip("加速作用的非负权重：前段权重大则更早提速，后段权重大则更晚提速。组件负责速度和加速度的边界衔接。")]
        [InspectorName("Acceleration Weight")]
        [SerializeField]
        private AnimationCurve m_AccelerationRhythm = AnimationCurve.Constant(0, 1, 1);

        [SerializeField]
        private bool m_CustomDecelerationRhythm;

        [Tooltip("制动作用的非负权重：前段权重大则更早降速，后段权重大则更晚降速。总减速时间不变。")]
        [InspectorName("Braking Weight")]
        [SerializeField]
        private AnimationCurve m_DecelerationRhythm = AnimationCurve.Constant(0, 1, 1);

        [SerializeField]
        private bool m_UseUnscaledTime;

        [Tooltip("主旋转前反向预摆的时长；0 表示跳过此阶段。")]
        [Min(0), SerializeField]
        private float m_InRotationTime = 0.2f;

        [Tooltip("沿主旋转反方向预摆的角度。")]
        [Min(0), SerializeField]
        private float m_InRotationAngle;

        [InspectorName("Custom Pre-swing Rhythm")]
        [SerializeField]
        private bool m_CustomInRhythm;

        [Tooltip("预摆的非负速度权重，不是角度进度。组件自动归一化距离，并在两端平滑静止。")]
        [InspectorName("Pre-swing Velocity Weight")]
        [SerializeField]
        private AnimationCurve m_InRotationCurve = AnimationCurve.Constant(0, 1, 1);

        [Tooltip("主旋转越过目标后回摆归位的时长；0 表示直接停到目标。")]
        [Min(0), SerializeField]
        private float m_OutRotationTime = 0.2f;

        [Tooltip("主旋转沿转动方向越过目标的角度；停转阶段反向回到目标。")]
        [Min(0), SerializeField]
        private float m_OutRotationAngle;

        [InspectorName("Custom Recoil Rhythm")]
        [SerializeField]
        private bool m_CustomOutRhythm;

        [Tooltip("回摆的非负速度权重，不是角度进度。组件自动归一化距离，并在两端平滑静止。")]
        [InspectorName("Recoil Velocity Weight")]
        [SerializeField]
        private AnimationCurve m_OutRotationCurve = AnimationCurve.Constant(0, 1, 1);

        [SerializeField]
        private AudioClip m_SpinSound;

        [SerializeField]
        private AudioClip m_HitTargetSound;

        [SerializeField]
        private UnityEvent m_StartedEvent = new();

        [SerializeField]
        private UnityEvent<int> m_ResultEvent = new();

        [SerializeField]
        private UnityEvent<WheelReward> m_RewardEvent = new();

        [SerializeField]
        private UnityEvent m_CompleteEvent = new();

        [SerializeField]
        private UnityEvent m_CancelledEvent = new();

        public WheelState State { get; private set; }
        public bool IsSpinning => State != WheelState.Idle;

        public bool CanSubmitResult =>
            State is WheelState.Starting or WheelState.Accelerating or WheelState.Spinning
            && TargetSectorIndex < 0;

        /// <summary>实际本地 Z 角速度（度/秒），顺时针为负；包含反向预摆和回摆。</summary>
        public float CurrentAngularSpeed => m_DelayRemaining > 0 ? 0 : m_Speed * m_ActiveDirection;

        public int WinSectorIndex { get; private set; } = -1;
        public int TargetSectorIndex { get; private set; } = -1;
        public int SectorsCount => HasRewards ? m_Rewards.Count : m_SectorsCount;
        public bool HasCustomSectors => m_CustomSectors;
        public WheelLandingMode LandingMode => m_LandingMode;
        public bool HasRewards => m_Rewards != null && m_Rewards.Count > 0;
        public IReadOnlyList<WheelReward> Rewards => m_Rewards.AsReadOnly();
        public WheelReward TargetReward { get; private set; }
        public WheelReward WinReward { get; private set; }

        /// <summary>本轮表现完成时返回锁定的映射副本；表现对象与业务数据仍为引用。</summary>
        public event Action<WheelReward> RewardCompleted;

        public float ZeroAngle => m_ZeroAngle;
        public SpinDirection SectorDirection => m_SectorDirection;
        public Transform Reel => m_Reel;
        public bool UseUnscaledTime => m_UseUnscaledTime;
        public event Action<int> Completed;
        public event Action Cancelled;

        private AudioSource m_AudioSource;
        private Action m_OnComplete;
        private float m_DelayRemaining;
        private double m_Elapsed;
        private float m_Speed;
        private double m_Angle;
        private double m_StopStartAngle;
        private double m_CruiseDuration;
        private double m_CruiseElapsed;
        private double m_ForwardDistance;
        private double m_DecelerationStartAngle;
        private float m_LandingAngle;
        private float m_ActiveDirection;
        private double m_StageStartAngle;
        private double m_MainEndAngle;
        private readonly RhythmProfile m_AccelerationProfile = new();
        private readonly RhythmProfile m_DecelerationProfile = new();
        private readonly RhythmProfile m_InProfile = new();
        private readonly RhythmProfile m_OutProfile = new();

        private static float Direction(SpinDirection direction) => direction == SpinDirection.ClockWise ? -1f : 1f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void Awake() => m_AudioSource = GetComponent<AudioSource>();
        private void Reset() => m_Reel = transform;

        private void OnValidate() {
            m_SectorsCount = HasRewards ? m_Rewards.Count : Mathf.Max(1, m_SectorsCount);
            m_MainRotationTime = IsFinite(m_MainRotationTime) ? Mathf.Max(0, m_MainRotationTime) : 3f;
            m_MinimumTurns = Mathf.Clamp(m_MinimumTurns, 0, 100);
            m_SpinStartDelay = IsFinite(m_SpinStartDelay) ? Mathf.Max(0, m_SpinStartDelay) : 0;
            m_WaitingSpeed = IsFinite(m_WaitingSpeed) ? Mathf.Clamp(m_WaitingSpeed, 1, 100000) : 720f;
            m_AccelerationTime = IsFinite(m_AccelerationTime) ? Mathf.Max(0, m_AccelerationTime) : 0.4f;
            m_DecelerationTime = IsFinite(m_DecelerationTime) ? Mathf.Max(0.05f, m_DecelerationTime) : 2f;
            m_LandingOffset = IsFinite(m_LandingOffset) ? Mathf.Clamp(m_LandingOffset, -0.49f, 0.49f) : 0;
            m_LandingRange.x = IsFinite(m_LandingRange.x) ? Mathf.Clamp(m_LandingRange.x, -0.49f, 0.49f) : -0.25f;
            m_LandingRange.y = IsFinite(m_LandingRange.y) ? Mathf.Clamp(m_LandingRange.y, m_LandingRange.x, 0.49f)
                : m_LandingRange.x;

            if (!IsFinite(m_ZeroAngle)) {
                m_ZeroAngle = 0;
            }

            m_InRotationTime = SanitizeNonNegative(m_InRotationTime);
            m_OutRotationTime = SanitizeNonNegative(m_OutRotationTime);
            m_InRotationAngle = SanitizeNonNegative(m_InRotationAngle);
            m_OutRotationAngle = SanitizeNonNegative(m_OutRotationAngle);

            if (!IsValidStageCurve(m_InRotationCurve)) {
                m_InRotationCurve = AnimationCurve.Constant(0, 1, 1);
            }

            if (!IsValidStageCurve(m_OutRotationCurve)) {
                m_OutRotationCurve = AnimationCurve.Constant(0, 1, 1);
            }

            if (!IsValidStageCurve(m_AccelerationRhythm)) m_AccelerationRhythm = AnimationCurve.Constant(0, 1, 1);
            if (!IsValidStageCurve(m_DecelerationRhythm)) m_DecelerationRhythm = AnimationCurve.Constant(0, 1, 1);
        }

        /// <summary>配置布局，不改变当前视觉角度。正在转动时不允许更换布局。</summary>
        public void Configure(Transform reel, int sectorsCount, float zeroAngle = 0,
            SpinDirection sectorDirection = SpinDirection.ClockWise) {
            RequireIdle();

            if (!reel) {
                throw new ArgumentNullException(nameof(reel));
            }

            ValidateLayout(sectorsCount, zeroAngle);
            RequireMatchingRewardCount(sectorsCount);
            RequireMatchingSectorCount(sectorsCount);
            m_Reel = reel;
            m_SectorsCount = sectorsCount;
            m_ZeroAngle = zeroAngle;
            m_SectorDirection = sectorDirection;
            WinSectorIndex = -1;
            TargetSectorIndex = -1;
            TargetReward = WinReward = null;
        }

        /// <summary>Inspector 已配置布局时无需调用；此方法供运行时更新扇区数量。</summary>
        public void Initialize(int sectorsCount) {
            RequireIdle();
            ValidateLayout(sectorsCount, m_ZeroAngle);
            RequireMatchingRewardCount(sectorsCount);
            RequireMatchingSectorCount(sectorsCount);
            m_SectorsCount = sectorsCount;
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;
        }

        /// <summary>运行时配置每格奖励。列表顺序即奖格顺序；先校验全部数据再替换，不保存到场景。</summary>
        public void ConfigureRewards(IEnumerable<WheelReward> rewards) {
            RequireIdle();
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            var replacement = new List<WheelReward>(rewards);
            if (replacement.Count == 0)
                throw new ArgumentException("At least one reward is required. Use ClearRewards for index-only mode.",
                    nameof(rewards));
            if (!ValidateRewardList(replacement, out var error)) throw new ArgumentException(error, nameof(rewards));
            RequireMatchingSectorCount(replacement.Count);
            for (var index = 0; index < replacement.Count; index++) replacement[index] = replacement[index].Copy();
            m_Rewards = replacement;
            m_SectorsCount = replacement.Count;
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;
        }

        /// <summary>清除奖励映射并保留当前奖格数量，恢复仅使用编号的转盘。</summary>
        public void ClearRewards() {
            RequireIdle();
            m_SectorsCount = SectorsCount;
            m_Rewards.Clear();
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;
        }

        public WheelReward GetReward(int sectorIndex) {
            if (sectorIndex < 0 || sectorIndex >= SectorsCount)
                throw new ArgumentOutOfRangeException(nameof(sectorIndex));
            return HasRewards ? m_Rewards[sectorIndex] : null;
        }

        /// <summary>按唯一标识查找奖格；区分大小写。未找到返回 -1。</summary>
        public int FindRewardIndex(string rewardId) {
            if (!HasRewards || string.IsNullOrWhiteSpace(rewardId)) return -1;

            for (var index = 0; index < m_Rewards.Count; index++) {
                if (m_Rewards[index] != null && string.Equals(m_Rewards[index].Id, rewardId, StringComparison.Ordinal))
                    return index;
            }

            return -1;
        }

        public bool TryStartSpinReward(string rewardId, Action onComplete = null) {
            var index = FindRewardIndex(rewardId);
            return index >= 0 && TryStartSpin(index, onComplete);
        }

        public bool StopAtReward(string rewardId, Action onComplete = null) {
            var index = FindRewardIndex(rewardId);
            return index >= 0 && StopAt(index, onComplete);
        }

        public bool TryStartSpinReward(string rewardId, float offset, Action onComplete = null) {
            var index = FindRewardIndex(rewardId);
            return index >= 0 && TryStartSpin(index, offset, onComplete);
        }

        public bool StopAtReward(string rewardId, float offset, Action onComplete = null) {
            var index = FindRewardIndex(rewardId);
            return index >= 0 && StopAt(index, offset, onComplete);
        }

        public bool ValidateRewards(out string error) => ValidateRewardList(m_Rewards, out error);

        private static bool ValidateRewardList(IReadOnlyList<WheelReward> rewards, out string error) {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);

            if (rewards != null) {
                for (var index = 0; index < rewards.Count; index++) {
                    var reward = rewards[index];

                    if (reward == null || string.IsNullOrWhiteSpace(reward.Id)) {
                        error = $"第 {index} 格缺少奖励标识。";
                        return false;
                    }

                    if (!identifiers.Add(reward.Id)) {
                        error = $"第 {index} 格的奖励标识“{reward.Id}”重复。指定结果用的标识必须唯一，名称可以相同。";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private void RequireMatchingRewardCount(int count) {
            if (HasRewards && count != m_Rewards.Count) {
                throw new InvalidOperationException(
                    "Reward configuration determines the sector count. Use ConfigureRewards to change it.");
            }
        }

        /// <summary>自定义每格宽度（度），按编号顺序排列，总和须为 360。宽度不是中奖概率。</summary>
        public void ConfigureSectorWidths(IEnumerable<float> widths) {
            RequireIdle();
            if (widths == null) throw new ArgumentNullException(nameof(widths));
            var replacement = new List<float>(widths);
            if (!ValidateSectorWidths(replacement, replacement.Count, out var error))
                throw new ArgumentException(error, nameof(widths));
            RequireMatchingRewardCount(replacement.Count);
            m_SectorWidths = replacement;
            m_CustomSectors = true;
            m_SectorsCount = replacement.Count;
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;
        }

        public void ConfigureEqualSectors() {
            RequireIdle();
            m_CustomSectors = false;
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;
        }

        public bool ValidateSectors(out string error) {
            error = null;
            return !m_CustomSectors || ValidateSectorWidths(m_SectorWidths, SectorsCount, out error);
        }

        private static bool ValidateSectorWidths(IReadOnlyList<float> widths, int count, out string error) {
            if (count < 1 || widths == null || widths.Count != count) {
                error = "每个奖格都需要一个角度宽度，数量须与奖格数量一致。";
                return false;
            }

            double sum = 0;

            for (var index = 0; index < widths.Count; index++) {
                if (!IsFinite(widths[index]) || widths[index] <= 0) {
                    error = $"第 {index} 格的角度宽度必须是有限的正数。";
                    return false;
                }

                sum += widths[index];
            }

            if (Math.Abs(sum - 360) > 0.001) {
                error = $"奖格角度总和为 {sum:G}°，须为 360°。";
                return false;
            }

            error = null;
            return true;
        }

        private void RequireMatchingSectorCount(int count) {
            if (m_CustomSectors && count != m_SectorWidths.Count)
                throw new InvalidOperationException(
                    "Custom sector widths must match the sector count. ConfigureEqualSectors before changing the count.");
        }

        // 仅消除允许范围内的浮点求和误差，保证圆周首尾完全闭合。
        private double SectorScale() {
            double sum = 0;
            for (var index = 0; index < m_SectorWidths.Count; index++) sum += m_SectorWidths[index];
            return 360.0 / sum;
        }

        public float GetSectorWidth(int sectorIndex) {
            ValidateSectorIndex(sectorIndex);
            return m_CustomSectors ? (float)(m_SectorWidths[sectorIndex] * SectorScale()) : 360f / SectorsCount;
        }

        /// <summary>从 0 号格中心沿编号方向量到指定格中心的角度，与 Reel 当前姿态无关。</summary>
        public float GetSectorCenterOffset(int sectorIndex) {
            ValidateSectorIndex(sectorIndex);
            if (!m_CustomSectors) return sectorIndex * (360f / SectorsCount);
            double offset = (m_SectorWidths[sectorIndex] - m_SectorWidths[0]) * 0.5;
            for (var index = 0; index < sectorIndex; index++) offset += m_SectorWidths[index];
            return (float)(offset * SectorScale());
        }

        private void ValidateSectorIndex(int sectorIndex) {
            ValidateLayout(SectorsCount, m_ZeroAngle);
            if (!ValidateSectors(out var error)) throw new InvalidOperationException(error);
            if (sectorIndex < 0 || sectorIndex >= SectorsCount)
                throw new ArgumentOutOfRangeException(nameof(sectorIndex));
        }

        /// <summary>offset 与随机范围以目标格宽度为单位，0 为中心，安全范围为 -0.49～0.49。</summary>
        public void ConfigureLanding(WheelLandingMode mode = WheelLandingMode.Center, float offset = 0,
            Vector2? randomRange = null) {
            RequireIdle();
            if (!Enum.IsDefined(typeof(WheelLandingMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            var range = randomRange ?? new Vector2(-0.25f, 0.25f);
            if (!IsFinite(offset) || Mathf.Abs(offset) > 0.49f) throw new ArgumentOutOfRangeException(nameof(offset));
            if (!IsFinite(range.x) || !IsFinite(range.y) || range.x < -0.49f || range.y > 0.49f || range.x > range.y)
                throw new ArgumentOutOfRangeException(nameof(randomRange));
            m_LandingMode = mode;
            m_LandingOffset = offset;
            m_LandingRange = range;
        }

        private float DefaultLandingOffset() =>
            m_LandingMode switch {
                WheelLandingMode.Fixed => m_LandingOffset,
                WheelLandingMode.Random => UnityEngine.Random.Range(m_LandingRange.x, m_LandingRange.y),
                _ => 0
            };

        /// <summary>配置最少匀速时间、整轮最少圈数、稳定转速和加速时间。旋转中不允许修改。</summary>
        public void ConfigureMotion(float duration, int minimumTurns, SpinDirection direction,
            float waitingSpeed = 720, float accelerationTime = 0.4f,
            float startDelay = 0, float landingJitter = 0, bool useUnscaledTime = false) {
            RequireIdle();

            if (!IsFinite(duration) || duration < 0) {
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
            ConfigureLanding(landingJitter > 0 ? WheelLandingMode.Random : WheelLandingMode.Center,
                randomRange: new Vector2(-landingJitter, landingJitter));
            m_UseUnscaledTime = useUnscaledTime;
        }

        /// <summary>独立配置预摆和回摆；时长或角度为 0 时跳过。传入曲线启用对应的非负速度权重。</summary>
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
                throw new ArgumentException(
                    "Rhythm weights must be finite, non-negative and contain a non-zero interior interval.",
                    nameof(inCurve));
            }

            if (outCurve != null && !IsValidStageCurve(outCurve)) {
                throw new ArgumentException(
                    "Rhythm weights must be finite, non-negative and contain a non-zero interior interval.",
                    nameof(outCurve));
            }

            m_InRotationTime = inDuration;
            m_InRotationAngle = inAngle;
            m_OutRotationTime = outDuration;
            m_OutRotationAngle = outAngle;
            m_CustomInRhythm = inCurve != null;
            m_CustomOutRhythm = outCurve != null;
            m_InRotationCurve = inCurve == null ? AnimationCurve.Constant(0, 1, 1)
                : new AnimationCurve(inCurve.keys);
            m_OutRotationCurve = outCurve == null ? AnimationCurve.Constant(0, 1, 1)
                : new AnimationCurve(outCurve.keys);
        }

        private static float SanitizeNonNegative(float value) => IsFinite(value) ? Mathf.Max(0, value) : 0;

        /// <summary>配置减速时间，以及可选的加速/制动权重；不传曲线使用自然节奏。</summary>
        public void ConfigureRhythm(float decelerationTime, AnimationCurve accelerationRhythm = null,
            AnimationCurve decelerationRhythm = null) {
            RequireIdle();
            if (!IsFinite(decelerationTime) || decelerationTime < 0.05f)
                throw new ArgumentOutOfRangeException(nameof(decelerationTime));
            if (accelerationRhythm != null && !IsValidStageCurve(accelerationRhythm))
                throw new ArgumentException("Invalid acceleration weights.", nameof(accelerationRhythm));
            if (decelerationRhythm != null && !IsValidStageCurve(decelerationRhythm))
                throw new ArgumentException("Invalid braking weights.", nameof(decelerationRhythm));
            m_DecelerationTime = decelerationTime;
            m_CustomAccelerationRhythm = accelerationRhythm != null;
            m_CustomDecelerationRhythm = decelerationRhythm != null;
            m_AccelerationRhythm = accelerationRhythm == null ? AnimationCurve.Constant(0, 1, 1)
                : new AnimationCurve(accelerationRhythm.keys);
            m_DecelerationRhythm = decelerationRhythm == null ? AnimationCurve.Constant(0, 1, 1)
                : new AnimationCurve(decelerationRhythm.keys);
        }

        private static bool IsValidStageCurve(AnimationCurve curve) => RhythmProfile.IsValid(curve);

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
            ValidateSectorIndex(sectorIndex);

            if (!IsFinite(offset) || Mathf.Abs(offset) > 0.49f) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return Mathf.Repeat(
                m_ZeroAngle - Direction(m_SectorDirection)
                * (GetSectorCenterOffset(sectorIndex) + offset * GetSectorWidth(sectorIndex)), 360f);
        }

        /// <summary>编辑器对齐和运行时复位入口。仅空闲时生效，不触发中奖通知。</summary>
        public bool SnapToSector(int sectorIndex) => SnapToSector(sectorIndex, 0);

        public bool SnapToSector(int sectorIndex, float offset) {
            var angle = GetSectorAngle(sectorIndex, offset);

            if (IsSpinning || !m_Reel) {
                return false;
            }

            ApplyAngle(angle, false);
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;
            return true;
        }

        /// <summary>兼容简单调用。需要判断忙碌/不可用时，使用 TryStartSpin。</summary>
        public void StartSpin(int winSectorIndex, Action onComplete = null) => TryStartSpin(winSectorIndex, onComplete);

        public bool TryStartSpin(int winSectorIndex, Action onComplete = null) =>
            TryStartSpin(winSectorIndex, DefaultLandingOffset(), onComplete);

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
            StopAt(sectorIndex, DefaultLandingOffset(), onComplete);

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

            ValidateLayout(SectorsCount, m_ZeroAngle);

            if (!ValidateSectors(out var error) || !ValidateRewards(out error)) {
                Debug.LogError("LuckyWheel: " + error, this);
                return false;
            }

            OnValidate();
            return true;
        }

        private void PrepareSpin() {
            m_Angle = m_Reel.localEulerAngles.z;
            m_ActiveDirection = Direction(m_SpinDirection);
            m_Elapsed = 0;
            m_Speed = 0;
            m_CruiseElapsed = 0;
            m_ForwardDistance = 0;
            m_AccelerationProfile.Build(m_CustomAccelerationRhythm ? m_AccelerationRhythm : null);
            m_DecelerationProfile.Build(m_CustomDecelerationRhythm ? m_DecelerationRhythm : null);
            m_InProfile.Build(m_CustomInRhythm ? m_InRotationCurve : null);
            m_OutProfile.Build(m_CustomOutRhythm ? m_OutRotationCurve : null);
            m_DelayRemaining = m_SpinStartDelay;
            WinSectorIndex = TargetSectorIndex = -1;
            TargetReward = WinReward = null;

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
            TargetReward = GetReward(target)?.Copy();
            m_LandingAngle = angle;
            m_OnComplete = callback;
        }

        private void BeginMotion() {
            m_Elapsed = 0;
            m_StageStartAngle = m_Angle;

            if (m_InRotationTime > 0 && m_InRotationAngle > 0) {
                State = WheelState.Starting;
            } else {
                FinishStarting();
            }
        }

        private void FinishStarting() {
            m_Elapsed = 0;
            m_StageStartAngle = m_Angle;
            m_Speed = 0;
            if (m_AccelerationTime > 0) State = WheelState.Accelerating;
            else FinishAcceleration();
        }

        private void FinishAcceleration() {
            m_Elapsed = 0;
            m_Speed = m_WaitingSpeed;
            if (TargetSectorIndex >= 0) PlanMainStop();
            else State = WheelState.Spinning;
        }

        private void PlanMainStop() {
            m_StopStartAngle = m_Angle;
            var overshoot = m_OutRotationTime > 0 ? m_OutRotationAngle : 0;
            var decelerationDistance = m_WaitingSpeed * m_DecelerationTime * (1 - m_DecelerationProfile.TotalIntegral);
            var current = (float)(m_Angle % 360);
            var distance = Mathf.Repeat((m_LandingAngle - current) * m_ActiveDirection, 360f) + (double)overshoot;
            // 保持稳定转速和设定的减速时间。只延长匀速段去满足落点、整轮圈数与最少匀速时间。
            var minimumDistance = Math.Max(m_MinimumTurns * 360.0 - m_ForwardDistance,
                m_WaitingSpeed * Math.Max(0, m_MainRotationTime - m_CruiseElapsed) + decelerationDistance);
            if (distance < minimumDistance) distance += Math.Ceiling((minimumDistance - distance) / 360.0) * 360.0;
            var cruiseDistance = Math.Max(0, distance - decelerationDistance);
            m_CruiseDuration = cruiseDistance / m_WaitingSpeed;
            m_DecelerationStartAngle = m_StopStartAngle + cruiseDistance * m_ActiveDirection;
            m_MainEndAngle = m_StopStartAngle + distance * m_ActiveDirection;
            m_Elapsed = 0;
            State = WheelState.Stopping;
        }

        private void Update() => Advance(m_UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        // 各阶段使用同一条时间线。大帧跨越边界时继续消耗余量，完成回调开启的新轮次不消耗旧轮余量。
        private void Advance(float deltaTime) {
            if (!IsSpinning || deltaTime <= 0) return;

            if (!m_Reel) {
                CancelSpin();
                return;
            }

            double remaining = deltaTime;

            if (m_DelayRemaining > 0) {
                var consumed = Math.Min(remaining, m_DelayRemaining);
                m_DelayRemaining -= (float)consumed;
                remaining -= consumed;
            }

            while (remaining > 0) {
                if (State == WheelState.Spinning) {
                    m_CruiseElapsed += remaining;
                    ApplyAngle(m_Angle + m_WaitingSpeed * remaining * m_ActiveDirection, true);
                    return;
                }

                var duration = State switch {
                    WheelState.Starting => m_InRotationTime,
                    WheelState.Accelerating => m_AccelerationTime,
                    WheelState.Stopping => m_CruiseDuration,
                    WheelState.Decelerating => m_DecelerationTime,
                    _ => m_OutRotationTime
                };
                var step = Math.Min(remaining, Math.Max(0, duration - m_Elapsed));
                m_Elapsed += step;
                remaining -= step;
                var t = duration <= 0 ? 1 : Math.Min(1, m_Elapsed / duration);

                switch (State) {
                    case WheelState.Starting:
                    case WheelState.Settling: {
                        var starting = State == WheelState.Starting;
                        var angle = starting ? m_InRotationAngle : m_OutRotationAngle;
                        var profile = starting ? m_InProfile : m_OutProfile;
                        profile.Evaluate(t, out var progress, out _, out var velocity);
                        m_Speed = (float)(-angle / duration * velocity);
                        ApplyAngle(m_StageStartAngle - angle * m_ActiveDirection * progress, true);
                        if (t < 1) return;

                        if (starting) FinishStarting();
                        else {
                            CompleteSpin();
                            return;
                        }

                        break;
                    }
                    case WheelState.Accelerating: {
                        m_AccelerationProfile.Evaluate(t, out var speed, out var distance, out _);
                        m_Speed = (float)(m_WaitingSpeed * speed);
                        ApplyAngle(m_StageStartAngle + m_WaitingSpeed * duration * distance * m_ActiveDirection, true);
                        if (t < 1) return;
                        FinishAcceleration();
                        break;
                    }
                    case WheelState.Stopping:
                        m_Speed = m_WaitingSpeed;
                        m_CruiseElapsed += step;
                        ApplyAngle(m_StopStartAngle + m_WaitingSpeed * m_Elapsed * m_ActiveDirection, true);
                        if (t < 1) return;
                        m_Elapsed = 0;
                        State = WheelState.Decelerating;
                        break;
                    case WheelState.Decelerating: {
                        m_DecelerationProfile.Evaluate(t, out var braking, out var integral, out _);
                        m_Speed = (float)(m_WaitingSpeed * (1 - braking));
                        ApplyAngle(t >= 1 ? m_MainEndAngle
                                : m_DecelerationStartAngle
                                  + m_WaitingSpeed * duration * (t - integral) * m_ActiveDirection,
                            true);
                        if (t < 1) return;

                        if (m_OutRotationTime <= 0 || m_OutRotationAngle <= 0) {
                            CompleteSpin();
                            return;
                        }

                        m_StageStartAngle = m_MainEndAngle;
                        m_Elapsed = 0;
                        State = WheelState.Settling;
                        break;
                    }
                }
            }
        }

        private bool CrossedSectorBoundary(double from, double to) {
            if (!m_CustomSectors) {
                var step = 360.0 / SectorsCount;
                return Math.Floor((from - m_ZeroAngle) / step + 0.5) != Math.Floor((to - m_ZeroAngle) / step + 0.5);
            }

            var scale = SectorScale();
            double edge = -m_SectorWidths[0] * 0.5;

            for (var index = 0; index < m_SectorWidths.Count; index++) {
                var boundary = m_ZeroAngle - Direction(m_SectorDirection) * edge * scale;
                if (Math.Floor((from - boundary) / 360) != Math.Floor((to - boundary) / 360)) return true;
                edge += m_SectorWidths[index];
            }

            return false;
        }

        private void ApplyAngle(double angle, bool flick) {
            if (flick && m_PointerController) {
                // 一帧跨多格只触发一次；按真实边界检测，覆盖反向预摆、回摆和跨多圈。
                if (CrossedSectorBoundary(m_Angle, angle)) {
                    m_PointerController.Flick(angle < m_Angle, m_UseUnscaledTime);
                }
            }

            if (flick && IsSpinning && State != WheelState.Starting && State != WheelState.Settling) {
                m_ForwardDistance += Math.Max(0, (angle - m_Angle) * m_ActiveDirection);
            }

            m_Angle = angle;
            var euler = m_Reel.localEulerAngles;
            euler.z = (float)(angle % 360.0);
            m_Reel.localEulerAngles = euler;
        }

        private void CompleteSpin() {
            ApplyAngle(m_LandingAngle, false);
            var result = TargetSectorIndex;
            var reward = TargetReward;
            var callback = m_OnComplete;
            m_OnComplete = null;
            State = WheelState.Idle;
            m_Speed = 0;
            WinSectorIndex = result;
            WinReward = reward;
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

            if (reward != null) {
                m_RewardEvent.Invoke(reward);
                RewardCompleted?.Invoke(reward);
            }

            callback?.Invoke();
        }

        /// <summary>保留当前转盘角度，停止声音并恢复指针；丢弃完成回调，不产生中奖结果。</summary>
        public void CancelSpin() {
            var wasSpinning = IsSpinning;
            State = WheelState.Idle;
            m_Speed = 0;
            m_OnComplete = null;
            TargetSectorIndex = -1;
            TargetReward = null;
            if (wasSpinning) WinReward = null;

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

        /// <summary>
        /// 权重乘 t²(1-t)²，归一化后积分为进度，再积分为位移。
        /// 两端权重及其一阶导数为零，因此任意合法节奏都不会破坏阶段边界的速度/加速度。
        /// 只在每轮开始采样；每帧按分段线性权重的解析积分求值，结果不依赖帧步长。
        /// </summary>
        private sealed class RhythmProfile {
            private const int k_Segments = 128;
            private readonly double[] m_Weights = new double[k_Segments + 1];
            private readonly double[] m_Progress = new double[k_Segments + 1];
            private readonly double[] m_Integral = new double[k_Segments + 1];
            public double TotalIntegral => m_Integral[k_Segments];

            public static bool IsValid(AnimationCurve curve) {
                if (curve == null || curve.length < 1) return false;
                foreach (var key in curve.keys)
                    if (!IsFinite(key.time) || !IsFinite(key.value))
                        return false;
                double area = 0;

                for (var i = 0; i <= k_Segments; i++) {
                    var t = (float)i / k_Segments;
                    var weight = curve.Evaluate(t);
                    if (!IsFinite(weight) || weight < 0) return false;
                    if (i > 1 && i < k_Segments - 1) area += weight * (double)t * t * (1 - t) * (1 - t);
                }

                return area > 0;
            }

            public void Build(AnimationCurve curve) {
                const double step = 1.0 / k_Segments;
                double area = 0;

                for (var i = 0; i <= k_Segments; i++) {
                    var t = i * step;
                    var weight = curve == null ? 1 : curve.Evaluate((float)t);
                    // 边界相邻采样也归零，避免分段线性近似让预摆/回摆端点出现微小加速度台阶。
                    m_Weights[i] = i <= 1 || i >= k_Segments - 1 ? 0 : weight * t * t * (1 - t) * (1 - t);
                    if (i > 0) area += (m_Weights[i - 1] + m_Weights[i]) * step * 0.5;
                }

                m_Progress[0] = m_Integral[0] = 0;
                for (var i = 0; i <= k_Segments; i++) m_Weights[i] /= area;

                for (var i = 1; i <= k_Segments; i++) {
                    m_Progress[i] = m_Progress[i - 1] + (m_Weights[i - 1] + m_Weights[i]) * step * 0.5;
                    m_Integral[i] = m_Integral[i - 1] + m_Progress[i - 1] * step
                                                      + (2 * m_Weights[i - 1] + m_Weights[i]) * step * step / 6;
                }
            }

            public void Evaluate(double t, out double progress, out double integral, out double derivative) {
                if (t >= 1) {
                    progress = 1;
                    integral = TotalIntegral;
                    derivative = 0;
                    return;
                }

                if (t <= 0) {
                    progress = integral = derivative = 0;
                    return;
                }

                var index = Math.Min(k_Segments - 1, (int)(t * k_Segments));
                var x = t - (double)index / k_Segments;
                var slope = (m_Weights[index + 1] - m_Weights[index]) * k_Segments;
                derivative = m_Weights[index] + slope * x;
                progress = m_Progress[index] + m_Weights[index] * x + slope * x * x * 0.5;
                integral = m_Integral[index] + m_Progress[index] * x + m_Weights[index] * x * x * 0.5
                           + slope * x * x * x / 6;
            }
        }

        private void OnDisable() => CancelSpin();
        private void OnDestroy() => CancelSpin();
    }
}