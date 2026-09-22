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

namespace Spine {
    /// <summary>
    /// Stores the setup pose for a <see cref="PhysicsConstraint"/>.
    /// <para>
    /// See <a href="http://esotericsoftware.com/spine-physics-constraints">Physics constraints</a> in the Spine User Guide.</para>
    /// </summary>
    public class PhysicsConstraintData : ConstraintData {
        internal BoneData bone;
        internal float x, y, rotate, scaleX, shearX, limit;
        internal float step, inertia, strength, damping, massInverse, wind, gravity, mix;
        internal bool inertiaGlobal, strengthGlobal, dampingGlobal, massGlobal, windGlobal, gravityGlobal, mixGlobal;

        public PhysicsConstraintData(string name) : base(name) { }

        /// <summary>The bone constrained by this physics constraint.</summary>
        public BoneData Bone => bone;

        public float Step {
            get => step;
            set => step = value;
        }

        public float X {
            get => x;
            set => x = value;
        }

        public float Y {
            get => y;
            set => y = value;
        }

        public float Rotate {
            get => rotate;
            set => rotate = value;
        }

        public float ScaleX {
            get => scaleX;
            set => scaleX = value;
        }

        public float ShearX {
            get => shearX;
            set => shearX = value;
        }

        public float Limit {
            get => limit;
            set => limit = value;
        }

        public float Inertia {
            get => inertia;
            set => inertia = value;
        }

        public float Strength {
            get => strength;
            set => strength = value;
        }

        public float Damping {
            get => damping;
            set => damping = value;
        }

        public float MassInverse {
            get => massInverse;
            set => massInverse = value;
        }

        public float Wind {
            get => wind;
            set => wind = value;
        }

        public float Gravity {
            get => gravity;
            set => gravity = value;
        }

        /// <summary>A percentage (0-1) that controls the mix between the constrained and unconstrained poses.</summary>
        public float Mix {
            get => mix;
            set => mix = value;
        }

        public bool InertiaGlobal {
            get => inertiaGlobal;
            set => inertiaGlobal = value;
        }

        public bool StrengthGlobal {
            get => strengthGlobal;
            set => strengthGlobal = value;
        }

        public bool DampingGlobal {
            get => dampingGlobal;
            set => dampingGlobal = value;
        }

        public bool MassGlobal {
            get => massGlobal;
            set => massGlobal = value;
        }

        public bool WindGlobal {
            get => windGlobal;
            set => windGlobal = value;
        }

        public bool GravityGlobal {
            get => gravityGlobal;
            set => gravityGlobal = value;
        }

        public bool MixGlobal {
            get => mixGlobal;
            set => mixGlobal = value;
        }
    }
}