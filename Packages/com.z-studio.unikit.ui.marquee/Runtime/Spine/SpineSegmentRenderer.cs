using Spine.Unity;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// Spine 的布局根节点与绘制子节点分离：根节点表示最终占位，子节点处理骨骼原点与缩放。
    /// 测量始终使用 setup pose；播放状态和动画首帧不会改变环形布局的周期长度。
    /// </summary>
    public sealed class SpineSegmentRenderer : IMarqueeSegmentRenderer, IMarqueeSegmentTimeControl {
        public string Key => "spine.skeletongraphic";
        public bool CanRender(MarqueeSegment segment) => segment is SpineSegment;

        public RectTransform CreateView(Transform parent) {
            var root = (RectTransform)new GameObject("SpineSegment", typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            
            var child = new GameObject("SkeletonGraphic", typeof(RectTransform), typeof(CanvasRenderer));
            child.transform.SetParent(root, false);
            
            var graphic = child.AddComponent<SkeletonGraphic>();
            graphic.raycastTarget = true;
            
            // 门控挂在布局根上，检测包含 Scale 的最终占位矩形。
            root.gameObject.AddComponent<SpinePlayWhenVisible>();
            return root;
        }

        public Vector2 Bind(RectTransform view, MarqueeSegment segment, MarqueeRenderContext context) {
            var seg = (SpineSegment)segment;
            var sg = view.GetComponentInChildren<SkeletonGraphic>(true);
            var gate = view.GetComponent<SpinePlayWhenVisible>();
            gate.Disarm();
            view.localScale = Vector3.one;
            sg.UnscaledTime = context.IgnoreTimeScale;
            sg.enabled = seg.SkeletonDataAsset != null;
            
            if (seg.SkeletonDataAsset == null) {
                sg.AnimationState?.ClearTracks();
                sg.skeletonDataAsset = null;
                sg.Initialize(true);
                return Vector2.zero;
            }

            if (sg.skeletonDataAsset != seg.SkeletonDataAsset || sg.Skeleton == null) {
                sg.skeletonDataAsset = seg.SkeletonDataAsset;
                sg.Initialize(true);
            }

            if (sg.Skeleton == null || sg.AnimationState == null) {
                return Vector2.zero;
            }

            sg.AnimationState.ClearTracks();
            
            sg.Skeleton.SetSkin(string.IsNullOrEmpty(seg.SkinName)
                ? sg.Skeleton.Data.DefaultSkin : sg.Skeleton.Data.FindSkin(seg.SkinName)
                                                 ?? throw new System.ArgumentException($"Spine 皮肤不存在：{seg.SkinName}"));
            sg.Skeleton.SetToSetupPose();
            sg.timeScale = 0f;

            RectTransform child = sg.rectTransform;
            child.anchorMin = child.anchorMax = child.pivot = new Vector2(0.5f, 0.5f);
            child.localScale = Vector3.one;
            child.anchoredPosition = Vector2.zero;
            
            // 强制更新 setup pose，避免复用视图沿用上一动画的 mesh 或 bounds。
            sg.Update(0f);
            var boundsSize = Vector2.zero;
            var boundsCenter = Vector2.zero;
            
            if (sg.MatchRectTransformWithBounds()) {
                boundsSize = child.sizeDelta;
                // Spine 根据 mesh 中心设置 pivot，可反推出骨骼原点到包围盒中心的偏移。
                boundsCenter = Vector2.Scale(new Vector2(0.5f, 0.5f) - child.pivot, boundsSize);
            }

            Vector2 size = seg.Size;
            
            if (size.x <= 0f) {
                size.x = Mathf.Abs(boundsSize.x);
            }
            
            if (size.y <= 0f) {
                size.y = Mathf.Abs(boundsSize.y);
            }
            
            child.pivot = new Vector2(0.5f, 0.5f);
            child.sizeDelta = size;
            child.localScale = new Vector3(seg.Scale, seg.Scale, 1f);
            child.anchoredPosition = -boundsCenter * seg.Scale;

            // 在绑定阶段验证，避免延迟门控在每帧 LateUpdate 中重复抛出同一异常。
            if (!string.IsNullOrEmpty(seg.AnimationName) && sg.Skeleton.Data.FindAnimation(seg.AnimationName) == null) {
                throw new System.ArgumentException($"Spine 动画不存在：{seg.AnimationName}");
            }

            if (!context.IsMeasuring) {
                sg.timeScale = seg.TimeScale;
                
                if (!string.IsNullOrEmpty(seg.AnimationName)) {
                    if (seg.PlayWhenFullyVisible && context.Viewport != null) {
                        gate.Arm(sg, context.Viewport, seg.AnimationName, seg.Loop, seg.TimeScale);
                    } else {
                        sg.AnimationState.SetAnimation(0, seg.AnimationName, seg.Loop);
                    }
                }
            }

            return size * Mathf.Abs(seg.Scale);
        }

        public void SetTimeMode(RectTransform view, bool ignoreTimeScale) {
            var sg = view.GetComponentInChildren<SkeletonGraphic>(true);
            
            if (sg != null) {
                sg.UnscaledTime = ignoreTimeScale;
            }
        }

        public void OnRecycle(RectTransform view) {
            view.GetComponent<SpinePlayWhenVisible>()?.Disarm();
            var sg = view.GetComponentInChildren<SkeletonGraphic>(true);
            
            if (sg != null) {
                sg.AnimationState?.ClearTracks();
                sg.Skeleton?.SetToSetupPose();
                sg.timeScale = 0f;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister() => MarqueeSegmentRendererRegistry.Register(new SpineSegmentRenderer());
    }
}