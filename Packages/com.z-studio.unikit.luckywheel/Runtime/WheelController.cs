using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ZStudio.UniKit.LuckyWheel {
    /// <summary>从圆盘正面看的转动方向；不改变奖格固定按顺时针编号的约定。</summary>
    public enum SpinDirection {
        Counter,
        ClockWise
    }

    /// <summary>只决定目标格内的停止位置，不决定哪个奖格中奖。</summary>
    public enum WheelLandingMode {
        Center,
        Fixed,
        Random
    }

    /// <summary>每轮运行阶段。声明顺序保留序列化值；实际执行顺序见控制器类说明。</summary>
    public enum WheelState {
        Idle, // 空闲，可以改配置或发起下一轮。
        Spinning, // 匀速等待业务结果，尚未锁定目标。
        Stopping, // 已有目标，仍匀速补足距离，尚未开始减速。
        Starting, // 可选的起转前反向回拉。
        Settling, // 可选的越过目标后反向回摆。
        Accelerating, // 从静止加速到设定转速。
        Decelerating // 从设定转速减速到静止。
    }

    /// <summary>
    /// 转盘的表现组件。调用方决定结果；组件只负责旋转、对齐、指针反馈和完成通知。
    /// 阅读主线：TryStartSpin / BeginSpin → PrepareSpin → Advance → CompleteSpin。
    /// 阶段顺序：等待延迟 → 回拉（可选）→ 加速 → 匀速 → 减速 → 回摆（可选）→ 完成。
    /// BeginSpin 在匀速阶段等待 StopAt；提前收到结果也会先完成回拉和加速。
    /// Reel 是实际旋转的圆盘，角度就是它相对父物体的 Rotation Z；奖格固定按顺时针编号。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class WheelController : MonoBehaviour {
        // 配置区：圆盘与奖格几何、奖励映射、各阶段运动、音效和通知。
        // 所有配置接口要求空闲；本轮开始时锁定方向与节奏，结果提交时锁定目标。
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

        [Tooltip("列表顺序对应顺时针排列的 0、1、2……号奖格。配置后奖格数量自动跟随列表。空列表仍可只按编号转动。")]
        [SerializeField]
        private List<WheelReward> m_Rewards = new();

        [Tooltip("0 号扇区中心对准指针时，Reel 的本地 Z 角度（Local Rotation Z）。")]
        [SerializeField]
        private float m_ZeroAngle;

        [SerializeField]
        private SpinDirection m_SpinDirection = SpinDirection.Counter;

        [Tooltip("本轮至少匀速转动的时间，已等待的匀速时间计入。对齐目标或满足最少圈数时可能延长，不通过突然改变转速来凑时长。")]
        [Min(0), SerializeField]
        private float m_MainRotationTime = 3f;

        [Tooltip("本轮正向转动至少经过的完整圈数，包含加速、等待和减速；预摆和回摆不计入。")]
        [Min(0), SerializeField]
        private int m_MinimumTurns = 3;

        [Min(0), SerializeField]
        private float m_SpinStartDelay;

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
        [SerializeField]
        private AnimationCurve m_AccelerationRhythm = AnimationCurve.Constant(0, 1, 1);

        [SerializeField]
        private bool m_CustomDecelerationRhythm;

        [Tooltip("制动作用的非负权重：前段权重大则更早降速，后段权重大则更晚降速。总减速时间不变。")]
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

        [SerializeField]
        private bool m_CustomInRhythm;

        [Tooltip("预摆的非负速度权重，不是角度进度。组件自动归一化距离，并在两端平滑静止。")]
        [SerializeField]
        private AnimationCurve m_InRotationCurve = AnimationCurve.Constant(0, 1, 1);

        [Tooltip("主旋转越过目标后回摆归位的时长；0 表示直接停到目标。")]
        [Min(0), SerializeField]
        private float m_OutRotationTime = 0.2f;

        [Tooltip("主旋转沿转动方向越过目标的角度；停转阶段反向回到目标。")]
        [Min(0), SerializeField]
        private float m_OutRotationAngle;

        [SerializeField]
        private bool m_CustomOutRhythm;

        [Tooltip("回摆的非负速度权重，不是角度进度。组件自动归一化距离，并在两端平滑静止。")]
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
        private UnityEvent m_CancelledEvent = new();

        // 对外状态区：Target 是本轮已锁定的目标；Win 只在完整停准后成立，-1 表示无结果。
        public WheelState State { get; private set; }
        public bool IsSpinning => State != WheelState.Idle;

        /// <summary>等待流程尚未锁定结果时为 true；StopAt 仅接受第一次提交。</summary>
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
        public bool HasRewards => m_Rewards is { Count: > 0 };
        public IReadOnlyList<WheelReward> Rewards => m_Rewards.AsReadOnly();
        public WheelReward TargetReward { get; private set; }
        public WheelReward WinReward { get; private set; }

        public float ZeroAngle => m_ZeroAngle;
        public Transform Reel => m_Reel;
        public bool UseUnscaledTime => m_UseUnscaledTime;

        /// <summary>整轮表现完成后返回奖格索引；需要奖励配置时使用 GetReward(index)。取消不触发。</summary>
        public event Action<int> Completed;
        public event Action Cancelled;

        // 本轮运动数据：内部角度不按 360° 归零，用于累计圈数和检测跨格。
        // 只有写入 Transform 时取余；使用 double 降低多圈累计的精度损失。
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

            if (!IsValidStageCurve(m_AccelerationRhythm)) {
                m_AccelerationRhythm = AnimationCurve.Constant(0, 1, 1);
            }

            if (!IsValidStageCurve(m_DecelerationRhythm)) {
                m_DecelerationRhythm = AnimationCurve.Constant(0, 1, 1);
            }
        }

        // 配置入口：Inspector 已填写时，通常直接调用 TryStartSpin(index) 即可。
        /// <summary>配置顺时针编号的布局，不改变当前视觉角度。正在转动时不允许更换布局。</summary>
        public void Configure(Transform reel, int sectorsCount, float zeroAngle = 0) {
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

            if (rewards == null) {
                throw new ArgumentNullException(nameof(rewards));
            }

            var replacement = new List<WheelReward>(rewards);

            if (replacement.Count == 0) {
                throw new ArgumentException("At least one reward is required. Use ClearRewards for index-only mode.",
                    nameof(rewards));
            }

            if (!ValidateRewardList(replacement, out var error)) {
                throw new ArgumentException(error, nameof(rewards));
            }

            RequireMatchingSectorCount(replacement.Count);

            for (var index = 0; index < replacement.Count; index++) {
                replacement[index] = replacement[index].Copy();
            }

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

        /// <summary>按编号读取奖励配置；未配置奖励列表时返回 null，非法编号抛出异常。</summary>
        public WheelReward GetReward(int sectorIndex) {
            if (sectorIndex < 0 || sectorIndex >= SectorsCount) {
                throw new ArgumentOutOfRangeException(nameof(sectorIndex));
            }

            return HasRewards ? m_Rewards[sectorIndex] : null;
        }

        public bool ValidateRewards(out string error) => ValidateRewardList(m_Rewards, out error);

        private static bool ValidateRewardList(IReadOnlyList<WheelReward> rewards, out string error) {
            if (rewards != null) {
                for (var index = 0; index < rewards.Count; index++) {
                    var reward = rewards[index];

                    if (reward == null) {
                        error = $"第 {index} 格的奖励配置为空。";
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

            if (widths == null) {
                throw new ArgumentNullException(nameof(widths));
            }

            var replacement = new List<float>(widths);

            if (!ValidateSectorWidths(replacement, replacement.Count, out var error)) {
                throw new ArgumentException(error, nameof(widths));
            }

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
            if (m_CustomSectors && count != m_SectorWidths.Count) {
                throw new InvalidOperationException(
                    "Custom sector widths must match the sector count. ConfigureEqualSectors before changing the count.");
            }
        }

        // 仅消除允许范围内的浮点求和误差，保证圆周首尾完全闭合。
        private double SectorScale() {
            double sum = 0;

            for (var index = 0; index < m_SectorWidths.Count; index++) {
                sum += m_SectorWidths[index];
            }

            return 360.0 / sum;
        }

        public float GetSectorWidth(int sectorIndex) {
            ValidateSectorIndex(sectorIndex);
            return m_CustomSectors ? (float)(m_SectorWidths[sectorIndex] * SectorScale()) : 360f / SectorsCount;
        }

        /// <summary>从 0 号格中心沿顺时针量到指定格中心的角度，与 Reel 当前姿态无关。</summary>
        public float GetSectorCenterOffset(int sectorIndex) {
            ValidateSectorIndex(sectorIndex);

            if (!m_CustomSectors) {
                return sectorIndex * (360f / SectorsCount);
            }

            // 先累计 0..index-1 的整格宽度，再加目标半格、减 0 号半格，得到两个中心的间距。
            double offset = (m_SectorWidths[sectorIndex] - m_SectorWidths[0]) * 0.5;

            for (var index = 0; index < sectorIndex; index++) {
                offset += m_SectorWidths[index];
            }

            return (float)(offset * SectorScale());
        }

        private void ValidateSectorIndex(int sectorIndex) {
            ValidateLayout(SectorsCount, m_ZeroAngle);

            if (!ValidateSectors(out var error)) {
                throw new InvalidOperationException(error);
            }

            if (sectorIndex < 0 || sectorIndex >= SectorsCount) {
                throw new ArgumentOutOfRangeException(nameof(sectorIndex));
            }
        }

        /// <summary>offset 与随机范围以目标格宽度为单位，0 为中心，安全范围为 -0.49～0.49。</summary>
        public void ConfigureLanding(WheelLandingMode mode = WheelLandingMode.Center, float offset = 0,
            Vector2? randomRange = null) {
            RequireIdle();

            if (!Enum.IsDefined(typeof(WheelLandingMode), mode)) {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            var range = randomRange ?? new Vector2(-0.25f, 0.25f);

            if (!IsFinite(offset) || Mathf.Abs(offset) > 0.49f) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (!IsFinite(range.x) || !IsFinite(range.y) || range.x < -0.49f || range.y > 0.49f || range.x > range.y) {
                throw new ArgumentOutOfRangeException(nameof(randomRange));
            }

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

            if (!IsFinite(decelerationTime) || decelerationTime < 0.05f) {
                throw new ArgumentOutOfRangeException(nameof(decelerationTime));
            }

            if (accelerationRhythm != null && !IsValidStageCurve(accelerationRhythm)) {
                throw new ArgumentException("Invalid acceleration weights.", nameof(accelerationRhythm));
            }

            if (decelerationRhythm != null && !IsValidStageCurve(decelerationRhythm)) {
                throw new ArgumentException("Invalid braking weights.", nameof(decelerationRhythm));
            }

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

        /// <summary>指定扇区对准指针时的绝对本地 Z 角。offset 为一格的比例，正值沿顺时针偏向格内一侧。</summary>
        public float GetSectorAngle(int sectorIndex, float offset = 0) {
            ValidateSectorIndex(sectorIndex);

            if (!IsFinite(offset) || Mathf.Abs(offset) > 0.49f) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            // 奖格在美术上向顺时针偏多少度，圆盘就需多转相同的逆时针角度才能将它移到指针下。
            // 因此本地 Z 角增加；旋转方向只影响到达这个落点的路径。
            return Mathf.Repeat(m_ZeroAngle + GetSectorCenterOffset(sectorIndex) + offset * GetSectorWidth(sectorIndex),
                360f);
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

        /// <summary>按编号发起转动。无需返回值时可用；业务通常用 TryStartSpin 检查是否成功受理。</summary>
        public void StartSpin(int winSectorIndex, Action onComplete = null) => TryStartSpin(winSectorIndex, onComplete);

        /// <summary>编号模式入口。返回 false 表示忙碌、组件未启用或配置不可用；非法编号会抛出异常。</summary>
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

        // 每轮只从这里重置数据、缓存节奏；不会把圆盘吸附到某格，始终从当前姿态起转。
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

        // 保存本轮快照：后续播放和通知使用同一份结果，不从外部配置重新读取奖励。
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

            if (m_AccelerationTime > 0) {
                State = WheelState.Accelerating;
            } else {
                FinishAcceleration();
            }
        }

        private void FinishAcceleration() {
            m_Elapsed = 0;
            m_Speed = m_WaitingSpeed;

            if (TargetSectorIndex >= 0) {
                PlanMainStop();
            } else {
                State = WheelState.Spinning;
            }
        }

        // 规划停止路径，不修改稳定转速或减速时间。结果早到和晚到都复用此入口。
        private void PlanMainStop() {
            m_StopStartAngle = m_Angle;
            var overshoot = m_OutRotationTime > 0 ? m_OutRotationAngle : 0;
            var decelerationDistance = m_WaitingSpeed * m_DecelerationTime * (1 - m_DecelerationProfile.TotalIntegral);
            var current = (float)(m_Angle % 360);

            // 先求沿本轮转向抵达落点所需的不足一圈距离；启用回摆时，主运动先越过目标。
            var distance = Mathf.Repeat((m_LandingAngle - current) * m_ActiveDirection, 360f) + (double)overshoot;

            // 保持稳定转速和设定的减速时间。只延长匀速段去满足落点、整轮圈数与最少匀速时间。
            var minimumDistance = Math.Max(m_MinimumTurns * 360.0 - m_ForwardDistance,
                m_WaitingSpeed * Math.Max(0, m_MainRotationTime - m_CruiseElapsed) + decelerationDistance);

            if (distance < minimumDistance) {
                distance += Math.Ceiling((minimumDistance - distance) / 360.0) * 360.0;
            }

            // 总路程 = 匀速路程 + 固定减速路程；差额只用增加整圈和匀速时间补齐。
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
            if (!IsSpinning || deltaTime <= 0) {
                return;
            }

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

                        if (t < 1) {
                            return;
                        }

                        if (starting) {
                            FinishStarting();
                        } else {
                            CompleteSpin();
                            return;
                        }

                        break;
                    }
                    case WheelState.Accelerating: {
                        m_AccelerationProfile.Evaluate(t, out var speed, out var distance, out _);
                        m_Speed = (float)(m_WaitingSpeed * speed);
                        ApplyAngle(m_StageStartAngle + m_WaitingSpeed * duration * distance * m_ActiveDirection, true);

                        if (t < 1) {
                            return;
                        }

                        FinishAcceleration();
                        break;
                    }
                    case WheelState.Stopping:
                        m_Speed = m_WaitingSpeed;
                        m_CruiseElapsed += step;
                        ApplyAngle(m_StopStartAngle + m_WaitingSpeed * m_Elapsed * m_ActiveDirection, true);

                        if (t < 1) {
                            return;
                        }

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

                        if (t < 1) {
                            return;
                        }

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

        // 指针音效/拨动只关心有没有跨边界；一帧跨很多格也只反馈一次，避免高转速叠加音效。
        private bool CrossedSectorBoundary(double from, double to) {
            if (!m_CustomSectors) {
                var step = 360.0 / SectorsCount;
                return Math.Floor((from - m_ZeroAngle) / step + 0.5) != Math.Floor((to - m_ZeroAngle) / step + 0.5);
            }

            var scale = SectorScale();
            double edge = -m_SectorWidths[0] * 0.5;

            for (var index = 0; index < m_SectorWidths.Count; index++) {
                var boundary = m_ZeroAngle + edge * scale;

                if (Math.Floor((from - boundary) / 360) != Math.Floor((to - boundary) / 360)) {
                    return true;
                }

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

        // 只有这里产生停准结果；取消路径不会进入这里，也不会发出完成通知。
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
            Completed?.Invoke(result);

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

            if (wasSpinning) {
                WinReward = null;
            }

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
                if (curve == null || curve.length < 1) {
                    return false;
                }

                foreach (var key in curve.keys) {
                    if (!IsFinite(key.time) || !IsFinite(key.value)) {
                        return false;
                    }
                }

                double area = 0;

                for (var i = 0; i <= k_Segments; i++) {
                    var t = (float)i / k_Segments;
                    var weight = curve.Evaluate(t);

                    if (!IsFinite(weight) || weight < 0) {
                        return false;
                    }

                    if (i is > 1 and < k_Segments - 1) {
                        area += weight * (double)t * t * (1 - t) * (1 - t);
                    }
                }

                return area > 0;
            }

            // 预计算权重、一次积分（速度/进度）和二次积分（位移），避免逐帧数值积分带来帧率差异。
            public void Build(AnimationCurve curve) {
                const double k_Step = 1.0 / k_Segments;
                double area = 0;

                for (var i = 0; i <= k_Segments; i++) {
                    var t = i * k_Step;
                    var weight = curve?.Evaluate((float)t) ?? 1;

                    // 边界相邻采样也归零，避免分段线性近似让预摆/回摆端点出现微小加速度台阶。
                    m_Weights[i] = i is <= 1 or >= k_Segments - 1 ? 0 : weight * t * t * (1 - t) * (1 - t);

                    if (i > 0) {
                        area += (m_Weights[i - 1] + m_Weights[i]) * k_Step * 0.5;
                    }
                }

                m_Progress[0] = m_Integral[0] = 0;

                for (var i = 0; i <= k_Segments; i++) {
                    m_Weights[i] /= area;
                }

                for (var i = 1; i <= k_Segments; i++) {
                    m_Progress[i] = m_Progress[i - 1] + (m_Weights[i - 1] + m_Weights[i]) * k_Step * 0.5;
                    m_Integral[i] = m_Integral[i - 1] + m_Progress[i - 1] * k_Step
                                                      + (2 * m_Weights[i - 1] + m_Weights[i]) * k_Step * k_Step / 6;
                }
            }

            // t 为阶段时间比例。加速使用 progress 作速度、integral 作位移；
            // 回拉/回摆使用 progress 作位置、derivative 作速度，减速使用 1-progress 作剩余速度。
            public void Evaluate(double t, out double progress, out double integral, out double derivative) {
                switch (t) {
                    case >= 1:
                        progress = 1;
                        integral = TotalIntegral;
                        derivative = 0;
                        return;
                    case <= 0:
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