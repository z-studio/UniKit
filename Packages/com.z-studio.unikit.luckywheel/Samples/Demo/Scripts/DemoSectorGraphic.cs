using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.LuckyWheel.Samples {
    /// <summary>只用于 Demo 的不等分底图，不创建奖励内容，也不参与结果计算。</summary>
    public class DemoSectorGraphic : MaskableGraphic {
        private WheelController m_Wheel;
        private float[] m_Widths;
        private SpinDirection m_Direction;
        private float m_FirstAngle;

        public void SetLayout(WheelController wheel, float firstAngle) {
            var changed = m_Wheel != wheel || m_Direction != wheel.SectorDirection || m_FirstAngle != firstAngle;
            m_Wheel = wheel;
            m_Direction = wheel.SectorDirection;
            m_FirstAngle = firstAngle;
            if (m_Widths == null || m_Widths.Length != wheel.SectorsCount) {
                m_Widths = new float[wheel.SectorsCount];
                changed = true;
            }
            for (var index = 0; index < m_Widths.Length; index++) {
                var width = wheel.GetSectorWidth(index);
                changed |= m_Widths[index] != width;
                m_Widths[index] = width;
            }
            if (changed) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh) {
            mesh.Clear();
            if (m_Widths == null || m_Widths.Length == 0) return;
            var rect = rectTransform.rect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            var direction = m_Direction == SpinDirection.ClockWise ? 1 : -1;
            var edge = -m_Widths[0] * 0.5f;
            for (var index = 0; index < m_Widths.Length; index++) {
                var segments = Mathf.Max(1, Mathf.CeilToInt(m_Widths[index] / 3));
                var color = Color.HSVToRGB((float)index / m_Widths.Length, 0.75f, 1);
                for (var segment = 0; segment < segments; segment++) {
                    var start = (m_FirstAngle + direction * (edge + m_Widths[index] * segment / segments)) * Mathf.Deg2Rad;
                    var end = (m_FirstAngle + direction * (edge + m_Widths[index] * (segment + 1) / segments)) * Mathf.Deg2Rad;
                    var vertex = mesh.currentVertCount;
                    mesh.AddVert(rect.center, color, Vector2.zero);
                    mesh.AddVert(rect.center + new Vector2(Mathf.Sin(start), Mathf.Cos(start)) * radius, color, Vector2.zero);
                    mesh.AddVert(rect.center + new Vector2(Mathf.Sin(end), Mathf.Cos(end)) * radius, color, Vector2.zero);
                    if (direction > 0) mesh.AddTriangle(vertex, vertex + 2, vertex + 1);
                    else mesh.AddTriangle(vertex, vertex + 1, vertex + 2);
                }
                edge += m_Widths[index];
            }
        }
    }
}
