using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel.Editor {
    /// <summary>指针只配置拨动表现。固定方向仅在手动模式显示，试拨不产生中奖通知。</summary>
    [CustomEditor(typeof(PointerController))]
    public class PointerControllerEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.HelpBox("将组件挂在固定指针上，再拖到转盘组件的“指针”栏。指针应放在旋转圆盘外，轴心放在摆动支点；进入运行模式时的姿态就是回位位置。",
                MessageType.Info);

            DrawField("m_PointerHit", "拨动音效（可选）");
            DrawField("m_FlickAngle", "拨动角度（度）");
            DrawField("m_FlickTime", "拨开用时（秒）");
            DrawField("m_ReturnTime", "回位用时（秒）");

            var automatic = serializedObject.FindProperty("m_FollowWheelDirection");
            EditorGUILayout.PropertyField(automatic, new GUIContent("自动选择拨动方向", automatic.tooltip));

            if (!automatic.boolValue) {
                var clockwise = serializedObject.FindProperty("m_IsClockwise");
                clockwise.boolValue = EditorGUILayout.Popup(new GUIContent("固定拨动方向", "从画面正面看，指针绕自身支点摆动的方向。"),
                    clockwise.boolValue ? 0 : 1, new[] { "顺时针", "逆时针" }) == 0;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("试拨（运行模式）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("进入运行模式后，可模拟圆盘经过指针。测试时请先停下圆盘，以免实际转动重复触发拨动。", MessageType.None);
            var pointer = (PointerController)target;

            using (new EditorGUI.DisabledScope(!Application.isPlaying || !pointer.isActiveAndEnabled)) {
                if (GUILayout.Button("模拟圆盘顺时针经过")) {
                    pointer.Flick(true, true);
                }

                if (GUILayout.Button("模拟圆盘逆时针经过")) {
                    pointer.Flick(false, true);
                }

                if (GUILayout.Button("停止试拨并回位")) {
                    pointer.CancelFlick();
                }
            }
        }

        private void DrawField(string name, string label) {
            var property = serializedObject.FindProperty(name);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }
    }
}