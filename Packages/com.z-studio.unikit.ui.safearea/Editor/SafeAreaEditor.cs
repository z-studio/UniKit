using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace ZStudio.UniKit.UI.Editor {
    [CustomEditor(typeof(SafeArea))]
    [CanEditMultipleObjects]
    internal class SafeAreaEditor : UnityEditor.Editor {
        private SerializedProperty m_ReferenceOrientation;
        private SerializedProperty m_Edges;
        private SerializedProperty m_Alignment;
        private SerializedProperty m_Padding;
        private SerializedProperty m_OnSafeAreaChanged;

        private HelpBox m_SelfLayoutControllerHelpBox;
        private HelpBox m_ParentLayoutControllerHelpBox;
        private HelpBox m_RootCanvasHelpBox;
        private HelpBox m_MultiSelectWarningHelpBox;
        private StringBuilder m_Builder;

        private const string k_SelfLayoutWarningText =
            "Safe Area 与同一对象上的布局控制器存在冲突。请移除布局控制器，或将 Safe Area 移到其他游戏对象上。";

        private const string k_ParentLayoutWarningText =
            "Safe Area 与父对象上的布局控制器存在冲突。请移除父对象上的布局控制器，或将 Safe Area 移到其他游戏对象上。";

        private const string k_RootCanvasWarningText =
            "Safe Area 当前挂载在根画布上。请将 Safe Area 挂载到根画布的子对象上。";

        private const string k_MultiLayoutWarningText = "一个或多个选中对象存在组件冲突。请分别选中各对象以查看详细信息。";

        private SafeArea Component => (SafeArea)target;

        private void OnEnable() {
            m_ReferenceOrientation = serializedObject.FindProperty("m_ReferenceOrientation");
            m_Edges = serializedObject.FindProperty("m_Edges");
            m_Alignment = serializedObject.FindProperty("m_Alignment");
            m_Padding = serializedObject.FindProperty("m_Padding");
            m_OnSafeAreaChanged = serializedObject.FindProperty("m_OnSafeAreaChanged");
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI() {
            var root = new VisualElement();

            // 提示框
            m_SelfLayoutControllerHelpBox = new HelpBox(
                string.Empty,
                HelpBoxMessageType.Warning
            );

            m_ParentLayoutControllerHelpBox = new HelpBox(
                string.Empty,
                HelpBoxMessageType.Warning
            );

            m_RootCanvasHelpBox = new HelpBox(
                k_RootCanvasWarningText,
                HelpBoxMessageType.Warning
            );

            m_MultiSelectWarningHelpBox = new HelpBox(
                k_MultiLayoutWarningText,
                HelpBoxMessageType.Warning
            );

            root.Add(m_ParentLayoutControllerHelpBox);
            root.Add(m_SelfLayoutControllerHelpBox);
            root.Add(m_RootCanvasHelpBox);
            root.Add(m_MultiSelectWarningHelpBox);
            UpdateHelpBoxes();

            root.Add(new PropertyField(m_ReferenceOrientation));
            root.Add(new PropertyField(m_Edges));
            root.Add(new PropertyField(m_Alignment));
            root.Add(new PropertyField(m_Padding));
            root.Add(new PropertyField(m_OnSafeAreaChanged));

            root.schedule.Execute(UpdateHelpBoxes).Every(200);

            return root;
        }

        private void UpdateHelpBoxes() {
            var comp = Component;

            if (!comp) {
                return;
            }

            var showSelfWarning = false;
            var showParentWarning = false;
            var showRootCanvasWarning = false;
            var multiEdit = serializedObject.isEditingMultipleObjects;

            var hasErrors = false;

            if (multiEdit) {
                foreach (var obj in targets) {
                    var tgt = (SafeArea)obj;
                    var rectTr = tgt.transform as RectTransform;

                    if (rectTr == null) {
                        continue;
                    }

                    if (rectTr.drivenByObject == tgt) {
                        continue;
                    }

                    if (rectTr.drivenByObject != tgt && !tgt.enabled) {
                        continue;
                    }

                    hasErrors = true;
                    break;
                }
            } else if (target is SafeArea safeArea && safeArea.enabled) {
                showSelfWarning = ShowSelfWarning(comp);
                showParentWarning = ShowParentWarning(comp);
                showRootCanvasWarning = ShowRootCanvasWarning(comp);
            }

            m_SelfLayoutControllerHelpBox.style.display = showSelfWarning
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            m_ParentLayoutControllerHelpBox.style.display = showParentWarning
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            m_RootCanvasHelpBox.style.display = showRootCanvasWarning
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            m_MultiSelectWarningHelpBox.style.display = hasErrors
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private bool ShowParentWarning(SafeArea comp) {
            if (comp.transform.parent == null) {
                return false;
            }

            var showWarning = false;
            var layouts = ListPool<ILayoutController>.Get();
            comp.transform.parent.GetComponents(layouts);

            if (layouts.Count > 0) {
                m_Builder ??= new StringBuilder();
                m_Builder.Append(k_ParentLayoutWarningText);
                m_Builder.Append("\n\nConflicting Components:");

                foreach (var layout in layouts) {
                    if (layout is not ILayoutSelfController && layout is Behaviour behaviour && behaviour.enabled) {
                        showWarning = true;
                        m_Builder.Append($"\n{layout.GetType().Name}");
                    }
                }

                if (showWarning) {
                    m_ParentLayoutControllerHelpBox.text = m_Builder.ToString();
                }

                m_Builder.Clear();
            }

            ListPool<ILayoutController>.Release(layouts);

            return showWarning;
        }

        private static bool ShowRootCanvasWarning(SafeArea comp) {
            return comp.TryGetComponent<Canvas>(out var canvas) && canvas.isRootCanvas;
        }

        private bool ShowSelfWarning(SafeArea comp) {
            var showWarning = false;
            var layouts = ListPool<ILayoutSelfController>.Get();
            comp.GetComponents(layouts);

            if (layouts.Count > 0) {
                m_Builder ??= new StringBuilder();
                m_Builder.Append(k_SelfLayoutWarningText);
                m_Builder.Append("\n\nConflicting Components:");

                foreach (var layout in layouts) {
                    if (layout is Behaviour behaviour && behaviour.enabled) {
                        showWarning = true;
                        m_Builder.Append($"\n{layout.GetType().Name}");
                    }
                }

                if (showWarning) {
                    m_SelfLayoutControllerHelpBox.text = m_Builder.ToString();
                }

                m_Builder.Clear();
            }

            ListPool<ILayoutSelfController>.Release(layouts);

            return showWarning;
        }
    }
}
