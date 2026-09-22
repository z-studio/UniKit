namespace ZStudio.UniKit {
    /// <summary>屏幕宽高比变化时可采用的适配策略。</summary>
    public enum ScaleMode {
        /// <summary>保持参考分辨率下的垂直可视范围。</summary>
        ConstantHeight,

        /// <summary>保持参考分辨率下的水平可视范围。</summary>
        ConstantWidth,

        /// <summary>在保持宽度和保持高度之间按权重插值。</summary>
        MatchWidthOrHeight,

        /// <summary>确保参考分辨率内的区域始终可见，必要时扩展额外可视区域。</summary>
        Expand,

        /// <summary>避免显示参考分辨率之外的区域，必要时裁减可视区域。</summary>
        Shrink
    }
}
