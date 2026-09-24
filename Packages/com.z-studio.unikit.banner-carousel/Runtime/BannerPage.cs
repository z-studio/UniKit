using System;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>单页内容配置：可使用图片或完整的 UI 预制体（图片、Spine、文字等），同时配置时优先使用预制体。</summary>
    [Serializable]
    public sealed class BannerPage {
        [Tooltip("图片页使用的精灵；配置预制体时不使用此字段。")]
        public Sprite Sprite;

        [Tooltip("复合页面的预制体，根节点会拉伸到视口尺寸，具体布局应配置在子节点上。")]
        public RectTransform Prefab;
        
        [Tooltip("单页停留秒数，不含切换时间；零表示使用轮播的默认停留时长。")] [Min(0f)]
        public float Duration;

        [Tooltip("图片页是否保持原始宽高比；预制体页面由自身的布局决定。")]
        public bool PreserveAspect = true;
    }

    /// <summary>自动播放到达首尾时的处理方式。</summary>
    public enum BannerPlaybackMode {
        /// <summary>首尾相接，持续循环。</summary>
        Loop,

        /// <summary>到达端点后反向播放，不重复停留端点。</summary>
        PingPong,

        /// <summary>末页停留结束后停止，并触发播放完成事件。</summary>
        Once
    }

    /// <summary>程序或按钮切换时的过渡效果；手动拖拽始终使用跟手滑动。</summary>
    public enum BannerTransition {
        /// <summary>沿配置方向滑动切换。</summary>
        Slide,

        /// <summary>当前页与目标页交叉淡入淡出。</summary>
        CrossFade,

        /// <summary>直接切换，不播放过渡动画。</summary>
        Instant
    }
}