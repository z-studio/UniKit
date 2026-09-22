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

using UnityEngine;

namespace Spine.Unity {
    /// <summary>
    /// Utility component to support flipping of hinge chains (chains of HingeJoint objects) along with the parent skeleton.
    /// Note that flipping needs to be performed by 180 degree rotation at <see cref="SkeletonUtility"/>,
    /// by setting <see cref="SkeletonUtility.flipBy180DegreeRotation"/> to true, not via negative scale.
    ///
    /// Note: This component is automatically attached when calling "Create Hinge Chain" at <see cref="SkeletonUtilityBone"/>,
    /// do not attempt to use this component for other purposes.
    /// </summary>
    public class FollowSkeletonUtilityRootRotation : MonoBehaviour {
        private const float k_FlipAngleThreshold = 100.0f;

        public Transform reference;
        private Vector3 m_PrevLocalEulerAngles;

        private void Start() {
            m_PrevLocalEulerAngles = this.transform.localEulerAngles;
        }

        private void FixedUpdate() {
            this.transform.rotation = reference.rotation;

            bool wasFlippedAroundY = Mathf.Abs(this.transform.localEulerAngles.y - m_PrevLocalEulerAngles.y)
                                     > k_FlipAngleThreshold;

            bool wasFlippedAroundX = Mathf.Abs(this.transform.localEulerAngles.x - m_PrevLocalEulerAngles.x)
                                     > k_FlipAngleThreshold;

            if (wasFlippedAroundY) {
                CompensatePositionToYRotation();
            }

            if (wasFlippedAroundX) {
                CompensatePositionToXRotation();
            }

            m_PrevLocalEulerAngles = this.transform.localEulerAngles;
        }

        /// <summary>
        /// Compensates the position so that a child at the reference position remains in the same place,
        /// to counter any movement that occurred by rotation.
        /// </summary>
        private void CompensatePositionToYRotation() {
            Vector3 newPosition = reference.position + (reference.position - this.transform.position);
            newPosition.y = this.transform.position.y;
            this.transform.position = newPosition;
        }

        /// <summary>
        /// Compensates the position so that a child at the reference position remains in the same place,
        /// to counter any movement that occurred by rotation.
        /// </summary>
        private void CompensatePositionToXRotation() {
            Vector3 newPosition = reference.position + (reference.position - this.transform.position);
            newPosition.x = this.transform.position.x;
            this.transform.position = newPosition;
        }
    }
}