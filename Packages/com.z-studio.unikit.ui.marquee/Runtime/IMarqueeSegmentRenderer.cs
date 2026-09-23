using System.Collections.Generic;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 片段渲染器：负责把某种 <see cref="MarqueeSegment"/> 渲染成一个 UI 视图（RectTransform），
    /// 并提供创建 / 绑定 / 回收。视图由跑马灯统一做水平排列与对象池管理。
    /// 核心库内置文本/图片渲染器；spine 等外部类型通过
    /// <see cref="MarqueeSegmentRendererRegistry"/> 注册。
    /// </summary>
    public interface IMarqueeSegmentRenderer {
        /// <summary>对象池分类键：相同 Key 的视图可互相复用（建议每种渲染器一个稳定常量）。</summary>
        string Key { get; }

        /// <summary>能否渲染该片段。</summary>
        bool CanRender(MarqueeSegment segment);

        /// <summary>池为空时创建一个新视图（挂到 parent 下，可处于未激活状态）。</summary>
        RectTransform CreateView(Transform parent);

        /// <summary>把片段内容绑定到视图，并返回该视图应占用的尺寸（UI 本地单位，宽 × 高）；必须覆盖旧内容的显示状态。</summary>
        Vector2 Bind(RectTransform view, MarqueeSegment segment);

        /// <summary>视图回收前的清理（如停止 spine 动画、释放引用）。无需清理可空实现。</summary>
        void OnRecycle(RectTransform view);
    }

    /// <summary>
    /// 外部片段渲染器（如 spine）的全局注册表。
    /// 核心库内置的文本/图片渲染器无需在此注册；扩展程序集通常在
    /// <c>[RuntimeInitializeOnLoadMethod]</c> 中调用 <see cref="Register"/>。
    /// </summary>
    public static class MarqueeSegmentRendererRegistry {
        private static readonly List<IMarqueeSegmentRenderer> s_Renderers = new();

        public static IReadOnlyList<IMarqueeSegmentRenderer> Renderers => s_Renderers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => s_Renderers.Clear();

        public static void Register(IMarqueeSegmentRenderer renderer) {
            if (renderer == null) {
                return;
            }
            
            if (string.IsNullOrEmpty(renderer.Key) || renderer.Key.StartsWith("builtin.")) {
                throw new System.ArgumentException("渲染器 Key 不能为空或使用内置前缀 builtin。", nameof(renderer));
            }

            foreach (var registered in s_Renderers) {
                if (registered.Key != renderer.Key) {
                    continue;
                }
                
                if (ReferenceEquals(registered, renderer)) {
                    return;
                }
                
                throw new System.InvalidOperationException($"跑马灯渲染器 Key 已注册：{renderer.Key}");
            }

            s_Renderers.Add(renderer);
        }

        public static void Unregister(IMarqueeSegmentRenderer renderer) {
            s_Renderers.Remove(renderer);
        }
    }
}