using Spine.Unity;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 挂在 Spine 片段视图上：武装后每帧检测是否完全进入跑马灯 viewport，
    /// 首次满足时再 <see cref="SkeletonGraphic.AnimationState"/> 播放动画。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class SpinePlayWhenVisible : MonoBehaviour {
        private static readonly Vector3[] s_Corners = new Vector3[4];

        private SkeletonGraphic m_Skeleton;
        private RectTransform m_Viewport;
        private string m_AnimationName;
        private bool m_Loop;
        private float m_TimeScale;
        private bool m_Armed;

        public void Arm(SkeletonGraphic skeleton, RectTransform viewport, string animationName, bool loop,
            float timeScale) {
            m_Skeleton = skeleton;
            m_Viewport = viewport;
            m_AnimationName = animationName;
            m_Loop = loop;
            m_TimeScale = timeScale;
            m_Armed = skeleton != null
                      && viewport != null
                      && !string.IsNullOrEmpty(animationName);
            enabled = m_Armed;
        }

        public void Disarm() {
            m_Armed = false;
            enabled = false;
            m_Skeleton = null;
            m_Viewport = null;
            m_AnimationName = null;
        }

        private void LateUpdate() {
            if (!m_Armed || m_Skeleton == null || m_Viewport == null) {
                Disarm();
                return;
            }

            if (!IsFullyInside((RectTransform)transform, m_Viewport)) {
                return;
            }

            if (m_Skeleton.AnimationState != null) {
                m_Skeleton.AnimationState.SetAnimation(0, m_AnimationName, m_Loop);
                m_Skeleton.timeScale = m_TimeScale;
            }

            Disarm();
        }

        /// <summary>
        /// 判断 inner 是否完全落在 outer 的本地矩形内。
        /// 若 inner 在某轴上大于 outer（无法“完全进入”），该轴退化为有重叠即视为满足。
        /// </summary>
        internal static bool IsFullyInside(RectTransform inner, RectTransform outer) {
            if (inner == null || outer == null) {
                return false;
            }

            inner.GetWorldCorners(s_Corners);

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (var i = 0; i < 4; i++) {
                Vector3 local = outer.InverseTransformPoint(s_Corners[i]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }

            Rect r = outer.rect;
            const float k_E = 0.01f;

            bool xOk = AxisSatisfied(minX, maxX, r.xMin, r.xMax, k_E);
            bool yOk = AxisSatisfied(minY, maxY, r.yMin, r.yMax, k_E);
            return xOk && yOk;
        }

        private static bool AxisSatisfied(float min, float max, float outerMin, float outerMax, float e) {
            float innerSize = max - min;
            float outerSize = outerMax - outerMin;

            if (innerSize > outerSize + e) {
                // 无法完全装入：有重叠即通过
                return max >= outerMin - e && min <= outerMax + e;
            }

            return min >= outerMin - e && max <= outerMax + e;
        }
    }
}