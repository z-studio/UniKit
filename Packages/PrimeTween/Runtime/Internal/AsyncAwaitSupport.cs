using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using UnityEngine;

#pragma warning disable CS0618

namespace PrimeTween {
    public partial struct Tween {
        /// <summary>This method is needed for async/await support. Don't use it directly.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TweenAwaiter GetAwaiter() {
            return new TweenAwaiter(this);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("This struct is needed for async/await support, you should not use it directly.")]
        public readonly struct TweenAwaiter : INotifyCompletion {
            private readonly Tween m_Tween;

            internal TweenAwaiter(Tween tween) {
                if (tween.isAlive && !tween.TryManipulate()) {
                    m_Tween = default;
                } else {
                    m_Tween = tween;
                }
            }

            public bool IsCompleted => !m_Tween.isAlive;

            public void OnCompleted([NotNull] Action continuation) {
                // try-catch is needed here because any exception that is thrown inside the OnCompleted will be silenced
                // probably because this try in UnitySynchronizationContext.cs has no exception handling:
                // https://github.com/Unity-Technologies/UnityCsReference/blob/dd0d959800a675836a77dbe188c7dd55abc7c512/Runtime/Export/Scripting/UnitySynchronizationContext.cs#L157
                try {
                    Assert.IsTrue(m_Tween.isAlive);
                    var infiniteSettings = new TweenSettings<float>(0, 0, float.MaxValue, Ease.Linear, -1);
                    var wait = Animate(m_Tween.tween, ref infiniteSettings, TweenAnimation.TweenType.TweenAwaiter);
                    Assert.IsTrue(wait.isAlive);
                    wait.tween.longParam = m_Tween.id;
                    wait.tween.ManagedData.OnComplete(continuation, true);
                } catch (Exception e) {
                    Debug.LogException(e);
                    throw;
                }
            }

            internal static void UpdateTweenAwaiter(ref TweenData rt, ref UnmanagedTweenData d) {
                if (d.IsAlive) {
                    var target = rt.target as ColdData;

                    if (rt.cold.longParam != target.id || !target.Data.IsAlive) {
                        rt.ForceComplete(ref d);
                    }
                }
            }

            public void GetResult() { }
        }
    }

    public partial struct Sequence {
        /// <summary>This method is needed for async/await support. Don't use it directly.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public Tween.TweenAwaiter GetAwaiter() {
            return new Tween.TweenAwaiter(root);
        }
    }
}