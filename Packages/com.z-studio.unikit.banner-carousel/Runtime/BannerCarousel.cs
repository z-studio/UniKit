using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    /// <summary>支持图片与复合预制体的轮播，统一管理自动播放、手动拖拽、页面过渡和生命周期。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class BannerCarousel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {
        /// <summary>切到下一页时内容的移动方向，与页面索引的递增顺序分开定义。</summary>
        public enum SlideDirection {
            LeftToRight,
            RightToLeft,
            TopToBottom,
            BottomToTop
        }

        [Header("内容")] [Tooltip("按播放顺序配置图片页或 UI 预制体页面。")] [SerializeField]
        private BannerPage[] m_Pages = Array.Empty<BannerPage>();

        [Tooltip("视口应保持为实际可见尺寸，不要添加 LayoutGroup。留空时使用当前物体。")] [SerializeField]
        private RectTransform m_Content;

        [SerializeField] private bool m_ClipContent = true;
        [SerializeField, Min(0)] private int m_InitialIndex;

        [Header("播放")] [SerializeField] private BannerPlaybackMode m_PlaybackMode = BannerPlaybackMode.Loop;
        [SerializeField] private bool m_AutoPlay = true;
        [SerializeField, Min(0.01f)] private float m_AutoSlideInterval = 3f;
        [SerializeField] private bool m_UseUnscaledTime = true;
        [SerializeField] private bool m_PauseOnHover;

        [Header("过渡")] [SerializeField] private SlideDirection m_SlideDirection = SlideDirection.RightToLeft;
        [SerializeField] private BannerTransition m_Transition = BannerTransition.Slide;
        [SerializeField, Min(0f)] private float m_SlideDuration = 0.3f;
        [SerializeField] private AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("交互")] [SerializeField] private bool m_AllowDrag = true;

        [Tooltip("触发翻页所需的拖动距离，使用视口本地 UI 单位，实际阈值不超过半页。")] [SerializeField, Min(1f)]
        private float m_DragThreshold = 150f;

        [Tooltip("触发轻扫翻页的最低速度，单位为本地 UI 单位/秒；设为 0 时禁用轻扫。")] [SerializeField, Min(0f)]
        private float m_FlickVelocity = 600f;

        [Header("指示器")] [SerializeField] private bool m_ShowIndicators = true;

        [Header("事件")]
        /// <summary>非拖拽的页面点击事件，参数为当前有效页面索引。</summary>
        public UnityEvent<int> OnBannerClicked = new();

        [Tooltip("初始选中及每次自动或手动切换完成后触发。")] public UnityEvent<int> OnBannerChanged = new();

        /// <summary>单次播放模式下，末页停留结束时触发一次。</summary>
        public UnityEvent OnPlaybackCompleted = new();

        // 每个页面索引独立缓存一个实例，切换时复用，离屏后停用。
        private sealed class PageView {
            public RectTransform Root;
            public CanvasGroup Group;
            public BannerPageBehaviour[] Behaviours;
        }

        // 过滤空配置后的有效页面；所有对外索引均以此列表为准。
        private readonly List<BannerPage> m_Items = new();
        private readonly Dictionary<int, PageView> m_Views = new();
        private RectTransform m_Viewport;
        private RectTransform m_PageRoot;
        private PageView m_Current;
        private PageView m_Incoming;
        private int m_TargetIndex = -1;

        // 往返模式的自动播放方向独立于手动导航；只在自动播放到达端点时反转。
        private int m_AutoDirection = 1;
        private int m_IncomingDirection;
        private bool m_Initialized;
        private bool m_Hovered;
        private bool m_Completed;
        private bool m_IsDragging;
        private bool m_IsAnimating;
        private bool m_SnapBack;
        private bool m_DragTransition;
        private int m_PointerId;
        private Vector2 m_DragStart;
        private Vector2 m_LastDrag;
        private float m_LastDragTime;
        private float m_Velocity;

        // 用页宽/页高归一化的位移：0 为居中，+1 为向下一页切换一整页，-1 为上一页。
        private float m_Offset;
        private float m_AnimationFrom;
        private float m_AnimationTo;
        private float m_AnimationTime;
        private float m_DwellTime;

        /// <summary>当前已选中的有效页面索引；空列表时为 -1，过渡完成后才更新。</summary>
        public int CurrentIndex { get; private set; } = -1;

        /// <summary>过滤空配置后的有效页数。</summary>
        public int Count => m_Items.Count;

        /// <summary>是否正在拖拽、切换或回弹，此时不接受新的动画跳转请求。</summary>
        public bool IsTransitioning => m_IsAnimating || m_IsDragging;

        /// <summary>是否满足自动播放条件；实际倒计时还会等待拖拽和过渡结束。</summary>
        public bool IsPlaying =>
            isActiveAndEnabled && m_AutoPlay && !IsPaused && !m_Completed
            && !(m_PauseOnHover && m_Hovered) && Count > 1;

        /// <summary>当前页的停留进度（0～1），不包含过渡动画进度。</summary>
        public float Progress => Count == 0 ? 0f : Mathf.Clamp01(m_DwellTime / CurrentDuration);

        /// <summary>指示器总开关，由关联的指示器组件读取。</summary>
        public bool ShowIndicators {
            get => m_ShowIndicators;
            set => m_ShowIndicators = value;
        }

        /// <summary>悬停时暂停自动倒计时，保留已有进度。</summary>
        public bool PauseOnHover {
            get => m_PauseOnHover;
            set => m_PauseOnHover = value;
        }

        /// <summary>是否允许拖拽；在拖拽中关闭会取消预览并恢复已选页。</summary>
        public bool AllowDrag {
            get => m_AllowDrag;
            set {
                if (!value && m_IsDragging) {
                    CancelMotion();
                }

                m_AllowDrag = value;
            }
        }

        /// <summary>是否使用非缩放时间推进停留与过渡，不影响页面内部独立的动画时间配置。</summary>
        public bool UseUnscaledTime {
            get => m_UseUnscaledTime;
            set => m_UseUnscaledTime = value;
        }

        /// <summary>显式暂停标记，不包含悬停暂停、关闭自动播放或禁用组件等情况。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>自动播放开关；关闭后仍可手动切换，不会清空已有停留进度。</summary>
        public bool AutoPlay {
            get => m_AutoPlay;
            set => m_AutoPlay = value;
        }

        /// <summary>默认停留秒数，页面自身配置了正数 Duration 时优先使用页面配置。</summary>
        public float DwellDuration {
            get => m_AutoSlideInterval;
            set => m_AutoSlideInterval = Mathf.Max(0.01f, value);
        }

        /// <summary>切换与回弹所用的秒数，设为 0 时立即完成。</summary>
        public float TransitionDuration {
            get => m_SlideDuration;
            set => m_SlideDuration = Mathf.Max(0f, value);
        }

        /// <summary>播放模式；赋值会重置自动方向和已播完标记。</summary>
        public BannerPlaybackMode PlaybackMode {
            get => m_PlaybackMode;
            set {
                m_PlaybackMode = value;
                m_AutoDirection = 1;
                m_Completed = false;
            }
        }

        /// <summary>程序切换的过渡效果；建议在没有进行中的过渡时修改。</summary>
        public BannerTransition Transition {
            get => m_Transition;
            set => m_Transition = value;
        }

        /// <summary>下一页的视觉移动方向；赋值前会取消正在进行的拖拽或切换。</summary>
        public SlideDirection Direction {
            get => m_SlideDirection;
            set {
                CancelMotion();
                m_SlideDirection = value;
            }
        }

        /// <summary>是否存在可手动切换的下一页，仅判断页数和边界，不包含忙碌状态。</summary>
        public bool CanGoNext => Count > 1 && (m_PlaybackMode == BannerPlaybackMode.Loop || CurrentIndex < Count - 1);

        /// <summary>是否存在可手动切换的上一页；非循环模式在首尾停止。</summary>
        public bool CanGoPrevious => Count > 1 && (m_PlaybackMode == BannerPlaybackMode.Loop || CurrentIndex > 0);

        private float CurrentDuration =>
            CurrentIndex >= 0 && m_Items[CurrentIndex].Duration > 0f
                ? m_Items[CurrentIndex].Duration : Mathf.Max(0.01f, m_AutoSlideInterval);

        private bool IsHorizontal => m_SlideDirection is SlideDirection.LeftToRight or SlideDirection.RightToLeft;

        private Vector2 Forward =>
            m_SlideDirection switch {
                SlideDirection.LeftToRight => Vector2.right,
                SlideDirection.TopToBottom => Vector2.down,
                SlideDirection.BottomToTop => Vector2.up,
                _ => Vector2.left
            };

        // 每次读取实时尺寸，使视口缩放后仍保持正确的相邻页距离。
        private float PageSize => Mathf.Max(1f, IsHorizontal ? m_Viewport.rect.width : m_Viewport.rect.height);

        private void Start() {
            if (!m_Initialized) {
                Rebuild();
            }
        }

        private void OnEnable() {
            m_Hovered = false;

            if (m_Initialized && m_Current != null) {
                Show(m_Current);
                Select(m_Current);
            }
        }

        private void OnDisable() {
            CancelMotion();
            Hide(m_Current);
            m_Hovered = false;
        }

        /// <summary>替换运行时内容并重建实例。空配置会被过滤，空集合会清空轮播；初始索引按有效页数限制。</summary>
        public void SetPages(IEnumerable<BannerPage> pages, int initialIndex = 0) {
            m_Pages = pages == null ? Array.Empty<BannerPage>() : new List<BannerPage>(pages).ToArray();
            m_InitialIndex = Mathf.Max(0, initialIndex);
            Rebuild();
        }

        /// <summary>应用 Inspector 中的内容配置，清理已缓存的实例并重置选择；后续页面仍按首次访问时创建。</summary>
        public void Rebuild() {
            CancelMotion();
            Hide(m_Current);
            m_Current = null;

            foreach (var view in m_Views.Values) {
                DestroyOwned(view.Root.gameObject);
            }

            m_Views.Clear();
            m_Items.Clear();
            CurrentIndex = -1;
            m_AutoDirection = 1;
            m_Completed = false;
            m_Viewport = m_Content != null ? m_Content : (RectTransform)transform;

            // 使用独立根节点管理页面，避免移动或删除调用方配置的装饰节点。
            if (m_PageRoot == null) {
                m_PageRoot = new GameObject("Carousel Pages", typeof(RectTransform)).GetComponent<RectTransform>();
            }

            m_PageRoot.SetParent(m_Viewport, false);
            Stretch(m_PageRoot);
            var mask = m_PageRoot.GetComponent<RectMask2D>();

            if (m_ClipContent && mask == null) {
                mask = m_PageRoot.gameObject.AddComponent<RectMask2D>();
            }

            if (mask != null) {
                mask.enabled = m_ClipContent;
            }

            if (m_Pages != null && m_Pages.Length > 0) {
                foreach (var page in m_Pages) {
                    if (page != null && (page.Prefab != null || page.Sprite != null)) m_Items.Add(page);
                }
            }

            m_Initialized = true;

            if (Count == 0) {
                return;
            }

            CurrentIndex = Mathf.Clamp(m_InitialIndex, 0, Count - 1);
            m_Current = GetView(CurrentIndex);

            if (isActiveAndEnabled) {
                Show(m_Current);
                Select(m_Current);
            }

            OnBannerChanged?.Invoke(CurrentIndex);
        }

        /// <summary>开启自动播放，解除暂停和已播完状态，并重新计算当前页的停留时间。</summary>
        public void Play() {
            m_AutoPlay = true;
            IsPaused = false;
            m_Completed = false;
            m_DwellTime = 0f;
        }

        /// <summary>暂停自动倒计时，保留当前进度；已经开始的过渡仍会完成。</summary>
        public void Pause() => IsPaused = true;

        /// <summary>解除显式暂停，不重置进度，也不会自行开启 AutoPlay 或清除已播完标记。</summary>
        public void Resume() => IsPaused = false;

        /// <summary>手动切换到下一页，可直接绑定按钮事件。</summary>
        public void Next() => Navigate(1);

        /// <summary>手动切换到上一页，可直接绑定按钮事件。</summary>
        public void Previous() => Navigate(-1);

        /// <summary>使用当前过渡效果跳转到指定的有效页面索引。</summary>
        public void GoTo(int index) => GoTo(index, true);

        /// <summary>无效目标或忙碌时的动画请求返回 false；立即跳转可中断过渡。目标为当前页时重置倒计时并返回 false。</summary>
        public bool GoTo(int index, bool animated) {
            if (!m_Initialized || !isActiveAndEnabled || index < 0 || index >= Count) {
                return false;
            }

            if (animated && IsTransitioning) {
                return false;
            }

            if (!animated) {
                CancelMotion();
            }

            if (index == CurrentIndex) {
                m_DwellTime = 0f;
                return false;
            }

            int direction = index > CurrentIndex ? 1 : -1;

            // 循环模式跨首尾跳转时选择较短方向，目标页直接作为相邻页参与过渡。
            if (m_PlaybackMode == BannerPlaybackMode.Loop && Mathf.Abs(index - CurrentIndex) > Count / 2) {
                direction = -direction;
            }

            PrepareIncoming(index, direction);
            m_Completed = false;
            m_DragTransition = false;
            BeginMotion(direction, !animated);
            return true;
        }

        private void Navigate(int direction) {
            if (!m_Initialized || !isActiveAndEnabled || IsTransitioning) {
                return;
            }

            int target = Neighbour(direction);

            if (target < 0) {
                return;
            }

            PrepareIncoming(target, direction);
            m_Completed = false;
            m_DragTransition = false;
            BeginMotion(direction, false);
        }

        // direction 为 +1 或 -1；循环模式映射首尾，其他模式越界时返回 -1。
        private int Neighbour(int direction) {
            if (Count <= 1) {
                return -1;
            }

            int target = CurrentIndex + direction;

            if (m_PlaybackMode == BannerPlaybackMode.Loop) {
                return (target + Count) % Count;
            }

            return target >= 0 && target < Count ? target : -1;
        }

        private void Update() {
            if (!m_Initialized || Count == 0) {
                return;
            }

            float delta = m_UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            // 过渡、拖拽和停留计时互斥，确保一页停稳后才开始计算停留时间。
            if (m_IsAnimating) {
                m_AnimationTime += delta;
                float t = m_SlideDuration <= 0f ? 1f : Mathf.Clamp01(m_AnimationTime / m_SlideDuration);
                float eased = m_Ease == null || m_Ease.length == 0 ? t : Mathf.Clamp01(m_Ease.Evaluate(t));
                RenderMotion(Mathf.Lerp(m_AnimationFrom, m_AnimationTo, eased));

                if (t >= 1f) {
                    FinishMotion();
                }
            } else if (!m_IsDragging && IsPlaying) {
                m_DwellTime += delta;

                if (m_DwellTime >= CurrentDuration) {
                    AdvanceAutomatically();
                }
            }
        }

        private void AdvanceAutomatically() {
            // 只有末页的停留时间也结束后才通知播放完成，标记可防止重复触发。
            if (m_PlaybackMode == BannerPlaybackMode.Once && CurrentIndex == Count - 1) {
                m_Completed = true;
                OnPlaybackCompleted?.Invoke();
                return;
            }

            if (m_PlaybackMode == BannerPlaybackMode.PingPong && Neighbour(m_AutoDirection) < 0) {
                m_AutoDirection = -m_AutoDirection;
            }

            Navigate(m_PlaybackMode == BannerPlaybackMode.PingPong ? m_AutoDirection : 1);
        }

        // 拖动反向时复用同一目标实例，只更新进入方向；双页循环不需要复制页面。
        private void PrepareIncoming(int index, int direction) {
            var view = GetView(index);

            if (m_Incoming != view) {
                Hide(m_Incoming);
                m_Incoming = view;
                Show(view);
            }

            m_TargetIndex = index;
            m_IncomingDirection = direction;
            m_Incoming.Root.anchoredPosition = -Forward * (direction * PageSize);
        }

        private void BeginMotion(int direction, bool immediate) {
            m_SnapBack = false;

            // 从当前拖拽位移续播，松手后不会先跳回居中位置。
            m_AnimationFrom = m_Offset;
            m_AnimationTo = direction;
            m_AnimationTime = 0f;
            m_IsAnimating = true;
            m_Current.Group.blocksRaycasts = false;
            m_DwellTime = 0f;

            if (immediate || m_SlideDuration <= 0f || (!m_DragTransition && m_Transition == BannerTransition.Instant)) {
                FinishMotion();
            } else {
                RenderMotion(m_Offset);
            }
        }

        // 滑动时两页保持一页间距；淡入淡出时重叠显示。拖拽及回弹始终使用滑动。
        private void RenderMotion(float offset) {
            m_Offset = offset;
            bool fade = !m_DragTransition && m_Transition == BannerTransition.CrossFade && !m_SnapBack;
            m_Current.Root.anchoredPosition = fade ? Vector2.zero : Forward * (offset * PageSize);
            m_Current.Group.alpha = fade ? 1f - Mathf.Abs(offset) : 1f;

            if (m_Incoming == null) {
                return;
            }

            m_Incoming.Root.anchoredPosition =
                fade ? Vector2.zero : Forward * ((offset - m_IncomingDirection) * PageSize);
            m_Incoming.Group.alpha = fade ? Mathf.Abs(offset) : 1f;
        }

        // 完成后交换当前页和进入页；回弹只恢复原页，不重复触发选中或页码变更事件。
        private void FinishMotion() {
            bool changed = !m_SnapBack && m_Incoming != null;

            if (changed) {
                Hide(m_Current);
                m_Current = m_Incoming;
                CurrentIndex = m_TargetIndex;
                m_Incoming = null;
            } else {
                Hide(m_Incoming);
                m_Incoming = null;
            }

            m_IsAnimating = false;
            m_SnapBack = false;
            m_DragTransition = false;
            m_Offset = 0f;
            m_DwellTime = 0f;
            m_Current.Root.anchoredPosition = Vector2.zero;
            m_Current.Group.alpha = 1f;
            m_Current.Group.blocksRaycasts = true;

            if (changed) {
                Select(m_Current);
                OnBannerChanged?.Invoke(CurrentIndex);
            }
        }

        // 中断时以已提交的当前页为准，丢弃候选页，不发送切换完成事件。
        private void CancelMotion() {
            m_IsDragging = false;
            m_IsAnimating = false;
            m_SnapBack = false;
            m_DragTransition = false;
            m_Offset = 0f;
            m_DwellTime = 0f;
            Hide(m_Incoming);
            m_Incoming = null;

            if (m_Current == null) {
                return;
            }

            m_Current.Root.anchoredPosition = Vector2.zero;
            m_Current.Group.alpha = 1f;
            m_Current.Group.blocksRaycasts = true;
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (!m_AllowDrag || !isActiveAndEnabled || IsTransitioning || Count <= 1
                || eventData.button != PointerEventData.InputButton.Left
                || !LocalPoint(eventData, out m_DragStart)) {
                return;
            }

            m_IsDragging = true;
            m_DragTransition = true;

            // 只接收发起拖拽的指针，避免另一根手指移动或松开时干扰当前操作。
            m_PointerId = eventData.pointerId;
            m_LastDrag = m_DragStart;
            m_LastDragTime = Time.unscaledTime;
            m_Velocity = 0f;
            m_DwellTime = 0f;
            m_Current.Group.blocksRaycasts = false;
            eventData.eligibleForClick = false;
        }

        public void OnDrag(PointerEventData eventData) {
            if (!m_IsDragging || eventData.pointerId != m_PointerId || !LocalPoint(eventData, out var point)) {
                return;
            }

            // 手势速度使用真实时间，避免游戏时间缩放改变轻扫判定。
            float elapsed = Time.unscaledTime - m_LastDragTime;

            if (elapsed > 0f) {
                m_Velocity = Vector2.Dot(point - m_LastDrag, Forward) / elapsed;
            }

            m_LastDrag = point;
            m_LastDragTime = Time.unscaledTime;
            float distance = Vector2.Dot(point - m_DragStart, Forward);
            int direction = distance >= 0f ? 1 : -1;
            int target = Neighbour(direction);

            if (target >= 0) {
                m_IncomingDirection = direction;
                PrepareIncoming(target, direction);
            } else {
                Hide(m_Incoming);
                m_Incoming = null;
            }

            float offset = Mathf.Clamp(distance / PageSize, -1f, 1f);

            // 非循环边界没有候选页时施加阻尼，松手后回弹。
            RenderMotion(target >= 0 ? offset : offset * 0.2f);
        }

        public void OnEndDrag(PointerEventData eventData) {
            if (!m_IsDragging || eventData.pointerId != m_PointerId) {
                return;
            }

            m_IsDragging = false;
            eventData.eligibleForClick = false;
            float distance = m_Offset * PageSize;

            // 仅认可最近 0.1 秒内且与位移同向的速度，避免停住后松手仍被当成轻扫。
            bool flick = m_FlickVelocity > 0f && Time.unscaledTime - m_LastDragTime < 0.1f
                                              && Mathf.Abs(m_Velocity) >= m_FlickVelocity
                                              && Mathf.Sign(m_Velocity) == Mathf.Sign(distance);

            if (m_Incoming != null && (Mathf.Abs(distance) >= Mathf.Min(m_DragThreshold, PageSize * 0.5f) || flick)) {
                m_Completed = false;
                BeginMotion(m_IncomingDirection, false);
            } else {
                m_SnapBack = true;
                m_AnimationFrom = m_Offset;
                m_AnimationTo = 0f;
                m_AnimationTime = 0f;
                m_IsAnimating = true;

                if (m_SlideDuration <= 0f) {
                    FinishMotion();
                }
            }
        }

        // 将屏幕坐标转到视口本地空间，让拖拽阈值和位移不受 Canvas 缩放影响。
        private bool LocalPoint(PointerEventData data, out Vector2 point) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Viewport, data.position, data.pressEventCamera,
                out point);

        public void OnPointerClick(PointerEventData eventData) {
            if (isActiveAndEnabled && Count > 0 && !IsTransitioning && eventData.eligibleForClick
                && eventData.button == PointerEventData.InputButton.Left) {
                OnBannerClicked?.Invoke(CurrentIndex);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => m_Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => m_Hovered = false;

        private PageView GetView(int index) {
            if (m_Views.TryGetValue(index, out var cached)) {
                return cached;
            }

            var page = m_Items[index];
            var root = new GameObject($"Page {index}", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            root.SetActive(false);
            var rt = (RectTransform)root.transform;
            rt.SetParent(m_PageRoot, false);
            Stretch(rt);

            // 透明点击区域保证复合页面中的空白位置也能响应拖拽。
            root.GetComponent<Image>().color = Color.clear;

            if (page.Prefab != null) {
                var content = Instantiate(page.Prefab, rt, false);
                Stretch(content);
                content.gameObject.SetActive(true);
            } else {
                var content = new GameObject("Image", typeof(RectTransform), typeof(Image));
                content.transform.SetParent(rt, false);
                Stretch((RectTransform)content.transform);

                var image = content.GetComponent<Image>();
                image.sprite = page.Sprite;
                image.preserveAspect = page.PreserveAspect;
                content.SetActive(true);
            }

            var view = new PageView {
                Root = rt,
                Group = root.GetComponent<CanvasGroup>(),
                Behaviours = root.GetComponentsInChildren<BannerPageBehaviour>(true)
            };

            m_Views.Add(index, view);
            return view;
        }

        // 只规范页面根节点，内容的具体位置和缩放由预制体子节点自行配置。
        private static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        // 预览阶段只显示页面，等正式选中后再允许页内控件接收点击。
        private static void Show(PageView view) {
            if (view == null || view.Root.gameObject.activeSelf) {
                return;
            }

            view.Group.alpha = 1f;
            view.Group.blocksRaycasts = false;
            view.Root.gameObject.SetActive(true);

            foreach (var behaviour in view.Behaviours) {
                if (behaviour != null) {
                    behaviour.OnPageShown();
                }
            }
        }

        private static void Select(PageView view) {
            view.Group.blocksRaycasts = true;

            foreach (var behaviour in view.Behaviours) {
                if (behaviour != null) {
                    behaviour.OnPageSelected();
                }
            }
        }

        // 先通知页面清理或暂停演出，再停用节点；缓存实例仍保留供后续复用。
        private static void Hide(PageView view) {
            if (view == null || view.Root == null || !view.Root.gameObject.activeSelf) {
                return;
            }

            foreach (var behaviour in view.Behaviours) {
                if (behaviour != null) {
                    behaviour.OnPageHidden();
                }
            }

            view.Root.gameObject.SetActive(false);
        }

        private static void DestroyOwned(GameObject target) {
            if (Application.isPlaying) {
                Destroy(target);
            } else {
                DestroyImmediate(target);
            }
        }

        private void OnDestroy() {
            if (m_PageRoot != null) {
                DestroyOwned(m_PageRoot.gameObject);
            }
        }
    }
}