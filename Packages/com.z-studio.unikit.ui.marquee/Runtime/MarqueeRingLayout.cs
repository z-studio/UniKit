using System;
using System.Collections.Generic;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 与 GameObject 无关的环形几何进度。Offset 是当前逻辑单元流出侧边缘越过视口边缘的距离。
    /// 单帧可跨越任意多个条目；实例化窗口的大小不会影响播放进度。
    /// </summary>
    internal sealed class MarqueeRingLayout {
        internal readonly struct Placement {
            public readonly long Occurrence;
            public readonly int Index;
            public readonly float Center;

            public Placement(long occurrence, int index, float center) {
                Occurrence = occurrence;
                Index = index;
                Center = center;
            }
        }

        private readonly List<float> m_Lengths = new();
        private double m_Length;
        private double m_Offset;
        private float m_Spacing;
        private long m_First;

        public void Reset(IReadOnlyList<float> lengths, float spacing) {
            if (float.IsNaN(spacing) || float.IsInfinity(spacing)) {
                throw new ArgumentOutOfRangeException(nameof(spacing));
            }

            m_Lengths.Clear();
            m_Length = m_Offset = 0;
            m_First = 0;
            m_Spacing = Math.Max(0f, spacing);

            foreach (float length in lengths) {
                if (length <= 0 || float.IsNaN(length) || float.IsInfinity(length)) {
                    throw new ArgumentOutOfRangeException(nameof(lengths));
                }

                m_Lengths.Add(length);
                m_Length += length + (double)m_Spacing;
            }
        }

        public void Advance(float distance) {
            if (m_Lengths.Count == 0 || distance <= 0) {
                return;
            }

            if (float.IsNaN(distance) || float.IsInfinity(distance)) {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }

            // 完整周期具有相同布局，直接跳过；只处理余下不足一圈的距离。
            m_Offset += distance % m_Length;

            while (m_Offset >= m_Lengths[(int)(m_First % m_Lengths.Count)] + (double)m_Spacing) {
                m_Offset -= m_Lengths[(int)(m_First % m_Lengths.Count)] + (double)m_Spacing;
                m_First++;
            }
        }

        public void GetPlacements(float viewport, List<Placement> result) {
            if (float.IsNaN(viewport) || float.IsInfinity(viewport)) {
                throw new ArgumentOutOfRangeException(nameof(viewport));
            }

            result.Clear();

            if (m_Lengths.Count == 0) {
                return;
            }

            double leading = Math.Max(0f, viewport) * 0.5 + m_Offset;
            double entrance = -Math.Max(0f, viewport) * 0.5;
            long occurrence = m_First;

            while (true) {
                int index = (int)(occurrence % m_Lengths.Count);
                float length = m_Lengths[index];

                if (leading - length <= Math.Max(0f, viewport) * 0.5) {
                    result.Add(new Placement(occurrence, index, (float)(leading - length * 0.5)));
                }

                // 保留一个完全在入口外的待命单元。
                if (leading <= entrance) {
                    break;
                }

                leading -= length + (double)m_Spacing;
                occurrence++;
            }
        }
    }
}