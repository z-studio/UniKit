using Spine.Unity;
using UnityEngine;

namespace ZStudio.UniKit.UI.Spine {
    /// <summary>可选的 Spine 4.2 页面适配器，挂在复合页面中各个 SkeletonGraphic 所在的物体上。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkeletonGraphic))]
    public sealed class BannerSpinePage : BannerPageBehaviour {
        [SerializeField]
        private SkeletonGraphic m_Skeleton;

        [Tooltip("留空时使用 SkeletonGraphic 的 Starting Animation。")]
        [SpineAnimation, SerializeField]
        private string m_Animation;

        [SerializeField]
        private bool m_Loop = true;

        [Tooltip("再次进入页面时从头播放；关闭后保留已有动画的播放进度。")]
        [SerializeField]
        private bool m_RestartOnSelection = true;

        [Tooltip("进入过程中保持姿势，页面停稳并选中后再开始播放。")]
        [SerializeField]
        private bool m_WaitUntilSelected = true;

        [SerializeField, Min(0f)]
        private float m_TimeScale = 1f;

        [SerializeField]
        private bool m_UnscaledTime = true;

        // 记录已播放的动画状态；骨骼重建后状态实例变化，需要重新建立动画轨道。
        private global::Spine.AnimationState m_PlayedState;

        /// <summary>初始化骨骼，并按配置准备进入期间的姿势与播放状态。</summary>
        public override void OnPageShown() {
            if (m_Skeleton == null) {
                m_Skeleton = GetComponent<SkeletonGraphic>();
            }

            m_Skeleton.Initialize(false);
            m_Skeleton.UnscaledTime = m_UnscaledTime;
            m_Skeleton.timeScale = m_WaitUntilSelected ? 0f : m_TimeScale;

            // 页面进入时就准备目标动画；等待选中只暂停时间，不能显示默认动画或 Setup Pose。
            PlayAnimation();
        }

        /// <summary>页面停稳后开始演出，并恢复配置的动画速度。</summary>
        public override void OnPageSelected() {
            if (m_Skeleton == null || m_Skeleton.AnimationState == null) {
                return;
            }

            // 正常进入时轨道已经准备好；仅在准备失败或期间发生重建时补建。
            if (m_Skeleton.AnimationState != m_PlayedState) {
                PlayAnimation();
            }

            m_Skeleton.timeScale = m_TimeScale;
        }

        /// <summary>离屏前暂停动画，等待下一次显示时决定重播或继续。</summary>
        public override void OnPageHidden() {
            if (m_Skeleton != null) {
                m_Skeleton.timeScale = 0f;
            }
            // 轮播随后还会停用整个页面，避免离屏时继续更新网格。
        }

        // 仅在首次播放或启用重播时重新设置轨道；继续播放模式保留原轨道及其进度。
        private void PlayAnimation() {
            var state = m_Skeleton.AnimationState;

            if (state == null || (state == m_PlayedState && !m_RestartOnSelection)) {
                return;
            }

            string animation = string.IsNullOrEmpty(m_Animation) ? m_Skeleton.startingAnimation : m_Animation;

            if (string.IsNullOrEmpty(animation)) {
                return;
            }

            if (m_Skeleton.Skeleton.Data.FindAnimation(animation) == null) {
                Debug.LogWarning($"[BannerSpinePage] Animation '{animation}' was not found.", this);
                return;
            }

            // 首次播放和重播都清除默认动画及旧姿势；继续播放已在上方提前返回。
            state.ClearTracks();
            m_Skeleton.Skeleton.SetToSetupPose();

            state.SetAnimation(0, animation, m_Loop);

            // 立即应用首帧并刷新网格，避免渲染到默认动画或上次离开时的画面。
            m_Skeleton.Update(0f);
            m_Skeleton.UpdateMesh();
            m_PlayedState = state;
        }
    }
}
