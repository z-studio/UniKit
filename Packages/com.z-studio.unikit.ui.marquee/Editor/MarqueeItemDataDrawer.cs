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
        private const float k_Field = 4f;   // 字段行间距
        private const float k_BadgeW = 48f; // 左侧类型名标签宽度

        // 所有可实例化的片段子类（含扩展程序集里的 SpineSegment，按需自动出现）
        // 脚本重新加载后清除类型缓存，以发现新增或移除的扩展类型。
        private static List<Type> s_SegmentTypes;

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() => s_SegmentTypes = null;

        // 每个属性路径一个 ReorderableList（保留展开/拖拽/选中状态）
        private readonly Dictionary<string, (SerializedObject Owner, ReorderableList List)> m_Lists = new();

        private static List<Type> SegmentTypes {
            get {
                if (s_SegmentTypes == null) {
                    s_SegmentTypes = new List<Type>();

                    foreach (Type t in TypeCache.GetTypesDerivedFrom<MarqueeSegment>()) {
                        if (!t.IsAbstract && !t.IsGenericType && t.IsDefined(typeof(SerializableAttribute), false)
                            && t.GetConstructor(Type.EmptyTypes) != null) {
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

            int indent = EditorGUI.indentLevel;
            float labelWidth = EditorGUIUtility.labelWidth;

            try {
                // 外层列表负责拖拽；卡片内部使用独立列宽，避免窄 Inspector 下输入框被挤到右侧。
                EditorGUI.indentLevel = 0;
                float line = EditorGUIUtility.singleLineHeight;
                var card = new Rect(pos.x, pos.y, pos.width, pos.height - k_Pad);
                GUI.Box(card, GUIContent.none, EditorStyles.helpBox);
                var body = new Rect(card.x + 10f, card.y + k_Pad, card.width - 20f, line);
                EditorGUIUtility.labelWidth = Mathf.Clamp(body.width * 0.34f, 70f, 130f);
                bool multiple = property.serializedObject.isEditingMultipleObjects;

                var header = new Rect(body.x + 12f, body.y, body.width - 12f, line);
                property.isExpanded = EditorGUI.Foldout(header, property.isExpanded,
                    multiple ? label : BuildSummary(property), true, EditorStyles.foldoutHeader);

                if (!property.isExpanded) {
                    return;
                }

                body.y += line + k_Pad * 2f;
                EditorGUI.PropertyField(body, property.FindPropertyRelative("ID"), new GUIContent("标识", "可选的业务标识"));
                body.y += line + k_Pad;
                EditorGUI.PropertyField(body, property.FindPropertyRelative("Cycles"),
                    new GUIContent("展示次数", "逐条模式：-1 无限，0 跳过，正数为展示次数；连续模式只区分 0 与非 0。"));
                body.y += line + k_Pad * 2f;

                if (multiple) {
                    body.height = line * 2f;
                    EditorGUI.HelpBox(body, "片段列表请单独选中对象编辑。", MessageType.Info);
                } else {
                    ReorderableList list = GetList(property, property.FindPropertyRelative("Segments"));
                    body.height = list.GetHeight();
                    list.DoList(body);
                }
            } finally {
                EditorGUI.indentLevel = indent;
                EditorGUIUtility.labelWidth = labelWidth;
                EditorGUI.EndProperty();
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            float line = EditorGUIUtility.singleLineHeight;
            float height = line + k_Pad * 3f; // 标题、卡片内边距、卡片间距

            if (!property.isExpanded) {
                return height;
            }

            height += line * 2f + k_Pad * 5f; // 标识与次数，分组间距
            height += property.serializedObject.isEditingMultipleObjects
                ? line * 2f
                : GetList(property, property.FindPropertyRelative("Segments")).GetHeight();
            return height;
        }

        // ---- ReorderableList ----

        private ReorderableList GetList(SerializedProperty property, SerializedProperty segs) {
            string key = property.serializedObject.targetObject.GetInstanceID() + "::" + property.propertyPath;

            // 不读取缓存属性来判断有效性：它可能已被 Dispose。
            // SerializedObject 更换时重建列表，避免内部仍持有旧的序列化上下文。
            if (m_Lists.TryGetValue(key, out var cached)
                && ReferenceEquals(cached.Owner, property.serializedObject)) {
                cached.List.serializedProperty = segs;
                return cached.List;
            }

            var rl = new ReorderableList(property.serializedObject, segs, true, true, true, true) {
                drawHeaderCallback = r =>
                    EditorGUI.LabelField(r, new GUIContent("内容片段", "从上到下对应显示时从左到右的顺序；拖动手柄排序，点击 + 添加。"), EditorStyles.miniBoldLabel)
            };

            // 回调始终读取本轮绑定的属性，不能捕获创建列表时的 segs。
            rl.elementHeightCallback = i => ElementHeight(rl.serializedProperty.GetArrayElementAtIndex(i));
            rl.drawElementCallback = (r, i, _, _) => DrawElement(r, rl.serializedProperty.GetArrayElementAtIndex(i));
            
            rl.drawElementBackgroundCallback = (r, i, active, focused) => {
                if (Event.current.type != EventType.Repaint) {
                    return;
                }

                Color bg = i % 2 == 0
                    ? new Color(0f, 0f, 0f, EditorGUIUtility.isProSkin ? 0.12f : 0.04f)
                    : new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.06f : 0.08f);
                
                EditorGUI.DrawRect(r, bg);

                if (active) {
                    EditorGUI.DrawRect(r, new Color(0.24f, 0.49f, 0.91f, 0.25f));
                }
            };
            
            rl.onAddDropdownCallback = (_, list) => ShowAddMenu(list.serializedProperty);
            rl.drawNoneElementCallback = r =>
                EditorGUI.LabelField(r, "点击 + 添加文本、图片或 Spine");

            m_Lists[key] = (property.serializedObject, rl);
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

                // 首字段与类型标签同行，但仍保留属性 Drawer 和嵌套字段所需的高度。
                GUIContent fieldLabel = isFirst ? GUIContent.none : new GUIContent(it.displayName, it.tooltip);
                float fh = EditorGUI.GetPropertyHeight(it, fieldLabel, true);
                
                if (isFirst) {
                    EditorGUI.LabelField(new Rect(r.x, y, k_BadgeW - k_Pad, line), new GUIContent(label, label), EditorStyles.miniBoldLabel);
                    isFirst = false;
                }
                
                EditorGUI.PropertyField(new Rect(r.x + k_BadgeW, y, r.width - k_BadgeW, fh),
                    it, fieldLabel, true);
                
                y += fh + k_Field;
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
                
                // 标签和 includeChildren 必须与绘制端一致，否则扩展 Drawer 会出现高度偏差。
                GUIContent fieldLabel = isFirst ? GUIContent.none : new GUIContent(it.displayName, it.tooltip);
                float fh = EditorGUI.GetPropertyHeight(it, fieldLabel, true);
                
                isFirst = false;
                h += fh + k_Field;
            }

            if (!any) {
                h += EditorGUIUtility.singleLineHeight + k_Field;
            }

            return h + k_Pad;
        }

        private static void ShowAddMenu(SerializedProperty segs) {
            // 菜单回调延后执行，不能继续使用本次 Inspector 绘制的属性句柄。
            UnityEngine.Object target = segs.serializedObject.targetObject;
            string path = segs.propertyPath;
            var menu = new GenericMenu();
            List<Type> types = SegmentTypes;

            if (types.Count == 0) {
                menu.AddDisabledItem(new GUIContent("无可用片段类型"));
            } else {
                foreach (Type t in types) {
                    Type captured = t;
                    
                    menu.AddItem(new GUIContent(FriendlyName(captured.Name)), false, () => {
                        if (target == null) {
                            return;
                        }

                        using var serialized = new SerializedObject(target);
                        SerializedProperty current = serialized.FindProperty(path);
                        
                        if (current == null || !current.isArray) {
                            return;
                        }

                        // 先实例化，构造失败时不改变数组结构。
                        object segment = Activator.CreateInstance(captured);
                        int index = current.arraySize;
                        current.InsertArrayElementAtIndex(index);
                        current.GetArrayElementAtIndex(index).managedReferenceValue = segment;
                        serialized.ApplyModifiedProperties();
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

            if (n.StartsWith("Marquee", StringComparison.Ordinal)) {
                n = n["Marquee".Length..];
            }

            if (n.EndsWith("Segment", StringComparison.Ordinal) && n.Length > "Segment".Length) {
                n = n[..^"Segment".Length];
            }

            return n switch {
                "Text" => "文本",
                "Image" => "图片",
                _ => n
            };
        }

        private static GUIContent BuildSummary(SerializedProperty property) {
            string id = property.FindPropertyRelative("ID").stringValue;
            SerializedProperty segs = property.FindPropertyRelative("Segments");

            string body;

            if (segs.arraySize == 0) {
                body = "空条目 · 展开添加内容";
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

            return new GUIContent(string.IsNullOrEmpty(id) ? body : $"{id} · {body}", body);
        }
    }
}
