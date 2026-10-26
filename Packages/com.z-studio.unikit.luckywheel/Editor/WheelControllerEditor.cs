using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel.Editor {
    [CustomEditor(typeof(WheelController))]
    public class WheelControllerEditor : UnityEditor.Editor {
        // 分组只属于编辑器展示，展开状态保存在本机，不写入场景或 Prefab。
        private const string k_PreferencePrefix = "ZStudio.UniKit.LuckyWheel.Inspector.";

        private static readonly InspectorGroup[] s_Groups = {
            new("References", "对象引用 / References", true, "m_Reel", "m_PointerController"),
            new("SectorLayout", "扇区布局 / Sector Layout", true, "m_SectorsCount", "m_ZeroAngle", "m_SectorDirection"),
            new("Motion", "主旋转 / Motion", false, "m_SpinDirection", "m_MainRotationTime", "m_MinimumTurns",
                "m_SpinStartDelay", "m_WaitingSpeed", "m_AccelerationTime", "m_LandingJitter", "m_UseUnscaledTime"),
            new("SpinIn", "起转预摆 / Spin In", false, "m_InRotationTime", "m_InRotationAngle", "m_InRotationCurve"),
            new("SpinOut", "停转回摆 / Spin Out", false, "m_OutRotationTime", "m_OutRotationAngle", "m_OutRotationCurve"),
            new("Audio", "音效（可选）/ Audio", false, "m_SpinSound", "m_HitTargetSound"), new("Events", "事件 / Events", false,
                "m_StartedEvent", "m_ResultEvent", "m_CompleteEvent",
                "m_CancelledEvent")
        };

        private sealed class InspectorGroup {
            public readonly string key;
            public readonly string title;
            public readonly bool defaultExpanded;
            public readonly string[] propertyNames;

            public InspectorGroup(string key, string title, bool defaultExpanded, params string[] propertyNames) {
                this.key = key;
                this.title = title;
                this.defaultExpanded = defaultExpanded;
                this.propertyNames = propertyNames;
            }
        }

        private int m_TestIndex;
        private float m_TestOffset;
        private bool m_RandomTarget;

        public override void OnInspectorGUI() {
            var wheel = (WheelController)target;
            serializedObject.Update();

            foreach (var group in s_Groups) {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    if (!DrawFoldout(group.key, group.title, group.defaultExpanded)) {
                        continue;
                    }

                    using (new EditorGUI.IndentLevelScope()) {
                        // 只锁定配置项，不锁定折叠标题，运行时仍可查看各组。
                        using (new EditorGUI.DisabledScope(Application.isPlaying && wheel.IsSpinning)) {
                            foreach (var propertyName in group.propertyNames) {
                                EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), true);
                            }
                        }

                        if (group.key == "SectorLayout") {
                            EditorGUILayout.HelpBox(
                                "等分扇区，索引从 0 开始。Zero Angle 是 0 号中心对准指针时的 Reel 本地 Z 角。Sector Direction 是美术编号方向，Spin Direction 是动画方向。",
                                MessageType.Info);
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (!wheel.Reel) {
                EditorGUILayout.HelpBox("请绑定 Reel；Pointer 和音效可不配置。", MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                if (DrawFoldout("Debug", "对齐与调试 / Alignment & Debug", false)) {
                    using (new EditorGUI.IndentLevelScope()) {
                        DrawDebug(wheel);
                    }
                }
            }

            if (Application.isPlaying && wheel.IsSpinning) {
                Repaint();
            }
        }

        private static bool DrawFoldout(string key, string title, bool defaultExpanded) {
            var preferenceKey = k_PreferencePrefix + key;
            var expanded = EditorPrefs.GetBool(preferenceKey, defaultExpanded);
            var next = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);

            if (next != expanded) {
                EditorPrefs.SetBool(preferenceKey, next);
            }

            return next;
        }

        private void DrawDebug(WheelController wheel) {
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
        }

        private int TestIndex(WheelController wheel) =>
            m_RandomTarget ? Random.Range(0, wheel.SectorsCount) : m_TestIndex;
    }
}