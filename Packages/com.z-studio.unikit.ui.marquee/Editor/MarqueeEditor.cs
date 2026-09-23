using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.UI.Editor {
    [CustomEditor(typeof(Marquee))]
    [CanEditMultipleObjects]
    public class MarqueeEditor : UnityEditor.Editor {
        private SerializedProperty m_Viewport;
        private SerializedProperty m_ContentTemplate;
        private SerializedProperty m_Items;

        private SerializedProperty m_ScrollMode;
        private SerializedProperty m_Direction;
        private SerializedProperty m_PlayMode;

        private SerializedProperty m_EdgeMargin;
        private SerializedProperty m_CenterWhenFit;
        private SerializedProperty m_DisplayDurationWhenFit;
        private SerializedProperty m_DisplayDurationBeforeScroll;
        private SerializedProperty m_ScrollSpeed;
        private SerializedProperty m_Spacing;
        private SerializedProperty m_SegmentSpacing;
        private SerializedProperty m_Ease;
        private SerializedProperty m_CustomCurve;

        private SerializedProperty m_PlayOnStart;
        private SerializedProperty m_IgnoreTimeScale;

        private void OnEnable() {
            m_Viewport = serializedObject.FindProperty("Viewport");
            m_ContentTemplate = serializedObject.FindProperty("ContentTemplate");
            m_Items = serializedObject.FindProperty("Items");

            m_ScrollMode = serializedObject.FindProperty("ScrollMode");
            m_Direction = serializedObject.FindProperty("Direction");
            m_PlayMode = serializedObject.FindProperty("PlayMode");

            m_EdgeMargin = serializedObject.FindProperty("EdgeMargin");
            m_CenterWhenFit = serializedObject.FindProperty("CenterWhenFit");
            m_DisplayDurationWhenFit = serializedObject.FindProperty("DisplayDurationWhenFit");
            m_DisplayDurationBeforeScroll = serializedObject.FindProperty("DisplayDurationBeforeScroll");
            m_ScrollSpeed = serializedObject.FindProperty("ScrollSpeed");
            m_Spacing = serializedObject.FindProperty("Spacing");
            m_SegmentSpacing = serializedObject.FindProperty("SegmentSpacing");
            m_Ease = serializedObject.FindProperty("Ease");
            m_CustomCurve = serializedObject.FindProperty("CustomCurve");

            m_PlayOnStart = serializedObject.FindProperty("PlayOnStart");
            m_IgnoreTimeScale = serializedObject.FindProperty("IgnoreTimeScale");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                DrawReferences();
            }
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                DrawMode();
            }
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                DrawLayoutTiming();
            }
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                DrawPlayback();
            }
            EditorGUILayout.Space();
            DrawItems();

            DrawValidation();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying) {
                DrawRuntimeControls();
            }
        }

        private void DrawReferences() {
            EditorGUILayout.LabelField("显示引用", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Viewport, new GUIContent("可视区域", m_Viewport.tooltip));
            EditorGUILayout.PropertyField(m_ContentTemplate, new GUIContent("内容模板", m_ContentTemplate.tooltip));
        }

        private void DrawMode() {
            EditorGUILayout.LabelField("滚动模式", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_ScrollMode, new GUIContent("滚动方式", m_ScrollMode.tooltip));
            EditorGUILayout.PropertyField(m_Direction, new GUIContent("滚动方向", m_Direction.tooltip));

            bool sequential = m_ScrollMode.enumValueIndex == (int)MarqueeScrollMode.Sequential;
            using (new EditorGUI.DisabledScope(!sequential)) {
                EditorGUILayout.PropertyField(m_PlayMode, new GUIContent("循环方式", m_PlayMode.tooltip));
            }
        }

        private void DrawLayoutTiming() {
            EditorGUILayout.LabelField("布局与节奏", EditorStyles.boldLabel);

            bool continuous = m_ScrollMode.enumValueIndex == (int)MarqueeScrollMode.Continuous;

            EditorGUILayout.PropertyField(m_ScrollSpeed, new GUIContent("滚动速度", m_ScrollSpeed.tooltip));
            EditorGUILayout.PropertyField(m_SegmentSpacing, new GUIContent("片段间距", m_SegmentSpacing.tooltip));

            if (continuous) {
                EditorGUILayout.PropertyField(m_Spacing, new GUIContent("条目间距", m_Spacing.tooltip));
            } else {
                EditorGUILayout.PropertyField(m_EdgeMargin, new GUIContent("边缘留白", m_EdgeMargin.tooltip));
                EditorGUILayout.PropertyField(m_DisplayDurationBeforeScroll, new GUIContent("滚动前停留", m_DisplayDurationBeforeScroll.tooltip));
                EditorGUILayout.PropertyField(m_Ease, new GUIContent("缓动", m_Ease.tooltip));

                if (m_Ease.enumValueIndex == (int)MarqueeEase.Custom) {
                    EditorGUILayout.PropertyField(m_CustomCurve, new GUIContent("自定义曲线", m_CustomCurve.tooltip));
                }

                EditorGUILayout.PropertyField(m_CenterWhenFit, new GUIContent("短内容居中", m_CenterWhenFit.tooltip));

                using (new EditorGUI.DisabledScope(!m_CenterWhenFit.boolValue)) {
                    EditorGUILayout.PropertyField(m_DisplayDurationWhenFit, new GUIContent("短内容停留", m_DisplayDurationWhenFit.tooltip));
                }
            }
        }

        private void DrawPlayback() {
            EditorGUILayout.LabelField("播放设置", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PlayOnStart, new GUIContent("启动时播放", m_PlayOnStart.tooltip));
            EditorGUILayout.PropertyField(m_IgnoreTimeScale, new GUIContent("忽略时间缩放", m_IgnoreTimeScale.tooltip));
        }

        private void DrawItems() {
            EditorGUILayout.LabelField("播放内容", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Items, new GUIContent("条目列表"), true);
        }

        private void DrawValidation() {
            if (m_Viewport.objectReferenceValue == null) {
                EditorGUILayout.HelpBox("未指定 viewport：运行时将默认使用自身 RectTransform。建议为其挂载 RectMask2D 以裁剪溢出内容。", MessageType.Info);
            }

            if (m_ContentTemplate.objectReferenceValue == null) {
                EditorGUILayout.HelpBox("未指定 contentTemplate：运行时将尝试使用 viewport 下的第一个子节点。该节点需包含 Image 或 TextMeshProUGUI。", MessageType.Warning);
            }

            if (m_Items.arraySize == 0) {
                EditorGUILayout.HelpBox("items 为空：请在此处填充条目，或在运行时通过 SetItems / AddItem 提供。", MessageType.Info);
            }
        }

        private void DrawRuntimeControls() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("运行控制", EditorStyles.boldLabel);

            var marquee = (Marquee)target;
            EditorGUILayout.LabelField($"IsPlaying: {marquee.IsPlaying}    IsPaused: {marquee.IsPaused}    CurrentIndex: {marquee.CurrentIndex}");

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Play")) {
                    marquee.Play();
                }

                if (GUILayout.Button(marquee.IsPaused ? "Unpause" : "Pause")) {
                    if (marquee.IsPaused) {
                        marquee.Unpause();
                    } else {
                        marquee.Pause();
                    }
                }

                if (GUILayout.Button("Stop")) {
                    marquee.Stop();
                }
            }

            Repaint();
        }
    }
}
