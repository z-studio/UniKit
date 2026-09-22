using Spine.Unity;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// <see cref="SpineSegment"/> 的渲染器：创建并复用 <see cref="SkeletonGraphic"/> 视图。
    /// 通过 <c>[RuntimeInitializeOnLoadMethod]</c> 在游戏启动时自动注册到
    /// <see cref="MarqueeSegmentRendererRegistry"/>，业务侧无需手动接入。
    /// </summary>
    public sealed class SpineSegmentRenderer : IMarqueeSegmentRenderer {
        public string Key => "spine.skeletongraphic";

        public bool CanRender(MarqueeSegment segment) => segment is SpineSegment;

        public RectTransform CreateView(Transform parent) {
            var go = new GameObject("SpineSegment", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            var sg = go.AddComponent<SkeletonGraphic>();
            sg.raycastTarget = true;
            go.AddComponent<SpinePlayWhenVisible>();
            return rt;
        }

        public Vector2 Bind(RectTransform view, MarqueeSegment segment) {
            var seg = (SpineSegment)segment;
            var sg = view.GetComponent<SkeletonGraphic>();
            var gate = view.GetComponent<SpinePlayWhenVisible>();

            if (gate == null) {
                gate = view.gameObject.AddComponent<SpinePlayWhenVisible>();
            }

            gate.Disarm();

            if (sg != null && seg.skeletonDataAsset != null) {
                bool needInit = sg.skeletonDataAsset != seg.skeletonDataAsset || sg.Skeleton == null;

                if (sg.skeletonDataAsset != seg.skeletonDataAsset) {
                    sg.skeletonDataAsset = seg.skeletonDataAsset;
                }

                if (needInit) {
                    sg.Initialize(true);
                }

                if (sg.Skeleton != null && !string.IsNullOrEmpty(seg.skinName)) {
                    sg.Skeleton.SetSkin(seg.skinName);
                    sg.Skeleton.SetSlotsToSetupPose();
                }

                if (sg.AnimationState != null) {
                    sg.AnimationState.ClearTracks();
                    sg.Skeleton?.SetToSetupPose();

                    RectTransform viewport = null;
                    bool deferPlay = seg.playWhenFullyVisible
                                     && !string.IsNullOrEmpty(seg.animationName)
                                     && TryResolveViewport(view, out viewport);

                    if (deferPlay) {
                        // 延迟播放：保持 setup pose，等完全进入可视区再 SetAnimation
                        sg.timeScale = seg.timeScale;
                        gate.Arm(sg, viewport, seg.animationName, seg.loop, seg.timeScale);
                    } else if (!string.IsNullOrEmpty(seg.animationName)) {
                        sg.AnimationState.SetAnimation(0, seg.animationName, seg.loop);
                        sg.timeScale = seg.timeScale;
                    } else {
                        sg.timeScale = seg.timeScale;
                    }
                }

                view.localScale = new Vector3(seg.scale, seg.scale, 1f);
            }

            // size == 0 时自动读取骨骼包围盒尺寸（同 ImageSegment 用原始尺寸的行为）。
            // 注意：SkeletonGraphic 的尺寸依赖 mesh 已生成，而跑马灯构建的当帧 mesh 尚未更新，
            // 直接读 ILayoutElement.preferredWidth/Height 会得到 0（需手动点 Inspector 的 “Match” 才生效）。
            // 这里主动调用 MatchRectTransformWithBounds()——它内部会强制 Update(0) + UpdateMesh() 再按
            // mesh 包围盒设置 sizeDelta（等价于 “Match RectTransform with Mesh”），从而在运行时即时生效。
            Vector2 size = seg.size;

            if ((size.x <= 0f || size.y <= 0f) && sg != null && sg.Skeleton != null) {
                // MatchRectTransformWithBounds 会改动 pivot（按骨骼原点偏移），
                // 跑马灯需要 pivot 居中来做统一水平排列，故取到尺寸后复原 pivot。
                Vector2 prevPivot = sg.rectTransform.pivot;

                if (sg.MatchRectTransformWithBounds()) {
                    Vector2 matched = sg.rectTransform.sizeDelta;

                    if (size.x <= 0f) {
                        size.x = Mathf.Abs(matched.x);
                    }

                    if (size.y <= 0f) {
                        size.y = Mathf.Abs(matched.y);
                    }
                }

                sg.rectTransform.pivot = prevPivot;
            }

            return size;
        }

        public void OnRecycle(RectTransform view) {
            var gate = view.GetComponent<SpinePlayWhenVisible>();
            gate?.Disarm();

            var sg = view.GetComponent<SkeletonGraphic>();

            if (sg != null && sg.AnimationState != null) {
                sg.AnimationState.ClearTracks();
                sg.Skeleton?.SetToSetupPose();
            }
        }

        private static bool TryResolveViewport(RectTransform view, out RectTransform viewport) {
            viewport = null;
            var marquee = view.GetComponentInParent<UniKit.UI.UIMarquee>();

            if (marquee == null) {
                return false;
            }

            viewport = marquee.Viewport != null ? marquee.Viewport : marquee.transform as RectTransform;
            return viewport != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister() {
            MarqueeSegmentRendererRegistry.Register(new SpineSegmentRenderer());
        }
    }
}
