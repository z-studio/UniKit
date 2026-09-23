using System;
using Spine.Unity;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// spine 片段：在跑马灯条目中嵌入一个 <see cref="SkeletonGraphic"/> 动画。
    /// 属于可选扩展（UIMarquee.Spine 程序集），不影响核心库的零依赖。
    /// 由 <see cref="SpineSegmentRenderer"/> 渲染并在运行时自动注册。
    /// </summary>
    [Serializable]
    public sealed class SpineSegment : MarqueeSegment {
        [Tooltip("Spine 骨骼数据资产")]
        public SkeletonDataAsset SkeletonDataAsset;

        [Tooltip("皮肤名（可选，留空使用默认皮肤）")]
        public string SkinName = "";

        [Tooltip("初始播放动画名（可选）")]
        public string AnimationName = "";

        [Tooltip("为 true 时，等 Spine 片段完全进入跑马灯可视区域后再开始播放；为 false 时在绑定（生成/复用）时立即播放")]
        public bool PlayWhenFullyVisible = true;

        [Tooltip("动画是否循环")]
        public bool Loop = true;

        [Tooltip("动画播放速度倍率")]
        public float TimeScale = 1f;

        [Tooltip("骨骼整体缩放，同时影响布局占位尺寸")]
        public float Scale = 1f;

        [Tooltip("缩放前的布局尺寸（UI 本地单位，宽 × 高）；最终占位会乘以 Scale 的绝对值。x 或 y 为 0 时自动读取骨骼 setup pose 的边界尺寸（同 ImageSegment 的行为）；若骨骼边界也为 0，则该分量保持 0。")]
        public Vector2 Size = Vector2.zero;
    }
}
