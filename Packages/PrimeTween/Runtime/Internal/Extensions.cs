using UnityEngine;

namespace PrimeTween {
    internal static class Extensions {
        internal static float CalcDistance(Vector3 v1, Vector3 v2) => Vector3.Distance(v1, v2);
        internal static float CalcDistance(Quaternion q1, Quaternion q2) => Quaternion.Angle(q1, q2);

        internal static float CalcDelta(this float val, TweenAnimation.ValueWrapper prevVal) => val - prevVal.single;

        internal static double CalcDelta(this double val, TweenAnimation.ValueWrapper prevVal) =>
            val - prevVal.DoubleVal;

        internal static Color CalcDelta(this Color val, TweenAnimation.ValueWrapper prevVal) => val - prevVal.color;

        internal static Vector2 CalcDelta(this Vector2 val, TweenAnimation.ValueWrapper prevVal) =>
            val - prevVal.vector2;

        internal static Vector3 CalcDelta(this Vector3 val, TweenAnimation.ValueWrapper prevVal) =>
            val - prevVal.vector3;

        internal static Vector4 CalcDelta(this Vector4 val, TweenAnimation.ValueWrapper prevVal) =>
            val - prevVal.vector4;

        internal static Quaternion CalcDelta(this Quaternion val, TweenAnimation.ValueWrapper prevVal) =>
            Quaternion.Inverse(prevVal.quaternion) * val;

        internal static Rect CalcDelta(this Rect val, TweenAnimation.ValueWrapper prevVal) =>
            new(
                val.x - prevVal.x,
                val.y - prevVal.y,
                val.width - prevVal.z,
                val.height - prevVal.w
            );

        internal static Color WithAlpha(this Color c, float alpha) {
            c.a = alpha;
            return c;
        }

        internal static TweenAnimation.ValueWrapper ToContainer(this float f) => new() { single = f };

        internal static TweenAnimation.ValueWrapper ToContainer(this Vector2 v) => new() { vector2 = v };

        internal static TweenAnimation.ValueWrapper ToContainer(this Vector3 v) => new() { vector3 = v };

        internal static TweenAnimation.ValueWrapper ToContainer(this Vector4 v) => new() { vector4 = v };

        internal static TweenAnimation.ValueWrapper XYToContainer(this Vector4 v) => new() { vector2 = new Vector2(v.x, v.y) };

        internal static TweenAnimation.ValueWrapper ZWToContainer(this Vector4 v) => new() { vector2 = new Vector2(v.z, v.w) };

        internal static TweenAnimation.ValueWrapper ToContainer(this Color c) => new() { color = c };

        internal static TweenAnimation.ValueWrapper ToContainer(this Quaternion q) => new() { quaternion = q };

        internal static TweenAnimation.ValueWrapper ToContainer(this Rect r) => new() { rect = r };

        internal static TweenAnimation.ValueWrapper ToContainer(this double d) => new() { DoubleVal = d };

        internal static Vector2 WithComponent(this Vector2 v, int index, float val) {
            v[index] = val;
            return v;
        }

        internal static Vector3 WithComponent(this Vector3 v, int index, float val) {
            v[index] = val;
            return v;
        }

#if UNITY_UGUI_INSTALLED
        internal static Vector2 GetFlexibleSize(this UnityEngine.UI.LayoutElement target) =>
            new(target.flexibleWidth, target.flexibleHeight);

        internal static void SetFlexibleSize(this UnityEngine.UI.LayoutElement target, Vector2 vector2) {
            target.flexibleWidth = vector2.x;
            target.flexibleHeight = vector2.y;
        }

        internal static Vector2 GetMinSize(this UnityEngine.UI.LayoutElement target) =>
            new(target.minWidth, target.minHeight);

        internal static void SetMinSize(this UnityEngine.UI.LayoutElement target, Vector2 vector2) {
            target.minWidth = vector2.x;
            target.minHeight = vector2.y;
        }

        internal static Vector2 GetPreferredSize(this UnityEngine.UI.LayoutElement target) =>
            new(target.preferredWidth, target.preferredHeight);

        internal static void SetPreferredSize(this UnityEngine.UI.LayoutElement target, Vector2 vector2) {
            target.preferredWidth = vector2.x;
            target.preferredHeight = vector2.y;
        }

        internal static Vector2 GetNormalizedPosition(this UnityEngine.UI.ScrollRect target) =>
            new(target.horizontalNormalizedPosition, target.verticalNormalizedPosition);

        internal static void SetNormalizedPosition(this UnityEngine.UI.ScrollRect target, Vector2 vector2) {
            target.horizontalNormalizedPosition = vector2.x;
            target.verticalNormalizedPosition = vector2.y;
        }
#endif

#if UI_ELEMENTS_MODULE_INSTALLED
        internal static Vector2 GetTopLeft(this UnityEngine.UIElements.VisualElement e) {
            var resolvedStyle = e.resolvedStyle;
            return new Vector2(resolvedStyle.left, resolvedStyle.top);
        }

        internal static void SetTopLeft(this UnityEngine.UIElements.VisualElement e, Vector2 c) {
            var style = e.style;
            style.left = c.x;
            style.top = c.y;
        }

        internal static Rect GetResolvedStyleRect(this UnityEngine.UIElements.VisualElement e) {
            var resolvedStyle = e.resolvedStyle;

            return new Rect(
                resolvedStyle.left,
                resolvedStyle.top,
                resolvedStyle.width,
                resolvedStyle.height
            );
        }

        internal static void SetStyleRect(this UnityEngine.UIElements.VisualElement e, Rect c) {
            var style = e.style;
            style.left = c.x;
            style.top = c.y;
            style.width = c.width;
            style.height = c.height;
        }
#endif

        static bool TryGetPropertyBlock(object target, out MaterialPropertyBlock result) {
            var renderer = target as Renderer;

            if (renderer.HasPropertyBlock()) {
                result = PrimeTweenManager.Instance.materialPropertyBlockForGetter;
                renderer.GetPropertyBlock(result);
                return true;
            }

            result = null;
            return false;
        }

        internal static bool TryGetPropertyBlockColor(object target, int propId, out Color result) {
            if (TryGetPropertyBlock(target, out var b) && b.HasColor(propId)) {
                result = b.GetColor(propId);
                return true;
            }

            result = default;
            return false;
        }

        internal static bool TryGetPropertyBlockVector(object target, int propId, out Vector4 result) {
            if (TryGetPropertyBlock(target, out var b) && b.HasVector(propId)) {
                result = b.GetVector(propId);
                return true;
            }

            result = default;
            return false;
        }

        internal static bool TryGetPropertyBlockFloat(object target, int propId, out float result) {
            if (TryGetPropertyBlock(target, out var b) && b.HasFloat(propId)) {
                result = b.GetFloat(propId);
                return true;
            }

            result = default;
            return false;
        }
    }
}