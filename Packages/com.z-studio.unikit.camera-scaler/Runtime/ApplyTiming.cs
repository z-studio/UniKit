namespace ZStudio.UniKit {
    /// <summary>运行模式下每帧应用结果的时机；编辑模式自动刷新，不受此选项限制。</summary>
    public enum ApplyTiming {
        /// <summary>在 Update 中写入。所有模式均在 OnEnable 首次适配，Start 可读取首次结果。</summary>
        Update,

        /// <summary>在 LateUpdate 中写入。需要通过脚本执行顺序保证晚于其他相机控制脚本。</summary>
        LateUpdate,

        /// <summary>在渲染前写入：Built-in 使用 OnPreCull，SRP 使用 beginCameraRendering。</summary>
        OnPreCull
    }
}
