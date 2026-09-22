using UnityEngine;

namespace ZStudio.UniKit {
    /// <summary>
    /// Camera Scaler 的纯计算逻辑，不依赖 MonoBehaviour 生命周期。
    /// </summary>
    public static class CameraScalerMath {
        public const float DefaultReferenceWidth = 720f;
        public const float DefaultReferenceHeight = 1280f;
        public const float DefaultOrthographicSize = 5f;
        public const float DefaultFieldOfView = 60f;
        public const float MinimumFieldOfView = 1f;
        public const float MaximumFieldOfView = 179f;

        // 宽高比的计算边界，限制两个比例相除时的放大倍数；不表示推荐的设备比例。
        public const float MinimumAspect = 0.01f;
        public const float MaximumAspect = 100f;

        /// <summary>按适配模式计算正交相机的垂直半尺寸。</summary>
        public static float CalculateOrthographicSize(float referenceSize, float referenceAspect, float currentAspect,
            ScaleMode mode, float matchWidthOrHeight, float zoom) {
            float safeReferenceAspect = GetSafeAspect(referenceAspect, DefaultReferenceWidth / DefaultReferenceHeight);
            float safeCurrentAspect = GetSafeAspect(currentAspect, safeReferenceAspect);
            float constantHeightSize = SanitizeOrthographicSize(referenceSize);
            
            // 水平半尺寸 = 垂直半尺寸 × 宽高比；保持参考宽度时，反推当前垂直半尺寸。
            float constantWidthSize = constantHeightSize * (safeReferenceAspect / safeCurrentAspect);
            float result = SelectFitValue(constantWidthSize, constantHeightSize, mode, matchWidthOrHeight);
            
            // Zoom 越大，可视范围越小；正交半尺寸不能降到零。
            return Mathf.Max(result / SanitizeZoom(zoom), Mathf.Epsilon);
        }

        /// <summary>按适配模式计算透视相机的垂直视野角。</summary>
        public static float CalculateFieldOfView(float referenceVerticalFov, float referenceAspect, float currentAspect,
            ScaleMode mode, float matchWidthOrHeight, float zoom) {
            float safeReferenceAspect = GetSafeAspect(referenceAspect, DefaultReferenceWidth / DefaultReferenceHeight);
            float safeCurrentAspect = GetSafeAspect(currentAspect, safeReferenceAspect);
            float safeReferenceFov = SanitizeFieldOfView(referenceVerticalFov);
            float constantHeightScale = FovToProjectionScale(safeReferenceFov);

            // tan(FOV / 2) 是单位距离处的投影半尺寸，可像正交尺寸一样按比例缩放。
            // 适配和 Zoom 完成后才限制输出角度，避免中间角度限幅丢失投影比例。
            float constantWidthScale = constantHeightScale * (safeReferenceAspect / safeCurrentAspect);
            float resultScale = SelectFitValue(constantWidthScale, constantHeightScale, mode, matchWidthOrHeight);
            return ProjectionScaleToFov(resultScale / SanitizeZoom(zoom));
        }

        /// <summary>在宽度匹配值和高度匹配值之间按模式取值。</summary>
        public static float SelectFitValue(float constantWidth, float constantHeight, ScaleMode mode,
            float matchWidthOrHeight) {
            return mode switch {
                ScaleMode.ConstantHeight => constantHeight,
                ScaleMode.ConstantWidth => constantWidth,
                ScaleMode.MatchWidthOrHeight => GeometricLerp(constantWidth, constantHeight,
                    SanitizeMatch(matchWidthOrHeight)),
                
                // 较大的垂直范围可容纳完整参考区域；较小的范围则通过裁切避免露出额外区域。
                ScaleMode.Expand => Mathf.Max(constantWidth, constantHeight),
                ScaleMode.Shrink => Mathf.Min(constantWidth, constantHeight),
                _ => constantWidth
            };
        }

        /// <summary>在对数空间插值，语义与 CanvasScaler 的 Match 一致。</summary>
        public static float GeometricLerp(float from, float to, float t) {
            // 对数只接受正数；不满足前提时退回线性插值，此分支不负责修正非法端点。
            if (!IsFinitePositive(from) || !IsFinitePositive(to)) {
                return Mathf.Lerp(from, to, t);
            }

            // 等价于 from^(1-t) × to^t，按倍率而非尺寸差值在宽/高匹配之间过渡。
            float fromLog = Mathf.Log(from, 2f);
            float toLog = Mathf.Log(to, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(fromLog, toLog, t));
        }

        /// <summary>将水平视野角转换为指定宽高比下的垂直视野角。</summary>
        public static float CalcVerticalFov(float horizontalFovInDegrees, float aspectRatio) {
            float safeAspect = GetSafeAspect(aspectRatio, 1f);
            float horizontalScale = FovToProjectionScale(horizontalFovInDegrees);
            return ProjectionScaleToFov(horizontalScale / safeAspect);
        }

        /// <summary>将垂直视野角转换为指定宽高比下的水平视野角。</summary>
        public static float CalcHorizontalFov(float verticalFovInDegrees, float aspectRatio) {
            float safeAspect = GetSafeAspect(aspectRatio, 1f);
            float verticalScale = FovToProjectionScale(verticalFovInDegrees);
            return ProjectionScaleToFov(verticalScale * safeAspect);
        }

        /// <summary>将以度为单位的视野角转换为单位距离处的投影半尺寸 tan(FOV / 2)。</summary>
        public static float FovToProjectionScale(float fovInDegrees) {
            return Mathf.Tan(SanitizeFieldOfView(fovInDegrees) * Mathf.Deg2Rad * 0.5f);
        }

        /// <summary>将投影半尺寸转换为角度并限制到 1～179 度；非正数或非有限值返回 1 度。</summary>
        public static float ProjectionScaleToFov(float projectionScale) {
            if (!IsFinitePositive(projectionScale)) {
                return MinimumFieldOfView;
            }

            float fov = 2f * Mathf.Atan(projectionScale) * Mathf.Rad2Deg;
            return Mathf.Clamp(fov, MinimumFieldOfView, MaximumFieldOfView);
        }

        /// <summary>分别修正参考分辨率的宽和高，非法分量回退为 1。</summary>
        public static Vector2 SanitizeReferenceResolution(Vector2 resolution) {
            return new Vector2(SanitizeDimension(resolution.x), SanitizeDimension(resolution.y));
        }

        /// <summary>保留有限正数尺寸，其余值回退为 1。</summary>
        public static float SanitizeDimension(float value) {
            return IsFinitePositive(value) ? value : 1f;
        }

        /// <summary>计算宽/高并限制比例范围；任一分量非法时使用默认参考比例。</summary>
        public static float CalculateAspect(Vector2 resolution) {
            if (!IsFinitePositive(resolution.x) || !IsFinitePositive(resolution.y)) {
                return DefaultReferenceWidth / DefaultReferenceHeight;
            }

            double aspect = (double)resolution.x / resolution.y;
            return Mathf.Clamp((float)aspect, MinimumAspect, MaximumAspect);
        }

        /// <summary>将匹配权重限制到 0～1；NaN 或无穷大回退为居中的 0.5。</summary>
        public static float SanitizeMatch(float value) {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0.5f;
        }

        /// <summary>将有限角度限制到 1～179 度；NaN 或无穷大回退为默认视野角。</summary>
        public static float SanitizeFieldOfView(float value) {
            return IsFinite(value)
                ? Mathf.Clamp(value, MinimumFieldOfView, MaximumFieldOfView)
                : DefaultFieldOfView;
        }

        /// <summary>保留有限正数半尺寸，其余值回退为默认正交半尺寸。</summary>
        public static float SanitizeOrthographicSize(float value) {
            return IsFinitePositive(value) ? value : DefaultOrthographicSize;
        }

        /// <summary>保留有限正数倍率，其余值回退为不缩放的 1。</summary>
        public static float SanitizeZoom(float value) {
            return IsFinitePositive(value) ? value : 1f;
        }

        /// <summary>限制有效比例的范围；非法比例原样返回备用值，调用者需保证备用值有效。</summary>
        public static float GetSafeAspect(float aspect, float fallbackAspect) {
            return IsFinitePositive(aspect)
                ? Mathf.Clamp(aspect, MinimumAspect, MaximumAspect)
                : fallbackAspect;
        }

        public static bool IsValidScaleMode(ScaleMode mode) {
            return mode is >= ScaleMode.ConstantHeight and <= ScaleMode.Shrink;
        }

        public static bool IsValidApplyTiming(ApplyTiming timing) {
            return timing is >= ApplyTiming.Update and <= ApplyTiming.OnPreCull;
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;

        public static bool Approximately(Vector2 left, Vector2 right) {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }
    }
}