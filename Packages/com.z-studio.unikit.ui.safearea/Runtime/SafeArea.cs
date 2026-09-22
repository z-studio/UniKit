using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 驱动 RectTransform，使其适配设备安全区域的组件。
    /// </summary>
    /// <remarks>
    /// 可配置内缩边缘、居中对齐方式和参考方向。边缘和对齐方向
    /// 均基于参考方向设置，并在运行时重新映射到当前设备方向。
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("UI (Canvas)/ZStudio/Safe Area")]
    [ExecuteAlways]
    [Preserve]
    public class SafeArea : UIBehaviour {
        private RectTransform m_RectTransform;
        private DrivenRectTransformTracker m_Tracker;

        /// <summary>
        /// 基于参考方向设置的各边内缩值（上、右、下、左）。
        /// </summary>
        [Serializable]
        public struct EdgeInsets {
            [Min(0)]
            [Tooltip("相对于参考方向的上边缘。")]
            public float Top;

            [Min(0)]
            [Tooltip("相对于参考方向的右边缘。")]
            public float Right;

            [Min(0)]
            [Tooltip("相对于参考方向的下边缘。")]
            public float Bottom;

            [Min(0)]
            [Tooltip("相对于参考方向的左边缘。")]
            public float Left;

            public static EdgeInsets Zero => default;

            public readonly bool Approximately(in EdgeInsets other) =>
                Mathf.Approximately(Top, other.Top)
                && Mathf.Approximately(Right, other.Right)
                && Mathf.Approximately(Bottom, other.Bottom)
                && Mathf.Approximately(Left, other.Left);
        }

        /// <summary>
        /// 指定需要适配设备安全区域的边缘，方向相对于参考方向。
        /// </summary>
        [Flags]
        public enum SafeAreaMode {
            /// <summary>
            /// 将上边缘内缩至设备顶部安全区域内，以避开刘海、状态栏等。
            /// </summary>
            Top = 1 << 0,

            /// <summary>
            /// 将右边缘内缩至设备右侧安全区域内。
            /// </summary>
            Right = 1 << 1,

            /// <summary>
            /// 将下边缘内缩至设备底部安全区域内，以避开主屏幕指示条等。
            /// </summary>
            Bottom = 1 << 2,

            /// <summary>
            /// 将左边缘内缩至设备左侧安全区域内。
            /// </summary>
            Left = 1 << 3
        }

        /// <summary>
        /// 指定内缩区域的居中对齐方向，使 UI 居中。方向相对于参考方向。
        /// </summary>
        [Flags]
        public enum AlignmentMode {
            /// <summary>
            /// 将左右两侧的内缩量统一为较大值，使内缩区域水平居中。
            /// </summary>
            CenterHorizontally = 1 << 0,

            /// <summary>
            /// 将上下两侧的内缩量统一为较大值，使内缩区域垂直居中。
            /// </summary>
            CenterVertically = 1 << 1,
        }

        internal enum ScreenOrientation {
            Portrait = 0,
            LandscapeLeft = 1,
            PortraitUpsideDown = 2,
            LandscapeRight = 3,
        }

        // 将 4 位标志值循环移位 shift 位（通过循环回绕支持负数位移）。
        internal static SafeAreaMode RotateFlag(SafeAreaMode mode, int shift) {
            int bits = (int)mode & 0b1111;

            // 将位移量归一化到 [0, 3]，兼容负数和大于 3 的值。
            int normalizedShift = (shift % 4 + 4) % 4;
            int inverseShift = 4 - normalizedShift;

            // 在 4 位范围内执行循环移位。
            int shiftedLeft = bits << normalizedShift; // 保留在范围内的位
            int shiftedRight = bits >> inverseShift; // 回绕到低位的位
            int rotated = (shiftedLeft | shiftedRight) & 0b1111; // 合并并仅保留低 4 位

            return (SafeAreaMode)rotated;
        }

        /// <summary>
        /// 使用与 <see cref="RotateFlag"/> 相同的旋转规则，将参考方向下的边缘内缩值映射到当前屏幕空间的边缘。
        /// 顺序为：上、右、下、左。
        /// </summary>
        internal static void RotateInsets(
            in EdgeInsets referenceInsets,
            int shift,
            out float screenTop,
            out float screenRight,
            out float screenBottom,
            out float screenLeft
        ) {
            float[] referenceValues = {
                referenceInsets.Top,
                referenceInsets.Right,
                referenceInsets.Bottom,
                referenceInsets.Left
            };

            int normalizedShift = (shift % 4 + 4) % 4;

            screenTop = referenceValues[(0 - normalizedShift + 4) % 4];
            screenRight = referenceValues[(1 - normalizedShift + 4) % 4];
            screenBottom = referenceValues[(2 - normalizedShift + 4) % 4];
            screenLeft = referenceValues[(3 - normalizedShift + 4) % 4];
        }

        /// <summary>
        /// 在执行边缘安全区域适配和对齐逻辑之前，在屏幕空间应用内边距。
        /// </summary>
        internal static Rect ApplyPadding(
            Rect safeArea,
            float screenLeftPadding,
            float screenRightPadding,
            float screenTopPadding,
            float screenBottomPadding
        ) {
            var total = safeArea;
            total.xMin += screenLeftPadding;
            total.xMax -= screenRightPadding;
            total.yMin += screenBottomPadding;
            total.yMax -= screenTopPadding;
            return total;
        }

        /// <summary>
        /// 将当前屏幕空间方向映射回参考方向空间所需的旋转次数。
        /// </summary>
        private int RotationsFromCurrentToReference => ((int)CurrentOrientation - (int)m_ReferenceOrientation + 4) % 4;

        /// <summary>
        /// 将参考方向空间中的方向映射到当前屏幕空间所需的旋转次数。
        /// </summary>
        private int RotationsFromReferenceToCurrent => -RotationsFromCurrentToReference;

        /// <summary>
        /// 以组件内部枚举表示的当前屏幕方向。
        /// </summary>
        private ScreenOrientation CurrentOrientation {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ToLocalScreenOrientation(Screen.orientation);
        }

        /// <summary>
        /// 在当前设备方向下，此 UI 组件是否将屏幕左边缘内缩至安全区域内。
        /// </summary>
        public bool RespectSafeAreaScreenLeft =>
            HasFlag(m_Edges, RotateFlag(SafeAreaMode.Left, RotationsFromCurrentToReference));

        /// <summary>
        /// 在当前设备方向下，此 UI 组件是否将屏幕右边缘内缩至安全区域内。
        /// </summary>
        public bool RespectSafeAreaScreenRight =>
            HasFlag(m_Edges, RotateFlag(SafeAreaMode.Right, RotationsFromCurrentToReference));

        /// <summary>
        /// 在当前设备方向下，此 UI 组件是否将屏幕下边缘内缩至安全区域内。
        /// </summary>
        public bool RespectSafeAreaScreenBottom =>
            HasFlag(m_Edges, RotateFlag(SafeAreaMode.Bottom, RotationsFromCurrentToReference));

        /// <summary>
        /// 在当前设备方向下，此 UI 组件是否将屏幕上边缘内缩至安全区域内。
        /// </summary>
        public bool RespectSafeAreaScreenTop =>
            HasFlag(m_Edges, RotateFlag(SafeAreaMode.Top, RotationsFromCurrentToReference));

        [Tooltip("设置边缘（Edges）和对齐（Alignment）方向时使用的参考方向。组件会在运行时将这些方向重新映射到当前设备方向。")]
        [SerializeField]
        private ScreenOrientation m_ReferenceOrientation;

        [Tooltip("需要内缩以适配安全区域的边缘。方向相对于参考方向。")]
        [SerializeField]
        private SafeAreaMode m_Edges = SafeAreaMode.Top | SafeAreaMode.Bottom | SafeAreaMode.Left | SafeAreaMode.Right;

        [Tooltip("调整内缩量，使 UI 区域居中。方向相对于参考方向。")]
        [SerializeField]
        private AlignmentMode m_Alignment;

        [Tooltip("基于参考方向设置的各边额外内缩量，单位为像素。在设备安全区域的基础上应用，并先于边缘安全区域适配逻辑执行。")]
        [SerializeField]
        private EdgeInsets m_Padding;

        [Tooltip("当有效安全区域矩形发生变化时触发（屏幕空间，单位为像素）。")]
        [SerializeField]
        private SafeAreaChangedUnityEvent m_OnSafeAreaChanged;

        [NonSerialized]
        private ScreenOrientation m_PreviousReferenceOrientation;

        [NonSerialized]
        private SafeAreaMode m_PreviousEdges;

        [NonSerialized]
        private AlignmentMode m_PreviousAlignment;

        [NonSerialized]
        private EdgeInsets m_PreviousPadding;

        [NonSerialized]
        private Rect m_PreviousSafeArea;

        [NonSerialized]
        private Rect m_PreviousEffectiveSafeArea;

        [NonSerialized]
        private Vector2Int m_PreviousResolution;

        [NonSerialized]
        private UnityEngine.ScreenOrientation m_PreviousOrientation;

        /// <summary>
        /// 应用内边距、边缘安全区域适配和对齐后的有效安全区域矩形，以屏幕像素表示。
        /// </summary>
        public Rect EffectiveSafeArea { get; private set; }

        /// <summary>
        /// 当 <see cref="EffectiveSafeArea"/> 发生变化时触发。
        /// </summary>
        public event Action<Rect> SafeAreaChanged;

        /// <summary>
        /// 用于确定安全区域适配边缘的参考方向。
        /// </summary>
        public UnityEngine.ScreenOrientation ReferenceOrientation {
            get => ToUnityScreenOrientation(m_ReferenceOrientation);
            set => m_ReferenceOrientation = ToLocalScreenOrientation(value);
        }

        /// <summary>
        /// 控制设备的哪些边缘需要内缩以适配安全区域。
        /// </summary>
        public SafeAreaMode Edges {
            get => m_Edges;
            set => m_Edges = value;
        }

        /// <summary>
        /// 指定居中轴，通过使该轴两侧的内缩量相等来将矩形居中。
        /// </summary>
        public AlignmentMode Alignment {
            get => m_Alignment;
            set => m_Alignment = value;
        }

        /// <summary>
        /// 基于参考方向设置的各边额外内缩量，单位为像素。
        /// </summary>
        public EdgeInsets Padding {
            get => m_Padding;
            set => m_Padding = value;
        }

        /// <summary>脚本实例加载时调用。缓存 RectTransform 并初始化受驱动变换跟踪器。</summary>
        protected override void Awake() {
            base.Awake();

            m_RectTransform = transform as RectTransform;
            m_Tracker = new DrivenRectTransformTracker();
        }

        /// <summary>组件启用时调用。获取 RectTransform 的驱动控制权并应用安全区域设置。</summary>
        protected override void OnEnable() {
            base.OnEnable();

            if (m_RectTransform == null) {
                m_RectTransform = transform as RectTransform;
            }

            if (m_RectTransform.drivenByObject == null || m_RectTransform.drivenByObject == this) {
                ClaimRectTransformDrivenOwnership();
                ApplySafeArea();
            }
        }

        /// <summary>组件禁用时调用。清除对 RectTransform 属性的跟踪。</summary>
        protected override void OnDisable() {
            SafeClearDrivenRectTransformTracker();
            base.OnDisable();
        }

        /// <summary>组件销毁时调用。清除对 RectTransform 属性的跟踪。</summary>
        protected override void OnDestroy() {
            SafeClearDrivenRectTransformTracker();
            base.OnDestroy();
        }

        private void SafeClearDrivenRectTransformTracker() {
            if (m_RectTransform == null) {
                m_RectTransform = transform as RectTransform;
            }

            if (m_RectTransform != null && m_RectTransform.drivenByObject == this) {
                m_Tracker.Clear();
            }
        }

        private void Update() {
            if (m_RectTransform.drivenByObject == null) {
                ClaimRectTransformDrivenOwnership();
                ApplySafeArea();
                return;
            }

            // 存在其他 RectTransform 驱动组件时让出控制权，并在自定义编辑器中显示冲突警告。
            if (m_RectTransform.drivenByObject != this) {
                return;
            }

            if (Screen.safeArea != m_PreviousSafeArea
                || Screen.width != m_PreviousResolution.x
                || Screen.height != m_PreviousResolution.y
                || Screen.orientation != m_PreviousOrientation
                || m_PreviousReferenceOrientation != m_ReferenceOrientation
                || m_PreviousEdges != m_Edges
                || m_PreviousAlignment != m_Alignment
                || !m_Padding.Approximately(m_PreviousPadding)
                || HasNaNDrivenValues()) {
                ApplySafeArea();
            }
        }

        private void ClaimRectTransformDrivenOwnership() {
            m_Tracker.Add(
                this,
                m_RectTransform,
                DrivenTransformProperties.AnchorMax
                | DrivenTransformProperties.AnchorMin
                | DrivenTransformProperties.SizeDelta
                | DrivenTransformProperties.AnchoredPosition
            );
        }

        private void ApplySafeArea() {
            var safe = Screen.safeArea;

            if (safe.width == 0 || safe.height == 0 || Screen.width == 0 || Screen.height == 0) {
                return; // 初始化保护：下一帧再尝试应用安全区域。
            }

            UpdatePreviousDataCache();

            bool isLandscape = m_PreviousOrientation is UnityEngine.ScreenOrientation.LandscapeLeft or
                                                        UnityEngine.ScreenOrientation.LandscapeRight;

            bool isReferenceLandscape =
                m_ReferenceOrientation is ScreenOrientation.LandscapeLeft or ScreenOrientation.LandscapeRight;

            RotateInsets(
                m_Padding,
                RotationsFromReferenceToCurrent,
                out var padTop,
                out var padRight,
                out var padBottom,
                out var padLeft
            );

            var adjustedSafeArea = ApplyPadding(
                m_PreviousSafeArea,
                padLeft,
                padRight,
                padTop,
                padBottom
            );

            var respectSafeAreaScreenEdges = RotateFlag(m_Edges, RotationsFromReferenceToCurrent);
            var isAlignmentFlipped = isLandscape != isReferenceLandscape;

            var (min, max) = CalculateAnchors(
                m_PreviousResolution.x,
                m_PreviousResolution.y,
                adjustedSafeArea,
                respectSafeAreaScreenEdges,
                Alignment,
                isAlignmentFlipped
            );

            m_RectTransform.anchorMin = min;
            m_RectTransform.anchorMax = max;
            m_RectTransform.offsetMin = Vector2.zero;
            m_RectTransform.offsetMax = Vector2.zero;

            var effectiveSafeArea = RectFromNormalizedAnchors(
                min,
                max,
                m_PreviousResolution.x,
                m_PreviousResolution.y
            );

            NotifyEffectiveSafeAreaChanged(effectiveSafeArea);
        }

        internal static (Vector2 min, Vector2 max) CalculateAnchors(
            int screenWidth,
            int screenHeight,
            Rect safeArea,
            SafeAreaMode respectSafeAreaScreenEdges,
            AlignmentMode alignmentMode,
            bool isAlignmentFlipped
        ) {
            Vector2 min = safeArea.position;
            Vector2 max = safeArea.position + safeArea.size;

            var horizontalAlignmentMode = isAlignmentFlipped
                ? AlignmentMode.CenterVertically
                : AlignmentMode.CenterHorizontally;

            var verticalAlignmentMode = isAlignmentFlipped
                ? AlignmentMode.CenterHorizontally
                : AlignmentMode.CenterVertically;

            // X 轴
            if (!HasFlag(respectSafeAreaScreenEdges, SafeAreaMode.Left)) {
                min.x = 0f;
            }

            if (!HasFlag(respectSafeAreaScreenEdges, SafeAreaMode.Right)) {
                max.x = screenWidth;
            }

            if (HasFlag(alignmentMode, horizontalAlignmentMode)) {
                var maxOffsetX = Mathf.Max(min.x, screenWidth - max.x);
                min.x = maxOffsetX;
                max.x = screenWidth - maxOffsetX;
            }

            // Y 轴
            if (!HasFlag(respectSafeAreaScreenEdges, SafeAreaMode.Bottom)) {
                min.y = 0f;
            }

            if (!HasFlag(respectSafeAreaScreenEdges, SafeAreaMode.Top)) {
                max.y = screenHeight;
            }

            if (HasFlag(alignmentMode, verticalAlignmentMode)) {
                var maxOffsetY = Mathf.Max(min.y, screenHeight - max.y);
                min.y = maxOffsetY;
                max.y = screenHeight - maxOffsetY;
            }

            min.x /= screenWidth;
            min.y /= screenHeight;
            max.x /= screenWidth;
            max.y /= screenHeight;

            return (min, max);
        }

        internal static Rect RectFromNormalizedAnchors(Vector2 anchorMin, Vector2 anchorMax, int screenWidth, int screenHeight) {
            var min = new Vector2(anchorMin.x * screenWidth, anchorMin.y * screenHeight);
            var max = new Vector2(anchorMax.x * screenWidth, anchorMax.y * screenHeight);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void NotifyEffectiveSafeAreaChanged(Rect effectiveSafeArea) {
            if (effectiveSafeArea == m_PreviousEffectiveSafeArea) {
                return;
            }

            m_PreviousEffectiveSafeArea = effectiveSafeArea;
            EffectiveSafeArea = effectiveSafeArea;

            SafeAreaChanged?.Invoke(effectiveSafeArea);
            m_OnSafeAreaChanged?.Invoke(effectiveSafeArea);
        }

        private void UpdatePreviousDataCache() {
            m_PreviousSafeArea = Screen.safeArea;
            m_PreviousResolution = new Vector2Int(Screen.width, Screen.height);
            m_PreviousOrientation = Screen.orientation;
            m_PreviousReferenceOrientation = m_ReferenceOrientation;
            m_PreviousAlignment = m_Alignment;
            m_PreviousEdges = m_Edges;
            m_PreviousPadding = m_Padding;
        }

#if UNITY_EDITOR
        /// <summary>在编辑器中加载脚本或修改检视面板的值时调用。对 RectTransform 驱动组件之间的冲突发出警告。</summary>
        protected override void OnValidate() {
            base.OnValidate();

            if (m_RectTransform == null) {
                m_RectTransform = transform as RectTransform;
            }

            if (TryGetComponent<ILayoutController>(out var layout)
                && layout is Behaviour behaviour
                && behaviour.enabled) {
                Debug.LogWarning(
                    $"'{GetType().Name}' conflicts with '{layout.GetType().Name}' on '{name}'.",
                    this
                );
            }

            var driver = m_RectTransform == null ? null : m_RectTransform.drivenByObject;

            if (driver != this && driver != null) {
                var component = (Component)driver;

                Debug.LogWarning(
                    $"'{GetType().Name}' conflicts with '{driver.GetType().Name}' on '{component.gameObject.name}'.",
                    this
                );
            }
        }
#endif

        /// <summary>
        /// 将 Unity 的 <see cref="UnityEngine.ScreenOrientation"/> 转换为 SafeArea 的 <see cref="ScreenOrientation"/>。
        /// </summary>
        internal static ScreenOrientation ToLocalScreenOrientation(UnityEngine.ScreenOrientation orientation) =>
            orientation switch {
                UnityEngine.ScreenOrientation.Portrait => ScreenOrientation.Portrait,
                UnityEngine.ScreenOrientation.PortraitUpsideDown => ScreenOrientation.PortraitUpsideDown,
                UnityEngine.ScreenOrientation.LandscapeLeft => ScreenOrientation.LandscapeLeft,
                UnityEngine.ScreenOrientation.LandscapeRight => ScreenOrientation.LandscapeRight,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(orientation),
                    orientation,
                    "Unsupported Unity screen orientation."
                )
            };

        /// <summary>
        /// 将 SafeArea 的 <see cref="ScreenOrientation"/> 转换为 Unity 的 <see cref="UnityEngine.ScreenOrientation"/>。
        /// </summary>
        internal static UnityEngine.ScreenOrientation ToUnityScreenOrientation(ScreenOrientation orientation) =>
            orientation switch {
                ScreenOrientation.Portrait => UnityEngine.ScreenOrientation.Portrait,
                ScreenOrientation.PortraitUpsideDown => UnityEngine.ScreenOrientation.PortraitUpsideDown,
                ScreenOrientation.LandscapeLeft => UnityEngine.ScreenOrientation.LandscapeLeft,
                ScreenOrientation.LandscapeRight => UnityEngine.ScreenOrientation.LandscapeRight,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(orientation),
                    orientation,
                    "Unsupported SafeArea screen orientation."
                )
            };

        /// <summary>
        /// 计算并返回给定参考方向映射到当前屏幕空间后的方向。
        /// </summary>
        /// <param name="referenceOrientationDirection">待映射的参考方向空间中的方向。</param>
        /// <returns>当前设备方向下的屏幕空间方向。</returns>
        public SafeAreaMode GetReferenceOrientationMappedDirection(SafeAreaMode referenceOrientationDirection) =>
            RotateFlag(referenceOrientationDirection, RotationsFromReferenceToCurrent);

        private bool HasNaNDrivenValues() {
            return float.IsNaN(m_RectTransform.anchorMin.x)
                   || float.IsNaN(m_RectTransform.anchorMin.y)
                   || float.IsNaN(m_RectTransform.anchorMax.x)
                   || float.IsNaN(m_RectTransform.anchorMax.y);
        }

        internal static bool HasFlag(SafeAreaMode value, SafeAreaMode flag) {
            return (value & flag) == flag;
        }

        private static bool HasFlag(AlignmentMode value, AlignmentMode flag) {
            return (value & flag) == flag;
        }
    }

    /// <summary>
    /// 可在检视面板中配置的事件，传递以屏幕像素表示的有效安全区域矩形。
    /// </summary>
    [Serializable]
    public class SafeAreaChangedUnityEvent : UnityEvent<Rect> { }
}
