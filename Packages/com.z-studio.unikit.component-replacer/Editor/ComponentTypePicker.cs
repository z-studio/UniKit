using System;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    /// <summary>复用 Unity 的可搜索下拉框；拖入脚本、组件或对象也能确定类型。</summary>
    internal sealed class ComponentTypePicker : AdvancedDropdown {
        private sealed class TypeItem : AdvancedDropdownItem {
            internal Type ComponentType { get; }

            internal TypeItem(Type type) : base($"{type.Name}  ({type.Namespace})") {
                ComponentType = type;
            }
        }

        private readonly Action<Type> m_Select;
        private readonly Type[] m_Types;

        internal ComponentTypePicker(Action<Type> select) : base(new AdvancedDropdownState()) {
            m_Select = select;
            m_Types = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(type => ComponentReplacement.ValidateType(type) == null)
                .OrderBy(type => type.FullName).ToArray();
            minimumSize = new Vector2(420, 360);
        }

        protected override AdvancedDropdownItem BuildRoot() {
            var root = new AdvancedDropdownItem("选择组件（可搜索）");

            foreach (var type in m_Types) {
                root.AddChild(new TypeItem(type));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item) {
            // AddChild 会重写 ID；项直接携带类型，列表和搜索结果都不把 ID 当数组下标。
            if (item is TypeItem typeItem) {
                m_Select(typeItem.ComponentType);
            }
        }

        internal static void AcceptDrop(Rect rect, Action<Type> select) {
            var current = Event.current;

            if (!rect.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)) {
                return;
            }

            var objects = DragAndDrop.objectReferences;

            if (objects.Length != 1) {
                return;
            }

            var value = objects[0];
            var type = value is MonoScript script
                ? script.GetClass()
                : value is Component component
                    ? component.GetType()
                    : null;

            if (type != null && ComponentReplacement.ValidateType(type) == null) {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (current.type == EventType.DragPerform) {
                    DragAndDrop.AcceptDrag();
                    select(type);
                }

                current.Use();
                return;
            }

            if (value is not GameObject gameObject) {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (current.type == EventType.DragPerform) {
                DragAndDrop.AcceptDrag();
                var menu = new GenericMenu();

                foreach (var candidate in gameObject.GetComponents<Component>().Where(c => c != null)
                             .Select(c => c.GetType()).Distinct()
                             .Where(t => ComponentReplacement.ValidateType(t) == null)) {
                    var captured = candidate;
                    menu.AddItem(new GUIContent(candidate.FullName.Replace('.', '/')), false, () => select(captured));
                }

                menu.ShowAsContext();
            }

            current.Use();
        }
    }
}
