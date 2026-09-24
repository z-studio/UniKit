using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>完整演示场景的控制器；界面控件的事件绑定已保存在场景中，运行时直接使用。</summary>
    public sealed class Demo : MonoBehaviour {
        [Header("演示内容")] [SerializeField] private BannerCarousel m_Carousel;
        [SerializeField] private BannerCarouselIndicators m_Indicators;
        [SerializeField] private Sprite[] m_Images;
        [SerializeField] private RectTransform m_SpinePage;
        [SerializeField] private RectTransform m_MixedPage;
        [SerializeField] private Button m_BarIndicator;
        [SerializeField] private GameObject m_EditModePreview;

        [Header("实时反馈")] [SerializeField]
        private Text m_PageLabel;

        [SerializeField] private Text m_StateLabel;
        [SerializeField] private Text m_EventLog;
        [SerializeField] private Text m_DwellLabel;
        [SerializeField] private Text m_TransitionLabel;
        [SerializeField] private Text m_PauseLabel;
        [SerializeField] private Image m_Progress;
        [SerializeField] private GameObject m_EmptyMessage;
        [SerializeField] private Button m_Previous;
        [SerializeField] private Button m_Next;
        [SerializeField] private Toggle m_AutoPlay;

        // 只展示最近三条事件；页面名称列表与当前内容集合的索引保持一致。
        private readonly Queue<string> m_Events = new();
        private readonly List<string> m_PageNames = new();
        private int m_ContentSet;
        private bool m_PerPageDuration;
        private float m_OriginalTimeScale;
        private bool m_AlternateAccent;

        private void Awake() {
            m_OriginalTimeScale = Time.timeScale;
            
            // 编辑模式预览不参与运行时轮播，避免与轮播生成的实例重叠。
            m_EditModePreview.SetActive(false);
        }

        private void OnEnable() {
            m_Carousel.OnBannerChanged.AddListener(Selected);
            m_Carousel.OnBannerClicked.AddListener(Clicked);
            m_Carousel.OnPlaybackCompleted.AddListener(Completed);
        }

        private void Start() => ApplyContent();

        private void OnDisable() {
            m_Carousel.OnBannerChanged.RemoveListener(Selected);
            m_Carousel.OnBannerClicked.RemoveListener(Clicked);
            m_Carousel.OnPlaybackCompleted.RemoveListener(Completed);
            
            // 退出演示时撤销“冻结游戏时间”，避免影响之后运行的场景或其他逻辑。
            Time.timeScale = m_OriginalTimeScale;
        }

        // 在轮播推进本帧状态后刷新反馈，并同步导航按钮的边界与忙碌状态。
        private void LateUpdate() {
            int index = m_Carousel.CurrentIndex;
            string page = index >= 0 && index < m_PageNames.Count ? m_PageNames[index] : "NO CONTENT";
            m_PageLabel.text = $"{index + 1:00} / {m_Carousel.Count:00}   {page}";

            string state = m_Carousel.IsTransitioning
                ? "TRANSITION"
                : m_Carousel.IsPlaying
                    ? "PLAYING"
                    : "IDLE / PAUSED";

            m_StateLabel.text = $"{state}   |   {m_Carousel.Progress:P0}   |   Time scale {Time.timeScale:0}";
            m_PauseLabel.text = m_Carousel.IsPaused ? "Resume" : "Pause";
            m_Progress.fillAmount = m_Carousel.Progress;
            m_Previous.interactable = m_Carousel.CanGoPrevious && !m_Carousel.IsTransitioning;
            m_Next.interactable = m_Carousel.CanGoNext && !m_Carousel.IsTransitioning;
            m_EmptyMessage.SetActive(m_Carousel.Count == 0);
        }

        /// <summary>按内容下拉框的选项重建示例页面，并从第一页开始展示。</summary>
        public void SetContent(int value) {
            m_ContentSet = value;
            ApplyContent();
        }

        // 场景中的播放、过渡、方向下拉框顺序与对应枚举值一致。
        public void SetPlayback(int value) {
            m_Carousel.PlaybackMode = (BannerPlaybackMode)value;
            Record($"Playback: {(BannerPlaybackMode)value}");
        }

        public void SetTransition(int value) {
            // 先取消未完成的切换并恢复已选页，再改变过渡效果，避免两种效果混用。
            m_Carousel.GoTo(m_Carousel.CurrentIndex, false);
            m_Carousel.Transition = (BannerTransition)value;
        }

        public void SetDirection(int value) => m_Carousel.Direction = (BannerCarousel.SlideDirection)value;

        public void SetDwell(float value) {
            m_Carousel.DwellDuration = value;
            m_DwellLabel.text = $"Dwell   {value:0.0}s";
        }

        public void SetTransitionDuration(float value) {
            m_Carousel.TransitionDuration = value;
            m_TransitionLabel.text = $"Transition   {value:0.00}s";
        }

        public void SetAutoPlay(bool value) => m_Carousel.AutoPlay = value;
        public void SetHoverPause(bool value) => m_Carousel.PauseOnHover = value;
        public void SetDrag(bool value) => m_Carousel.AllowDrag = value;
        public void SetUnscaledTime(bool value) => m_Carousel.UseUnscaledTime = value;
        public void SetFreezeTime(bool value) => Time.timeScale = value ? 0f : m_OriginalTimeScale;

        /// <summary>切换逐页时长配置；重新生成数据，使新的 Duration 应用于所有页面。</summary>
        public void SetPerPageDuration(bool value) {
            m_PerPageDuration = value;
            ApplyContent();
        }

        public void SetIndicatorsVisible(bool value) => m_Carousel.ShowIndicators = value;

        /// <summary>示范内置圆点、径向进度以及自定义条形按钮模板三种外观。</summary>
        public void SetIndicatorStyle(int value) {
            m_Indicators.Template = value == 2 ? m_BarIndicator : null;
            m_Indicators.Style = value == 0 ? BannerCarouselIndicators.IndicatorStyle.Dots
                : BannerCarouselIndicators.IndicatorStyle.Progress;
            m_Indicators.Size = value == 2 ? new Vector2(64f, 6f) : new Vector2(14f, 14f);
        }

        public void ChangeAccent() {
            m_AlternateAccent = !m_AlternateAccent;
            m_Indicators.SelectedColor =
                m_AlternateAccent ? new Color(1f, 0.65f, 0.38f) : new Color(0.43f, 0.91f, 0.81f);
        }

        public void TogglePause() {
            if (m_Carousel.IsPaused) m_Carousel.Resume();
            else m_Carousel.Pause();
        }

        public void Restart() {
            m_Carousel.GoTo(0, false);
            m_Carousel.Play();
            
            // 只同步开关外观，不再次触发控件回调；Play 已负责恢复自动播放。
            m_AutoPlay.SetIsOnWithoutNotify(true);
            Record("Restarted from first page");
        }

        public void JumpToLast() => m_Carousel.GoTo(m_Carousel.Count - 1);

        public void ToggleCarousel() {
            m_Carousel.enabled = !m_Carousel.enabled;
            Record(m_Carousel.enabled ? "Carousel enabled" : "Carousel disabled (click again to restore)");
        }

        /// <summary>在两种视口尺寸间切换，观察当前页布局与后续切换距离是否随尺寸更新。</summary>
        public void ToggleViewportSize() {
            var rect = (RectTransform)m_Carousel.transform;
            rect.sizeDelta = rect.sizeDelta.x > 750f ? new Vector2(680f, 380f) : new Vector2(860f, 480f);
        }

        public void PageAction() => Record("Mixed page button clicked — independent UI interaction");

        // 选项编号与场景中的内容下拉框一致；每次切换都创建独立的页面配置。
        private void ApplyContent() {
            var pages = new List<BannerPage>();
            m_PageNames.Clear();

            switch (m_ContentSet) {
                case 0:
                    AddImage(pages, 0);
                    AddPrefab(pages, m_SpinePage, "SPINE / ORBIT BOT", 5f);
                    AddPrefab(pages, m_MixedPage, "MIXED / IMAGE + SPINE + UI", 4f);
                    break;
                case 1:
                    for (int i = 0; i < m_Images.Length; i++) AddImage(pages, i);
                    break;
                case 2:
                    AddPrefab(pages, m_SpinePage, "SPINE / ORBIT BOT", 5f);
                    AddPrefab(pages, m_SpinePage, "SPINE / REPLAY ON ENTRY", 3f);
                    break;
                case 3:
                    AddPrefab(pages, m_MixedPage, "MIXED / IMAGE + SPINE + UI", 4f);
                    AddPrefab(pages, m_SpinePage, "SPINE / ORBIT BOT", 5f);
                    break;
                case 4:
                    AddImage(pages, 0);
                    AddPrefab(pages, m_MixedPage, "MIXED / TWO-PAGE LOOP", 4f);
                    break;
                case 5:
                    AddImage(pages, 0);
                    break;
                // 选项 6 不添加任何页面，专门演示空数据源。
            }

            m_Carousel.SetPages(pages);
            Record($"Loaded {pages.Count} page(s){(m_PerPageDuration ? " with individual dwell times" : "")}");
        }

        private void AddImage(List<BannerPage> pages, int index) {
            pages.Add(new BannerPage { Sprite = m_Images[index], PreserveAspect = true });
            m_PageNames.Add($"IMAGE / PORTRAIT {index + 1}");
        }

        private void AddPrefab(List<BannerPage> pages, RectTransform prefab, string label, float dwell) {
            pages.Add(new BannerPage { Prefab = prefab, Duration = m_PerPageDuration ? dwell : 0f });
            m_PageNames.Add(label);
        }

        private void Selected(int index) => Record($"Selected page {index + 1}");
        private void Clicked(int index) => Record($"Clicked page {index + 1}");
        private void Completed() => Record("Once completed — press Restart to play again");

        // 使用非缩放时间记录事件，冻结游戏时间后仍可观察操作顺序。
        private void Record(string message) {
            m_Events.Enqueue($"{Time.unscaledTime:000.0}s  {message}");

            while (m_Events.Count > 3) {
                m_Events.Dequeue();
            }

            m_EventLog.text = string.Join("\n", m_Events);
        }
    }
}