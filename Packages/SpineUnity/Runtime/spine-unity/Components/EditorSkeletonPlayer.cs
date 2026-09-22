/******************************************************************************
 * Spine Runtimes License Agreement
 * Last updated April 5, 2025. Replaces all prior versions.
 *
 * Copyright (c) 2013-2025, Esoteric Software LLC
 *
 * Integration of the Spine Runtimes into software or otherwise creating
 * derivative works of the Spine Runtimes is permitted under the terms and
 * conditions of Section 2 of the Spine Editor License Agreement:
 * http://esotericsoftware.com/spine-editor-license
 *
 * Otherwise, it is permitted to integrate the Spine Runtimes into software
 * or otherwise create derivative works of the Spine Runtimes (collectively,
 * "Products"), provided that each user of the Products must obtain their own
 * Spine Editor license and redistribution of the Products in any form must
 * include this license and copyright notice.
 *
 * THE SPINE RUNTIMES ARE PROVIDED BY ESOTERIC SOFTWARE LLC "AS IS" AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL ESOTERIC SOFTWARE LLC BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES,
 * BUSINESS INTERRUPTION, OR LOSS OF USE, DATA, OR PROFITS) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THE SPINE RUNTIMES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Spine.Unity {
    /// <summary>
    /// Experimental Editor Skeleton Player component enabling Editor playback of the
    /// selected animation outside of Play mode for SkeletonAnimation and SkeletonGraphic.
    /// </summary>
    [ExecuteInEditMode]
    [AddComponentMenu("Spine/EditorSkeletonPlayer")]
    [RequireComponent(typeof(ISkeletonAnimation))]
    public class EditorSkeletonPlayer : MonoBehaviour {
        public bool playWhenSelected = true;
        public bool playWhenDeselected = true;
        public float fixedTrackTime = 0.0f;
        
        private IEditorSkeletonWrapper m_SkeletonWrapper;
        private TrackEntry m_TrackEntry;
        private string m_OldAnimationName;
        private bool m_OldLoop;
        private double m_OldTime;

        [DidReloadScripts]
        private static void OnReloaded() {
            // Force start when scripts are reloaded

            EditorSkeletonPlayer[] editorSpineAnimations =
                FindObjectsByType<EditorSkeletonPlayer>(FindObjectsSortMode.None);

            foreach (EditorSkeletonPlayer editorSpineAnimation in editorSpineAnimations) {
                editorSpineAnimation.Start();
            }
        }

        private void Reset() {
            // Note: when a skeleton has a varying number of active materials,
            // we're moving this component first in the hierarchy to still be
            // able to disable this component.
            for (int i = 0; i < 10; ++i) {
                UnityEditorInternal.ComponentUtility.MoveComponentUp(this);
            }
        }

        private void Start() {
            if (Application.isPlaying) {
                return;
            }

            if (m_SkeletonWrapper == null) {
                SkeletonAnimation skeletonAnimation;
                SkeletonGraphic skeletonGraphic;

                if (skeletonAnimation = this.GetComponent<SkeletonAnimation>()) {
                    m_SkeletonWrapper = new SkeletonAnimationWrapper(skeletonAnimation);
                } else if (skeletonGraphic = this.GetComponent<SkeletonGraphic>()) {
                    m_SkeletonWrapper = new SkeletonGraphicWrapper(skeletonGraphic);
                }
            }

            m_OldTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorUpdate;
        }

        private void OnDestroy() {
            EditorApplication.update -= EditorUpdate;
        }

        private void Update() {
            if (enabled == false || Application.isPlaying) {
                return;
            }

            if (m_SkeletonWrapper == null) {
                return;
            }

            if (m_SkeletonWrapper.State == null || m_SkeletonWrapper.State.Tracks.Count == 0) {
                return;
            }

            TrackEntry currentEntry = m_SkeletonWrapper.State.Tracks.Items[0];

            if (currentEntry != null && fixedTrackTime != 0) {
                currentEntry.TrackTime = fixedTrackTime;
            }
        }

        private void EditorUpdate() {
            if (enabled == false || Application.isPlaying) {
                return;
            }

            if (m_SkeletonWrapper == null) {
                return;
            }

            if (m_SkeletonWrapper.State == null) {
                return;
            }

            bool isSelected = Selection.Contains(this.gameObject);

            if (!this.playWhenSelected && isSelected) {
                return;
            }

            if (!this.playWhenDeselected && !isSelected) {
                return;
            }

            if (fixedTrackTime != 0) {
                return;
            }

            // Update animation
            if (m_OldAnimationName != m_SkeletonWrapper.AnimationName || m_OldLoop != m_SkeletonWrapper.Loop) {
                SkeletonData skeletonData = m_SkeletonWrapper.SkeletonData;

                Spine.Animation animation = (skeletonData == null || m_SkeletonWrapper.AnimationName == null) ?
                    null : skeletonData.FindAnimation(m_SkeletonWrapper.AnimationName);

                if (animation != null) {
                    m_TrackEntry = m_SkeletonWrapper.State.SetAnimation(
                        0,
                        m_SkeletonWrapper.AnimationName,
                        m_SkeletonWrapper.Loop
                    );
                } else {
                    m_TrackEntry = m_SkeletonWrapper.State.SetEmptyAnimation(0, 0);
                }

                m_OldAnimationName = m_SkeletonWrapper.AnimationName;
                m_OldLoop = m_SkeletonWrapper.Loop;
            }

            // Update speed
            if (m_TrackEntry != null) {
                m_TrackEntry.TimeScale = m_SkeletonWrapper.Speed;
            }

            float deltaTime = (float)(EditorApplication.timeSinceStartup - m_OldTime);
            m_SkeletonWrapper.Update(deltaTime);
            m_OldTime = EditorApplication.timeSinceStartup;

            // Force repaint to update animation smoothly
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private class SkeletonAnimationWrapper : IEditorSkeletonWrapper {
            private SkeletonAnimation m_SkeletonAnimation;

            public SkeletonAnimationWrapper(SkeletonAnimation skeletonAnimation) {
                m_SkeletonAnimation = skeletonAnimation;
            }

            public Spine.SkeletonData SkeletonData {
                get {
                    if (!m_SkeletonAnimation.SkeletonDataAsset) {
                        return null;
                    }

                    return m_SkeletonAnimation.SkeletonDataAsset.GetSkeletonData(true);
                }
            }

            public string AnimationName => m_SkeletonAnimation.AnimationName;

            public bool Loop => m_SkeletonAnimation.loop;

            public float Speed => m_SkeletonAnimation.timeScale;

            public Spine.AnimationState State => m_SkeletonAnimation.state;

            public void Update(float deltaTime) {
                m_SkeletonAnimation.Update(deltaTime);
            }
        }

        private class SkeletonGraphicWrapper : IEditorSkeletonWrapper {
            private SkeletonGraphic m_SkeletonGraphic;

            public SkeletonGraphicWrapper(SkeletonGraphic skeletonGraphic) {
                m_SkeletonGraphic = skeletonGraphic;
            }

            public Spine.SkeletonData SkeletonData => m_SkeletonGraphic.SkeletonData;

            public string AnimationName => m_SkeletonGraphic.startingAnimation;

            public bool Loop => m_SkeletonGraphic.startingLoop;

            public float Speed => m_SkeletonGraphic.timeScale;

            public Spine.AnimationState State => m_SkeletonGraphic.AnimationState;

            public void Update(float deltaTime) {
                m_SkeletonGraphic.Update(deltaTime);
            }
        }

        private interface IEditorSkeletonWrapper {
            string AnimationName { get; }
            Spine.SkeletonData SkeletonData { get; }
            bool Loop { get; }
            float Speed { get; }
            Spine.AnimationState State { get; }
            void Update(float deltaTime);
        }
    }
}
#endif