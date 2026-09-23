using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>绑定用途与宿主环境。扩展无需搜索父节点或猜测隐藏层级。</summary>
    public readonly struct MarqueeRenderContext {
        public RectTransform Viewport { get; }
        public bool IsMeasuring { get; }
        public bool IgnoreTimeScale { get; }

        public MarqueeRenderContext(RectTransform viewport, bool isMeasuring, bool ignoreTimeScale) {
            Viewport = viewport;
            IsMeasuring = isMeasuring;
            IgnoreTimeScale = ignoreTimeScale;
        }
    }

    /// <summary>可选的无视图测量能力；结果必须与 Bind 返回的最终布局尺寸一致。</summary>
    public interface IMarqueeSegmentMeasurer {
        Vector2 Measure(MarqueeSegment segment);
    }

    /// <summary>可选的时间尺度控制。跟随宿主 IgnoreTimeScale；宿主 Pause 不暂停片段动画。</summary>
    public interface IMarqueeSegmentTimeControl {
        void SetTimeMode(RectTransform view, bool ignoreTimeScale);
    }
}
