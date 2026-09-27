using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>只依赖 UGUI 的最小接入示例，运行时创建轮播、按钮和指示器。</summary>
    public sealed class MinimalDemo : MonoBehaviour {
        // 可选输入适配程序集在场景加载前注册，示例本身仍只依赖 UGUI。
        public static System.Action<GameObject> ConfigureInputModule { private get; set; }

        [SerializeField] private Sprite[] m_Images;
        private BannerCarousel m_Carousel;
        private Text m_Status;
        private Font m_Font;

        private void Awake() {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960f, 640f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = Rect("Background", canvas.transform, Vector2.zero, new Vector2(960f, 640f));
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.sizeDelta = Vector2.zero;
            background.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f);
            Label("Title", canvas.transform, "BANNER CAROUSEL / UGUI", new Vector2(0f, 260f), new Vector2(700f, 40f));

            var viewport = Rect("Carousel", canvas.transform, new Vector2(0f, 30f), new Vector2(720f, 360f));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.21f);
            m_Carousel = viewport.gameObject.AddComponent<BannerCarousel>();
            m_Carousel.DwellDuration = 3f;
            var pages = new BannerPage[m_Images.Length];
            for (var i = 0; i < pages.Length; i++) pages[i] = new BannerPage { Sprite = m_Images[i] };
            m_Carousel.SetPages(pages);

            var indicators = Rect("Indicators", canvas.transform, new Vector2(0f, -170f), new Vector2(400f, 24f))
                .gameObject.AddComponent<BannerCarouselIndicators>();
            indicators.Carousel = m_Carousel;
            Button("Previous", canvas.transform, -210f).onClick.AddListener(m_Carousel.Previous);
            Button("Pause / Resume", canvas.transform, 0f).onClick.AddListener(() => {
                if (m_Carousel.IsPaused) m_Carousel.Resume();
                else m_Carousel.Pause();
            });
            Button("Next", canvas.transform, 210f).onClick.AddListener(m_Carousel.Next);
            m_Status = Label("Status", canvas.transform, "", new Vector2(0f, -280f), new Vector2(850f, 35f));

            var events = EventSystem.current;
            if (events == null) {
                events = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
                events.transform.SetParent(transform, false);
            }

            // 复用场景已有的有效模块；只有空 EventSystem 时补齐输入。
            foreach (var module in events.GetComponents<BaseInputModule>()) {
                if (module.isActiveAndEnabled) return;
            }

            if (ConfigureInputModule != null) {
                ConfigureInputModule(events.gameObject);
            } else {
#if ENABLE_LEGACY_INPUT_MANAGER
                events.gameObject.AddComponent<StandaloneInputModule>();
#else
                Debug.LogError("[MinimalDemo] No input adapter is available. Include the sample's InputSystem folder "
                               + "when using the new Input System.", this);
#endif
            }
        }

        private void LateUpdate() {
            m_Status.text = $"{m_Carousel.CurrentIndex + 1} / {m_Carousel.Count}    "
                            + $"{(m_Carousel.IsPaused ? "PAUSED" : "PLAYING")}    Drag or use the buttons";
        }

        private Button Button(string label, Transform parent, float x) {
            var rect = Rect(label, parent, new Vector2(x, -225f), new Vector2(185f, 44f));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.27f, 0.37f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label("Label", rect, label, Vector2.zero, rect.sizeDelta);
            return button;
        }

        private Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size) {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size) {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
