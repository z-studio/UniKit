using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZStudio.UniKit.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace ZStudio.UIMarquee.Samples {
    /// <summary>
    /// 综合功能演示（自包含，打开场景直接运行，无需额外资源/prefab）。运行时构建两条跑马灯，
    /// 并用 **纯 UGUI**（配合 CanvasScaler 自适应分辨率）搭建完整控制台演示组件全部核心能力：
    /// - Sequential（逐条）：缓动 <c>ease</c>、Loop/Once 播放模式、<c>cycles</c> 限定次数、短内容居中停留、
    ///   四方向、文本 + 图片混排、<c>PlayOnce</c> 一次性播放，以及
    ///   OnItemStart / OnItemComplete / OnLoopComplete / OnAllComplete / OnItemClicked 全事件。
    /// - Continuous（无缝）：连续滚动、<c>scrollSpeed</c> 即时调速、<c>spacing</c> / <c>direction</c> 改后
    ///   <c>Refresh()</c> 生效、混排、运行时 <c>AddItem</c> 追加、OnItemClicked。
    /// 左侧 UGUI 面板提供全部运行时控制，右侧 ScrollRect 实时显示事件日志。
    /// 所有屏幕文本使用英文，以保证任何默认字体下都能正确显示（不依赖中文字体）。
    /// </summary>
        public class UIMarqueeDemo : MonoBehaviour {
        [Tooltip("逐条/无缝滚动速度（像素/秒）")]
        public float scrollSpeed = 120f;

        [Tooltip("无缝模式相邻条目间距")]
        public float spacing = 60f;

        [Tooltip("Sequential 跑马灯。留空则运行时自动创建；也可用右键菜单「Create Demo Marquees In Scene」在编辑器预先生成后自定义配置。")]
        [SerializeField] private UniKit.UI.UIMarquee m_Sequential;

        [Tooltip("Continuous 跑马灯。留空则运行时自动创建；也可用右键菜单预先生成。")]
        [SerializeField] private UniKit.UI.UIMarquee m_Continuous;

        private Sprite m_DemoSprite;
        private Sprite m_RoundSprite; // UI 控件背景（圆角由纯色代替，保持零资源依赖）

        private List<MarqueeItemData> m_SeqItems;
        private List<MarqueeItemData> m_ConItems;

        // ---- 控制面板状态 ----
        private MarqueeDirection m_Direction = MarqueeDirection.Left;
        private int m_EaseIndex;
        private bool m_Once;      // Sequential playMode == Once
        private int m_AddedCount; // Continuous 运行时追加计数

        // 面板上需要动态更新文字的控件
        private TextMeshProUGUI m_DirectionLabel;
        private TextMeshProUGUI m_EaseLabel;
        private TextMeshProUGUI m_PlayModeLabel;
        private TextMeshProUGUI m_SpeedLabel;
        private TextMeshProUGUI m_SpacingLabel;

        // 演示用的代表性缓动（覆盖线性 / 平滑 / 过冲 / 回弹）
        private static readonly MarqueeEase[] s_Eases = {
            MarqueeEase.Linear,
            MarqueeEase.QuadInOut,
            MarqueeEase.CubicOut,
            MarqueeEase.BackOut,
            MarqueeEase.ElasticOut,
            MarqueeEase.BounceOut,
        };

        // ---- 配色 ----
        private static readonly Color c_PanelBg = new(0.10f, 0.11f, 0.14f, 0.92f);
        private static readonly Color c_SectionBg = new(1f, 1f, 1f, 0.05f);
        private static readonly Color c_Button = new(0.22f, 0.25f, 0.33f, 1f);
        private static readonly Color c_Accent = new(0.20f, 0.60f, 1f, 1f);
        private static readonly Color c_Track = new(0f, 0f, 0f, 0.5f);
        private static readonly Color c_MarqueeBg = new(0f, 0f, 0f, 0.35f);

        private void Start() {
            Canvas canvas = EnsureCanvasAndEventSystem();
            m_RoundSprite = CreateSolidSprite(Color.white); // 控制面板控件背景（运行时生成）

            // 若已在编辑器用右键菜单预先创建（引用非空），运行时复用它们与其 Inspector 配置；
            // 否则运行时自动创建两条跑马灯（保持开箱即用）。
            bool prebuilt = m_Sequential != null && m_Continuous != null;

            if (prebuilt) {
                SyncPanelStateFromComponents();
            } else {
                BuildRuntimeMarquees(canvas);
            }

            BuildControlPanel(canvas.transform);

            SubscribeSequential();
            SubscribeContinuous();

            if (prebuilt) {
                // 复用预创建的跑马灯，沿用其 Inspector 里配置的 items（显式 SetItems 以免依赖各自 Start 时序）
                m_Sequential.SetItems(m_Sequential.Items, startPlay: true);
                m_Continuous.SetItems(m_Continuous.Items, startPlay: true);
            } else {
                // 显式 SetItems 启动，避免依赖各 UIMarquee.Start 的执行时序
                m_Sequential.SetItems(m_SeqItems, startPlay: true);
                m_Continuous.SetItems(m_ConItems, startPlay: true);
            }

            Log(prebuilt
                ? "Demo started (reusing pre-created marquees). Click content to fire OnItemClicked."
                : "Demo started (runtime-built marquees). Click content to fire OnItemClicked.");
        }

        // 运行时自动创建两条跑马灯（未预先创建时的默认路径）
        private void BuildRuntimeMarquees(Canvas canvas) {
            m_DemoSprite = CreateSolidSprite(new Color(0.2f, 0.6f, 1f));

            m_SeqItems = BuildSequentialItems();
            m_ConItems = BuildContinuousItems();

            // 两条跑马灯整体右移，让出左侧空间给控制面板（面板宽 620 + 边距）
            m_Sequential = BuildMarquee(
                canvas.transform, "Marquee_Sequential",
                anchoredX: 360f, anchoredY: 120f, width: 1040f, height: 72f,
                MarqueeScrollMode.Sequential, m_Direction, m_SeqItems);

            m_Continuous = BuildMarquee(
                canvas.transform, "Marquee_Continuous",
                anchoredX: 360f, anchoredY: 10f, width: 1040f, height: 72f,
                MarqueeScrollMode.Continuous, m_Direction, m_ConItems);

            ConfigureSequentialDefaults(m_Sequential);
        }

        // Sequential 细化：演示居中停留、缓动、Loop 播放
        private void ConfigureSequentialDefaults(UniKit.UI.UIMarquee marquee) {
            marquee.CenterWhenFit = true;
            marquee.DisplayDurationWhenFit = 1.5f;
            marquee.DisplayDurationBeforeScroll = 0.4f;
            marquee.Ease = s_Eases[m_EaseIndex];
            marquee.PlayMode = MarqueePlayMode.Loop;
        }

        // 复用预创建对象时，用组件当前配置初始化控制面板显示状态
        private void SyncPanelStateFromComponents() {
            m_Direction = m_Continuous.Direction;
            scrollSpeed = m_Continuous.ScrollSpeed;
            spacing = m_Continuous.Spacing;
            m_Once = m_Sequential.PlayMode == MarqueePlayMode.Once;
            m_EaseIndex = Mathf.Max(0, System.Array.IndexOf(s_Eases, m_Sequential.Ease));
        }

        // ------------------------------------------------------------------
        // 编辑器右键菜单：非运行时预先创建 / 清除跑马灯，便于在 Inspector 自定义配置
        // ------------------------------------------------------------------
#if UNITY_EDITOR
        [ContextMenu("Create Demo Marquees In Scene")]
        private void EditorCreateDemoMarquees() {
            if (m_Sequential != null || m_Continuous != null) {
                Debug.LogWarning("[UIMarqueeDemo] Demo marquees already exist. Run 'Clear Demo Marquees In Scene' first.", this);
                return;
            }

            Canvas canvas = EnsureCanvasAndEventSystem();

            // 编辑期图片段的 sprite 为空，可在 Inspector 中自行指定；文本条目照常可用
            List<MarqueeItemData> seqItems = BuildSequentialItems();
            List<MarqueeItemData> conItems = BuildContinuousItems();

            m_Sequential = BuildMarquee(
                canvas.transform, "Marquee_Sequential",
                anchoredX: 360f, anchoredY: 120f, width: 1040f, height: 72f,
                MarqueeScrollMode.Sequential, m_Direction, seqItems);

            m_Continuous = BuildMarquee(
                canvas.transform, "Marquee_Continuous",
                anchoredX: 360f, anchoredY: 10f, width: 1040f, height: 72f,
                MarqueeScrollMode.Continuous, m_Direction, conItems);

            ConfigureSequentialDefaults(m_Sequential);

            Undo.RegisterCreatedObjectUndo(m_Sequential.gameObject, "Create Demo Marquees");
            Undo.RegisterCreatedObjectUndo(m_Continuous.gameObject, "Create Demo Marquees");
            EditorUtility.SetDirty(this);
            MarkSceneDirty();
            Selection.activeObject = m_Sequential.gameObject;

            Debug.Log("[UIMarqueeDemo] Created demo marquees. Tweak them in the Inspector; entering Play will reuse them.", this);
        }

        [ContextMenu("Clear Demo Marquees In Scene")]
        private void EditorClearDemoMarquees() {
            if (m_Sequential != null) {
                Undo.DestroyObjectImmediate(m_Sequential.gameObject);
            }

            if (m_Continuous != null) {
                Undo.DestroyObjectImmediate(m_Continuous.gameObject);
            }

            m_Sequential = null;
            m_Continuous = null;
            EditorUtility.SetDirty(this);
            MarkSceneDirty();
            Debug.Log("[UIMarqueeDemo] Cleared demo marquees.", this);
        }

        private void MarkSceneDirty() {
            if (!Application.isPlaying) {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
#endif

        // ------------------------------------------------------------------
        // 事件订阅
        // ------------------------------------------------------------------

        private void SubscribeSequential() {
            m_Sequential.OnItemStart += (item, i) => Log($"[Seq] OnItemStart    #{i} \"{Preview(item)}\"");
            m_Sequential.OnItemComplete += (item, i) => Log($"[Seq] OnItemComplete #{i}");
            m_Sequential.OnLoopComplete += () => Log("[Seq] OnLoopComplete (round complete)");
            m_Sequential.OnAllComplete += () => Log("[Seq] OnAllComplete (all done — try Once mode)");
            m_Sequential.OnItemClicked += (item, i) => Log($"[Seq] OnItemClicked  #{i} id=\"{item.ID}\"");
        }

        private void SubscribeContinuous() {
            m_Continuous.OnItemClicked += (item, i) => Log($"[Con] OnItemClicked  #{i} id=\"{item.ID}\"");
        }

        // ------------------------------------------------------------------
        // 控制面板（UGUI）
        // ------------------------------------------------------------------

        private void BuildControlPanel(Transform parent) {
            RectTransform panel = CreatePanel(parent, "ControlPanel",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(24f, -24f), width: 620f);

            CreateLabel(panel, "UIMarquee — Full Feature Demo", 34f, FontStyles.Bold, Color.white);

            // ---- 全局 ----
            RectTransform global = CreateSection(panel, "Global");
            RectTransform gRow = CreateRow(global);
            CreateButton(gRow, "Pause", () => { m_Sequential.Pause(); m_Continuous.Pause(); Log("Pause()"); });
            CreateButton(gRow, "Resume", () => { m_Sequential.Unpause(); m_Continuous.Unpause(); Log("Unpause()"); });
            CreateButton(gRow, "Stop", () => { m_Sequential.Stop(); m_Continuous.Stop(); Log("Stop()"); });
            CreateButton(gRow, "Replay", () => { m_Sequential.Play(); m_Continuous.Play(); Log("Play()"); });

            m_DirectionLabel = CreateButton(global, $"Direction: {m_Direction}  (cycle)", CycleDirection);

            m_SpeedLabel = CreateLabel(global, "", 22f, FontStyles.Normal, Color.white);
            CreateSlider(global, 20f, 400f, scrollSpeed, OnSpeedChanged);
            OnSpeedChanged(scrollSpeed);

            // ---- Sequential ----
            RectTransform seq = CreateSection(panel, "Sequential (top row)");
            m_EaseLabel = CreateButton(seq, $"Ease: {s_Eases[m_EaseIndex]}  (cycle)", CycleEase);
            m_PlayModeLabel = CreateButton(seq, $"PlayMode: {(m_Once ? "Once" : "Loop")}  (toggle)", TogglePlayMode);
            CreateButton(seq, "PlayOnce(text) — one-shot then resume", PlayOnceDemo);

            // ---- Continuous ----
            RectTransform con = CreateSection(panel, "Continuous (bottom row)");
            m_SpacingLabel = CreateLabel(con, "", 22f, FontStyles.Normal, Color.white);
            CreateSlider(con, 0f, 160f, spacing, OnSpacingChanged);
            OnSpacingChanged(spacing);
            RectTransform cRow = CreateRow(con);
            CreateButton(cRow, "Apply Spacing (Refresh)", ApplySpacing);
            CreateButton(cRow, "AddItem (+Refresh)", AddItemDemo);
        }

        // ------------------------------------------------------------------
        // 控制回调
        // ------------------------------------------------------------------

        private void OnSpeedChanged(float v) {
            scrollSpeed = v;
            if (m_Sequential != null) m_Sequential.ScrollSpeed = v; // 下一条滚动生效
            if (m_Continuous != null) m_Continuous.ScrollSpeed = v; // 逐帧读取，立即生效
            if (m_SpeedLabel != null) m_SpeedLabel.text = $"Scroll Speed: {v:0} px/s  (Continuous is live)";
        }

        private void OnSpacingChanged(float v) {
            spacing = v;
            if (m_SpacingLabel != null) m_SpacingLabel.text = $"Spacing: {v:0} px  (Apply to Refresh)";
        }

        private void ApplySpacing() {
            m_Continuous.Spacing = spacing;
            m_Continuous.Refresh();
            Log($"[Con] spacing={spacing:0} + Refresh()");
        }

        private void AddItemDemo() {
            m_AddedCount++;
            m_Continuous.AddItem(MarqueeItemData.Text($"[Added #{m_AddedCount}] runtime-appended item", $"added_{m_AddedCount}"));
            m_Continuous.Refresh();
            Log($"[Con] AddItem #{m_AddedCount} + Refresh()");
        }

        private void PlayOnceDemo() {
            m_Sequential.PlayOnce(
                "PlayOnce: a one-shot message inserted at runtime.",
                () => {
                    Log("[Seq] PlayOnce onComplete -> resume loop");
                    m_Sequential.Play();
                });
            Log("[Seq] PlayOnce(text) invoked");
        }

        private void CycleDirection() {
            m_Direction = m_Direction switch {
                MarqueeDirection.Left => MarqueeDirection.Right,
                MarqueeDirection.Right => MarqueeDirection.Up,
                MarqueeDirection.Up => MarqueeDirection.Down,
                _ => MarqueeDirection.Left
            };

            m_Sequential.Direction = m_Direction;
            m_Continuous.Direction = m_Direction;
            // 方向改动需要重建布局才即时可见
            m_Sequential.Play();
            m_Continuous.Play();
            m_DirectionLabel.text = $"Direction: {m_Direction}  (cycle)";
            Log($"Direction = {m_Direction} (Play to rebuild)");
        }

        private void CycleEase() {
            m_EaseIndex = (m_EaseIndex + 1) % s_Eases.Length;
            m_Sequential.Ease = s_Eases[m_EaseIndex];
            // ease 每条滚动开始时读取，无需 Refresh，下一条即生效
            m_EaseLabel.text = $"Ease: {s_Eases[m_EaseIndex]}  (cycle)";
            Log($"[Seq] ease = {s_Eases[m_EaseIndex]} (applies to next scroll)");
        }

        private void TogglePlayMode() {
            m_Once = !m_Once;
            m_Sequential.PlayMode = m_Once ? MarqueePlayMode.Once : MarqueePlayMode.Loop;
            m_Sequential.Play(); // 从头开始，使新播放模式生效
            m_PlayModeLabel.text = $"PlayMode: {(m_Once ? "Once" : "Loop")}  (toggle)";
            Log($"[Seq] playMode = {(m_Once ? "Once" : "Loop")} (restarted)");
        }

        // ------------------------------------------------------------------
        // 数据
        // ------------------------------------------------------------------

        private List<MarqueeItemData> BuildSequentialItems() {
            return new List<MarqueeItemData> {
                MarqueeItemData.Text("Welcome to the UIMarquee full-feature demo!", "seq_welcome"),
                MarqueeItemData.Text("Short text (centered dwell)", "seq_short"),
                new MarqueeItemData(
                    new MarqueeTextSegment("Mixed: "),
                    new MarqueeImageSegment(m_DemoSprite) { Size = new Vector2(32f, 32f) },
                    new MarqueeTextSegment(" text + image in one item")) { ID = "seq_mixed" },
                MarqueeItemData.Text("[Limited x2] This notice appears only twice, then is skipped.", "seq_limited", cycles: 2),
                MarqueeItemData.Text("This is a long scrolling notice that demonstrates the easing curve and smooth scrolling when content exceeds the viewport.", "seq_long"),
                MarqueeItemData.Image(m_DemoSprite, "seq_icon"),
            };
        }

        private List<MarqueeItemData> BuildContinuousItems() {
            return new List<MarqueeItemData> {
                MarqueeItemData.Text("Continuous mode: items scroll end-to-end seamlessly", "con_tip1"),
                MarqueeItemData.Text("* Limited-time event is live *", "con_tip2"),
                new MarqueeItemData(
                    new MarqueeTextSegment("Reward: "),
                    new MarqueeImageSegment(m_DemoSprite) { Size = new Vector2(28f, 28f) }) { ID = "con_mixed" },
                MarqueeItemData.Text("Click me to fire OnItemClicked", "con_click"),
                MarqueeItemData.Image(m_DemoSprite, "con_icon"),
            };
        }

        // ------------------------------------------------------------------
        // 跑马灯构建
        // ------------------------------------------------------------------

        private UniKit.UI.UIMarquee BuildMarquee(
            Transform parent,
            string name,
            float anchoredX,
            float anchoredY,
            float width,
            float height,
            MarqueeScrollMode mode,
            MarqueeDirection direction,
            List<MarqueeItemData> items) {
            var viewportGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(parent, false);
            viewportGo.SetActive(false); // 待字段设置完成后再激活，避免 Awake 过早执行

            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.anchorMin = new Vector2(0.5f, 0.5f);
            viewport.anchorMax = new Vector2(0.5f, 0.5f);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = new Vector2(anchoredX, anchoredY);
            viewport.sizeDelta = new Vector2(width, height);

            var bg = viewportGo.GetComponent<Image>();
            bg.color = c_MarqueeBg;
            bg.raycastTarget = false;

            var templateGo = new GameObject("ContentTemplate", typeof(RectTransform));
            templateGo.transform.SetParent(viewport, false);
            var template = templateGo.GetComponent<RectTransform>();
            template.sizeDelta = new Vector2(100f, height);

            var imageGo = new GameObject("Image", typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(template, false);
            imageGo.SetActive(false);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(template, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 32f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = true; // 点击事件需要

            var marquee = viewportGo.AddComponent<UniKit.UI.UIMarquee>();
            marquee.Viewport = viewport;
            marquee.ContentTemplate = template;
            marquee.ScrollMode = mode;
            marquee.Direction = direction;
            marquee.ScrollSpeed = scrollSpeed;
            marquee.Spacing = spacing;
            marquee.EdgeMargin = 10f;
            marquee.DisplayDurationWhenFit = 2f;
            marquee.DisplayDurationBeforeScroll = 0.5f;
            marquee.PlayOnStart = false;
            marquee.Items = items;

            viewportGo.SetActive(true);
            return marquee;
        }

        // ------------------------------------------------------------------
        // UGUI 构建辅助
        // ------------------------------------------------------------------

        private Canvas EnsureCanvasAndEventSystem() {
            Canvas canvas = FindFirstObjectByType<Canvas>();

            if (canvas == null) {
                var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            // CanvasScaler：按屏幕尺寸缩放，保证不同分辨率下布局一致
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (canvas.GetComponent<GraphicRaycaster>() == null) {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (FindFirstObjectByType<EventSystem>() == null) {
                // 按工程启用的输入系统选择 UI 输入模块：新输入系统用 InputSystemUIInputModule，
                // 旧输入系统（Legacy）用 StandaloneInputModule。避免对 Input System 包形成硬依赖。
#if ENABLE_INPUT_SYSTEM
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            }

            return canvas;
        }

        // 一个带背景与垂直布局、宽度固定、高度随内容的面板
        private RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, float width) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(width, 0f);

            var img = go.GetComponent<Image>();
            img.sprite = m_RoundSprite;
            img.color = c_PanelBg;

            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            return rt;
        }

        // 分组：标题 + 一个带淡色背景、内部垂直排列的容器
        private RectTransform CreateSection(RectTransform parent, string title) {
            CreateLabel(parent, title, 26f, FontStyles.Bold, c_Accent);

            var go = new GameObject($"Section_{title}", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = m_RoundSprite;
            img.color = c_SectionBg;

            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 14, 14);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        // 一行（水平排列，等分宽度）
        private RectTransform CreateRow(RectTransform parent) {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            go.GetComponent<LayoutElement>().minHeight = 56f;
            return rt;
        }

        private TextMeshProUGUI CreateLabel(RectTransform parent, string text, float fontSize, FontStyles style, Color color) {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        // 返回按钮上的文字标签（便于动态更新，如 Direction/Ease/PlayMode）
        private TextMeshProUGUI CreateButton(RectTransform parent, string label, Action onClick) {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = m_RoundSprite;
            img.color = c_Button;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());

            go.GetComponent<LayoutElement>().minHeight = 56f;

            var textGo = new GameObject("Text", typeof(RectTransform));
            var trt = textGo.GetComponent<RectTransform>();
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 4f);
            trt.offsetMax = new Vector2(-12f, -4f);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 22f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        // 标准 Unity Slider 层级（Background / Fill Area→Fill / Handle Slide Area→Handle）
        private Slider CreateSlider(RectTransform parent, float min, float max, float value, Action<float> onChanged) {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 36f;

            const float handleR = 14f;

            Image bg = CreateUIImage(rt, "Background", c_Track);
            SetAnchors(bg.rectTransform, new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), Vector2.zero, Vector2.zero);

            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            var fillArea = fillAreaGo.GetComponent<RectTransform>();
            fillArea.SetParent(rt, false);
            SetAnchors(fillArea, new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), new Vector2(handleR, 0f), new Vector2(-handleR, 0f));

            Image fill = CreateUIImage(fillArea, "Fill", c_Accent);
            SetAnchors(fill.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            var handleArea = handleAreaGo.GetComponent<RectTransform>();
            handleArea.SetParent(rt, false);
            SetAnchors(handleArea, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(handleR, 0f), new Vector2(-handleR, 0f));

            Image handle = CreateUIImage(handleArea, "Handle", Color.white);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(handleR * 2f, 0f);

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        private Image CreateUIImage(RectTransform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = m_RoundSprite;
            img.color = color;
            return img;
        }

        private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private Sprite CreateSolidSprite(Color color) {
            var tex = new Texture2D(8, 8);
            var pixels = new Color[8 * 8];

            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = color;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------
        // 日志
        // ------------------------------------------------------------------

        private static void Log(string message) {
            Debug.Log($"[UIMarqueeDemo] {message}");
        }

        private static string Preview(MarqueeItemData item) {
            if (item == null || item.Segments == null || item.Segments.Count == 0) {
                return "";
            }

            var parts = new List<string>();

            foreach (MarqueeSegment seg in item.Segments) {
                parts.Add(seg switch {
                    MarqueeTextSegment t => t.Text ?? "",
                    MarqueeImageSegment => "<image>",
                    _ => $"<{seg.GetType().Name}>",
                });
            }

            string s = string.Join(" ", parts);
            return s.Length <= 24 ? s : s[..24] + "…";
        }
    }
}
