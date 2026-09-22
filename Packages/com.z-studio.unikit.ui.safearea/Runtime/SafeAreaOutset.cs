using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 在选定边缘上，将子对象的 <see cref="RectTransform"/> 从其布局基准向外扩展。
    /// </summary>
    /// <remarks>
    /// 当特定元素需要延伸至非安全区域（例如刘海下方的角标）时，
    /// 将此组件挂载到 <see cref="SafeArea"/> 容器内的子对象上。外扩值基于参考方向设置。
    /// 边缘延伸从父级的 <see cref="SafeArea.EffectiveSafeArea"/> 扩展到物理屏幕边缘。
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("UI (Canvas)/ZStudio/Safe Area Outset")]
    [DefaultExecutionOrder(100)]
    [ExecuteAlways]
    [Preserve]
    public class SafeAreaOutset : UIBehaviour {
        /// <summary>
        /// 将屏幕空间的外扩量应用到已记录的 RectTransform 布局基准的方式。
        /// </summary>
        public enum OutsetMode {
            /// <summary>
            /// 独立向外扩展每个选定边缘。适用于拉伸锚点和延伸至屏幕边缘的条形元素。
            /// </summary>
            Expand = 0,

            /// <summary>
            /// 平移整个矩形并保持尺寸不变。适用于固定尺寸的图标和角标。
            /// </summary>
            Translate = 1,
        }

        private RectTransform m_RectTransform;
        private DrivenRectTransformTracker m_Tracker;
        private SafeArea m_ParentSafeArea;

        // 原始布局随组件保存，编辑器和 Player 共用同一份数据，避免将外扩结果再次作为输入。
        [SerializeField, HideInInspector]
        private Vector2 m_BaselineAnchorMin;

        [SerializeField, HideInInspector]
        private Vector2 m_BaselineAnchorMax;

        [SerializeField, HideInInspector]
        private Vector2 m_BaselineOffsetMin;

        [SerializeField, HideInInspector]
        private Vector2 m_BaselineOffsetMax;

        [SerializeField, HideInInspector]
        private bool m_HasBaseline;

        [NonSerialized]
        private bool m_RefreshRequired = true;

        [NonSerialized]
        private float m_PreviousLocalScale;

        [Tooltip("设置外扩量（Outset）和延伸边缘（Bleed Edges）时使用的参考方向。运行时会将这些方向重新映射到当前设备方向。")]
        [SerializeField]
        private SafeArea.ScreenOrientation m_ReferenceOrientation;

        [Tooltip(
            "扩展模式（Expand）独立向外扩展各边缘（默认模式，与边缘延伸的含义一致）。"
            + "平移模式（Translate）移动整个矩形并保持尺寸不变（适用于固定尺寸的图标）。"
        )]
        [SerializeField]
        private OutsetMode m_Mode;

        [Header("Outset")]
        [Tooltip("基于参考方向设置的各边固定外扩量，单位为像素。在元素原始布局上应用，修改参数不会累加外扩。")]
        [SerializeField]
        private SafeArea.EdgeInsets m_Outset;

        [Header("Bleed")]
        [Tooltip(
            "从父级 SafeArea 的有效区域（包含其内边距）延伸至物理屏幕边界的边缘。"
            + "不存在父级 SafeArea 时，使用 Screen.safeArea 作为参考区域。"
            + "方向相对于参考方向。"
        )]
        [SerializeField]
        private SafeArea.SafeAreaMode m_BleedEdges;

        [NonSerialized]
        private SafeArea.ScreenOrientation m_PreviousReferenceOrientation;

        [NonSerialized]
        private SafeArea.EdgeInsets m_PreviousOutset;

        [NonSerialized]
        private SafeArea.SafeAreaMode m_PreviousBleedEdges;

        [NonSerialized]
        private OutsetMode m_PreviousApplyMode;

        [NonSerialized]
        private Rect m_PreviousBleedReferenceRect;

        [NonSerialized]
        private Rect m_PreviousSafeArea;

        [NonSerialized]
        private Vector2Int m_PreviousResolution;

        [NonSerialized]
        private ScreenOrientation m_PreviousOrientation;

        /// <summary>
        /// 设置 <see cref="Outset"/> 和 <see cref="BleedEdges"/> 时使用的参考方向。
        /// </summary>
        public ScreenOrientation ReferenceOrientation {
            get => SafeArea.ToUnityScreenOrientation(m_ReferenceOrientation);
            set => m_ReferenceOrientation = SafeArea.ToLocalScreenOrientation(value);
        }

        /// <summary>
        /// 基于参考方向设置的各边固定外扩量，单位为像素。
        /// </summary>
        public SafeArea.EdgeInsets Outset {
            get => m_Outset;
            set => m_Outset = value;
        }

        /// <summary>
        /// 需要延伸至物理屏幕边界的边缘，基于参考方向设置。
        /// </summary>
        public SafeArea.SafeAreaMode BleedEdges {
            get => m_BleedEdges;
            set => m_BleedEdges = value;
        }

        /// <summary>
        /// 控制外扩时是独立扩展各边缘，还是平移整个矩形。
        /// </summary>
        public OutsetMode Mode {
            get => m_Mode;
            set => m_Mode = value;
        }

        /// <summary>脚本实例加载时调用。</summary>
        protected override void Awake() {
            base.Awake();

            m_RectTransform = transform as RectTransform;
            m_Tracker = new DrivenRectTransformTracker();
        }

        /// <summary>组件启用时调用。</summary>
        protected override void OnEnable() {
            base.OnEnable();

            if (m_RectTransform == null) {
                m_RectTransform = transform as RectTransform;
            }

            SubscribeParentSafeArea();
            ApplyOutset();
        }

        /// <summary>组件禁用时调用。</summary>
        protected override void OnDisable() {
            UnsubscribeParentSafeArea();

            // 仅恢复自己控制的布局，避免覆盖其他驱动器的结果。
            if (m_RectTransform != null && m_RectTransform.drivenByObject == this) {
                RestoreBaseline();
            }

            SafeClearDrivenRectTransformTracker();
            m_HasBaseline = false;
            base.OnDisable();
        }

        /// <summary>组件销毁时调用。</summary>
        protected override void OnDestroy() {
            UnsubscribeParentSafeArea();
            SafeClearDrivenRectTransformTracker();
            base.OnDestroy();
        }

        private void Update() {
            if (!isActiveAndEnabled || m_RectTransform == null) {
                return;
            }

            if (m_RectTransform.drivenByObject == null) {
                ApplyOutset();
                return;
            }

            if (m_RectTransform.drivenByObject != this) {
                return;
            }

            if (NeedsRefresh()) {
                ApplyOutset();
            }
        }

        private bool NeedsRefresh() {
            return m_RefreshRequired
                   || !Mathf.Approximately(GetLocalPixelsPerScreenPixel(), m_PreviousLocalScale)
                   || Screen.safeArea != m_PreviousSafeArea
                   || GetBleedReferenceRect() != m_PreviousBleedReferenceRect
                   || Screen.width != m_PreviousResolution.x
                   || Screen.height != m_PreviousResolution.y
                   || Screen.orientation != m_PreviousOrientation
                   || m_PreviousReferenceOrientation != m_ReferenceOrientation
                   || !m_Outset.Approximately(m_PreviousOutset)
                   || m_PreviousBleedEdges != m_BleedEdges
                   || m_PreviousApplyMode != m_Mode;
        }

        /// <summary>
        /// 返回边缘延伸所依据的屏幕空间矩形：存在父级安全区域组件时，使用其有效安全区域，否则使用设备安全区域。
        /// </summary>
        private Rect GetBleedReferenceRect() {
            if (m_ParentSafeArea != null) {
                var effective = m_ParentSafeArea.EffectiveSafeArea;

                if (effective.width > 0 && effective.height > 0) {
                    return effective;
                }
            }

            return Screen.safeArea;
        }

        private void CaptureBaselineIfNeeded() {
            if (!m_HasBaseline) {
                CaptureBaseline();
            }
        }

        private void CaptureBaseline() {
            m_BaselineAnchorMin = m_RectTransform.anchorMin;
            m_BaselineAnchorMax = m_RectTransform.anchorMax;
            m_BaselineOffsetMin = m_RectTransform.offsetMin;
            m_BaselineOffsetMax = m_RectTransform.offsetMax;
            m_HasBaseline = true;
        }

        private void RestoreBaseline() {
            if (!m_HasBaseline || m_RectTransform == null) {
                return;
            }

            m_RectTransform.anchorMin = m_BaselineAnchorMin;
            m_RectTransform.anchorMax = m_BaselineAnchorMax;
            m_RectTransform.offsetMin = m_BaselineOffsetMin;
            m_RectTransform.offsetMax = m_BaselineOffsetMax;
        }

        protected override void OnTransformParentChanged() {
            base.OnTransformParentChanged();

            if (isActiveAndEnabled) {
                SubscribeParentSafeArea();
                m_RefreshRequired = true;
            }
        }

        private void SubscribeParentSafeArea() {
            UnsubscribeParentSafeArea();

            m_ParentSafeArea = GetComponentInParent<SafeArea>();

            if (m_ParentSafeArea != null) {
                m_ParentSafeArea.SafeAreaChanged += OnParentSafeAreaChanged;
            }
        }

        private void UnsubscribeParentSafeArea() {
            if (m_ParentSafeArea != null) {
                m_ParentSafeArea.SafeAreaChanged -= OnParentSafeAreaChanged;
                m_ParentSafeArea = null;
            }
        }

        private void OnParentSafeAreaChanged(Rect _) {
            if (isActiveAndEnabled) {
                ApplyOutset();
            }
        }

        private void ClaimRectTransformDrivenOwnership() {
            m_Tracker.Add(
                this,
                m_RectTransform,
                DrivenTransformProperties.AnchoredPosition
                | DrivenTransformProperties.SizeDelta
                | DrivenTransformProperties.AnchorMin
                | DrivenTransformProperties.AnchorMax
            );
        }

        private void SafeClearDrivenRectTransformTracker() {
            if (m_RectTransform == null) {
                m_RectTransform = transform as RectTransform;
            }

            if (m_RectTransform != null && m_RectTransform.drivenByObject == this) {
                m_Tracker.Clear();
            }
        }

        private void ApplyOutset() {
            if (!isActiveAndEnabled || m_RectTransform == null) {
                return;
            }

            if (m_RectTransform.drivenByObject != null && m_RectTransform.drivenByObject != this) {
                return;
            }

            if (Screen.width == 0 || Screen.height == 0) {
                return;
            }

            CaptureBaselineIfNeeded();

            if (m_RectTransform.drivenByObject == null) {
                ClaimRectTransformDrivenOwnership();
            }

            UpdatePreviousDataCache();
            m_RefreshRequired = false;

            int rotationsFromReferenceToCurrent = GetRotationsFromReferenceToCurrent();

            SafeArea.RotateInsets(
                m_Outset,
                rotationsFromReferenceToCurrent,
                out var screenTopOutset,
                out var screenRightOutset,
                out var screenBottomOutset,
                out var screenLeftOutset
            );

            GetInsetsToScreenEdge(
                GetBleedReferenceRect(),
                Screen.width,
                Screen.height,
                out var screenLeftBleed,
                out var screenRightBleed,
                out var screenTopBleed,
                out var screenBottomBleed
            );

            var bleedEdges = SafeArea.RotateFlag(m_BleedEdges, rotationsFromReferenceToCurrent);

            if (SafeArea.HasFlag(bleedEdges, SafeArea.SafeAreaMode.Top)) {
                screenTopOutset += screenTopBleed;
            }

            if (SafeArea.HasFlag(bleedEdges, SafeArea.SafeAreaMode.Right)) {
                screenRightOutset += screenRightBleed;
            }

            if (SafeArea.HasFlag(bleedEdges, SafeArea.SafeAreaMode.Bottom)) {
                screenBottomOutset += screenBottomBleed;
            }

            if (SafeArea.HasFlag(bleedEdges, SafeArea.SafeAreaMode.Left)) {
                screenLeftOutset += screenLeftBleed;
            }

            float localScale = GetLocalPixelsPerScreenPixel();

            m_RectTransform.anchorMin = m_BaselineAnchorMin;
            m_RectTransform.anchorMax = m_BaselineAnchorMax;

            ApplyOutsetToOffsets(
                m_BaselineOffsetMin,
                m_BaselineOffsetMax,
                screenLeftOutset,
                screenRightOutset,
                screenTopOutset,
                screenBottomOutset,
                localScale,
                m_Mode,
                out var offsetMin,
                out var offsetMax
            );

            m_RectTransform.offsetMin = offsetMin;
            m_RectTransform.offsetMax = offsetMax;
        }

        internal static void ApplyOutsetToOffsets(
            Vector2 baselineOffsetMin,
            Vector2 baselineOffsetMax,
            float screenLeftOutset,
            float screenRightOutset,
            float screenTopOutset,
            float screenBottomOutset,
            float localScale,
            OutsetMode applyMode,
            out Vector2 offsetMin,
            out Vector2 offsetMax
        ) {
            if (applyMode == OutsetMode.Translate) {
                var shift = new Vector2(
                                screenRightOutset - screenLeftOutset,
                                screenTopOutset - screenBottomOutset
                            )
                            * localScale;

                offsetMin = baselineOffsetMin + shift;
                offsetMax = baselineOffsetMax + shift;
                return;
            }

            offsetMin = baselineOffsetMin
                        - new Vector2(
                            screenLeftOutset * localScale,
                            screenBottomOutset * localScale
                        );

            offsetMax = baselineOffsetMax
                        + new Vector2(
                            screenRightOutset * localScale,
                            screenTopOutset * localScale
                        );
        }

        /// <summary>
        /// <paramref name="referenceRect"/> 的各边缘到物理屏幕边缘的距离，单位为屏幕像素。
        /// </summary>
        internal static void GetInsetsToScreenEdge(
            Rect referenceRect,
            int screenWidth,
            int screenHeight,
            out float screenLeft,
            out float screenRight,
            out float screenTop,
            out float screenBottom
        ) {
            screenLeft = referenceRect.xMin;
            screenBottom = referenceRect.yMin;
            screenRight = screenWidth - referenceRect.xMax;
            screenTop = screenHeight - referenceRect.yMax;
        }

        private float GetLocalPixelsPerScreenPixel() {
            var canvas = m_RectTransform.GetComponentInParent<Canvas>();

            if (canvas == null) {
                return 1f;
            }

            return 1f / canvas.scaleFactor;
        }

        private int RotationsFromCurrentToReference => ((int)CurrentOrientation - (int)m_ReferenceOrientation + 4) % 4;

        private int GetRotationsFromReferenceToCurrent() => -RotationsFromCurrentToReference;

        private SafeArea.ScreenOrientation CurrentOrientation {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => SafeArea.ToLocalScreenOrientation(Screen.orientation);
        }

        private void UpdatePreviousDataCache() {
            m_PreviousLocalScale = GetLocalPixelsPerScreenPixel();
            m_PreviousSafeArea = Screen.safeArea;
            m_PreviousBleedReferenceRect = GetBleedReferenceRect();
            m_PreviousResolution = new Vector2Int(Screen.width, Screen.height);
            m_PreviousOrientation = Screen.orientation;
            m_PreviousReferenceOrientation = m_ReferenceOrientation;
            m_PreviousOutset = m_Outset;
            m_PreviousBleedEdges = m_BleedEdges;
            m_PreviousApplyMode = m_Mode;
        }

#if UNITY_EDITOR
        /// <summary>在编辑器中修改检视面板的值时调用。</summary>
        protected override void OnValidate() {
            base.OnValidate();

            // 延迟到 Update，在反序列化和 Undo/Redo 完成后再根据参数重算布局。
            m_RefreshRequired = true;
        }
#endif
    }
}