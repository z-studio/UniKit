using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel.Editor {
    [CustomEditor(typeof(WheelController))]
    public class WheelControllerEditor : UnityEditor.Editor {
        private int m_TestIndex;
        private float m_TestOffset;
        private bool m_RandomTarget;

        public override void OnInspectorGUI() {
            var wheel = (WheelController)target;
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(Application.isPlaying && wheel.IsSpinning)) {
                DrawPropertiesExcluding(serializedObject, "m_Script");
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "等分扇区，索引从 0 开始。Zero Angle 是 0 号中心对准指针时的 Reel 本地 Z 角。Sector Direction 是美术编号方向，Spin Direction 是动画方向。",
                MessageType.Info);

            if (!wheel.Reel) {
                EditorGUILayout.HelpBox("请绑定 Reel；Pointer 和音效可不配置。", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Alignment / Debug", EditorStyles.boldLabel);
            m_TestIndex =
                EditorGUILayout.IntSlider("Target Index", m_TestIndex, 0, Mathf.Max(0, wheel.SectorsCount - 1));
            m_TestOffset = EditorGUILayout.Slider("Landing Offset", m_TestOffset, -0.49f, 0.49f);
            EditorGUILayout.LabelField("Center Z", wheel.GetSectorAngle(m_TestIndex).ToString("F2") + "°");

            using (new EditorGUI.DisabledScope(wheel.IsSpinning)) {
                if (GUILayout.Button("Align Selected Sector (No Result Event)")) {
                    Undo.RecordObject(wheel.Reel, "Align wheel sector");
                    wheel.SnapToSector(m_TestIndex);
                    EditorUtility.SetDirty(wheel.Reel);
                }

                if (GUILayout.Button("Use Current Z As Sector 0")) {
                    serializedObject.FindProperty("m_ZeroAngle").floatValue = wheel.Reel.localEulerAngles.z;
                    serializedObject.ApplyModifiedProperties();
                }
            }

            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("上方按钮可在编辑模式校准美术；进入 Play Mode 后可测试指定结果、等待结果和取消。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("State", wheel.State.ToString());
            EditorGUILayout.LabelField("Target / Result", $"{wheel.TargetSectorIndex} / {wheel.WinSectorIndex}");
            m_RandomTarget = EditorGUILayout.Toggle("Random Target (Debug Only)", m_RandomTarget);

            using (new EditorGUI.DisabledScope(wheel.IsSpinning || !wheel.isActiveAndEnabled)) {
                if (GUILayout.Button("Spin To Target")) {
                    wheel.TryStartSpin(TestIndex(wheel), m_TestOffset);
                }

                if (GUILayout.Button("Begin Waiting For Result")) {
                    wheel.BeginSpin();
                }
            }

            using (new EditorGUI.DisabledScope(!wheel.CanSubmitResult)) {
                if (GUILayout.Button("Stop At Target + Offset")) {
                    wheel.StopAt(TestIndex(wheel), m_TestOffset);
                }
            }

            using (new EditorGUI.DisabledScope(!wheel.IsSpinning)) {
                if (GUILayout.Button("Cancel")) {
                    wheel.CancelSpin();
                }
            }

            if (wheel.IsSpinning) {
                Repaint();
            }
        }

        private int TestIndex(WheelController wheel) =>
            m_RandomTarget ? Random.Range(0, wheel.SectorsCount) : m_TestIndex;
    }
}