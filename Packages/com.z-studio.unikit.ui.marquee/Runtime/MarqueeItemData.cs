using System.Collections.Generic;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 跑马灯单条内容：由若干片段（文本 / 图片 / spine 等）按顺序水平排列组成，
    /// 支持「文本 + 图片 + spine」的任意组合。
    /// </summary>
    [System.Serializable]
    public class MarqueeItemData {
        [Tooltip("业务标识（可选）：用于在点击/事件回调中识别条目，例如公告 ID 或跳转链接")]
        public string ID = "";

        [Tooltip("内容片段：文本 / 图片 / spine 等任意组合，按顺序水平排列、整体居中、各段垂直居中")]
        [SerializeReference]
        public List<MarqueeSegment> Segments = new();

        [Tooltip("限定内容出现的次数，默认为 -1，表示一直重复出现。如果设置为 0，则一次都不会出现")]
        [Min(-1)]
        public int Cycles = -1;

        public MarqueeItemData() {
        }

        /// <summary>用一组片段构造条目（顺序即水平排列顺序）。</summary>
        public MarqueeItemData(params MarqueeSegment[] segments) {
            if (segments != null) {
                Segments.AddRange(segments);
            }
        }

        /// <summary>便捷构造：单段纯文本条目。</summary>
        public static MarqueeItemData Text(string text, string id = "", int cycles = -1) {
            return new MarqueeItemData(new MarqueeTextSegment(text)) { ID = id, Cycles = cycles };
        }

        /// <summary>便捷构造：单段纯图片条目。</summary>
        public static MarqueeItemData Image(Sprite sprite, string id = "", int cycles = -1) {
            return new MarqueeItemData(new MarqueeImageSegment(sprite)) { ID = id, Cycles = cycles };
        }
    }
}
