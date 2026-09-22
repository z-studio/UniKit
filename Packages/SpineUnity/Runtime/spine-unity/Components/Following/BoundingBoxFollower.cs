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

using System.Collections.Generic;
using UnityEngine;

namespace Spine.Unity {
    [ExecuteAlways]
    [HelpURL("http://esotericsoftware.com/spine-unity#BoundingBoxFollower")]
    public class BoundingBoxFollower : MonoBehaviour {
        internal static bool DebugMessages = true;


        #region Inspector

        public SkeletonRenderer skeletonRenderer;

        [SpineSlot(dataField: "skeletonRenderer", containsBoundingBoxes: true)]
        public string slotName;

        public bool isTrigger, usedByEffector, usedByComposite;
        public bool clearStateOnDisable = true;

        #endregion


        private Slot m_Slot;
        private BoundingBoxAttachment m_CurrentAttachment;
        private string m_CurrentAttachmentName;
        private PolygonCollider2D m_CurrentCollider;
        private bool m_SkinBoneEnabled = true;

        public readonly Dictionary<BoundingBoxAttachment, PolygonCollider2D> colliderTable = new();

        public readonly Dictionary<BoundingBoxAttachment, string> nameTable = new();

        public Slot Slot => m_Slot;

        public BoundingBoxAttachment CurrentAttachment => m_CurrentAttachment;

        public string CurrentAttachmentName => m_CurrentAttachmentName;

        public PolygonCollider2D CurrentCollider => m_CurrentCollider;

        public bool IsTrigger => isTrigger;

        private void Start() {
            Initialize();
        }

        private void OnEnable() {
            if (skeletonRenderer != null) {
                skeletonRenderer.OnRebuild -= HandleRebuild;
                skeletonRenderer.OnRebuild += HandleRebuild;
            }

            Initialize();
        }

        private void HandleRebuild(SkeletonRenderer sr) {
            //if (BoundingBoxFollower.DebugMessages) Debug.Log("Skeleton was rebuilt. Repopulating BoundingBoxFollower.");
            Initialize();
        }

        /// <summary>
        /// Initialize and instantiate the BoundingBoxFollower colliders. This is method checks if the BoundingBoxFollower has already been initialized for the skeleton instance and slotName and prevents overwriting unless it detects a new setup.</summary>
        public void Initialize(bool overwrite = false) {
            if (skeletonRenderer == null) {
                return;
            }

            skeletonRenderer.Initialize(false);

            if (string.IsNullOrEmpty(slotName)) {
                return;
            }

            // Don't reinitialize if the setup did not change.
            if (!overwrite
                && colliderTable.Count > 0
                && m_Slot != null
                && // Slot is set and colliders already populated.
                skeletonRenderer.skeleton == m_Slot.Skeleton
                && // Skeleton object did not change.
                slotName == m_Slot.Data.Name // Slot object did not change.
               ) {
                return;
            }

            m_Slot = null;
            m_CurrentAttachment = null;
            m_CurrentAttachmentName = null;
            m_CurrentCollider = null;
            colliderTable.Clear();
            nameTable.Clear();

            Skeleton skeleton = skeletonRenderer.skeleton;

            if (skeleton == null) {
                return;
            }

            m_Slot = skeleton.FindSlot(slotName);

            if (m_Slot == null) {
                if (BoundingBoxFollower.DebugMessages) {
                    Debug.LogWarning(
                        string.Format(
                            "Slot '{0}' not found for BoundingBoxFollower on '{1}'. (Previous colliders were disposed.)",
                            slotName,
                            this.gameObject.name
                        )
                    );
                }

                return;
            }

            int slotIndex = m_Slot.Data.Index;

            int requiredCollidersCount = 0;
            PolygonCollider2D[] colliders = GetComponents<PolygonCollider2D>();

            if (this.gameObject.activeInHierarchy) {
                foreach (Skin skin in skeleton.Data.Skins) {
                    AddCollidersForSkin(skin, slotIndex, colliders, ref requiredCollidersCount);
                }

                if (skeleton.Skin != null) {
                    AddCollidersForSkin(skeleton.Skin, slotIndex, colliders, ref requiredCollidersCount);
                }
            }

            DisposeExcessCollidersAfter(requiredCollidersCount);
            m_SkinBoneEnabled = m_Slot.Bone.Active;

            if (BoundingBoxFollower.DebugMessages) {
                bool valid = colliderTable.Count != 0;

                if (!valid) {
                    if (this.gameObject.activeInHierarchy) {
                        Debug.LogWarning(
                            "Bounding Box Follower not valid! Slot ["
                            + slotName
                            + "] does not contain any Bounding Box Attachments!"
                        );
                    } else {
                        Debug.LogWarning("Bounding Box Follower tried to rebuild as a prefab.");
                    }
                }
            }
        }

        private void AddCollidersForSkin(
            Skin skin,
            int slotIndex,
            PolygonCollider2D[] previousColliders,
            ref int collidersCount
        ) {
            if (skin == null) {
                return;
            }

            List<Skin.SkinEntry> skinEntries = new List<Skin.SkinEntry>();
            skin.GetAttachments(slotIndex, skinEntries);

            foreach (Skin.SkinEntry entry in skinEntries) {
                Attachment attachment = skin.GetAttachment(slotIndex, entry.Name);
                BoundingBoxAttachment boundingBoxAttachment = attachment as BoundingBoxAttachment;

                if (BoundingBoxFollower.DebugMessages && attachment != null && boundingBoxAttachment == null) {
                    Debug.Log(
                        "BoundingBoxFollower tried to follow a slot that contains non-boundingbox attachments: "
                        + slotName
                    );
                }

                if (boundingBoxAttachment != null) {
                    if (!colliderTable.ContainsKey(boundingBoxAttachment)) {
                        PolygonCollider2D bbCollider = collidersCount < previousColliders.Length ?
                            previousColliders[collidersCount] : gameObject.AddComponent<PolygonCollider2D>();

                        ++collidersCount;
                        SkeletonUtility.SetColliderPointsLocal(bbCollider, m_Slot, boundingBoxAttachment);
                        bbCollider.isTrigger = isTrigger;
                        bbCollider.usedByEffector = usedByEffector;

                        bbCollider.compositeOperation = usedByComposite ?
                            Collider2D.CompositeOperation.Merge : Collider2D.CompositeOperation.None;

                        bbCollider.enabled = false;
                        bbCollider.hideFlags = HideFlags.NotEditable;
                        colliderTable.Add(boundingBoxAttachment, bbCollider);
                        nameTable.Add(boundingBoxAttachment, entry.Name);
                    }
                }
            }
        }

        private void OnDisable() {
            if (clearStateOnDisable) {
                ClearState();
            }

            if (skeletonRenderer != null) {
                skeletonRenderer.OnRebuild -= HandleRebuild;
            }
        }

        public void ClearState() {
            if (colliderTable != null) {
                foreach (PolygonCollider2D col in colliderTable.Values) {
                    col.enabled = false;
                }
            }

            m_CurrentAttachment = null;
            m_CurrentAttachmentName = null;
            m_CurrentCollider = null;
        }

        private void DisposeExcessCollidersAfter(int requiredCount) {
            PolygonCollider2D[] colliders = GetComponents<PolygonCollider2D>();

            if (colliders.Length == 0) {
                return;
            }

            for (int i = requiredCount; i < colliders.Length; ++i) {
                PolygonCollider2D collider = colliders[i];

                if (collider != null) {
#if UNITY_EDITOR
                    if (Application.isEditor && !Application.isPlaying) {
                        DestroyImmediate(collider);
                    } else
#endif
                        Destroy(collider);
                }
            }
        }

        void LateUpdate() {
            if (m_Slot != null && (m_Slot.Attachment != m_CurrentAttachment || m_SkinBoneEnabled != m_Slot.Bone.Active)) {
                m_SkinBoneEnabled = m_Slot.Bone.Active;
                MatchAttachment(m_Slot.Attachment);
            }
        }

        /// <summary>Sets the current collider to match attachment.</summary>
        /// <param name="attachment">If the attachment is not a bounding box, it will be treated as null.</param>
        void MatchAttachment(Attachment attachment) {
            BoundingBoxAttachment bbAttachment = attachment as BoundingBoxAttachment;

            if (BoundingBoxFollower.DebugMessages && attachment != null && bbAttachment == null) {
                Debug.LogWarning(
                    "BoundingBoxFollower tried to match a non-boundingbox attachment. It will treat it as null."
                );
            }

            if (m_CurrentCollider != null) {
                m_CurrentCollider.enabled = false;
            }

            if (bbAttachment == null || !m_SkinBoneEnabled) {
                m_CurrentCollider = null;
                m_CurrentAttachment = null;
                m_CurrentAttachmentName = null;
            } else {
                PolygonCollider2D foundCollider;
                colliderTable.TryGetValue(bbAttachment, out foundCollider);

                if (foundCollider != null) {
                    m_CurrentCollider = foundCollider;
                    m_CurrentCollider.enabled = true;
                    m_CurrentAttachment = bbAttachment;
                    m_CurrentAttachmentName = nameTable[bbAttachment];
                } else {
                    m_CurrentCollider = null;
                    m_CurrentAttachment = bbAttachment;
                    m_CurrentAttachmentName = null;

                    if (BoundingBoxFollower.DebugMessages) {
                        Debug.LogFormat(
                            "Collider for BoundingBoxAttachment named '{0}' was not initialized. It is possibly from a new skin. currentAttachmentName will be null. You may need to call BoundingBoxFollower.Initialize(overwrite: true);",
                            bbAttachment.Name
                        );
                    }
                }
            }
        }
    }
}