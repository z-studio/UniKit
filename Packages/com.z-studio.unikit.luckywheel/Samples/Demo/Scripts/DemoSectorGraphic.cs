using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.LuckyWheel.Samples {
    /// <summary>只用于 Demo 的不等分底图，不创建奖励内容，也不参与结果计算。</summary>
    public class DemoSectorGraphic : MaskableGraphic {
        private WheelController m_Wheel;
        private float[] m_Widths;
        private float m_FirstAngle;

        public void SetLayout(WheelController wheel, float firstAngle) {
            var changed = m_Wheel != wheel || !Mathf.Approximately(m_FirstAngle, firstAngle);
            m_Wheel = wheel;
            m_FirstAngle = firstAngle;

            if (m_Widths == null || m_Widths.Length != wheel.SectorsCount) {
                m_Widths = new float[wheel.SectorsCount];
                changed = true;
            }

            for (var index = 0; index < m_Widths.Length; index++) {
                var width = wheel.GetSectorWidth(index);
                changed |= !Mathf.Approximately(m_Widths[index], width);
                m_Widths[index] = width;
            }

            if (changed) {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh) {
            mesh.Clear();

            if (m_Widths == null || m_Widths.Length == 0) {
                return;
            }

            var rect = rectTransform.rect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            
            // firstAngle 指向第 0 格中心，因此第一条边界要向前退半格。
            // 这里用 sin 作 X、cos 作 Y：0° 在顶部，角度增加时沿顺时针，与奖格编号一致。
            var edge = -m_Widths[0] * 0.5f;

            for (var index = 0; index < m_Widths.Length; index++) {
                // 每个扇形分成不大于 3° 的小三角形，近似圆弧；颜色仅用于辨认格子。
                var segments = Mathf.Max(1, Mathf.CeilToInt(m_Widths[index] / 3));
                var color = Color.HSVToRGB((float)index / m_Widths.Length, 0.75f, 1);

                for (var segment = 0; segment < segments; segment++) {
                    var start = (m_FirstAngle + edge + m_Widths[index] * segment / segments) * Mathf.Deg2Rad;
                    var end = (m_FirstAngle + edge + m_Widths[index] * (segment + 1) / segments) * Mathf.Deg2Rad;
                    var vertex = mesh.currentVertCount;
                    mesh.AddVert(rect.center, color, Vector2.zero);
                    mesh.AddVert(rect.center + new Vector2(Mathf.Sin(start), Mathf.Cos(start)) * radius, color,
                        Vector2.zero);
                    mesh.AddVert(rect.center + new Vector2(Mathf.Sin(end), Mathf.Cos(end)) * radius, color,
                        Vector2.zero);
                    mesh.AddTriangle(vertex, vertex + 2, vertex + 1);
                }

                edge += m_Widths[index];
            }
        }
    }
}