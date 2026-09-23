using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.UI.Editor {
    [CustomEditor(typeof(UIMarquee))]
    [CanEditMultipleObjects]
    public class UIMarqueeEditor : UnityEditor.Editor {
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

            DrawReferences();
            EditorGUILayout.Space();
            DrawMode();
            EditorGUILayout.Space();
            DrawLayoutTiming();
            EditorGUILayout.Space();
            DrawPlayback();
            EditorGUILayout.Space();
            DrawItems();

            DrawValidation();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying) {
                DrawRuntimeControls();
            }
        }

        private void DrawReferences() {
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Viewport);
            EditorGUILayout.PropertyField(m_ContentTemplate);
        }

        private void DrawMode() {
            EditorGUILayout.LabelField("Mode", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_ScrollMode);
            EditorGUILayout.PropertyField(m_Direction);

            bool sequential = m_ScrollMode.enumValueIndex == (int)MarqueeScrollMode.Sequential;
            using (new EditorGUI.DisabledScope(!sequential)) {
                EditorGUILayout.PropertyField(m_PlayMode);
            }
        }

        private void DrawLayoutTiming() {
            EditorGUILayout.LabelField("Layout & Timing", EditorStyles.boldLabel);

            bool continuous = m_ScrollMode.enumValueIndex == (int)MarqueeScrollMode.Continuous;

            EditorGUILayout.PropertyField(m_ScrollSpeed);
            EditorGUILayout.PropertyField(m_SegmentSpacing);

            if (continuous) {
                EditorGUILayout.PropertyField(m_Spacing);
            } else {
                EditorGUILayout.PropertyField(m_EdgeMargin);
                EditorGUILayout.PropertyField(m_DisplayDurationBeforeScroll);
                EditorGUILayout.PropertyField(m_Ease);

                if (m_Ease.enumValueIndex == (int)MarqueeEase.Custom) {
                    EditorGUILayout.PropertyField(m_CustomCurve);
                }

                EditorGUILayout.PropertyField(m_CenterWhenFit);

                using (new EditorGUI.DisabledScope(!m_CenterWhenFit.boolValue)) {
                    EditorGUILayout.PropertyField(m_DisplayDurationWhenFit);
                }
            }
        }

        private void DrawPlayback() {
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PlayOnStart);
            EditorGUILayout.PropertyField(m_IgnoreTimeScale);
        }

        private void DrawItems() {
            EditorGUILayout.PropertyField(m_Items, true);
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
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

            var marquee = (UIMarquee)target;
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
