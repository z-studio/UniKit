using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>窗口收集范围、显示预览和执行反馈；扫描、资产保存与迁移各自独立。</summary>
    internal sealed class ComponentReplacerWindow : EditorWindow {
        private static readonly string[] s_ScopeNames = {
            "当前选择（对象、Prefab 或文件夹）", "指定对象、Prefab 或文件夹", "已打开场景 或 当前 Prefab Mode", "Project 全部 Prefab（Assets）"
        };

        [SerializeField]
        private string m_SourceType;

        [SerializeField]
        private string m_TargetType;

        [SerializeField]
        private ComponentReplacementScope m_Scope;

        [SerializeField]
        private List<Object> m_Roots = new();

        [SerializeField]
        private bool m_CopyFields = true;

        private ComponentReplacementPlan m_Plan;
        private Vector2 m_Scroll;
        private Vector2 m_RootScroll;
        private Vector2 m_ReferenceScroll;
        private string m_Message;
        private MessageType m_MessageType;
        private string m_Search = "";
        private bool m_ShowReferenceAssets;
        private Type SourceType => string.IsNullOrEmpty(m_SourceType) ? null : Type.GetType(m_SourceType);
        private Type TargetType => string.IsNullOrEmpty(m_TargetType) ? null : Type.GetType(m_TargetType);

        [MenuItem("Tools/UniKit/Component Replacer", priority = 840)]
        private static void Open() => GetWindow<ComponentReplacerWindow>("Component Replacer");

        [MenuItem("CONTEXT/Component/Replace Component…")]
        private static void OpenForComponent(MenuCommand command) {
            var source = (Component)command.context;
            var window = GetWindow<ComponentReplacerWindow>("Component Replacer");
            window.m_SourceType = source.GetType().AssemblyQualifiedName;
            window.m_Scope = ComponentReplacementScope.Specified;
            window.m_Roots = new List<Object> { source.gameObject };
            window.Invalidate();
        }

        private void OnEnable() {
            minSize = new Vector2(560, 560);
            Selection.selectionChanged += SelectionChanged;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        private void OnDisable() {
            Selection.selectionChanged -= SelectionChanged;
            Undo.undoRedoPerformed -= Invalidate;
            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.projectChanged -= Invalidate;
        }

        private void SelectionChanged() {
            if (m_Scope == ComponentReplacementScope.Selection) {
                Invalidate();
            }

            Repaint();
        }

        private void Invalidate() {
            m_Plan = null;
            Repaint();
        }

        private void OnGUI() {
            using var page = new EditorGUILayout.VerticalScope();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("组件批量替换", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选择 A、B 和范围 → 扫描 → 勾选替换。Prefab 资产无需打开即可批量处理。",
                EditorStyles.wordWrappedMiniLabel);
            DrawType("原组件 A", SourceType, type => m_SourceType = type.AssemblyQualifiedName);
            DrawType("目标组件 B", TargetType, type => m_TargetType = type.AssemblyQualifiedName);
            EditorGUI.BeginChangeCheck();
            m_Scope = (ComponentReplacementScope)EditorGUILayout.Popup("扫描范围", (int)m_Scope, s_ScopeNames);
            m_CopyFields = EditorGUILayout.Toggle(new GUIContent("迁移兼容字段", "同名且同类型的序列化字段；否则使用 B 默认值。"), m_CopyFields);

            if (EditorGUI.EndChangeCheck()) {
                Invalidate();
            }

            switch (m_Scope) {
                case ComponentReplacementScope.Specified:
                    DrawRoots();
                    break;
                case ComponentReplacementScope.Selection:
                    EditorGUILayout.LabelField($"当前选中 {Selection.objects.Length} 项，包含子对象；选中的文件夹递归扫描 Prefab。",
                        EditorStyles.wordWrappedMiniLabel);
                    break;
                case ComponentReplacementScope.ProjectPrefabs:
                    EditorGUILayout.LabelField("递归扫描 Assets 中的所有 Prefab，结果可逐项取消勾选。", EditorStyles.wordWrappedMiniLabel);
                    break;
            }

            EditorGUILayout.HelpBox("场景修改：Ctrl/Cmd + Z 撤销，自行保存。Prefab 资产：直接保存并自动备份，可用下方按钮还原。", MessageType.Info);
            var busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;
            var typeError = ComponentReplacement.ValidateType(SourceType)
                            ?? ComponentReplacement.ValidateType(TargetType);

            if (SourceType != null && SourceType == TargetType) {
                typeError = "A 和 B 不能相同。";
            }

            if (typeError != null) {
                EditorGUILayout.HelpBox(typeError, MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(typeError != null || busy)) {
                    if (GUILayout.Button("扫描并预览", GUILayout.Height(28))) {
                        Scan();
                    }
                }

                using (new EditorGUI.DisabledScope(!ComponentAssetBackup.CanRestore || busy)) {
                    if (GUILayout.Button("还原上次资产替换", GUILayout.Height(28), GUILayout.Width(150))) {
                        try {
                            var count = ComponentAssetBackup.Restore();
                            ShowMessage($"已还原 {count} 个 Prefab 资产。", MessageType.Info);
                            Invalidate();
                        } catch (Exception exception) {
                            ShowError(exception);
                        }
                    }
                }
            }

            if (busy) {
                EditorGUILayout.HelpBox("请在编辑模式且编译完成后使用。", MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(m_Message)) {
                EditorGUILayout.HelpBox(m_Message, m_MessageType);
            }

            if (m_Plan == null) {
                GUILayout.FlexibleSpace();
                return;
            }

            DrawPreview(busy);
        }

        private void DrawRoots() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField($"指定范围 · {m_Roots.Count(root => root != null)} 项", EditorStyles.boldLabel);

                if (GUILayout.Button("添加当前选择", GUILayout.Width(100))) {
                    AddRoots(Selection.objects);
                }

                if (GUILayout.Button("添加一项", GUILayout.Width(72))) {
                    m_Roots.Add(null);
                    Invalidate();
                }

                if (GUILayout.Button("清空", GUILayout.Width(44))) {
                    m_Roots.Clear();
                    Invalidate();
                }
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_RootScroll,
                       GUILayout.Height(Mathf.Clamp(m_Roots.Count * 23, 30, 115)))) {
                m_RootScroll = scroll.scrollPosition;

                for (var i = 0; i < m_Roots.Count; i++) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUI.BeginChangeCheck();
                        m_Roots[i] = EditorGUILayout.ObjectField(m_Roots[i], typeof(Object), true);

                        if (EditorGUI.EndChangeCheck()) {
                            Invalidate();
                        }

                        if (GUILayout.Button("×", GUILayout.Width(24))) {
                            m_Roots.RemoveAt(i--);
                            Invalidate();
                        }
                    }
                }
            }

            var area = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            GUI.Box(area, "拖入多个场景对象、Prefab 或文件夹（含子目录）", EditorStyles.helpBox);
            var current = Event.current;

            if (!area.Contains(current.mousePosition) ||
                current.type != EventType.DragUpdated && current.type != EventType.DragPerform) {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (current.type == EventType.DragPerform) {
                DragAndDrop.AcceptDrag();
                AddRoots(DragAndDrop.objectReferences);
            }

            current.Use();
        }

        private void AddRoots(IEnumerable<Object> roots) {
            foreach (var root in roots.Where(root => root != null)) {
                if (!m_Roots.Contains(root)) {
                    m_Roots.Add(root);
                }
            }

            Invalidate();
        }

        private void Scan() {
            try {
                m_Plan = ComponentReplacementPlan.Scan(SourceType, TargetType, m_CopyFields, m_Scope,
                    m_Scope == ComponentReplacementScope.Selection ? Selection.objects : m_Roots, Progress);

                var issues = string.Join("\n", m_Plan.issues.Take(8));
                ShowMessage(m_Plan.entries.Count == 0 ? "未找到 A。请检查范围和组件类型（精确匹配）。\n" + issues : issues,
                    m_Plan.issues.Count > 0 ? MessageType.Warning : MessageType.Info);
            } catch (OperationCanceledException) {
                Invalidate();
                ShowMessage("已取消扫描。", MessageType.Info);
            } catch (Exception exception) {
                ShowError(exception);
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawPreview(bool busy) {
            var rows = m_Plan.entries;
            var selected = rows.Where(row => row.selected && row.error == null).ToArray();
            var referenceAssets = m_Plan.ReferenceAssets(selected);
            var assetCount = selected.Where(row => row.IsAsset).Select(row => row.assetPath).Union(referenceAssets)
                .Count();
            var sceneCount = selected.Count(row => !row.IsAsset);

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(
                    $"找到 {rows.Count} 个 · 已选 {selected.Length} 个（{assetCount} 个 Prefab / {sceneCount} 个场景组件）");

                if (GUILayout.Button("全选", GUILayout.Width(44))) {
                    rows.ForEach(row => row.selected = row.error == null);
                }

                if (GUILayout.Button("清空", GUILayout.Width(44))) {
                    rows.ForEach(row => row.selected = false);
                }
            }

            var extraAssets = referenceAssets.Except(selected.Where(row => row.IsAsset).Select(row => row.assetPath))
                .ToArray();

            if (extraAssets.Length > 0) {
                m_ShowReferenceAssets = EditorGUILayout.Foldout(m_ShowReferenceAssets,
                    $"同时保存 {extraAssets.Length} 个引用持有 Prefab（点击检查路径）", true);

                if (m_ShowReferenceAssets) {
                    using var references = new EditorGUILayout.ScrollViewScope(m_ReferenceScroll,
                        GUILayout.Height(Mathf.Min(80, extraAssets.Length * 22)));
                    m_ReferenceScroll = references.scrollPosition;

                    foreach (var path in extraAssets) {
                        using var referenceRow = new EditorGUILayout.HorizontalScope();
                        EditorGUILayout.LabelField(path, EditorStyles.wordWrappedMiniLabel);

                        if (GUILayout.Button("定位", GUILayout.Width(44))) {
                            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                        }
                    }
                }
            }

            m_Search = EditorGUILayout.TextField("筛选路径", m_Search);

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_Scroll)) {
                m_Scroll = scroll.scrollPosition;

                foreach (var row in rows.Where(row => ((row.assetPath ?? "") + row.objectPath)
                             .IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            using (new EditorGUI.DisabledScope(row.error != null)) {
                                row.selected = EditorGUILayout.Toggle(row.selected && row.error == null,
                                    GUILayout.Width(18));
                            }

                            EditorGUILayout.ObjectField(row.source, typeof(Component), true);
                        }

                        EditorGUILayout.LabelField(row.IsAsset ? row.assetPath : "场景对象",
                            EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.LabelField(row.objectPath, EditorStyles.wordWrappedMiniLabel);

                        if (row.error != null) {
                            EditorGUILayout.LabelField(row.error, EditorStyles.wordWrappedMiniLabel);
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(selected.Length == 0 || busy)) {
                var action = assetCount > 0 ? $"替换并保存 {assetCount} 个 Prefab · {sceneCount} 个场景组件"
                    : $"2. 替换 {sceneCount} 个场景组件";

                if (!GUILayout.Button(action, GUILayout.Height(32))) {
                    return;
                }

                try {
                    var result = ComponentReplacementBatch.Apply(m_Plan, Progress);
                    var message = $"{(result.cancelled ? "已取消，保留已完成项。" : "处理完成。")} " +
                                  $"已保存 {result.assets} 个 Prefab（{result.assetComponents} 个组件），替换 {result.sceneComponents} 个场景组件，{result.inherited} 项随源 Prefab 更新。";

                    if (result.issues.Count > 0) {
                        message += $"\n{result.issues.Count} 项问题：\n" + string.Join("\n", result.issues.Take(5));

                        foreach (var issue in result.issues) {
                            Debug.LogWarning("[Component Replacer] " + issue);
                        }
                    }

                    ShowMessage(message, result.issues.Count > 0 ? MessageType.Warning : MessageType.Info);
                    Invalidate();
                } catch (Exception exception) {
                    ShowError(exception);
                    Invalidate();
                } finally {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private void DrawType(string label, Type type, Action<Type> select) {
            using var row = new EditorGUILayout.HorizontalScope();
            EditorGUILayout.PrefixLabel(label);
            var rect = GUILayoutUtility.GetRect(new GUIContent(type?.Name ?? "选择或拖入组件 / 脚本 / 对象"), EditorStyles.popup);

            void Pick(Type picked) {
                select(picked);
                m_Message = null;
                Invalidate();
            }

            if (GUI.Button(rect, new GUIContent(type?.Name ?? "选择或拖入组件 / 脚本 / 对象", type?.FullName),
                    EditorStyles.popup)) {
                new ComponentTypePicker(Pick).Show(rect);
            }

            ComponentTypePicker.AcceptDrop(rect, Pick);
        }

        private static void Progress(string label, float progress) {
            if (EditorUtility.DisplayCancelableProgressBar("Component Replacer", label, progress)) {
                throw new OperationCanceledException();
            }
        }

        private void ShowMessage(string message, MessageType type) {
            m_Message = message;
            m_MessageType = type;
        }

        private void ShowError(Exception exception) => ShowMessage(exception.Message, MessageType.Error);
    }
}