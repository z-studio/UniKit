using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>只依赖 UGUI 的最小接入示例，运行时创建轮播、按钮和指示器。</summary>
    public sealed class MinimalDemo : MonoBehaviour {
        [SerializeField]
        private Sprite[] m_Images;

        [SerializeField]
        private Button m_Previous, m_PauseResume, m_Next;

        [SerializeField]
        private BannerCarousel m_Carousel;

        [SerializeField]
        private TextMeshProUGUI m_Status;

        private void Awake() {
            var pages = new BannerPage[m_Images.Length];

            for (var i = 0; i < pages.Length; i++) {
                pages[i] = new BannerPage { Sprite = m_Images[i] };
            }

            m_Carousel.SetPages(pages);

            m_Previous.onClick.AddListener(m_Carousel.Previous);

            m_PauseResume.onClick.AddListener(() => {
                if (m_Carousel.IsPaused) {
                    m_Carousel.Resume();
                } else {
                    m_Carousel.Pause();
                }
            });

            m_Next.onClick.AddListener(m_Carousel.Next);
        }

        private void LateUpdate() {
            m_Status.text = $"{m_Carousel.CurrentIndex + 1} / {m_Carousel.Count}    "
                            + $"{(m_Carousel.IsPaused ? "PAUSED" : "PLAYING")}    Drag or use the buttons";
        }
    }
}