using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 根据轮播页数生成可点击的指示器，并同步选中状态与停留进度。
    /// 可指定按钮模板来自定义外观；关闭自动布局后可保留模板布局或交由布局组件排列。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BannerCarouselIndicators : MonoBehaviour {
        /// <summary>圆点样式突出当前页；进度样式显示当前页的停留进度。</summary>
        public enum IndicatorStyle {
            Dots,
            Progress
        }

        [SerializeField] private BannerCarousel m_Carousel;

        [Tooltip("指示器专用容器，应放在轮播视口之外。留空时使用当前物体。")] [SerializeField]
        private RectTransform m_Container;

        [Tooltip("可选按钮模板。Target Graphic 必须为 Image；进度样式需指定非空 Sprite，并将 Image 类型设为 Filled。")] [SerializeField]
        private Button m_Template;

        [SerializeField] private IndicatorStyle m_Style;
        [SerializeField] private bool m_Visible = true;
        [SerializeField] private bool m_HideForSinglePage = true;
        [SerializeField] private bool m_Clickable = true;
        [SerializeField] private bool m_AutomaticLayout = true;
        [SerializeField] private Vector2 m_Size = new(12f, 12f);
        [SerializeField, Min(0f)] private float m_Spacing = 8f;
        [SerializeField] private Color m_NormalColor = new(1f, 1f, 1f, 0.4f);
        [SerializeField] private Color m_SelectedColor = Color.white;
        [SerializeField, Min(0.1f)] private float m_SelectedScale = 1.2f;

        // 两个列表按页面索引一一对应，只缓存本组件生成的按钮及其目标图像。
        private readonly List<Button> m_Buttons = new();
        private readonly List<Image> m_Images = new();
        private static Sprite s_DotSprite;
        private static Texture2D s_DotTexture;

        /// <summary>关联的轮播组件；重新赋值时重建指示器。</summary>
        public BannerCarousel Carousel {
            get => m_Carousel;
            set {
                m_Carousel = value;
                Rebuild();
            }
        }

        /// <summary>显示样式；改变时重建，确保默认进度图像的层级和填充方式正确。</summary>
        public IndicatorStyle Style {
            get => m_Style;
            set {
                if (m_Style == value) {
                    return;
                }

                m_Style = value;
                Rebuild();
            }
        }

        /// <summary>自定义按钮模板；设为空时恢复内置外观，改变时重建。</summary>
        public Button Template {
            get => m_Template;
            set {
                if (m_Template == value) {
                    return;
                }

                m_Template = value;
                Rebuild();
            }
        }

        /// <summary>自动布局时的指示器尺寸，各轴至少为 1 个 UI 单位；赋值时重建。</summary>
        public Vector2 Size {
            get => m_Size;
            set {
                m_Size = Vector2.Max(Vector2.one, value);
                Rebuild();
            }
        }

        /// <summary>选中页的颜色，在下一次状态同步时生效，无需重建。</summary>
        public Color SelectedColor {
            get => m_SelectedColor;
            set => m_SelectedColor = value;
        }

        /// <summary>本组件的显示开关；最终还受轮播总开关和单页隐藏配置约束。</summary>
        public bool Visible {
            get => m_Visible;
            set => m_Visible = value;
        }

        private void LateUpdate() {
            // 轮播在 Update 中推进状态，这里读取本帧最新的页码与进度。
            int count = m_Carousel != null ? m_Carousel.Count : 0;

            if (m_Buttons.Count != count) {
                Rebuild();
            }

            bool visible = m_Visible && m_Carousel != null && m_Carousel.ShowIndicators
                           && (!m_HideForSinglePage || count > 1);

            for (var i = 0; i < m_Buttons.Count; i++) {
                var button = m_Buttons[i];
                button.gameObject.SetActive(visible);

                if (!visible) {
                    continue;
                }

                bool selected = i == m_Carousel.CurrentIndex;

                // 拖拽或过渡期间禁用跳转，避免连续点击产生相互冲突的切换请求。
                button.interactable = m_Clickable && !m_Carousel.IsTransitioning;
                button.transform.localScale = Vector3.one * (selected ? m_SelectedScale : 1f);
                var image = m_Images[i];

                if (image == null) {
                    continue;
                }

                image.color = selected ? m_SelectedColor : m_NormalColor;

                if (m_Template == null && m_Style == IndicatorStyle.Progress) {
                    button.GetComponent<Image>().color = m_NormalColor;
                }

                // 只有当前页显示停留进度；单页没有自动翻页，直接显示完整填充。
                image.fillAmount = m_Style == IndicatorStyle.Progress
                    ? (selected ? (count == 1 ? 1f : m_Carousel.Progress) : 0f) : 1f;
            }
        }

        /// <summary>清理已生成的实例，按当前页数、模板和布局配置重新创建。</summary>
        public void Rebuild() {
            Clear();

            if (m_Carousel == null) {
                return;
            }

            var container = m_Container != null ? m_Container : transform;

            for (var i = 0; i < m_Carousel.Count; i++) {
                Button button;

                if (m_Template != null) {
                    button = Instantiate(m_Template, container, false);
                } else {
                    var go = new GameObject($"Indicator {i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
                    go.transform.SetParent(container, false);
                    button = go.GetComponent<Button>();

                    var image = go.GetComponent<Image>();
                    button.targetGraphic = image;
                    image.sprite = DotSprite;

                    if (m_Style == IndicatorStyle.Progress) {
                        // 父图像保留为底图和点击区域，子图像负责填充；进度为零时仍可见、可点击。
                        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                        fill.transform.SetParent(go.transform, false);

                        var fillRect = (RectTransform)fill.transform;
                        fillRect.anchorMin = Vector2.zero;
                        fillRect.anchorMax = Vector2.one;
                        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
                        image = fill.GetComponent<Image>();
                        image.sprite = DotSprite;
                        image.raycastTarget = false;
                        button.targetGraphic = image;
                        image.type = Image.Type.Filled;
                        image.fillMethod = Image.FillMethod.Radial360;
                        image.fillOrigin = 2;
                    }
                }

                // 颜色由本组件统一控制，避免按钮自身的状态过渡覆盖选中样式。
                button.transition = Selectable.Transition.None;

                // 每个回调捕获独立的页面索引，不能直接捕获 for 循环变量。
                int index = i;
                button.onClick.AddListener(() => m_Carousel.GoTo(index));

                if (m_AutomaticLayout) {
                    var rt = (RectTransform)button.transform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = m_Size;

                    // 以整组中心为原点排列，奇数和偶数页数都能水平居中。
                    rt.anchoredPosition = new Vector2((i - (m_Carousel.Count - 1) * 0.5f) * (m_Size.x + m_Spacing), 0f);
                }

                m_Buttons.Add(button);
                m_Images.Add(button.targetGraphic as Image);
            }
        }

        private void OnDisable() {
            // 容器可能位于外部层级；禁用组件时也要隐藏实例，但保留它们供再次启用时使用。
            foreach (var button in m_Buttons) {
                if (button != null) {
                    button.gameObject.SetActive(false);
                }
            }
        }

        private void OnDestroy() => Clear();

        private void Clear() {
            foreach (var button in m_Buttons) {
                if (button == null) {
                    continue;
                }

                // 运行时销毁会延迟到帧末，先隐藏可避免重建当帧出现两组指示器。
                button.gameObject.SetActive(false);

                if (Application.isPlaying) {
                    Destroy(button.gameObject);
                } else {
                    DestroyImmediate(button.gameObject);
                }
            }

            m_Buttons.Clear();
            m_Images.Clear();
        }

        /// <summary>按需生成并在所有指示器组件间共享圆点贴图，无需额外图片资源。</summary>
        private static Sprite DotSprite {
            get {
                if (s_DotSprite != null) {
                    return s_DotSprite;
                }

                const int k_Size = 32;

                s_DotTexture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false) {
                    name = "Carousel Dot", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear
                };

                var pixels = new Color[k_Size * k_Size];

                for (var y = 0; y < k_Size; y++) {
                    for (var x = 0; x < k_Size; x++) {
                        // 在像素中心计算到圆心的距离，以一像素宽的透明度渐变柔化圆周边缘。
                        float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * k_Size * 0.5f);
                        pixels[y * k_Size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(k_Size * 0.5f - distance));
                    }
                }

                s_DotTexture.SetPixels(pixels);

                // 上传后不再修改像素，释放 CPU 侧的可读数据副本。
                s_DotTexture.Apply(false, true);
                s_DotSprite = Sprite.Create(s_DotTexture, new Rect(0, 0, k_Size, k_Size), Vector2.one * 0.5f);
                s_DotSprite.hideFlags = HideFlags.HideAndDontSave;
                return s_DotSprite;
            }
        }

        // 每次进入运行环境时清理共享资源，兼容编辑器关闭域重载的情况。
        // 不在单个组件销毁时释放，避免影响其他仍在使用圆点的组件。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSprite() {
            if (s_DotSprite != null) {
                Destroy(s_DotSprite);
            }

            if (s_DotTexture != null) {
                Destroy(s_DotTexture);
            }

            s_DotSprite = null;
            s_DotTexture = null;
        }
    }
}