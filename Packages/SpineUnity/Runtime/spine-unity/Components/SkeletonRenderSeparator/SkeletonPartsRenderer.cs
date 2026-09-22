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
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    [HelpURL("http://esotericsoftware.com/spine-unity#SkeletonRenderSeparator")]
    public class SkeletonPartsRenderer : MonoBehaviour {
        #region Properties

        private MeshGenerator m_MeshGenerator;

        public MeshGenerator MeshGenerator {
            get {
                LazyIntialize();
                return m_MeshGenerator;
            }
        }

        private MeshRenderer m_MeshRenderer;

        public MeshRenderer MeshRenderer {
            get {
                LazyIntialize();
                return m_MeshRenderer;
            }
        }

        private MeshFilter m_MeshFilter;

        public MeshFilter MeshFilter {
            get {
                LazyIntialize();
                return m_MeshFilter;
            }
        }

        #endregion


        #region Callback Delegates

        public delegate void SkeletonPartsRendererDelegate(SkeletonPartsRenderer skeletonPartsRenderer);

        /// <summary>OnMeshAndMaterialsUpdated is called at the end of LateUpdate after the Mesh and
        /// all materials have been updated.</summary>
        public event SkeletonPartsRendererDelegate OnMeshAndMaterialsUpdated;

        #endregion


        private MeshRendererBuffers m_Buffers;
        private SkeletonRendererInstruction m_CurrentInstructions = new();

        private void LazyIntialize() {
            if (m_Buffers == null) {
                m_Buffers = new MeshRendererBuffers();
                m_Buffers.Initialize();

                if (m_MeshGenerator != null) {
                    return;
                }

                m_MeshGenerator = new MeshGenerator();
                m_MeshFilter = GetComponent<MeshFilter>();
                m_MeshRenderer = GetComponent<MeshRenderer>();
                m_CurrentInstructions.Clear();
            }
        }

        private void OnDestroy() {
            m_Buffers?.Dispose();
        }

        public void ClearMesh() {
            LazyIntialize();
            m_MeshFilter.sharedMesh = null;
        }

        public void RenderParts(ExposedList<SubmeshInstruction> instructions, int startSubmesh, int endSubmesh) {
            LazyIntialize();

            // STEP 1: Create instruction
            MeshRendererBuffers.SmartMesh smartMesh = m_Buffers.GetNextMesh();
            m_CurrentInstructions.SetWithSubset(instructions, startSubmesh, endSubmesh);

            bool updateTriangles = SkeletonRendererInstruction.GeometryNotEqual(
                m_CurrentInstructions,
                smartMesh.instructionUsed
            );

            // STEP 2: Generate mesh buffers.
            SubmeshInstruction[] currentInstructionsSubmeshesItems = m_CurrentInstructions.submeshInstructions.Items;
            m_MeshGenerator.Begin();

            if (m_CurrentInstructions.hasActiveClipping) {
                for (int i = 0; i < m_CurrentInstructions.submeshInstructions.Count; i++)
                    m_MeshGenerator.AddSubmesh(currentInstructionsSubmeshesItems[i], updateTriangles);
            } else {
                m_MeshGenerator.BuildMeshWithArrays(m_CurrentInstructions, updateTriangles);
            }

            m_Buffers.UpdateSharedMaterials(m_CurrentInstructions.submeshInstructions);

            // STEP 3: modify mesh.
            Mesh mesh = smartMesh.mesh;

            if (m_MeshGenerator.VertexCount <= 0) { // Clear an empty mesh
                updateTriangles = false;
                mesh.Clear();
            } else {
                m_MeshGenerator.FillVertexData(mesh);

                if (updateTriangles) {
                    m_MeshGenerator.FillTriangles(mesh);
                    m_MeshRenderer.sharedMaterials = m_Buffers.GetUpdatedSharedMaterialsArray();
                } else if (m_Buffers.MaterialsChangedInLastUpdate()) {
                    m_MeshRenderer.sharedMaterials = m_Buffers.GetUpdatedSharedMaterialsArray();
                }

                m_MeshGenerator.FillLateVertexData(mesh);
            }

            m_MeshFilter.sharedMesh = mesh;
            smartMesh.instructionUsed.Set(m_CurrentInstructions);

            OnMeshAndMaterialsUpdated?.Invoke(this);
        }

        public void SetPropertyBlock(MaterialPropertyBlock block) {
            LazyIntialize();
            m_MeshRenderer.SetPropertyBlock(block);
        }

        public static SkeletonPartsRenderer NewPartsRendererGameObject(
            Transform parent,
            string name,
            int sortingOrder = 0
        ) {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            SkeletonPartsRenderer returnComponent = go.AddComponent<SkeletonPartsRenderer>();
            returnComponent.MeshRenderer.sortingOrder = sortingOrder;

            return returnComponent;
        }
    }
}