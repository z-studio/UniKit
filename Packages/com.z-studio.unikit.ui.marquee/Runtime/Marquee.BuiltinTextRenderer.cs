using TMPro;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    [RequireComponent(typeof(RectTransform))]
    public partial class Marquee {
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

            public Vector2 Bind(RectTransform view, MarqueeSegment segment, MarqueeRenderContext context) {
                var seg = (MarqueeTextSegment)segment;
                var tmp = view.GetComponent<TextMeshProUGUI>() ?? view.GetComponentInChildren<TextMeshProUGUI>(true);

                if (tmp == null) {
                    return Vector2.zero;
                }

                string content = seg.Text ?? "";
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.text = content;
                return tmp.GetPreferredValues(content, Mathf.Infinity, Mathf.Infinity);
            }

            public void OnRecycle(RectTransform view) {
                var text = view.GetComponent<TextMeshProUGUI>();

                if (text != null) {
                    text.text = string.Empty;
                }
            }
        }
    }
}