using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    [RequireComponent(typeof(RectTransform))]
    public partial class Marquee {
        // ----------------------------------------------------------------
        // 内置片段渲染器（文本 / 图片）：克隆 contentTemplate 中的样式模板
        // ----------------------------------------------------------------
        private sealed class BuiltinImageRenderer : IMarqueeSegmentRenderer, IMarqueeSegmentMeasurer {
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

            public Vector2 Bind(RectTransform view, MarqueeSegment segment, MarqueeRenderContext context) {
                var seg = (MarqueeImageSegment)segment;
                var img = view.GetComponent<Image>() ?? view.GetComponentInChildren<Image>(true);

                if (img == null) {
                    return Vector2.zero;
                }

                img.sprite = seg.Sprite;
                img.enabled = seg.Sprite != null;

                return Measure(segment);
            }

            public Vector2 Measure(MarqueeSegment segment) {
                var seg = (MarqueeImageSegment)segment;
                Vector2 size = seg.Size;

                if (seg.Sprite != null) {
                    Vector2 original = seg.Sprite.rect.size;

                    if (size.x <= 0f) {
                        size.x = original.x;
                    }

                    if (size.y <= 0f) {
                        size.y = original.y;
                    }
                }

                return size;
            }

            public void OnRecycle(RectTransform view) {
                var image = view.GetComponent<Image>();

                if (image != null) {
                    image.sprite = null;
                }
            }
        }
    }
}