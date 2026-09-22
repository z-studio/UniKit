using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    [RequireComponent(typeof(RectTransform))]
    public class UIMarquee : MonoBehaviour {
        [Tooltip("可视区域（建议挂载 Mask 或 RectMask2D 以裁剪）")]
        public RectTransform Viewport;

        [Tooltip("内容模板：需包含 Image 与 Text，跑马灯会复用并设置其内容")]
        public RectTransform ContentTemplate;

        [Tooltip("跑马灯条目：图片或文字")]
        public List<MarqueeItemData> Items = new();

        [Tooltip("滚动模式：Sequential 逐条轮播；Continuous 无缝连续滚动")]
        public EMarqueeScrollMode ScrollMode = EMarqueeScrollMode.Sequential;

        [Tooltip("滚动方向")]
        public EMarqueeDirection Direction = EMarqueeDirection.Left;

        [Tooltip("循环 或 单次（仅 Sequential 模式生效）")]
        public EMarqueePlayMode PlayMode = EMarqueePlayMode.Loop;

        [Tooltip("内容边缘与可视区域边缘的距离（仅在内容超出可视区域时有效）")]
        [Min(0f)]
        public float EdgeMargin = 10f;

        [Tooltip("内容尺寸未超过视口时是否居中显示（仅 Sequential 模式）")]
        public bool CenterWhenFit = true;

        [Tooltip("内容未超过视口时的展示时长（秒），仅当 centerWhenFit 启用时生效")]
        [Min(0.1f)]
        public float DisplayDurationWhenFit = 3f;

        [Tooltip("内容超过视口时，开始滚动前的停留时长（秒）")]
        [Min(0f)]
        public float DisplayDurationBeforeScroll = 1f;

        [Tooltip("滚动速度：每秒移动的像素数")]
        [Min(1f)]
        public float ScrollSpeed = 100f;

        [Tooltip("滚动缓动（仅 Sequential 模式生效；Continuous 为保证无缝始终匀速）")]
        public EMarqueeEase Ease = EMarqueeEase.Linear;

        [Tooltip("自定义缓动曲线（ease == Custom 时生效）：横轴 0→1 为进度，纵轴 0→1 为位移比例")]
        public AnimationCurve CustomCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("无缝连续滚动模式下，相邻条目之间的间距（像素）")]
        [Min(0f)]
        public float Spacing = 40f;

        [Tooltip("同一条目内，相邻片段（文本/图片/spine）之间的间距（像素）")]
        [Min(0f)]
        public float SegmentSpacing = 4f;

        [Tooltip("是否在 Start 时自动开始播放")]
        public bool PlayOnStart = true;

        [Tooltip("是否忽略 Time.timeScale 的影响")]
        public bool IgnoreTimeScale = false;

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
        // 片段样式模板（来自 contentTemplate，运行时仅作克隆来源，不直接显示）
        private RectTransform m_TextTemplate;
        private RectTransform m_ImageTemplate;

        // 片段渲染器：内置（文本/图片，绑定本实例模板）+ 外部注册（spine 等）
        private readonly List<IMarqueeSegmentRenderer> m_Renderers = new();
        // 片段视图对象池：renderer.Key -> 空闲视图
        private readonly Dictionary<string, Stack<RectTransform>> m_SegmentPools = new();
        private RectTransform m_PoolParent; // 隐藏容器：存放空闲片段视图与空闲单元

        private MarqueeUnit m_SeqUnit;     // Sequential / Once 的显示单元
        private MarqueeUnit m_MeasureUnit; // 仅用于测量条目尺寸（隐藏，不参与显示）

        private RectTransform m_Track;

        // Continuous 环形复用：只铺满视口所需的单元，滚出流出边的单元绕回入场端并换下一条数据
        private readonly List<MarqueeUnit> m_ActiveUnits = new(); // 按出场顺序：[0] 最先滚出，[^1] 最靠入场端
        private readonly Stack<MarqueeUnit> m_UnitPool = new();   // 空闲待复用单元（Refresh 复用，减少 GC）
        private List<MarqueeItemData> m_Ring;                     // 参与滚动的有效条目
        private readonly List<int> m_RingSource = new();          // m_Ring[i] 对应的原始 m_Items 下标（点击回调用，保证与 Sequential 一致）
        private float m_RingLength;                               // 一份序列的轴向总长（含 spacing），用于 clamp
        private int m_LastDataIndex;                              // 最近分配给单元的数据下标

        private int m_CurrentIndex = -1;
        private List<MarqueeItemData> m_Items;
        private readonly List<int> m_Remaining = new(); // 剩余出现次数副本，避免污染用户数据
        private Coroutine m_Routine;
        private int m_RunId;
        private bool m_IsOneShot;
        private bool m_ResumeOnEnable;
        private int m_ResumeIndex;
        private bool m_RaycasterChecked;

        // 本次 Sequential 播放是否强制 Once 语义（忽略 playMode=Loop）；PlaySequenceOnceAsync 使用
        private bool m_ForceOnce;
        
        // 当前一次性 await（PlayOnceAsync / PlaySequenceOnceAsync）的完成源；null 表示无挂起的等待
        private AwaitableCompletionSource m_PendingOnce;
        
        // 外部取消令牌在 m_PendingOnce 上的注册，随该等待的完成/取消一并释放
        private CancellationTokenRegistration m_OnceCtReg;

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        /// <summary>当前正在展示的条目索引（Sequential 模式有效）。</summary>
        public int CurrentIndex => m_CurrentIndex;

        private void Awake() {
            if (Viewport == null) {
                Viewport = GetComponent<RectTransform>();
            }

            if (ContentTemplate == null && Viewport != null && Viewport.childCount > 0) {
                ContentTemplate = Viewport.GetChild(0) as RectTransform;
            }

            if (ContentTemplate == null) {
                Debug.LogError("UIMarquee: contentTemplate 未设置，且 viewport 下无子节点。", this);
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
                SetItemsInternal(Items is { Count: > 0 } ? new List<MarqueeItemData>(Items) : new List<MarqueeItemData>());
            }

            if (PlayOnStart && !IsPlaying && m_Items.Count > 0) {
                Play();
            }
        }

        private void OnEnable() {
            if (m_ResumeOnEnable) {
                m_ResumeOnEnable = false;
                Play(m_ResumeIndex < 0 ? 0 : m_ResumeIndex);
            }
        }

        private void OnDisable() {
            // 仅持续播放（非一次性）才记录恢复点；禁用 GameObject 时 Unity 会自动停协程，
            // 但仅禁用组件(enabled=false)时不会，因此必须显式 CancelRun 以保证两种路径都停止。
            if (IsPlaying && !m_IsOneShot) {
                m_ResumeOnEnable = true;
                m_ResumeIndex = m_CurrentIndex;
            }

            CancelRun();
            IsPlaying = false;
            IsPaused = false;
        }

        private void OnDestroy() {
            CancelRun();
        }

        private void SetupContent() {
            if (Viewport == null || ContentTemplate == null) {
                return;
            }

            // 从 contentTemplate 提取文本/图片的样式模板（字体、颜色、Image 属性等）
            var img = ContentTemplate.GetComponentInChildren<Image>(true);
            var txt = ContentTemplate.GetComponentInChildren<TextMeshProUGUI>(true);

            if (txt != null) {
                txt.textWrappingMode = TextWrappingModes.NoWrap;
                txt.overflowMode = TextOverflowModes.Overflow;
                m_TextTemplate = txt.rectTransform;
            }

            if (img != null) {
                m_ImageTemplate = img.rectTransform;
            }

            if (m_TextTemplate == null && m_ImageTemplate == null) {
                Debug.LogError("UIMarquee: contentTemplate 下未找到 Image 或 Text 作为样式模板，请确保至少有一个。", this);
            }

            // contentTemplate 仅作模板来源，运行时不直接显示
            ContentTemplate.gameObject.SetActive(false);

            EnsurePoolParent();

            // 内置渲染器（文本/图片）+ 隐藏的显示/测量单元
            m_Renderers.Clear();

            if (m_TextTemplate != null) {
                m_Renderers.Add(new BuiltinTextRenderer(m_TextTemplate));
            }

            if (m_ImageTemplate != null) {
                m_Renderers.Add(new BuiltinImageRenderer(m_ImageTemplate));
            }

            m_SeqUnit = CreateUnit("MarqueeContent", Viewport);
            m_SeqUnit.root.gameObject.SetActive(false);
            m_MeasureUnit = CreateUnit("MarqueeMeasure", m_PoolParent);
        }

        private void EnsurePoolParent() {
            if (m_PoolParent != null) {
                return;
            }

            var go = new GameObject("MarqueePool", typeof(RectTransform));
            m_PoolParent = (RectTransform)go.transform;
            m_PoolParent.SetParent(Viewport, false);
            AlignCenter(m_PoolParent);
            m_PoolParent.sizeDelta = Vector2.zero;
            go.SetActive(false);
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
            m_IsOneShot = false;
            m_ForceOnce = false;

            if (ScrollMode == EMarqueeScrollMode.Continuous) {
                m_Routine = StartCoroutine(RunContinuous(runId));
            } else {
                ResetRemaining();
                m_CurrentIndex = Mathf.Clamp(startIndex, 0, m_Items.Count - 1) - 1;
                m_Routine = StartCoroutine(RunSequential(runId));
            }
        }

        /// <summary>停止播放并返回当前索引。</summary>
        public int Stop() {
            CancelRun();
            IsPlaying = false;
            IsPaused = false;
            m_ResumeOnEnable = false;
            return m_CurrentIndex;
        }

        /// <summary>暂停播放（停留与滚动都会冻结）。</summary>
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

        /// <summary>使用当前配置重新开始播放（运行时修改 direction/spacing 后调用以即时生效）。</summary>
        public void Refresh() {
            if (!IsPlaying) {
                return;
            }

            Play(ScrollMode == EMarqueeScrollMode.Continuous ? 0 : Mathf.Max(0, m_CurrentIndex));
        }

        /// <summary>设置条目并可选是否立即开始播放。</summary>
        public void SetItems(List<MarqueeItemData> newItems, bool startPlay = true) {
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
            }
        }

        /// <summary>在当前基础上追加新数据（不会打断正在进行的播放）。</summary>
        public void AddItems(List<MarqueeItemData> newItems) {
            EnsureItemBuffers();

            if (newItems is { Count: > 0 }) {
                foreach (MarqueeItemData item in newItems) {
                    m_Items.Add(item);
                    m_Remaining.Add(item?.Cycles ?? 0);
                }
            }
        }

        /// <summary>播放单条文字（一次性），完成或被打断时回调 onComplete。</summary>
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
            m_IsOneShot = true;
            m_ForceOnce = false;

            DestroyTrack();
            m_SeqUnit.root.gameObject.SetActive(true);
            m_Routine = StartCoroutine(RunOnce(runId, item, onComplete));
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
            m_IsOneShot = true;
            m_ForceOnce = false;
            var acs = new AwaitableCompletionSource();
            RegisterPendingOnce(acs, cancellationToken); // 须在 BeginRun 之后：BeginRun 已取消上一个挂起的 await

            DestroyTrack();
            m_SeqUnit.root.gameObject.SetActive(true);
            m_Routine = StartCoroutine(RunOnce(runId, item, null));
            return acs.Awaitable;
        }

        /// <summary>
        /// 以 Once 语义播放整个条目序列（忽略 <see cref="PlayMode"/> 的 Loop）的 async 版本：
        /// await 直到全部条目播放完毕（等价于等待 <see cref="OnAllComplete"/>）。仅 Sequential 模式有意义。
        /// 被打断或 <paramref name="cancellationToken"/> 取消时，await 处抛出 <see cref="OperationCanceledException"/>。
        /// </summary>
        public Awaitable PlaySequenceOnceAsync(int startIndex = 0, CancellationToken cancellationToken = default) {
            if (ScrollMode == EMarqueeScrollMode.Continuous) {
                Debug.LogWarning("UIMarquee: PlaySequenceOnceAsync 仅适用于 Sequential 模式，Continuous 无自然终点。已忽略。", this);
                return CompletedAwaitable();
            }

            if (!ValidateForPlay() || cancellationToken.IsCancellationRequested) {
                return CanceledAwaitable();
            }

            WarnIfNoRaycaster();
            int runId = BeginRun();
            m_IsOneShot = true;  // 一次性序列，被禁用打断后不自动恢复
            m_ForceOnce = true;  // 强制 Once：即使 playMode 为 Loop 也会自然结束
            var acs = new AwaitableCompletionSource();
            RegisterPendingOnce(acs, cancellationToken);

            ResetRemaining();
            m_CurrentIndex = Mathf.Clamp(startIndex, 0, m_Items.Count - 1) - 1;
            m_Routine = StartCoroutine(RunSequential(runId));
            return acs.Awaitable;
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
                Debug.LogWarning("UIMarquee: 组件未激活，无法开始播放。", this);
                return false;
            }

            if (Viewport == null || m_SeqUnit == null) {
                Debug.LogError("UIMarquee: viewport 或 content 未设置。", this);
                return false;
            }

            if (requireItems && (m_Items == null || m_Items.Count == 0)) {
                Debug.LogError("UIMarquee: 条目列表为空。", this);
                return false;
            }

            return true;
        }

        private int BeginRun() {
            CancelRun();
            IsPlaying = true;
            IsPaused = false;
            return ++m_RunId;
        }

        private void CancelRun() {
            if (m_Routine != null) {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            // 中断当前播放：若存在未完成的一次性 await，将其置为已取消（await 处会抛 OperationCanceledException）。
            // 所有中断路径（BeginRun / Stop / OnDisable / OnDestroy）都经过此处，故 async 版本不会悬挂。
            CancelPendingOnce();
        }

        // 登记一次性 await 的完成源（须在 BeginRun 之后调用，避免被本次的 CancelRun 误取消）。
        private void RegisterPendingOnce(AwaitableCompletionSource acs, CancellationToken cancellationToken) {
            m_PendingOnce = acs;

            if (cancellationToken.CanBeCanceled) {
                m_OnceCtReg = cancellationToken.Register(static state => ((UIMarquee)state).Stop(), this);
            }
        }

        // 正常完成：置结果，await 处正常返回。
        private void CompletePendingOnce() {
            DisposeOnceRegistration();
            AwaitableCompletionSource acs = m_PendingOnce;
            m_PendingOnce = null;
            acs?.TrySetResult();
        }

        // 被打断：置取消，await 处抛 OperationCanceledException。
        private void CancelPendingOnce() {
            DisposeOnceRegistration();
            AwaitableCompletionSource acs = m_PendingOnce;
            m_PendingOnce = null;
            acs?.TrySetCanceled();
        }

        private void DisposeOnceRegistration() {
            m_OnceCtReg.Dispose();
            m_OnceCtReg = default;
        }

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
                Debug.LogWarning("UIMarquee: 已订阅 OnItemClicked，但所在 Canvas 未找到 GraphicRaycaster，点击事件不会触发。", this);
            }
        }

        // ----------------------------------------------------------------
        // Sequential 逐条轮播
        // ----------------------------------------------------------------

        private IEnumerator RunSequential(int runId) {
            DestroyTrack();

            if (m_SeqUnit != null) {
                m_SeqUnit.root.gameObject.SetActive(true);
            }

            bool loop = !m_ForceOnce && PlayMode == EMarqueePlayMode.Loop;
            bool naturalEnd = false;

            while (runId == m_RunId) {
                if (IsPaused) {
                    yield return null;
                    continue;
                }

                int next = MarqueeMath.GetNextPlayableIndex(m_Remaining, m_CurrentIndex, loop, out bool loopCompleted);

                if (next < 0) {
                    naturalEnd = true;
                    break;
                }

                if (loopCompleted) {
                    OnLoopComplete?.Invoke();
                }

                m_CurrentIndex = next;
                MarqueeItemData item = m_Items[next];

                if (m_Remaining[next] > 0) {
                    m_Remaining[next]--;
                }

                Vector2 size = ApplySegments(m_SeqUnit, item, next);

                if (size.x <= 0f && size.y <= 0f) {
                    m_Remaining[next] = 0; // 无有效内容，标记跳过
                    yield return null;
                    continue;
                }

                OnItemStart?.Invoke(item, next);
                yield return MoveSequentialItem(size);
                OnItemComplete?.Invoke(item, next);
            }

            if (runId == m_RunId) {
                IsPlaying = false;
                IsPaused = false;

                if (naturalEnd) {
                    OnAllComplete?.Invoke();
                }

                CompletePendingOnce(); // PlaySequenceOnceAsync 的 await 在序列自然结束时返回
            }
        }

        private IEnumerator RunOnce(int runId, MarqueeItemData item, Action onComplete) {
            Vector2 size = ApplySegments(m_SeqUnit, item, 0);

            if (size.x > 0f || size.y > 0f) {
                yield return MoveSequentialItem(size);
            }

            if (runId == m_RunId) {
                IsPlaying = false;
                IsPaused = false;
                onComplete?.Invoke();
                CompletePendingOnce(); // PlayOnceAsync 的 await 在此正常返回
            }
        }

        private IEnumerator MoveSequentialItem(Vector2 contentSize) {
            yield return EnsureViewportReady();

            Vector2 viewportSize = Viewport.rect.size;
            bool overflow = MarqueeMath.IsOverflow(Direction, viewportSize, contentSize);

            AlignCenter(m_SeqUnit.root);
            m_SeqUnit.root.sizeDelta = contentSize;

            if (!overflow && CenterWhenFit) {
                m_SeqUnit.root.anchoredPosition = Vector2.zero;
                yield return WaitSeconds(DisplayDurationWhenFit);
                yield break;
            }

            MarqueeMath.ComputeScrollPositions(Direction, viewportSize, contentSize, EdgeMargin, out Vector2 start, out Vector2 end);
            m_SeqUnit.root.anchoredPosition = start;

            yield return WaitSeconds(DisplayDurationBeforeScroll);

            float distance = Vector2.Distance(start, end);
            float duration = distance / Mathf.Max(1f, ScrollSpeed);
            float elapsed = 0f;

            while (elapsed < duration) {
                if (!IsPaused) {
                    elapsed += DeltaTime();
                    float t = Mathf.Clamp01(elapsed / duration);
                    float eased = Ease == EMarqueeEase.Custom && CustomCurve != null
                        ? CustomCurve.Evaluate(t)
                        : MarqueeMath.Evaluate(Ease, t);
                    m_SeqUnit.root.anchoredPosition = Vector2.LerpUnclamped(start, end, eased);
                }

                yield return null;
            }

            m_SeqUnit.root.anchoredPosition = end;
        }

        // ----------------------------------------------------------------
        // Continuous 无缝连续滚动（环形复用，对象数 ≈ 铺满视口所需）
        //
        // 每个单元按 center 沿流向自移。每帧解耦地：
        //   1) 回收所有已“完全滚出流出边”的队首单元入池；
        //   2) 在入场端补单元，直到入场端外存在一个“完全不可见”的待命单元。
        // 补位与回收解耦 => 任意条目尺寸 / 任意帧位移下，入场端都先补齐再可见，
        // 绝不会在视口内换数据；对象数 ≈ 铺满视口所需，与条目总数无关。
        // 单元经对象池复用，Refresh / 重新 Play 时回收而非销毁。
        // ----------------------------------------------------------------

        private IEnumerator RunContinuous(int runId) {
            if (m_SeqUnit != null) {
                m_SeqUnit.root.gameObject.SetActive(false);
            }

            yield return EnsureViewportReady();

            if (!BuildRing()) {
                if (runId == m_RunId) {
                    IsPlaying = false;
                    IsPaused = false;
                    OnAllComplete?.Invoke(); // 无可播放条目：与 Sequential naturalEnd 及文档语义保持一致
                }

                yield break;
            }

            EnsureTrackContainer();
            LayoutRing(MarqueeMath.AxisSize(Direction, Viewport.rect.size));

            while (runId == m_RunId) {
                if (!IsPaused) {
                    float viewportAxis = MarqueeMath.AxisSize(Direction, Viewport.rect.size);
                    // 单帧位移上限为一份序列长，避免极端卡顿帧的过量绕回迭代
                    float move = Mathf.Min(DeltaTime() * Mathf.Max(1f, ScrollSpeed), m_RingLength);
                    AdvanceRing(move, viewportAxis);
                }

                yield return null;
            }
        }

        /// <summary>测量有效条目（cycles!=0 且轴向尺寸>0），构建环形数据。返回是否存在可滚动内容。</summary>
        private bool BuildRing() {
            ReleaseAllActiveUnits();

            var data = new List<MarqueeItemData>();
            m_RingSource.Clear();
            m_RingLength = 0f;

            for (int i = 0; i < m_Items.Count; i++) {
                MarqueeItemData item = m_Items[i];

                if (item == null || item.Cycles == 0) {
                    continue;
                }

                Vector2 size = MeasureItem(item);
                float axis = MarqueeMath.AxisSize(Direction, size);

                if (axis <= 0f) {
                    continue;
                }

                data.Add(item);
                m_RingSource.Add(i); // 记录原始下标，使点击回调 index 与 Sequential 保持一致
                m_RingLength += axis + Spacing;
            }

            // 测量结束，归还测量单元占用的片段视图，供显示单元复用
            ClearUnit(m_MeasureUnit);

            if (data.Count == 0) {
                return false;
            }

            m_Ring = data;
            return m_RingLength > 0f;
        }

        /// <summary>初始铺设：清空后从流出边补满视口（含 1 个完全在入场端外的待命单元）。</summary>
        private void LayoutRing(float viewportAxis) {
            ReleaseAllActiveUnits();
            m_LastDataIndex = -1;
            FillEntrance(viewportAxis);
        }

        /// <summary>推进一帧：所有单元沿流向移动，回收已滚出流出边的队首，并在入场端补足。</summary>
        private void AdvanceRing(float move, float viewportAxis) {
            float f = MarqueeMath.FlowSign(Direction);
            bool horizontal = MarqueeMath.IsHorizontal(Direction);

            foreach (MarqueeUnit unit in m_ActiveUnits) {
                unit.center += f * move;
                PlaceUnit(unit, horizontal);
            }

            RecycleExitedUnits(viewportAxis);
            FillEntrance(viewportAxis);
        }

        /// <summary>回收所有已完全滚出流出边的队首单元（入池复用）。</summary>
        private void RecycleExitedUnits(float viewportAxis) {
            int guard = 0;

            while (m_ActiveUnits.Count > 0 && guard++ <= m_ActiveUnits.Count) {
                MarqueeUnit head = m_ActiveUnits[0];

                if (!MarqueeMath.ContinuousUnitFullyExited(Direction, head.center, head.axisSize, viewportAxis)) {
                    break;
                }

                m_ActiveUnits.RemoveAt(0);
                ReleaseUnit(head);
            }
        }

        /// <summary>
        /// 在入场端补单元，直到入场端外存在一个“完全不可见”的待命单元。
        /// 补位与回收解耦：保证任何帧位移/任意条目尺寸下，入场端都先补齐再可见，不会在视口内换数据。
        /// </summary>
        private void FillEntrance(float viewportAxis) {
            int n = m_Ring?.Count ?? 0;

            if (n == 0) {
                return;
            }

            float f = MarqueeMath.FlowSign(Direction);
            bool horizontal = MarqueeMath.IsHorizontal(Direction);
            float leadingEdge = f * (viewportAxis * 0.5f);
            float entranceEdge = -viewportAxis * 0.5f; // f 坐标系下入场边

            int guard = 0;
            float minAxis = Mathf.Max(1f, m_RingLength / n);
            int maxFill = n + Mathf.CeilToInt(viewportAxis / minAxis) + 8;

            while (guard++ < maxFill) {
                if (m_ActiveUnits.Count > 0) {
                    MarqueeUnit tail = m_ActiveUnits[^1];
                    float tailLeadingScalar = f * tail.center + tail.axisSize * 0.5f; // 队尾流出侧

                    if (tailLeadingScalar <= entranceEdge) {
                        break; // 入场端外已有完全不可见的待命单元
                    }
                }

                int d = (m_LastDataIndex + 1) % n;
                MarqueeUnit unit = AcquireUnit();
                unit.dataIndex = d;

                // 点击回调统一使用原始 items 下标（与 Sequential 一致），而非过滤后的环形下标
                int sourceIndex = d < m_RingSource.Count ? m_RingSource[d] : d;
                Vector2 size = ApplySegments(unit, m_Ring[d], sourceIndex);
                unit.axisSize = MarqueeMath.AxisSize(Direction, size);

                if (m_ActiveUnits.Count == 0) {
                    unit.center = leadingEdge - f * (unit.axisSize * 0.5f); // 首个贴流出边
                } else {
                    MarqueeUnit tail = m_ActiveUnits[^1];
                    unit.center = tail.center - f * (tail.axisSize * 0.5f + Spacing + unit.axisSize * 0.5f);
                }

                PlaceUnit(unit, horizontal);
                m_ActiveUnits.Add(unit);
                m_LastDataIndex = d;
            }
        }

        private void PlaceUnit(MarqueeUnit unit, bool horizontal) {
            unit.root.anchoredPosition = horizontal ? new Vector2(unit.center, 0f) : new Vector2(0f, unit.center);
        }

        private void EnsureTrackContainer() {
            if (m_Track != null) {
                return;
            }

            var trackGo = new GameObject("MarqueeTrack", typeof(RectTransform));
            m_Track = trackGo.GetComponent<RectTransform>();
            m_Track.SetParent(Viewport, false);
            AlignCenter(m_Track);
            m_Track.sizeDelta = Vector2.zero;
        }

        private MarqueeUnit AcquireUnit() {
            MarqueeUnit unit = m_UnitPool.Count > 0 ? m_UnitPool.Pop() : CreateUnit("MarqueeUnit", m_Track);
            unit.root.SetParent(m_Track, false);
            unit.root.gameObject.SetActive(true);
            return unit;
        }

        private void ReleaseUnit(MarqueeUnit unit) {
            ClearUnit(unit); // 片段视图归还共享池，便于其它单元复用

            if (unit.root != null) {
                unit.root.gameObject.SetActive(false);
                unit.root.SetParent(m_PoolParent, false);
            }

            m_UnitPool.Push(unit);
        }

        /// <summary>把当前所有活动单元回收入池（不销毁），供下次铺设复用。</summary>
        private void ReleaseAllActiveUnits() {
            foreach (MarqueeUnit unit in m_ActiveUnits) {
                ReleaseUnit(unit);
            }

            m_ActiveUnits.Clear();
        }

        /// <summary>隐藏 Continuous 轨道内容（回收所有活动单元入池）。切换到 Sequential / Once 时调用。</summary>
        private void DestroyTrack() {
            ReleaseAllActiveUnits();
        }

        private Vector2 MeasureItem(MarqueeItemData item) {
            return ApplySegments(m_MeasureUnit, item, 0);
        }

        // ----------------------------------------------------------------
        // 单元 / 片段渲染
        //
        // 每个单元是一个容器，其下按 item 的片段列表水平排列若干片段视图（文本/图片/spine）。
        // 片段视图按渲染器 Key 分类入对象池复用；单元本身亦入池复用。
        // ----------------------------------------------------------------

        /// <summary>创建一个空单元容器（带点击中继）。</summary>
        private MarqueeUnit CreateUnit(string unitName, Transform parent) {
            var go = new GameObject(unitName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AlignCenter(rt);
            rt.sizeDelta = Vector2.zero;
            var relay = go.AddComponent<MarqueeClickRelay>();
            return new MarqueeUnit { root = rt, relay = relay };
        }

        /// <summary>
        /// 把 item 的片段渲染进单元：水平排列、垂直居中，返回内容总尺寸（宽 × 高）。
        /// 旧片段先回收入池，再按片段列表逐个取视图、绑定、定位。
        /// </summary>
        private Vector2 ApplySegments(MarqueeUnit unit, MarqueeItemData item, int index) {
            ClearUnit(unit);

            if (item == null || unit == null || item.Segments == null) {
                return Vector2.zero;
            }

            List<MarqueeSegment> segments = item.Segments;
            float totalW = 0f;
            float maxH = 0f;
            int count = 0;

            for (int i = 0; i < segments.Count; i++) {
                MarqueeSegment seg = segments[i];

                if (seg == null) {
                    continue;
                }

                IMarqueeSegmentRenderer renderer = FindRenderer(seg);

                if (renderer == null) {
                    Debug.LogWarning($"UIMarquee: 找不到可渲染片段「{seg.GetType().Name}」的渲染器（spine 片段需引入 UIMarquee.Spine 扩展并自动注册）。", this);
                    continue;
                }

                RectTransform view = AcquireSegmentView(renderer);
                view.SetParent(unit.root, false);
                AlignCenter(view);

                Vector2 sz = renderer.Bind(view, seg);
                sz = new Vector2(Mathf.Max(0f, sz.x), Mathf.Max(0f, sz.y));
                view.sizeDelta = sz;

                unit.views.Add(new SegView { view = view, renderer = renderer, size = sz });

                if (count > 0) {
                    totalW += SegmentSpacing;
                }

                totalW += sz.x;
                maxH = Mathf.Max(maxH, sz.y);
                count++;
            }

            // 水平排列：左→右，整体水平居中；每段垂直居中
            float cursor = -totalW * 0.5f;

            for (int i = 0; i < unit.views.Count; i++) {
                float w = unit.views[i].size.x;
                unit.views[i].view.anchoredPosition = new Vector2(cursor + w * 0.5f, 0f);
                cursor += w + SegmentSpacing;
            }

            var size = new Vector2(totalW, maxH);
            unit.size = size;
            unit.root.sizeDelta = size;
            unit.relay.Bind(this, item, index);
            return size;
        }

        /// <summary>回收单元当前所有片段视图入池，清空视图列表。</summary>
        private void ClearUnit(MarqueeUnit unit) {
            if (unit == null) {
                return;
            }

            for (int i = 0; i < unit.views.Count; i++) {
                RecycleSegmentView(unit.views[i]);
            }

            unit.views.Clear();
        }

        private IMarqueeSegmentRenderer FindRenderer(MarqueeSegment seg) {
            for (int i = 0; i < m_Renderers.Count; i++) {
                if (m_Renderers[i].CanRender(seg)) {
                    return m_Renderers[i];
                }
            }

            IReadOnlyList<IMarqueeSegmentRenderer> ext = MarqueeSegmentRendererRegistry.Renderers;

            for (int i = 0; i < ext.Count; i++) {
                if (ext[i].CanRender(seg)) {
                    return ext[i];
                }
            }

            return null;
        }

        private RectTransform AcquireSegmentView(IMarqueeSegmentRenderer renderer) {
            Stack<RectTransform> pool = GetPool(renderer.Key);
            RectTransform view = pool.Count > 0 ? pool.Pop() : renderer.CreateView(m_PoolParent);
            view.gameObject.SetActive(true);
            return view;
        }

        private void RecycleSegmentView(SegView sv) {
            if (sv.view == null) {
                return;
            }

            sv.renderer?.OnRecycle(sv.view);
            sv.view.SetParent(m_PoolParent, false);
            sv.view.gameObject.SetActive(false);
            GetPool(sv.renderer.Key).Push(sv.view);
        }

        private Stack<RectTransform> GetPool(string key) {
            if (!m_SegmentPools.TryGetValue(key, out Stack<RectTransform> pool)) {
                pool = new Stack<RectTransform>();
                m_SegmentPools[key] = pool;
            }

            return pool;
        }

        // 显示单元：容器 + 点击中继 + 当前片段视图列表（+ Continuous 几何）
        private sealed class MarqueeUnit {
            public RectTransform root;
            public MarqueeClickRelay relay;
            public readonly List<SegView> views = new();
            public int dataIndex;
            public float axisSize;
            public float center;
            public Vector2 size;
        }

        private struct SegView {
            public RectTransform view;
            public IMarqueeSegmentRenderer renderer;
            public Vector2 size;
        }

        // ----------------------------------------------------------------
        // 内置片段渲染器（文本 / 图片）：克隆 contentTemplate 中的样式模板
        // ----------------------------------------------------------------

        private sealed class BuiltinTextRenderer : IMarqueeSegmentRenderer {
            private readonly RectTransform m_Template;

            public BuiltinTextRenderer(RectTransform template) {
                m_Template = template;
            }

            public string Key => "builtin.text";

            public bool CanRender(MarqueeSegment segment) => segment is MarqueeTextSegment;

            public RectTransform CreateView(Transform parent) {
                var go = Instantiate(m_Template.gameObject, parent);
                go.name = "TextSegment";
                return (RectTransform)go.transform;
            }

            public Vector2 Bind(RectTransform view, MarqueeSegment segment) {
                var seg = (MarqueeTextSegment)segment;
                var tmp = view.GetComponent<TextMeshProUGUI>() ?? view.GetComponentInChildren<TextMeshProUGUI>(true);

                if (tmp == null) {
                    return Vector2.zero;
                }

                string content = seg.Text ?? "";
                tmp.text = content;
                return tmp.GetPreferredValues(content);
            }

            public void OnRecycle(RectTransform view) {
            }
        }

        private sealed class BuiltinImageRenderer : IMarqueeSegmentRenderer {
            private readonly RectTransform m_Template;

            public BuiltinImageRenderer(RectTransform template) {
                m_Template = template;
            }

            public string Key => "builtin.image";

            public bool CanRender(MarqueeSegment segment) => segment is MarqueeImageSegment;

            public RectTransform CreateView(Transform parent) {
                var go = Instantiate(m_Template.gameObject, parent);
                go.name = "ImageSegment";
                return (RectTransform)go.transform;
            }

            public Vector2 Bind(RectTransform view, MarqueeSegment segment) {
                var seg = (MarqueeImageSegment)segment;
                var img = view.GetComponent<Image>() ?? view.GetComponentInChildren<Image>(true);

                if (img == null) {
                    return Vector2.zero;
                }

                img.sprite = seg.Sprite;
                img.enabled = seg.Sprite != null;

                Vector2 size = seg.Size;

                if ((size.x <= 0f || size.y <= 0f) && seg.Sprite != null) {
                    Rect r = seg.Sprite.rect;
                    size = new Vector2(r.width, r.height);
                }

                return size;
            }

            public void OnRecycle(RectTransform view) {
            }
        }

        private IEnumerator EnsureViewportReady() {
            const int maxFrames = 120;
            int frames = 0;

            // 等待滚动方向对应的视口轴向尺寸完成布局（Left/Right 看宽、Up/Down 看高），
            // 避免首条在该轴尺寸尚为 0 时按未就绪尺寸计算滚动/居中布局。
            while (Viewport != null
                   && MarqueeMath.AxisSize(Direction, Viewport.rect.size) <= 0f
                   && frames < maxFrames) {
                frames++;
                yield return null;
            }
        }

        private IEnumerator WaitSeconds(float seconds) {
            float t = 0f;

            while (t < seconds) {
                if (!IsPaused) {
                    t += DeltaTime();
                }

                yield return null;
            }
        }

        // ----------------------------------------------------------------
        // 对齐辅助
        // ----------------------------------------------------------------

        private static void AlignCenter(RectTransform rt) {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
