using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    [RequireComponent(typeof(RectTransform))]
    public partial class Marquee : MonoBehaviour {
        [Tooltip("可视区域（建议挂载 Mask 或 RectMask2D 以裁剪）")]
        public RectTransform Viewport;

        [Tooltip("内容样式模板：可包含 Image 或 Text；扩展片段使用各自的渲染器")]
        public RectTransform ContentTemplate;

        [Tooltip("跑马灯条目：可混排文本、图片与扩展片段")] public List<MarqueeItemData> Items = new();

        [Tooltip("滚动模式：Sequential 逐条轮播；Continuous 无缝连续滚动")]
        public MarqueeScrollMode ScrollMode = MarqueeScrollMode.Sequential;

        [Tooltip("滚动方向")] public MarqueeDirection Direction = MarqueeDirection.Left;

        [Tooltip("循环 或 单次（仅 Sequential 模式生效）")]
        public MarqueePlayMode PlayMode = MarqueePlayMode.Loop;

        [Tooltip("内容边缘与可视区域边缘的距离（仅在内容超出可视区域时有效）")] [Min(0f)]
        public float EdgeMargin = 10f;

        [Tooltip("内容尺寸未超过视口时是否居中显示（仅 Sequential 模式）")]
        public bool CenterWhenFit = true;

        [Tooltip("内容未超过视口时的展示时长（秒），仅当 centerWhenFit 启用时生效")] [Min(0.1f)]
        public float DisplayDurationWhenFit = 3f;

        [Tooltip("内容超过视口时，开始滚动前的停留时长（秒）")] [Min(0f)]
        public float DisplayDurationBeforeScroll = 1f;

        [Tooltip("滚动速度：每秒移动的像素数")] [Min(1f)] public float ScrollSpeed = 100f;

        [Tooltip("滚动缓动（仅 Sequential 模式生效；Continuous 为保证无缝始终匀速）")]
        public MarqueeEase Ease = MarqueeEase.Linear;

        [Tooltip("自定义缓动曲线（ease == Custom 时生效）：横轴 0→1 为进度，纵轴 0→1 为位移比例")]
        public AnimationCurve CustomCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("无缝连续滚动模式下，相邻条目之间的间距（像素）")] [Min(0f)]
        public float Spacing = 40f;

        [Tooltip("同一条目内，相邻片段（文本/图片/spine）之间的间距（像素）")] [Min(0f)]
        public float SegmentSpacing = 4f;

        [Tooltip("是否在 Start 时自动开始播放")] public bool PlayOnStart = true;

        [Tooltip("是否忽略 Time.timeScale 的影响")] public bool IgnoreTimeScale = false;

        // ---- 事件回调 ----
        /// <summary>开始展示某条目时触发（item, index）。Continuous 模式下不触发。</summary>
        public event Action<MarqueeItemData, int> OnItemStart;

        /// <summary>某条目展示/滚动完成时触发（item, index）。Continuous 模式下不触发。</summary>
        public event Action<MarqueeItemData, int> OnItemComplete;

        /// <summary>完成一轮（遍历完全部条目）时触发。仅 Sequential + Loop 有意义。</summary>
        public event Action OnLoopComplete;

        /// <summary>所有条目播放结束（Once 模式或无可播放条目）时触发。</summary>
        public event Action OnAllComplete;

        /// <summary>条目被点击时触发（item, index）。需内容 raycastTarget 开启且场景含 GraphicRaycaster。</summary>
        public event Action<MarqueeItemData, int> OnItemClicked;

        // ---- 内部状态 ----
        private MarqueeContentLayout m_Content;
        private MarqueeContentLayout.Unit m_SeqUnit;
        private readonly List<MarqueeContentLayout.Unit> m_ActiveUnits = new();
        private readonly List<MarqueeItemData> m_Ring = new();
        private readonly List<int> m_RingSource = new();
        private readonly MarqueeRingLayout m_RingLayout = new();
        private readonly List<MarqueeRingLayout.Placement> m_Placements = new();
        private readonly Dictionary<long, MarqueeContentLayout.Unit> m_VisibleUnits = new();

        private List<MarqueeItemData> m_Items;
        private readonly List<int> m_Remaining = new(); // 剩余出现次数副本，避免污染用户数据

        private readonly MarqueePlayback m_Playback = new();
        private int RunId => m_Playback.Version;
        private bool m_TemplateWasActive;
        private bool m_ContentInitialized;
        private bool m_IsOneShot;
        private bool m_ResumeOnEnable;
        private int m_ResumeIndex;
        private bool m_ResumePaused;
        private bool m_RaycasterChecked;

        // 本次 Sequential 播放是否强制 Once 语义（忽略 playMode=Loop）；PlaySequenceOnceAsync 使用
        private bool m_ForceOnce;

        public bool IsPlaying => m_Playback.IsRunning;

        public bool IsPaused {
            get => m_Playback.IsPaused;
            private set => m_Playback.IsPaused = value;
        }

        /// <summary>当前正在展示的条目索引（Sequential 模式有效）。</summary>
        public int CurrentIndex { get; private set; } = -1;

        private void Awake() {
            if (Viewport == null) {
                Viewport = GetComponent<RectTransform>();
            }

            if (ContentTemplate == null && Viewport != null && Viewport.childCount > 0) {
                ContentTemplate = Viewport.GetChild(0) as RectTransform;
            }

            if (ContentTemplate == null) {
                Debug.LogError("Marquee: contentTemplate 未设置，且 viewport 下无子节点。", this);
                return;
            }

            SetupContent();
        }

        private void Start() {
            if (ContentTemplate == null) {
                return;
            }

            // 仅当尚未被外部 SetItems 初始化时，才从 Inspector 的 items 字段初始化，
            // 避免覆盖用户在 Start 之前通过 API 设置的数据。
            if (m_Items == null) {
                SetItemsInternal(Items is { Count: > 0 } ? new List<MarqueeItemData>(Items)
                    : new List<MarqueeItemData>());
            }

            if (PlayOnStart && !IsPlaying && m_Items.Count > 0) {
                Play();
            }
        }

        private void OnEnable() {
            if (m_ResumeOnEnable) {
                m_ResumeOnEnable = false;
                ResumePlayback();
            }
        }

        private void OnDisable() {
            // 仅持续播放（非一次性）才记录恢复点；禁用 GameObject 时 Unity 会自动停协程，
            // 但仅禁用组件(enabled=false)时不会，因此必须显式 CancelRun 以保证两种路径都停止。
            if (IsPlaying && !m_IsOneShot) {
                m_ResumeOnEnable = true;
                m_ResumeIndex = CurrentIndex;
                m_ResumePaused = IsPaused;
            }

            CancelRun();
        }

        private void OnDestroy() {
            CancelRun();
            m_Content?.Dispose();

            if (m_ContentInitialized && ContentTemplate != null) {
                ContentTemplate.gameObject.SetActive(m_TemplateWasActive);
            }
        }

        private void Update() {
            m_Content?.SetTimeMode(IgnoreTimeScale);

            // CancellationToken 可以从任意线程取消，只在 Unity 主线程操作协程和视图。
            if (m_Playback.CancellationRequested) {
                Stop();
            }
        }

        private void SetupContent() {
            if (Viewport == null || ContentTemplate == null) {
                return;
            }

            var renderers = new List<IMarqueeSegmentRenderer>();
            var image = ContentTemplate.GetComponentInChildren<Image>(true);
            var text = ContentTemplate.GetComponentInChildren<TextMeshProUGUI>(true);

            if (image != null) {
                renderers.Add(new BuiltinImageRenderer(image.rectTransform));
            }

            if (text != null) {
                renderers.Add(new BuiltinTextRenderer(text.rectTransform));
            }

            m_TemplateWasActive = ContentTemplate.gameObject.activeSelf;
            m_ContentInitialized = true;
            ContentTemplate.gameObject.SetActive(false);
            m_Content = new MarqueeContentLayout(this, Viewport, renderers);
            m_SeqUnit = m_Content.SequentialUnit;
        }

        // ----------------------------------------------------------------
        // 公共 API
        // ----------------------------------------------------------------

        /// <summary>开始播放（从 startIndex 开始，仅 Sequential 模式生效）。</summary>
        public void Play(int startIndex = 0) {
            if (!ValidateForPlay()) {
                return;
            }

            WarnIfNoRaycaster();
            int runId = BeginRun();

            if (runId != RunId) {
                return;
            }

            m_IsOneShot = false;
            m_ForceOnce = false;

            ResetRemaining();
            CurrentIndex = Mathf.Clamp(startIndex, 0, m_Items.Count - 1) - 1;
            StartPlayback(RunConfigured(runId), runId);
        }

        /// <summary>停止播放并返回当前索引。</summary>
        public int Stop() {
            int index = CurrentIndex;
            m_ResumeOnEnable = false;
            CancelRun();
            return index;
        }

        /// <summary>暂停停留计时与滚动；片段动画继续播放。</summary>
        public void Pause() {
            if (!IsPlaying || IsPaused) {
                return;
            }

            IsPaused = true;
        }

        /// <summary>取消暂停。</summary>
        public void Unpause() {
            if (!IsPlaying || !IsPaused) {
                return;
            }

            IsPaused = false;
        }

        /// <summary>请求重建布局，保留剩余次数；一次性播放在后续布局更新中应用配置。</summary>
        public void Refresh() {
            m_LayoutDirty = true;
        }

        private bool m_LayoutDirty;

        private void ResumePlayback() {
            int runId = BeginRun();

            if (runId != RunId) {
                return;
            }

            IsPaused = m_ResumePaused;
            m_IsOneShot = false;
            m_ForceOnce = false;
            int index = Mathf.Max(0, m_ResumeIndex);

            if (ScrollMode == MarqueeScrollMode.Sequential && index < m_Remaining.Count
                                                           && m_Items[index]?.Cycles > 0) {
                m_Remaining[index]++;
            }

            CurrentIndex = index - 1;
            StartPlayback(RunConfigured(runId), runId);
        }

        /// <summary>设置条目并可选是否立即开始播放。</summary>
        public void SetItems(List<MarqueeItemData> newItems, bool startPlay = true) {
            int expectedId = RunId + 1;
            Stop();

            if (RunId != expectedId) {
                return; // 取消回调中的新请求优先。
            }

            m_Content?.ClearUnit(m_SeqUnit);
            m_SeqUnit?.Root.gameObject.SetActive(false);
            ReleaseAllActiveUnits();
            m_Ring.Clear();
            CurrentIndex = -1;
            SetItemsInternal(newItems != null ? new List<MarqueeItemData>(newItems) : new List<MarqueeItemData>());

            if (startPlay && m_Items.Count > 0) {
                Play();
            }
        }

        /// <summary>在当前基础上追加新数据（不会打断正在进行的播放）。</summary>
        public void AddItem(MarqueeItemData newItem) {
            EnsureItemBuffers();

            if (newItem != null) {
                m_Items.Add(newItem);
                m_Remaining.Add(newItem.Cycles);
                m_LayoutDirty = true;
            }
        }

        /// <summary>在当前基础上追加新数据（不会打断正在进行的播放）。</summary>
        public void AddItems(List<MarqueeItemData> newItems) {
            EnsureItemBuffers();

            if (newItems is { Count: > 0 }) {
                foreach (MarqueeItemData item in newItems) {
                    m_Items.Add(item);
                    m_Remaining.Add(item?.Cycles ?? 0);
                    m_LayoutDirty = true;
                }
            }
        }

        /// <summary>播放单条文字（一次性），正常完成时回调 onComplete（被打断不回调）。</summary>
        public void PlayOnce(string text, Action onComplete = null) {
            PlayOnce(MarqueeItemData.Text(text), onComplete);
        }

        /// <summary>播放单条内容（一次性），完成时回调 onComplete（被新的播放/停止打断则不回调）。</summary>
        public void PlayOnce(MarqueeItemData item, Action onComplete = null) {
            if (item == null || !ValidateForPlay(requireItems: false)) {
                return;
            }

            WarnIfNoRaycaster();
            int runId = BeginRun();

            if (runId != RunId) {
                return;
            }

            m_IsOneShot = true;
            m_ForceOnce = false;

            ReleaseContinuousUnits();
            m_SeqUnit.Root.gameObject.SetActive(true);
            StartPlayback(RunOnce(runId, item, onComplete), runId);
        }

        /// <summary>播放单条文字（一次性）的 async 版本：await 直到播放完成。语义同 <see cref="PlayOnceAsync(MarqueeItemData, CancellationToken)"/>。</summary>
        public Awaitable PlayOnceAsync(string text, CancellationToken cancellationToken = default) {
            return PlayOnceAsync(MarqueeItemData.Text(text), cancellationToken);
        }

        /// <summary>
        /// 播放单条内容（一次性）的 async 版本：await 直到该条播放完成。
        /// 被新的播放 / <see cref="Stop"/> / 组件禁用或销毁打断，或 <paramref name="cancellationToken"/> 取消时，
        /// await 处会抛出 <see cref="OperationCanceledException"/>（可用 try/catch 处理）。
        /// </summary>
        /// <example><code>await marquee.PlayOnceAsync(item); DoNext();</code></example>
        public Awaitable PlayOnceAsync(MarqueeItemData item, CancellationToken cancellationToken = default) {
            if (item == null || !ValidateForPlay(requireItems: false) || cancellationToken.IsCancellationRequested) {
                return CanceledAwaitable();
            }

            WarnIfNoRaycaster();
            int runId = BeginRun();

            if (runId != RunId) {
                return CanceledAwaitable();
            }

            m_IsOneShot = true;
            m_ForceOnce = false;
            var acs = new AwaitableCompletionSource();
            Awaitable result = acs.Awaitable;
            RegisterPendingOnce(acs, cancellationToken); // 须在 BeginRun 之后：BeginRun 已取消上一个挂起的 await

            ReleaseContinuousUnits();
            m_SeqUnit.Root.gameObject.SetActive(true);
            StartPlayback(RunOnce(runId, item, null), runId);
            return result;
        }

        /// <summary>
        /// 以 Once 语义播放整个条目序列（忽略 <see cref="PlayMode"/> 的 Loop）的 async 版本：
        /// await 直到全部条目播放完毕（等价于等待 <see cref="OnAllComplete"/>）。仅 Sequential 模式有意义。
        /// 被打断或 <paramref name="cancellationToken"/> 取消时，await 处抛出 <see cref="OperationCanceledException"/>。
        /// </summary>
        public Awaitable PlaySequenceOnceAsync(int startIndex = 0, CancellationToken cancellationToken = default) {
            if (ScrollMode == MarqueeScrollMode.Continuous) {
                Debug.LogWarning("Marquee: PlaySequenceOnceAsync 仅适用于 Sequential 模式，Continuous 无自然终点。已忽略。", this);
                return CompletedAwaitable();
            }

            if (!ValidateForPlay() || cancellationToken.IsCancellationRequested) {
                return CanceledAwaitable();
            }

            WarnIfNoRaycaster();
            int runId = BeginRun();

            if (runId != RunId) {
                return CanceledAwaitable();
            }

            m_IsOneShot = true; // 一次性序列，被禁用打断后不自动恢复
            m_ForceOnce = true; // 强制 Once：即使 playMode 为 Loop 也会自然结束
            var acs = new AwaitableCompletionSource();
            Awaitable result = acs.Awaitable;
            RegisterPendingOnce(acs, cancellationToken);

            ResetRemaining();
            CurrentIndex = Mathf.Clamp(startIndex, 0, m_Items.Count - 1) - 1;
            StartPlayback(RunSequential(runId), runId);
            return result;
        }

        // 立即完成 / 立即取消的 Awaitable 工厂（先取 Awaitable 引用再置状态，避免持有已完成的池化对象）。
        private static Awaitable CompletedAwaitable() {
            var acs = new AwaitableCompletionSource();
            Awaitable awaitable = acs.Awaitable;
            acs.SetResult();
            return awaitable;
        }

        private static Awaitable CanceledAwaitable() {
            var acs = new AwaitableCompletionSource();
            Awaitable awaitable = acs.Awaitable;
            acs.SetCanceled();
            return awaitable;
        }

        // 由 MarqueeClickRelay 调用
        internal void NotifyItemClicked(MarqueeItemData item, int index) {
            OnItemClicked?.Invoke(item, index);
        }

        // ----------------------------------------------------------------
        // 运行控制
        // ----------------------------------------------------------------

        private bool ValidateForPlay(bool requireItems = true) {
            if (!isActiveAndEnabled) {
                Debug.LogWarning("Marquee: 组件未激活，无法开始播放。", this);
                return false;
            }

            if (Viewport == null || m_SeqUnit == null) {
                Debug.LogError("Marquee: viewport 或 content 未设置。", this);
                return false;
            }

            if (requireItems && (m_Items == null || m_Items.Count == 0)) {
                Debug.LogError("Marquee: 条目列表为空。", this);
                return false;
            }

            return true;
        }

        private int BeginRun() => m_Playback.Begin();

        private void StartPlayback(IEnumerator routine, int runId) {
            m_Playback.Start(this, routine, runId, () => {
                m_Content?.ClearUnit(m_SeqUnit);
                ReleaseAllActiveUnits();
            });
        }

        private void CancelRun() => m_Playback.Cancel(this);

        private void RegisterPendingOnce(AwaitableCompletionSource completion, CancellationToken token) {
            m_Playback.Register(completion, token);
        }

        private void FinishRun(int runId, Action onComplete = null) => m_Playback.Finish(runId, onComplete);

        private float DeltaTime() {
            return IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        private void SetItemsInternal(List<MarqueeItemData> newItems) {
            m_Items = newItems;
            ResetRemaining();
        }

        private void EnsureItemBuffers() {
            m_Items ??= new List<MarqueeItemData>();

            if (m_Remaining.Count != m_Items.Count) {
                ResetRemaining();
            }
        }

        private void ResetRemaining() {
            m_Remaining.Clear();

            if (m_Items == null) {
                return;
            }

            foreach (MarqueeItemData item in m_Items) {
                m_Remaining.Add(item?.Cycles ?? 0);
            }
        }

        private void WarnIfNoRaycaster() {
            if (m_RaycasterChecked) {
                return;
            }

            m_RaycasterChecked = true;

            if (OnItemClicked != null && GetComponentInParent<GraphicRaycaster>() == null) {
                Debug.LogWarning("Marquee: 已订阅 OnItemClicked，但所在 Canvas 未找到 GraphicRaycaster，点击事件不会触发。", this);
            }
        }

        // ----------------------------------------------------------------
        // Sequential 逐条轮播
        // ----------------------------------------------------------------

        private IEnumerator RunConfigured(int runId) {
            while (runId == RunId && m_Playback.IsRunning) {
                yield return ScrollMode == MarqueeScrollMode.Continuous
                    ? RunContinuous(runId) : RunSequential(runId);
            }
        }

        private IEnumerator RunSequential(int runId) {
            ReleaseContinuousUnits();
            m_SeqUnit?.Root.gameObject.SetActive(true);
            bool naturalEnd = false;

            while (runId == RunId) {
                if (IsPaused) {
                    yield return null;
                    continue;
                }

                if (!m_IsOneShot && ScrollMode != MarqueeScrollMode.Sequential) {
                    yield break;
                }

                bool loop = !m_ForceOnce && PlayMode == MarqueePlayMode.Loop;
                int next = MarqueeMath.GetNextPlayableIndex(m_Remaining, CurrentIndex, loop, out bool loopCompleted);

                if (next < 0) {
                    naturalEnd = true;
                    break;
                }

                if (loopCompleted) {
                    OnLoopComplete?.Invoke();

                    if (runId != RunId) {
                        yield break;
                    }
                }

                CurrentIndex = next;
                MarqueeItemData item = m_Items[next];

                if (m_Remaining[next] > 0) {
                    m_Remaining[next]--;
                }

                Vector2 size = m_Content.Bind(m_SeqUnit, item, next, SegmentSpacing, IgnoreTimeScale);

                if (size.x <= 0f && size.y <= 0f) {
                    m_Remaining[next] = 0; // 无有效内容，标记跳过
                    yield return null;
                    continue;
                }

                OnItemStart?.Invoke(item, next);

                if (runId != RunId) {
                    yield break;
                }

                yield return MoveSequentialItem(size);

                if (runId != RunId || (!m_IsOneShot && ScrollMode != MarqueeScrollMode.Sequential)) {
                    yield break;
                }

                OnItemComplete?.Invoke(item, next);

                // 零停留、零尺寸等极端配置也不能在同一帧无限遍历循环列表。
                yield return null;
            }

            FinishRun(runId, naturalEnd ? () => OnAllComplete?.Invoke() : null);
        }

        private IEnumerator RunOnce(int runId, MarqueeItemData item, Action onComplete) {
            Vector2 size = m_Content.Bind(m_SeqUnit, item, 0, SegmentSpacing, IgnoreTimeScale);

            if (size.x > 0f || size.y > 0f) {
                yield return MoveSequentialItem(size);
            }

            FinishRun(runId, onComplete);
        }

        private IEnumerator MoveSequentialItem(Vector2 contentSize) {
            yield return EnsureViewportReady();
            var holdElapsed = 0f;
            var progress = 0f;
            float segmentSpacing = SegmentSpacing;
            m_LayoutDirty = false;

            while (true) {
                if (!m_IsOneShot && ScrollMode != MarqueeScrollMode.Sequential) {
                    yield break;
                }

                if (m_LayoutDirty || !Mathf.Approximately(segmentSpacing, SegmentSpacing)) {
                    m_LayoutDirty = false;
                    contentSize = m_Content.Bind(m_SeqUnit, m_SeqUnit.Item, m_SeqUnit.SourceIndex, SegmentSpacing,
                        IgnoreTimeScale);
                    segmentSpacing = SegmentSpacing;
                }

                Vector2 viewportSize = Viewport.rect.size;
                bool fit = CenterWhenFit && !MarqueeMath.IsOverflow(Direction, viewportSize, contentSize);
                MarqueeMath.ComputeScrollPositions(Direction, viewportSize, contentSize, EdgeMargin,
                    out Vector2 start, out Vector2 end);
                float holdDuration = fit ? DisplayDurationWhenFit : DisplayDurationBeforeScroll;
                float duration = Vector2.Distance(start, end) / Mathf.Max(1f, ScrollSpeed);

                if (!IsPaused) {
                    if (holdElapsed < Mathf.Max(0f, holdDuration)) {
                        holdElapsed += DeltaTime();
                    } else if (fit) {
                        yield break;
                    } else {
                        progress = duration > 0f ? Mathf.Min(1f, progress + DeltaTime() / duration) : 1f;
                    }
                }

                float eased = Ease == MarqueeEase.Custom && CustomCurve != null
                    ? CustomCurve.Evaluate(progress) : MarqueeMath.Evaluate(Ease, progress);
                m_SeqUnit.Root.anchoredPosition = fit ? Vector2.zero : Vector2.LerpUnclamped(start, end, eased);

                if (!fit && progress >= 1f) {
                    m_SeqUnit.Root.anchoredPosition = end;
                    yield break;
                }

                yield return null;
            }
        }

        // ----------------------------------------------------------------
        // Continuous 的逻辑进度独立于显示单元：跨过未实例化的条目也不会丢失位移。
        // 布局只返回可见范围及入口待命单元，视图按 occurrence 复用，避免每帧重新绑定动画。
        // ----------------------------------------------------------------

        private IEnumerator RunContinuous(int runId) {
            m_SeqUnit?.Root.gameObject.SetActive(false);
            yield return EnsureViewportReady();

            if (!BuildRing()) {
                FinishRun(runId, () => OnAllComplete?.Invoke());
                yield break;
            }

            MarqueeDirection direction = Direction;
            float spacing = Spacing;
            float segmentSpacing = SegmentSpacing;
            m_LayoutDirty = false;

            while (runId == RunId) {
                if (ScrollMode != MarqueeScrollMode.Continuous) {
                    yield break;
                }

                if (m_LayoutDirty || direction != Direction || !Mathf.Approximately(spacing, Spacing)
                    || !Mathf.Approximately(segmentSpacing, SegmentSpacing)) {
                    m_LayoutDirty = false;

                    if (!BuildRing()) {
                        FinishRun(runId, () => OnAllComplete?.Invoke());
                        yield break;
                    }

                    direction = Direction;
                    spacing = Spacing;
                    segmentSpacing = SegmentSpacing;
                }

                if (!IsPaused) {
                    m_RingLayout.Advance(DeltaTime() * Mathf.Max(1f, ScrollSpeed));
                }

                LayoutRing(MarqueeMath.AxisSize(Direction, Viewport.rect.size));
                yield return null;
            }
        }

        private bool BuildRing() {
            ReleaseAllActiveUnits();
            m_Ring.Clear();
            m_RingSource.Clear();
            var lengths = new List<float>();

            for (var i = 0; i < m_Items.Count; i++) {
                MarqueeItemData item = m_Items[i];

                if (item == null || item.Cycles == 0) {
                    continue;
                }

                Vector2 size = m_Content.Measure(item, SegmentSpacing, IgnoreTimeScale);
                float axis = MarqueeMath.AxisSize(Direction, size);

                if (axis <= 0f) {
                    continue;
                }

                m_Ring.Add(item);
                m_RingSource.Add(i);
                lengths.Add(axis);
            }

            m_RingLayout.Reset(lengths, Spacing);
            return lengths.Count > 0;
        }

        private void LayoutRing(float viewportAxis) {
            m_RingLayout.GetPlacements(viewportAxis, m_Placements);

            // 回收已经不在窗口中的 occurrence；相同数据可以在一屏中出现多次。
            for (int i = m_ActiveUnits.Count - 1; i >= 0; i--) {
                var unit = m_ActiveUnits[i];
                bool keep = m_Placements.Count > 0 && unit.Occurrence >= m_Placements[0].Occurrence
                                                   && unit.Occurrence <= m_Placements[^1].Occurrence;

                if (keep) {
                    continue;
                }

                m_VisibleUnits.Remove(unit.Occurrence);
                m_ActiveUnits.RemoveAt(i);
                m_Content.ReleaseUnit(unit);
            }

            float flow = MarqueeMath.FlowSign(Direction);
            bool horizontal = MarqueeMath.IsHorizontal(Direction);

            foreach (var placement in m_Placements) {
                if (!m_VisibleUnits.TryGetValue(placement.Occurrence, out var unit)) {
                    unit = m_Content.AcquireUnit();
                    
                    // 先登记所有权，再调用扩展代码；失败由播放会话统一清理。
                    unit.Occurrence = placement.Occurrence;
                    m_ActiveUnits.Add(unit);
                    m_VisibleUnits.Add(unit.Occurrence, unit);
                    m_Content.Bind(unit, m_Ring[placement.Index], m_RingSource[placement.Index],
                        SegmentSpacing, IgnoreTimeScale);
                }

                float center = flow * placement.Center;
                unit.Root.anchoredPosition = horizontal ? new Vector2(center, 0f) : new Vector2(0f, center);
            }
        }

        private void ReleaseAllActiveUnits() {
            foreach (var unit in m_ActiveUnits) m_Content.ReleaseUnit(unit);
            m_ActiveUnits.Clear();
            m_VisibleUnits.Clear();
        }

        // 切换到逐条播放时只回收单元，轨道容器由内容布局对象统一持有。
        private void ReleaseContinuousUnits() => ReleaseAllActiveUnits();

        private IEnumerator EnsureViewportReady() {
            const int k_MaxFrames = 120;
            var frames = 0;

            // 等待滚动方向对应的视口轴向尺寸完成布局（Left/Right 看宽、Up/Down 看高），
            // 避免首条在该轴尺寸尚为 0 时按未就绪尺寸计算滚动/居中布局。
            while (Viewport != null
                   && MarqueeMath.AxisSize(Direction, Viewport.rect.size) <= 0f
                   && frames < k_MaxFrames) {
                frames++;
                yield return null;
            }
        }
    }
}