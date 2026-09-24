using Spine.Unity;
using UnityEngine;

namespace ZStudio.UniKit.UI.Spine {
    /// <summary>可选的 Spine 4.2 页面适配器，挂在复合页面中各个 SkeletonGraphic 所在的物体上。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkeletonGraphic))]
    public sealed class BannerSpinePage : BannerPageBehaviour {
        [SerializeField] private SkeletonGraphic m_Skeleton;

        [Tooltip("留空时使用 SkeletonGraphic 的 Starting Animation。")] [SpineAnimation, SerializeField]
        private string m_Animation;

        [SerializeField] private bool m_Loop = true;

        [Tooltip("再次进入页面时从头播放；关闭后保留已有动画的播放进度。")] [SerializeField]
        private bool m_RestartOnSelection = true;

        [Tooltip("进入过程中保持姿势，页面停稳并选中后再开始播放。")] [SerializeField]
        private bool m_WaitUntilSelected = true;

        [SerializeField, Min(0f)] private float m_TimeScale = 1f;

        [SerializeField] private bool m_UnscaledTime = true;

        // 区分首次播放与再次进入，使“继续播放”模式仍能在首次显示时建立动画轨道。
        private bool m_HasPlayed;

        /// <summary>初始化骨骼，并按配置准备进入期间的姿势与播放状态。</summary>
        public override void OnPageShown() {
            if (m_Skeleton == null) {
                m_Skeleton = GetComponent<SkeletonGraphic>();
            }

            m_Skeleton.Initialize(false);
            m_Skeleton.UnscaledTime = m_UnscaledTime;
            m_Skeleton.timeScale = m_WaitUntilSelected ? 0f : m_TimeScale;

            if (!m_WaitUntilSelected) {
                PlayAnimation();
            } else if (m_RestartOnSelection && m_Skeleton.Skeleton != null) {
                // 重播模式先清理上次动画的姿势，避免滑入时短暂显示上次退出时的画面。
                m_Skeleton.AnimationState.ClearTracks();
                m_Skeleton.Skeleton.SetToSetupPose();
                m_Skeleton.Update(0f);
            }
        }

        /// <summary>页面停稳后开始演出，并恢复配置的动画速度。</summary>
        public override void OnPageSelected() {
            if (m_Skeleton == null || m_Skeleton.AnimationState == null) {
                return;
            }

            if (m_WaitUntilSelected) {
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
            if (m_Skeleton.AnimationState == null || (m_HasPlayed && !m_RestartOnSelection)) {
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

            m_Skeleton.AnimationState.SetAnimation(0, animation, m_Loop);

            // 立即应用动画的当前姿势，不推进时间，避免等待下一帧才刷新画面。
            m_Skeleton.Update(0f);
            m_HasPlayed = true;
        }
    }
}