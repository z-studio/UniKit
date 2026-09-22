using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    /// <summary>绘制适配配置、当前模式说明及相机兼容性提示，实际适配由运行时组件负责。</summary>
    [CustomEditor(typeof(CameraScaler))]
    [CanEditMultipleObjects]
    public sealed class CameraScalerEditor : UnityEditor.Editor {
        private SerializedProperty m_ReferenceResolution;
        private SerializedProperty m_ScaleMode;
        private SerializedProperty m_MatchWidthOrHeight;
        private SerializedProperty m_ReferenceOrthographicSize;
        private SerializedProperty m_ReferenceFieldOfView;
        private SerializedProperty m_CameraZoom;
        private SerializedProperty m_ApplyTiming;
        private GUIStyle m_RightAlignedLabel;

        // 通过序列化属性编辑，保留 Unity 的多对象编辑、Undo 和 Prefab 覆盖支持。
        private void OnEnable() {
            m_ReferenceResolution = serializedObject.FindProperty(nameof(m_ReferenceResolution));
            m_ScaleMode = serializedObject.FindProperty(nameof(m_ScaleMode));
            m_MatchWidthOrHeight = serializedObject.FindProperty(nameof(m_MatchWidthOrHeight));
            m_ReferenceOrthographicSize = serializedObject.FindProperty(nameof(m_ReferenceOrthographicSize));
            m_ReferenceFieldOfView = serializedObject.FindProperty(nameof(m_ReferenceFieldOfView));
            m_CameraZoom = serializedObject.FindProperty(nameof(m_CameraZoom));
            m_ApplyTiming = serializedObject.FindProperty(nameof(m_ApplyTiming));
        }

        public override void OnInspectorGUI() {
            // EditorStyles 依赖 GUI 初始化，不能在 OnEnable 中访问。
            m_RightAlignedLabel ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };

            serializedObject.Update();
            EditorGUILayout.PropertyField(m_ReferenceResolution);

            if (!m_ReferenceResolution.hasMultipleDifferentValues) {
                Vector2 resolution = m_ReferenceResolution.vector2Value;

                if (!CameraScalerMath.IsFinitePositive(resolution.x) ||
                    !CameraScalerMath.IsFinitePositive(resolution.y)) {
                    EditorGUILayout.HelpBox("参考分辨率的宽和高必须大于 0，非法值将被自动修正为 1。", MessageType.Error);
                }
            }

            EditorGUILayout.PropertyField(m_ScaleMode);

            // 多选且模式不一致时，不用第一个对象的模式决定所有对象的附加控件和说明。
            if (!m_ScaleMode.hasMultipleDifferentValues) {
                var scaleMode = (ScaleMode)m_ScaleMode.intValue;

                switch (scaleMode) {
                    case ScaleMode.ConstantHeight:
                        EditorGUILayout.HelpBox("保持相机的垂直可视范围；CameraZoom 仍然有效。", MessageType.Info);
                        break;
                    case ScaleMode.ConstantWidth:
                        EditorGUILayout.HelpBox("保持相机的水平可视范围；竖屏游戏常用此模式。", MessageType.Info);
                        break;

                    case ScaleMode.MatchWidthOrHeight: {
                        Rect r = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 12);
                        DualLabeledSlider(r, m_MatchWidthOrHeight, "Match", "Width", "Height");
                        EditorGUILayout.HelpBox("在固定宽度和固定高度之间按权重做对数插值，语义与 CanvasScaler 一致。", MessageType.Info);
                        break;
                    }

                    case ScaleMode.Expand:
                        EditorGUILayout.HelpBox("保证参考区域全部可见，设备多出的空间显示额外内容。", MessageType.Info);
                        break;
                    case ScaleMode.Shrink:
                        EditorGUILayout.HelpBox("不显示参考区域之外的内容，必要时裁减参考区域。", MessageType.Info);
                        break;
                }
            }

            EditorGUILayout.PropertyField(m_ReferenceOrthographicSize);
            EditorGUILayout.PropertyField(m_ReferenceFieldOfView);
            EditorGUILayout.PropertyField(m_CameraZoom);
            EditorGUILayout.PropertyField(m_ApplyTiming);

            DrawCameraWarnings();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>仅对单个目标给出相机状态提示，避免多选时误用某一个相机的设置。</summary>
        private void DrawCameraWarnings() {
            if (targets.Length != 1) {
                return;
            }

            var scaler = (CameraScaler)target;
            var camera = scaler.GetComponent<Camera>();

            if (camera == null) {
                return;
            }

            if (camera.usePhysicalProperties) {
                EditorGUILayout.HelpBox(
                    "当前 Camera 启用了 Physical Camera。写入 FOV 可能与焦距/传感器参数互相覆盖，建议关闭 Physical Camera，或确保没有其他系统同时写入这些属性。",
                    MessageType.Warning);
            }
        }

        /// <summary>绘制带宽/高端点说明的匹配滑块，使用序列化属性保留混合值与撤销行为。</summary>
        private void DualLabeledSlider(Rect position, SerializedProperty property, string mainLabel, string labelLeft,
            string labelRight) {
            position.height = EditorGUIUtility.singleLineHeight;
            Rect pos = position;

            // 端点文字放在滑轨下方；左右避开属性名和数值输入框，使说明与滑轨两端对齐。
            position.y += 12;
            position.xMin += EditorGUIUtility.labelWidth;
            position.xMax -= EditorGUIUtility.fieldWidth;

            GUI.Label(position, labelLeft, EditorStyles.label);
            GUI.Label(position, labelRight, m_RightAlignedLabel);

            EditorGUI.Slider(pos, property, 0, 1, mainLabel);
        }
    }
}