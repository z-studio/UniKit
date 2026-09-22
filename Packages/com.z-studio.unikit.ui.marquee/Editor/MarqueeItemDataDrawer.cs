using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ZStudio.UniKit.UI.Editor {
    /// <summary>
    /// <see cref="MarqueeItemData"/> 的自定义抽屉：用可拖拽排序的列表 + 「+」类型下拉
    /// 直观配置 segments（文本 / 图片 / Spine 等任意混排）。单段即纯文本/纯图片，
    /// 多段即混排，避免「不知道怎么配混排」。
    /// </summary>
    [CustomPropertyDrawer(typeof(MarqueeItemData))]
    public class MarqueeItemDataDrawer : PropertyDrawer {
        private const float k_Pad = 4f;
        private const float k_Field = 2f;   // 字段行间距
        private const float k_BadgeW = 48f; // 左侧类型名标签宽度

        // 所有可实例化的片段子类（含扩展程序集里的 SpineSegment，按需自动出现）
        // 用 [InitializeOnLoadMethod] 在每次 Domain Reload 后重置，兼容"关闭 Domain Reload"模式
        private static List<Type> s_SegmentTypes;

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() => s_SegmentTypes = null;

        // 每个属性路径一个 ReorderableList（保留展开/拖拽/选中状态）
        private readonly Dictionary<string, ReorderableList> m_Lists = new();

        private static List<Type> SegmentTypes {
            get {
                if (s_SegmentTypes == null) {
                    s_SegmentTypes = new List<Type>();

                    foreach (Type t in TypeCache.GetTypesDerivedFrom<MarqueeSegment>()) {
                        if (!t.IsAbstract && !t.IsGenericType && t.GetConstructor(Type.EmptyTypes) != null) {
                            s_SegmentTypes.Add(t);
                        }
                    }

                    s_SegmentTypes.Sort((a, b) => string.CompareOrdinal(FriendlyName(a.Name), FriendlyName(b.Name)));
                }

                return s_SegmentTypes;
            }
        }

        public override void OnGUI(Rect pos, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(pos, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float y = pos.y;

            if (property.serializedObject.isEditingMultipleObjects) {
                EditorGUI.HelpBox(new Rect(pos.x, y, pos.width, line * 2f),
                    "多选编辑下不支持配置 segments（SerializeReference 限制）。请单独选中一个对象编辑。", MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            var foldoutRect = new Rect(pos.x, y, pos.width, line);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, BuildSummary(property), true);
            y += line + k_Pad;

            if (property.isExpanded) {
                SerializedProperty idProp = property.FindPropertyRelative("ID");
                EditorGUI.PropertyField(new Rect(pos.x, y, pos.width, line), idProp);
                y += line + k_Pad;

                SerializedProperty segs = property.FindPropertyRelative("Segments");
                ReorderableList rl = GetList(property, segs);
                float h = rl.GetHeight();

                // 列表用全宽绘制，并把 indent 归零，避免在拖拽手柄列左侧再叠加缩进空白
                int prevIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                rl.DoList(new Rect(pos.x, y, pos.width, h));
                EditorGUI.indentLevel = prevIndent;
                y += h + k_Pad;

                SerializedProperty cyclesProp = property.FindPropertyRelative("Cycles");
                EditorGUI.PropertyField(new Rect(pos.x, y, pos.width, line), cyclesProp);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            float line = EditorGUIUtility.singleLineHeight;

            if (property.serializedObject.isEditingMultipleObjects) {
                return line * 2f;
            }

            float h = line + k_Pad; // foldout

            if (!property.isExpanded) {
                return h;
            }

            h += line + k_Pad; // id

            SerializedProperty segs = property.FindPropertyRelative("Segments");
            h += GetList(property, segs).GetHeight() + k_Pad; // segments

            h += line; // cycles

            return h;
        }

        // ---- ReorderableList ----

        private ReorderableList GetList(SerializedProperty property, SerializedProperty segs) {
            string key = property.serializedObject.targetObject.GetInstanceID() + "::" + property.propertyPath;

            if (m_Lists.TryGetValue(key, out ReorderableList rl)) {
                // 检查持有的 SerializedObject 是否仍有效（关闭 Domain Reload 时对象可能已被销毁）
                if (rl.serializedProperty?.serializedObject?.targetObject == null) {
                    m_Lists.Remove(key);
                } else {
                    rl.serializedProperty = segs;
                    return rl;
                }
            }

            rl = new ReorderableList(property.serializedObject, segs, true, true, true, true) {
                drawHeaderCallback = r =>
                    EditorGUI.LabelField(r, "Segments（按序水平排列：文本 / 图片 / Spine …）"),

                elementHeightCallback = i => ElementHeight(segs.GetArrayElementAtIndex(i)),

                drawElementCallback = (r, i, _, _) => DrawElement(r, segs.GetArrayElementAtIndex(i)),

                drawElementBackgroundCallback = (r, i, active, focused) => {
                    if (Event.current.type != EventType.Repaint) {
                        return;
                    }
                    
                    // 奇偶交替底色（幅度轻微，适配亮/暗两种皮肤）
                    Color bg = i % 2 == 0
                        ? new Color(0f, 0f, 0f, EditorGUIUtility.isProSkin ? 0.12f : 0.04f)
                        : new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.06f : 0.08f);
                    EditorGUI.DrawRect(r, bg);
                    
                    // 选中/激活高亮叠加
                    if (active) {
                        EditorGUI.DrawRect(r, new Color(0.24f, 0.49f, 0.91f, 0.25f));
                    }
                },

                onAddDropdownCallback = (_, _) => ShowAddMenu(segs),

                drawNoneElementCallback = r =>
                    EditorGUI.LabelField(r, "空：点「+」添加文本 / 图片 / Spine 片段"),
            };

            m_Lists[key] = rl;
            return rl;
        }

        // 单个片段：左侧固定宽度的类型名标签（不可点击）+ 字段从同行起平铺，节省垂直空间。
        // 要更换类型请删除后重新用「+」添加，避免中途切类型导致数据静默丢失。
        private static void DrawElement(Rect r, SerializedProperty el) {
            float line = EditorGUIUtility.singleLineHeight;
            float y = r.y + k_Pad;
            string label = ElementLabel(el);

            int baseDepth = el.depth;
            SerializedProperty it = el.Copy();
            bool enter = true;
            bool isFirst = true;

            while (it.NextVisible(enter)) {
                enter = false;

                if (it.depth <= baseDepth) {
                    break;
                }

                if (isFirst) {
                    isFirst = false;
                    // 第一个字段强制单行、直接绘制，彻底绕过 TextAreaDrawer 内部保留的 label 行空白。
                    // string → TextField；其它 → 无 label 的单行 PropertyField。
                    EditorGUI.LabelField(new Rect(r.x, y, k_BadgeW, line), label, EditorStyles.boldLabel);
                    var fieldRect = new Rect(r.x + k_BadgeW, y, r.width - k_BadgeW, line);
                   
                    if (it.propertyType == SerializedPropertyType.String) {
                        EditorGUI.BeginChangeCheck();
                        string v = EditorGUI.TextField(fieldRect, it.stringValue);
                        if (EditorGUI.EndChangeCheck()) it.stringValue = v;
                    } else {
                        EditorGUI.PropertyField(fieldRect, it, GUIContent.none);
                    }
                    
                    y += line + k_Field;
                } else {
                    float fh = EditorGUI.GetPropertyHeight(it, true);
                    EditorGUI.PropertyField(new Rect(r.x + k_BadgeW, y, r.width - k_BadgeW, fh), it, true);
                    y += fh + k_Field;
                }
            }

            if (isFirst) {
                EditorGUI.LabelField(new Rect(r.x, y, r.width, line), label, EditorStyles.boldLabel);
            }
        }

        private static float ElementHeight(SerializedProperty el) {
            float h = k_Pad;
            int baseDepth = el.depth;
            SerializedProperty it = el.Copy();
            bool enter = true;
            bool any = false;
            bool isFirst = true;

            while (it.NextVisible(enter)) {
                enter = false;

                if (it.depth <= baseDepth) {
                    break;
                }

                any = true;
                
                // 与 DrawElement 一致：第一个字段固定单行高度
                float fh = isFirst
                    ? EditorGUIUtility.singleLineHeight
                    : EditorGUI.GetPropertyHeight(it, true);
                
                isFirst = false;
                h += fh + k_Field;
            }

            if (!any) {
                h += EditorGUIUtility.singleLineHeight + k_Field;
            }

            return h + k_Pad;
        }

        private static void ShowAddMenu(SerializedProperty segs) {
            var menu = new GenericMenu();
            List<Type> types = SegmentTypes;

            if (types.Count == 0) {
                menu.AddDisabledItem(new GUIContent("无可用片段类型"));
            } else {
                foreach (Type t in types) {
                    Type captured = t;
                    menu.AddItem(new GUIContent(FriendlyName(captured.Name)), false, () => {
                        segs.serializedObject.Update();
                        segs.InsertArrayElementAtIndex(segs.arraySize);
                        segs.GetArrayElementAtIndex(segs.arraySize - 1).managedReferenceValue = Activator.CreateInstance(captured);
                        segs.serializedObject.ApplyModifiedProperties();
                    });
                }
            }

            menu.ShowAsContext();
        }

        // ---- 标签 ----

        private static string ElementLabel(SerializedProperty element) {
            string full = element.managedReferenceFullTypename;

            if (string.IsNullOrEmpty(full)) {
                return "（未设置类型）";
            }

            int space = full.IndexOf(' ');
            string typeName = space >= 0 ? full[(space + 1)..] : full;
            int dot = typeName.LastIndexOf('.');

            if (dot >= 0) {
                typeName = typeName[(dot + 1)..];
            }

            return FriendlyName(typeName);
        }

        private static string FriendlyName(string typeName) {
            string n = typeName;

            if (n.StartsWith("Marquee", StringComparison.Ordinal))
                n = n["Marquee".Length..];

            if (n.EndsWith("Segment", StringComparison.Ordinal) && n.Length > "Segment".Length)
                n = n[..^"Segment".Length];

            return n;
        }

        private static GUIContent BuildSummary(SerializedProperty property) {
            string id = property.FindPropertyRelative("ID").stringValue;
            SerializedProperty segs = property.FindPropertyRelative("Segments");

            string body;

            if (segs.arraySize == 0) {
                body = "（空，点开后在 Segments 添加片段）";
            } else {
                var sb = new System.Text.StringBuilder();

                for (int i = 0; i < segs.arraySize && i < 6; i++) {
                    if (i > 0) {
                        sb.Append(" + ");
                    }

                    sb.Append(ElementLabel(segs.GetArrayElementAtIndex(i)));
                }

                if (segs.arraySize > 6) {
                    sb.Append(" …");
                }

                body = sb.ToString();
            }

            return new GUIContent(string.IsNullOrEmpty(id) ? body : $"[{id}] {body}");
        }
    }
}
