using System.Collections.Generic;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 播放模式：循环 或 单次
    /// </summary>
    public enum EMarqueePlayMode {
        Loop,
        Once
    }

    /// <summary>
    /// 滚动模式：Sequential 逐条轮播；Continuous 无缝连续滚动
    /// </summary>
    public enum EMarqueeScrollMode {
        Sequential,
        Continuous
    }

    /// <summary>
    /// 滚动方向
    /// </summary>
    public enum EMarqueeDirection {
        Left,
        Right,
        Up,
        Down
    }

    /// <summary>
    /// 滚动缓动类型（仅 Sequential 模式生效）。涵盖主流缓动家族；Custom 表示使用自定义 AnimationCurve。
    /// 命名与公式遵循 easings.net 约定。
    /// </summary>
    public enum EMarqueeEase {
        Linear,
        SineIn,
        SineOut,
        SineInOut,
        QuadIn,
        QuadOut,
        QuadInOut,
        CubicIn,
        CubicOut,
        CubicInOut,
        QuartIn,
        QuartOut,
        QuartInOut,
        QuintIn,
        QuintOut,
        QuintInOut,
        ExpoIn,
        ExpoOut,
        ExpoInOut,
        CircIn,
        CircOut,
        CircInOut,
        BackIn,
        BackOut,
        BackInOut,
        ElasticIn,
        ElasticOut,
        ElasticInOut,
        BounceIn,
        BounceOut,
        BounceInOut,
        Custom
    }

    /// <summary>
    /// 跑马灯纯计算逻辑：不依赖任何运行时渲染状态，便于单元测试。
    /// </summary>
    public static class MarqueeMath {
        /// <summary>是否为水平方向。</summary>
        public static bool IsHorizontal(EMarqueeDirection dir) {
            return dir == EMarqueeDirection.Left || dir == EMarqueeDirection.Right;
        }

        /// <summary>取尺寸在滚动轴上的分量。</summary>
        public static float AxisSize(EMarqueeDirection dir, Vector2 size) {
            return IsHorizontal(dir) ? size.x : size.y;
        }

        /// <summary>滚动轴上的方向符号（Right/Up 为 +1，Left/Down 为 -1）。</summary>
        public static float FlowSign(EMarqueeDirection dir) {
            return dir is EMarqueeDirection.Right or EMarqueeDirection.Up ? 1f : -1f;
        }

        /// <summary>滚动方向的单位向量（内容流出的方向）。</summary>
        public static Vector2 FlowVector(EMarqueeDirection dir) {
            return dir switch {
                EMarqueeDirection.Left => new Vector2(-1f, 0f),
                EMarqueeDirection.Right => new Vector2(1f, 0f),
                EMarqueeDirection.Up => new Vector2(0f, 1f),
                EMarqueeDirection.Down => new Vector2(0f, -1f),
                _ => new Vector2(-1f, 0f)
            };
        }

        /// <summary>内容在滚动轴上的尺寸是否超过视口（需要滚动）。</summary>
        public static bool IsOverflow(EMarqueeDirection dir, Vector2 viewportSize, Vector2 contentSize) {
            return AxisSize(dir, contentSize) > AxisSize(dir, viewportSize) + 0.01f;
        }

        /// <summary>
        /// 计算逐条滚动模式下，内容（anchor/pivot 居中）的起止 anchoredPosition。
        /// 起点：内容贴“流出边”内侧 margin 处（初始即可见）；终点：内容完全移出“流出边”。
        /// </summary>
        public static void ComputeScrollPositions(
            EMarqueeDirection dir,
            Vector2 viewportSize,
            Vector2 contentSize,
            float margin,
            out Vector2 start,
            out Vector2 end
        ) {
            float vp = AxisSize(dir, viewportSize);
            float content = AxisSize(dir, contentSize);
            float f = FlowSign(dir);

            float startScalar = f * (vp * 0.5f - margin - content * 0.5f);
            float endScalar = f * (vp * 0.5f + content * 0.5f);

            if (IsHorizontal(dir)) {
                start = new Vector2(startScalar, 0f);
                end = new Vector2(endScalar, 0f);
            } else {
                start = new Vector2(0f, startScalar);
                end = new Vector2(0f, endScalar);
            }
        }

        /// <summary>
        /// 计算下一个可播放条目的索引（cycles==0 表示该条目被禁用并跳过）。
        /// 不会修改输入列表，因此不存在“边播边删导致索引错位”的问题。
        /// </summary>
        /// <param name="cycles">每个条目的剩余出现次数（-1 无限，0 跳过，&gt;0 限定）。</param>
        /// <param name="from">上一次播放的索引（首次传入 -1）。</param>
        /// <param name="loop">是否循环模式。</param>
        /// <param name="loopCompleted">本次推进是否跨越了列表末尾（完成了一轮）。</param>
        /// <returns>下一个可播放索引；若无可播放条目则返回 -1。</returns>
        public static int GetNextPlayableIndex(IReadOnlyList<int> cycles, int from, bool loop, out bool loopCompleted) {
            loopCompleted = false;

            int count = cycles?.Count ?? 0;

            if (count == 0) {
                return -1;
            }

            for (int step = 1; step <= count; step++) {
                int idx = from + step;

                if (idx >= count) {
                    if (!loop) {
                        return -1;
                    }

                    idx %= count;
                    loopCompleted = true;
                }

                if (cycles[idx] != 0) {
                    return idx;
                }
            }

            return -1;
        }

        /// <summary>是否还存在任意可播放（cycles!=0）的条目。</summary>
        public static bool HasPlayable(IReadOnlyList<int> cycles) {
            if (cycles == null) {
                return false;
            }

            for (var i = 0; i < cycles.Count; i++) {
                if (cycles[i] != 0) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 计算缓动后的进度（输入裁剪到 [0,1]；Back/Elastic/Bounce 输出可超出 [0,1] 形成过冲/回弹）。
        /// 公式遵循 easings.net，无外部依赖。Custom 无曲线信息，按线性返回（由调用方用 AnimationCurve 处理）。
        /// </summary>
        public static float Evaluate(EMarqueeEase ease, float t) {
            t = Mathf.Clamp01(t);

            const float c1 = 1.70158f;
            const float c2 = c1 * 1.525f;
            const float c3 = c1 + 1f;
            const float c4 = 2f * Mathf.PI / 3f;
            const float c5 = 2f * Mathf.PI / 4.5f;

            switch (ease) {
                case EMarqueeEase.Linear:
                    return t;

                case EMarqueeEase.SineIn:
                    return 1f - Mathf.Cos(t * Mathf.PI / 2f);
                case EMarqueeEase.SineOut:
                    return Mathf.Sin(t * Mathf.PI / 2f);
                case EMarqueeEase.SineInOut:
                    return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

                case EMarqueeEase.QuadIn:
                    return t * t;
                case EMarqueeEase.QuadOut:
                    return 1f - (1f - t) * (1f - t);
                case EMarqueeEase.QuadInOut:
                    return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

                case EMarqueeEase.CubicIn:
                    return t * t * t;
                case EMarqueeEase.CubicOut:
                    return 1f - Mathf.Pow(1f - t, 3f);
                case EMarqueeEase.CubicInOut:
                    return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

                case EMarqueeEase.QuartIn:
                    return t * t * t * t;
                case EMarqueeEase.QuartOut:
                    return 1f - Mathf.Pow(1f - t, 4f);
                case EMarqueeEase.QuartInOut:
                    return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f;

                case EMarqueeEase.QuintIn:
                    return t * t * t * t * t;
                case EMarqueeEase.QuintOut:
                    return 1f - Mathf.Pow(1f - t, 5f);
                case EMarqueeEase.QuintInOut:
                    return t < 0.5f ? 16f * t * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 5f) / 2f;

                case EMarqueeEase.ExpoIn:
                    return Mathf.Approximately(t, 0f) ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case EMarqueeEase.ExpoOut:
                    return Mathf.Approximately(t, 1f) ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case EMarqueeEase.ExpoInOut:
                    if (Mathf.Approximately(t, 0f)) {
                        return 0f;
                    }

                    if (Mathf.Approximately(t, 1f)) {
                        return 1f;
                    }

                    return t < 0.5f ? Mathf.Pow(2f, 20f * t - 10f) / 2f : (2f - Mathf.Pow(2f, -20f * t + 10f)) / 2f;

                case EMarqueeEase.CircIn:
                    return 1f - Mathf.Sqrt(1f - t * t);
                case EMarqueeEase.CircOut:
                    return Mathf.Sqrt(1f - (t - 1f) * (t - 1f));
                case EMarqueeEase.CircInOut:
                    return t < 0.5f
                        ? (1f - Mathf.Sqrt(1f - Mathf.Pow(2f * t, 2f))) / 2f
                        : (Mathf.Sqrt(1f - Mathf.Pow(-2f * t + 2f, 2f)) + 1f) / 2f;

                case EMarqueeEase.BackIn:
                    return c3 * t * t * t - c1 * t * t;
                case EMarqueeEase.BackOut:
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                case EMarqueeEase.BackInOut:
                    return t < 0.5f
                        ? Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2) / 2f
                        : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (2f * t - 2f) + c2) + 2f) / 2f;

                case EMarqueeEase.ElasticIn:
                    if (Mathf.Approximately(t, 0f))
                        return 0f;

                    if (Mathf.Approximately(t, 1f))
                        return 1f;

                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * c4);
                case EMarqueeEase.ElasticOut:
                    if (Mathf.Approximately(t, 0f)) {
                        return 0f;
                    }

                    if (Mathf.Approximately(t, 1f)) {
                        return 1f;
                    }

                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
                case EMarqueeEase.ElasticInOut:
                    if (Mathf.Approximately(t, 0f)) {
                        return 0f;
                    }

                    if (Mathf.Approximately(t, 1f)) {
                        return 1f;
                    }

                    return t < 0.5f
                        ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * c5)) / 2f
                        : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * c5) / 2f + 1f;

                case EMarqueeEase.BounceIn:
                    return 1f - BounceOut(1f - t);
                case EMarqueeEase.BounceOut:
                    return BounceOut(t);
                case EMarqueeEase.BounceInOut:
                    return t < 0.5f
                        ? (1f - BounceOut(1f - 2f * t)) / 2f
                        : (1f + BounceOut(2f * t - 1f)) / 2f;

                default:
                    return t; // Custom 等
            }
        }

        private static float BounceOut(float t) {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1) {
                return n1 * t * t;
            }

            if (t < 2f / d1) {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }

            if (t < 2.5f / d1) {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        /// <summary>
        /// 无缝环形复用：单元是否已沿流向完全滚出视口的“流出边”（其整体越过 +f 侧边界），应绕回入场端复用。
        /// </summary>
        public static bool ContinuousUnitFullyExited(EMarqueeDirection dir, float center, float axisSize, float viewportAxis) {
            float f = FlowSign(dir);
            return f * center > viewportAxis * 0.5f + axisSize * 0.5f;
        }
    }
}