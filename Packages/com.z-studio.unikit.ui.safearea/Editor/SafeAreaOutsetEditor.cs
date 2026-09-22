using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace ZStudio.UniKit.UI.Editor {
    [CustomEditor(typeof(SafeAreaOutset))]
    [CanEditMultipleObjects]
    internal class SafeAreaOutsetEditor : UnityEditor.Editor {
        private SerializedProperty m_ReferenceOrientation;
        private SerializedProperty m_Mode;
        private SerializedProperty m_Outset;
        private SerializedProperty m_BleedEdges;

        private HelpBox m_NoParentSafeAreaHelpBox;
        private HelpBox m_ApplyModeHelpBox;
        private HelpBox m_MultiSelectHelpBox;

        private const string k_NoParentSafeAreaText =
            "未找到挂载 SafeArea 的父级对象。边缘延伸将使用 Screen.safeArea 作为参考区域。固定外扩量始终有效。";

        private const string k_ApplyModeTranslateText =
            "平移模式（Translate）保持元素尺寸不变，并将其移向选定的延伸边缘。"
            + "固定尺寸的角标和图标适合使用此模式。建议选择上边缘（Top）或右边缘（Right），而非全部边缘（Everything）。";

        private const string k_ApplyModeExpandText =
            "扩展模式（Expand）独立向外扩展各边缘，并改变矩形尺寸。"
            + "这是默认模式，适用于拉伸条等大多数边缘延伸或外扩场景。";

        private const string k_MultiSelectText = "支持同时配置多个对象的外扩参数。";

        private SafeAreaOutset Component => (SafeAreaOutset)target;

        private void OnEnable() {
            m_ReferenceOrientation = serializedObject.FindProperty("m_ReferenceOrientation");
            m_Mode = serializedObject.FindProperty("m_Mode");
            m_Outset = serializedObject.FindProperty("m_Outset");
            m_BleedEdges = serializedObject.FindProperty("m_BleedEdges");
        }

        public override VisualElement CreateInspectorGUI() {
            var root = new VisualElement();

            m_NoParentSafeAreaHelpBox = new HelpBox(k_NoParentSafeAreaText, HelpBoxMessageType.Info);
            m_ApplyModeHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            m_MultiSelectHelpBox = new HelpBox(k_MultiSelectText, HelpBoxMessageType.Info);

            root.Add(m_NoParentSafeAreaHelpBox);
            root.Add(m_ApplyModeHelpBox);
            root.Add(m_MultiSelectHelpBox);
            UpdateHelpBoxes();

            root.Add(new PropertyField(m_ReferenceOrientation));
            root.Add(new PropertyField(m_Mode));
            root.Add(new PropertyField(m_Outset));
            root.Add(new PropertyField(m_BleedEdges));

            root.schedule.Execute(UpdateHelpBoxes).Every(200);

            return root;
        }

        private void UpdateHelpBoxes() {
            var showNoParent = !serializedObject.isEditingMultipleObjects
                               && Component
                               && Component.GetComponentInParent<SafeArea>() == null;

            m_NoParentSafeAreaHelpBox.style.display = showNoParent
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            m_MultiSelectHelpBox.style.display = serializedObject.isEditingMultipleObjects
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (serializedObject.isEditingMultipleObjects || !Component) {
                m_ApplyModeHelpBox.style.display = DisplayStyle.None;
                return;
            }

            m_ApplyModeHelpBox.text = Component.Mode == SafeAreaOutset.OutsetMode.Translate
                ? k_ApplyModeTranslateText
                : k_ApplyModeExpandText;
            m_ApplyModeHelpBox.style.display = DisplayStyle.Flex;
        }
    }
}
