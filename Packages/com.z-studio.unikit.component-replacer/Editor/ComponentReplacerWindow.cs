using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 三个页面对应用户任务：设置组件与范围 → 检查预览 → 查看结果。
    /// 只有底部主按钮推进流程；扫描和保存仍交给已有服务，窗口不直接修改组件。
    /// </summary>
    internal sealed class ComponentReplacerWindow : EditorWindow {
        private enum Page {
            Setup,
            Preview,
            Result
        }

        private static readonly string[] s_ScopeNames = { "编辑器当前选中的内容", "我指定的对象或文件夹", "所有已打开的场景", "Assets 中的全部预制体" };

        private static class Styles {
            internal static readonly GUIStyle Page = new() { padding = new RectOffset(12, 12, 10, 10) };

            internal static readonly GUIStyle Card = new(EditorStyles.helpBox) {
                padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(0, 0, 0, 10)
            };

            internal static readonly GUIStyle Title = new(EditorStyles.boldLabel) { fontSize = 18 };
            internal static readonly GUIStyle Heading = new(EditorStyles.boldLabel) { fontSize = 13, wordWrap = true };

            internal static readonly GUIStyle DropArea = new(EditorStyles.helpBox) {
                alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(8, 8, 8, 8)
            };

            internal static readonly GUIStyle Text = new(EditorStyles.wordWrappedLabel);

            internal static readonly GUIStyle Caption = new(EditorStyles.wordWrappedLabel) {
                normal = { textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor }
            };
        }

        [SerializeField]
        private string m_SourceType;

        [SerializeField]
        private string m_TargetType;

        [SerializeField]
        private ComponentReplacementScope m_Scope = ComponentReplacementScope.Specified;

        [SerializeField]
        private List<Object> m_Roots = new();

        [SerializeField]
        private bool m_CopyFields = true;

        [SerializeField]
        private bool m_ShowOptions;

        private Page m_Page;
        private ComponentReplacementPlan m_Plan;
        private ComponentReplacementResult m_Result;
        private Vector2 m_BodyScroll;
        private string m_Message;
        private MessageType m_MessageType;
        private string m_Search = "";
        private bool m_ShowAllRoots;
        private bool m_ShowReferenceAssets;
        private Type SourceType => string.IsNullOrEmpty(m_SourceType) ? null : Type.GetType(m_SourceType);
        private Type TargetType => string.IsNullOrEmpty(m_TargetType) ? null : Type.GetType(m_TargetType);

        [MenuItem("Tools/UniKit/Component Replacer", priority = 840)]
        private static void Open() {
            var window = GetWindow<ComponentReplacerWindow>("组件替换");

            if (window.m_Scope == ComponentReplacementScope.Specified && window.m_Roots.Count == 0) {
                window.AddRoots(Selection.objects);
            }
        }

        [MenuItem("CONTEXT/Component/Replace Component…")]
        private static void OpenForComponent(MenuCommand command) {
            var source = (Component)command.context;
            var window = GetWindow<ComponentReplacerWindow>("组件替换");
            window.BeginSetup();
            window.m_SourceType = source.GetType().AssemblyQualifiedName;
            window.m_Scope = ComponentReplacementScope.Specified;
            window.m_Roots = new List<Object> { source.gameObject };
        }

        private void OnEnable() {
            minSize = new Vector2(560, 520);
            m_Roots = m_Roots.Select(ComponentScopePicker.Normalize).Where(value => value != null).Distinct().ToList();
            Selection.selectionChanged += SelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        private void OnDisable() {
            Selection.selectionChanged -= SelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.projectChanged -= Invalidate;
        }

        private void SelectionChanged() {
            if (m_Scope == ComponentReplacementScope.Selection) {
                Invalidate();
            }

            Repaint();
        }

        private void OnUndoRedo() {
            BeginSetup();
            ShowMessage("已撤销或重做。可以重新预览，检查现在的对象。", MessageType.Info);
        }

        private void Invalidate() {
            m_Plan = null;

            if (m_Page == Page.Preview) {
                m_Page = Page.Setup;
                ShowMessage("设置或对象已变化，请重新预览。", MessageType.Info);
            }

            Repaint();
        }

        private void BeginSetup() {
            m_Page = Page.Setup;
            m_Plan = null;
            m_Result = null;
            m_Message = null;
            m_BodyScroll = Vector2.zero;
            Repaint();
        }

        private void InputChanged() {
            m_Message = null;
            Invalidate();
        }

        private void OnGUI() {
            m_Roots.RemoveAll(value => value == null);
            using var page = new EditorGUILayout.VerticalScope(Styles.Page);
            EditorGUILayout.LabelField("组件替换", Styles.Title, GUILayout.Height(26));
            EditorGUILayout.LabelField(m_Page switch {
                Page.Setup => "把对象上的一种组件换成另一种。先预览，确认后再修改。",
                Page.Preview => "检查下面的对象。取消勾选，就不会替换该项。",
                _ => "查看本次处理结果，再决定继续操作或还原修改。"
            }, Styles.Caption);
            GUILayout.Space(12);

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_BodyScroll)) {
                m_BodyScroll = scroll.scrollPosition;

                switch (m_Page) {
                    case Page.Setup:
                        DrawSetup();
                        break;
                    case Page.Preview:
                        DrawPreview();
                        break;
                    case Page.Result:
                        DrawResult();
                        break;
                }

                if (!string.IsNullOrEmpty(m_Message)) EditorGUILayout.HelpBox(m_Message, m_MessageType);
            }

            GUILayout.Space(8);
            DrawFooter();
        }

        private void DrawSetup() {
            using (new EditorGUILayout.VerticalScope(Styles.Card)) {
                EditorGUILayout.LabelField("1  选择组件", Styles.Heading);
                GUILayout.Space(8);
                DrawType("原组件", SourceType, type => m_SourceType = type.AssemblyQualifiedName);
                DrawType("替换成", TargetType, type => m_TargetType = type.AssemblyQualifiedName);
                GUILayout.Space(4);
                EditorGUILayout.LabelField("点击搜索组件名称，也可以从 Inspector 拖入组件，或从 Project 拖入脚本。", Styles.Caption);
            }

            using (new EditorGUILayout.VerticalScope(Styles.Card)) {
                EditorGUILayout.LabelField("2  选择处理范围", Styles.Heading);
                GUILayout.Space(8);
                EditorGUI.BeginChangeCheck();
                m_Scope = (ComponentReplacementScope)EditorGUILayout.Popup("处理哪些内容", (int)m_Scope, s_ScopeNames);

                if (EditorGUI.EndChangeCheck()) {
                    InputChanged();
                }

                GUILayout.Space(6);

                if (m_Scope == ComponentReplacementScope.Specified) {
                    DrawRoots();
                } else if (m_Scope == ComponentReplacementScope.Selection) {
                    var roots = Selection.objects.Select(ComponentScopePicker.Normalize).Where(value => value != null)
                        .Distinct().ToArray();
                    EditorGUILayout.LabelField($"可处理的选中内容：{roots.Length} 项。支持预制体、文件夹和场景对象。", Styles.Text);

                    foreach (var value in roots.Take(3)) {
                        EditorGUILayout.LabelField("• " + RootPath(value), Styles.Caption);
                    }

                    if (roots.Length > 3) {
                        EditorGUILayout.LabelField($"另有 {roots.Length - 3} 项。", Styles.Caption);
                    }
                } else {
                    EditorGUILayout.LabelField(m_Scope == ComponentReplacementScope.ProjectPrefabs
                        ? "检查 Assets 文件夹中的全部预制体。下一步仍可取消不需要的项。"
                        : "检查已打开的场景；如果正在编辑预制体，则只检查当前预制体。", Styles.Text);
                }

                GUILayout.Space(6);
                EditorGUILayout.LabelField("对象包含子对象；文件夹包含子目录中的预制体（Prefab）。", Styles.Caption);
            }

            EditorGUILayout.LabelField(m_CopyFields ? "将保留能复制的原组件设置。" : "新组件将使用默认设置。", Styles.Caption);
            m_ShowOptions = EditorGUILayout.Foldout(m_ShowOptions, "更多选项", true);

            if (m_ShowOptions) {
                using var options = new EditorGUILayout.VerticalScope(Styles.Card);
                EditorGUI.BeginChangeCheck();
                m_CopyFields = EditorGUILayout.ToggleLeft("尽量保留原组件的设置", m_CopyFields);

                if (EditorGUI.EndChangeCheck()) {
                    InputChanged();
                }

                EditorGUILayout.LabelField("同名、同类型的设置会保留，其他设置使用新组件的默认值。关闭后，新组件全部使用默认设置。", Styles.Caption);
            }
        }

        private void DrawType(string label, Type type, Action<Type> select) {
            using var row = new EditorGUILayout.HorizontalScope();
            GUILayout.Label(label, GUILayout.Width(70));
            var rect = GUILayoutUtility.GetRect(new GUIContent(type?.Name ?? "选择组件…"), EditorStyles.popup,
                GUILayout.Height(27));

            void Pick(Type picked) {
                select(picked);
                InputChanged();
            }

            if (GUI.Button(rect, new GUIContent(type?.Name ?? "选择组件…", type?.FullName), EditorStyles.popup)) {
                new ComponentTypePicker(Pick).Show(rect);
            }

            ComponentTypePicker.AcceptDrop(rect, Pick);
        }

        private void DrawRoots() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(m_Roots.Count == 0 ? "尚未添加内容" : $"已添加 {m_Roots.Count} 项",
                    EditorStyles.boldLabel);
                var addRect = GUILayoutUtility.GetRect(new GUIContent("添加…"), GUI.skin.button, GUILayout.Width(72));

                if (GUI.Button(addRect, "添加…")) {
                    new ComponentScopePicker(value => AddRoots(new[] { value })).Show(addRect);
                }

                if (m_Roots.Count > 0 && GUILayout.Button("清空", GUILayout.Width(44))) {
                    m_Roots.Clear();
                    InputChanged();
                }
            }

            var count = m_ShowAllRoots ? m_Roots.Count : Math.Min(4, m_Roots.Count);

            for (var i = 0; i < count; i++) {
                var index = i;
                var value = m_Roots[i];

                using (new EditorGUILayout.HorizontalScope()) {
                    var content = EditorGUIUtility.ObjectContent(value, value.GetType());
                    var rect = GUILayoutUtility.GetRect(content, EditorStyles.objectField, GUILayout.Height(24));

                    if (GUI.Button(rect, content, EditorStyles.objectField)) {
                        new ComponentScopePicker(picked => SetRoot(index, picked), false, value).Show(rect);
                    }

                    AcceptRootDrop(rect, index);

                    if (GUILayout.Button(new GUIContent("×", "移除此项"), GUILayout.Width(26))) {
                        m_Roots.RemoveAt(i--);
                        count--;
                        InputChanged();
                    }
                }

                EditorGUILayout.LabelField(RootPath(value), Styles.Caption);
            }

            if (m_Roots.Count > 4) {
                m_ShowAllRoots = EditorGUILayout.Foldout(m_ShowAllRoots, $"显示全部 {m_Roots.Count} 项", true);
            }

            var area = GUILayoutUtility.GetRect(0, m_Roots.Count == 0 ? 64 : 34, GUILayout.ExpandWidth(true));

            if (GUI.Button(area, m_Roots.Count == 0 ? "把对象、预制体或文件夹拖到这里\n也可以点击这里选择" : "继续拖入内容，或点击这里添加",
                    Styles.DropArea)) {
                new ComponentScopePicker(value => AddRoots(new[] { value })).Show(area);
            }

            AcceptRootDrop(area);
        }

        private static string RootPath(Object value) {
            var assetPath = AssetDatabase.GetAssetPath(value);
            return !string.IsNullOrEmpty(assetPath)
                ? assetPath
                : value is GameObject gameObject
                    ? $"{gameObject.scene.name} / {ComponentReplacementPlan.HierarchyPath(gameObject.transform)}"
                    : value.name;
        }

        private void AcceptRootDrop(Rect rect, int replaceIndex = -1) {
            var current = Event.current;

            if (!rect.Contains(current.mousePosition)
                || current.type != EventType.DragUpdated && current.type != EventType.DragPerform) {
                return;
            }

            var supported = DragAndDrop.objectReferences.Select(ComponentScopePicker.Normalize)
                .Where(value => value != null).Distinct().ToArray();
            var accepted = supported.Length > 0 && (replaceIndex < 0 || supported.Length == 1);
            DragAndDrop.visualMode = accepted ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (current.type == EventType.DragPerform && accepted) {
                DragAndDrop.AcceptDrag();

                if (replaceIndex < 0) {
                    AddRoots(supported);
                } else {
                    SetRoot(replaceIndex, supported[0]);
                }
            }

            current.Use();
        }

        private void SetRoot(int index, Object value) {
            if (index < 0 || index >= m_Roots.Count) {
                return;
            }

            var root = ComponentScopePicker.Normalize(value);

            if (root == null) {
                return;
            }

            m_Roots[index] = root;
            m_Roots = m_Roots.Where(item => item != null).Distinct().ToList();
            InputChanged();
        }

        private void AddRoots(IEnumerable<Object> roots) {
            foreach (var root in roots.Select(ComponentScopePicker.Normalize).Where(root => root != null)) {
                if (!m_Roots.Contains(root)) {
                    m_Roots.Add(root);
                }
            }

            InputChanged();
        }

        /// <summary>只返回当前需要完成的一步，不把初次打开时的空输入当作错误弹给用户。</summary>
        private string SetupIssue() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                return "请先退出播放模式。";
            }

            if (EditorApplication.isCompiling) {
                return "Unity 正在编译，完成后即可预览。";
            }

            if (SourceType == null) {
                return "请先选择要移除的原组件。";
            }

            if (TargetType == null) {
                return "请选择要换成的新组件。";
            }

            var error = ComponentReplacement.ValidateType(SourceType) ?? ComponentReplacement.ValidateType(TargetType);

            if (error != null) {
                return error;
            }

            if (SourceType == TargetType) {
                return "原组件和新组件相同，请选择不同的组件。";
            }

            return m_Scope switch {
                ComponentReplacementScope.Specified when m_Roots.All(value => value == null) => "请添加要处理的对象、预制体或文件夹。",
                ComponentReplacementScope.Selection when Selection.objects.All(value =>
                        ComponentScopePicker.Normalize(value) == null) =>
                    "当前选择中没有可处理的内容。请在 Project 中选择预制体或文件夹，或在 Hierarchy 中选择对象。",
                _ => null
            };
        }

        private void Scan() {
            var issue = SetupIssue();

            if (issue != null) {
                ShowMessage(issue, MessageType.Info);
                return;
            }

            try {
                m_Plan = ComponentReplacementPlan.Scan(SourceType, TargetType, m_CopyFields, m_Scope,
                    m_Scope == ComponentReplacementScope.Selection ? Selection.objects : m_Roots, Progress);
                m_Page = Page.Preview;
                m_Result = null;
                m_Search = "";
                m_BodyScroll = Vector2.zero;
                m_ShowReferenceAssets = false;
                m_Message = m_Plan.issues.Count == 0 ? null : string.Join("\n", m_Plan.issues);
                m_MessageType = MessageType.Warning;
            } catch (OperationCanceledException) {
                ShowMessage("已取消预览，没有修改任何组件。", MessageType.Info);
            } catch (Exception exception) {
                ShowError(exception);
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private ComponentReplacementEntry[] SelectedEntries() =>
            m_Plan == null ? Array.Empty<ComponentReplacementEntry>()
                : m_Plan.entries.Where(row => row.selected && row.error == null).ToArray();

        private ComponentReplacementEntry[] VisibleEntries() =>
            m_Plan == null ? Array.Empty<ComponentReplacementEntry>()
                : m_Plan.entries.Where(row =>
                        (EntryFile(row) + "/" + row.objectPath).IndexOf(m_Search, StringComparison.OrdinalIgnoreCase)
                        >= 0)
                    .ToArray();

        private static string EntryFile(ComponentReplacementEntry entry) {
            if (entry.IsAsset) {
                return entry.assetPath;
            }

            if (entry.gameObject == null) {
                return "场景对象（已失效）";
            }

            var scene = entry.gameObject.scene;
            return "场景 · " + (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path);
        }

        private void SelectVisible(bool selected) {
            foreach (var row in VisibleEntries()) {
                row.selected = selected && row.error == null;
            }
        }

        private void DrawPreview() {
            if (m_Plan == null) {
                return;
            }

            using (new EditorGUILayout.VerticalScope(Styles.Card)) {
                EditorGUILayout.LabelField("3  检查并替换", Styles.Heading);
                EditorGUILayout.LabelField($"{SourceType.Name}  →  {TargetType.Name}", Styles.Text);
                var ready = m_Plan.entries.Count(row => row.error == null);
                EditorGUILayout.LabelField(
                    $"找到 {m_Plan.entries.Count} 个组件：{ready} 个可替换，{m_Plan.entries.Count - ready} 个无法替换。", Styles.Text);
            }

            if (m_Plan.entries.Count == 0) {
                EditorGUILayout.HelpBox($"这个范围内没有找到 {SourceType.Name}。返回设置，检查原组件和处理范围后再试。", MessageType.Info);
                return;
            }

            m_Search = EditorGUILayout.TextField("搜索对象或路径", m_Search);
            var visible = VisibleEntries();

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField($"显示 {visible.Length} 项");

                if (GUILayout.Button("勾选显示项", GUILayout.Width(96))) {
                    SelectVisible(true);
                }

                if (GUILayout.Button("取消显示项", GUILayout.Width(96))) {
                    SelectVisible(false);
                }
            }

            if (visible.Length == 0) {
                EditorGUILayout.LabelField("没有匹配的对象，试试其他名称或清空搜索。", Styles.Caption);
            }

            foreach (var group in visible.GroupBy(EntryFile)) {
                using var card = new EditorGUILayout.VerticalScope(Styles.Card);
                EditorGUILayout.LabelField(group.Key, Styles.Heading);

                foreach (var row in group) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        using (new EditorGUI.DisabledScope(row.error != null)) {
                            row.selected =
                                EditorGUILayout.Toggle(row.selected && row.error == null, GUILayout.Width(20));
                        }

                        EditorGUILayout.LabelField(row.objectPath, Styles.Text);

                        if (GUILayout.Button("定位", GUILayout.Width(44)) && row.source != null) {
                            EditorGUIUtility.PingObject(row.source);
                        }
                    }

                    if (row.error != null) {
                        EditorGUILayout.LabelField("无法替换：" + row.error, Styles.Caption);
                    }
                }
            }

            var selected = SelectedEntries();
            var extra = m_Plan.ReferenceAssets(selected)
                .Except(selected.Where(row => row.IsAsset).Select(row => row.assetPath)).ToArray();

            if (extra.Length > 0) {
                m_ShowReferenceAssets =
                    EditorGUILayout.Foldout(m_ShowReferenceAssets, $"还会同步更新 {extra.Length} 个引用文件（查看路径）", true);
                EditorGUILayout.LabelField("这些预制体引用了被替换的组件，会一起保存以更新引用。", Styles.Caption);

                if (m_ShowReferenceAssets) {
                    foreach (var path in extra) {
                        EditorGUILayout.LabelField(path, Styles.Text);
                    }
                }
            }
        }

        private void DrawResult() {
            if (m_Result == null) {
                return;
            }

            using (new EditorGUILayout.VerticalScope(Styles.Card)) {
                EditorGUILayout.LabelField(m_Result.cancelled
                    ? "已取消，已完成的修改予以保留"
                    : m_Result.issues.Count > 0
                        ? "处理结束，部分项目需要检查"
                        : "替换完成", Styles.Heading);
                GUILayout.Space(8);
                EditorGUILayout.LabelField($"已保存 {m_Result.assets} 个预制体文件，替换其中 {m_Result.assetComponents} 个组件。",
                    Styles.Text);
                EditorGUILayout.LabelField($"已处理 {m_Result.sceneComponents} 个场景组件。", Styles.Text);

                if (m_Result.inherited > 0) {
                    EditorGUILayout.LabelField($"另有 {m_Result.inherited} 项随原预制体同步更新。", Styles.Caption);
                }
            }

            EditorGUILayout.LabelField("接下来怎么做", Styles.Heading);
            EditorGUILayout.LabelField("场景中的修改请保存；需要撤销时按 Ctrl / Cmd + Z。预制体文件已保存，可以用下方的还原按钮撤回最近一批预制体修改。", Styles.Text);

            if (m_Result.issues.Count > 0) {
                GUILayout.Space(10);
                EditorGUILayout.LabelField($"需要检查的项目（{m_Result.issues.Count}）", Styles.Heading);

                foreach (var issue in m_Result.issues) {
                    EditorGUILayout.HelpBox(issue, MessageType.Warning);
                }
            }
        }

        private void DrawFooter() {
            var busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;

            if (m_Page == Page.Setup) {
                var issue = SetupIssue();
                EditorGUILayout.LabelField(issue ?? "准备好了。下一步只生成预览，不会修改对象。", Styles.Caption);

                using (new EditorGUI.DisabledScope(issue != null)) {
                    if (GUILayout.Button("预览替换结果", GUILayout.Height(34))) {
                        Scan();
                    }
                }

                if (ComponentAssetBackup.CanRestore) {
                    DrawRestoreButton(busy);
                }

                return;
            }

            if (m_Page == Page.Preview) {
                var selected = SelectedEntries();
                var files = selected.Where(row => row.IsAsset).Select(row => row.assetPath)
                    .Union(m_Plan.ReferenceAssets(selected)).Count();
                var scene = selected.Count(row => !row.IsAsset);
                EditorGUILayout.LabelField($"已勾选 {selected.Length} 个组件 · 将保存 {files} 个预制体文件，处理 {scene} 个场景组件。",
                    Styles.Text);
                var hidden = selected.Except(VisibleEntries()).Count();

                if (hidden > 0) {
                    EditorGUILayout.LabelField($"其中 {hidden} 项被搜索条件隐藏，仍会替换。清空搜索可查看。", Styles.Caption);
                }

                EditorGUILayout.LabelField(busy
                    ? "请先退出播放模式，并等待 Unity 编译完成。"
                    : selected.Length == 0
                        ? "请勾选可替换的项，或返回设置调整范围。"
                        : "预制体会直接保存并自动备份；场景修改可撤销，需自行保存。", Styles.Caption);
                using var buttons = new EditorGUILayout.HorizontalScope();

                if (GUILayout.Button("返回设置", GUILayout.Height(34), GUILayout.Width(100))) {
                    BeginSetup();
                    return;
                }

                using (new EditorGUI.DisabledScope(selected.Length == 0 || busy)) {
                    if (GUILayout.Button($"替换所选 {selected.Length} 个组件", GUILayout.Height(34))) {
                        Apply();
                    }
                }

                return;
            }

            using (new EditorGUI.DisabledScope(busy)) {
                if (GUILayout.Button("继续替换", GUILayout.Height(34))) {
                    BeginSetup();
                }
            }

            DrawRestoreButton(busy);
        }

        private void DrawRestoreButton(bool busy) {
            using (new EditorGUI.DisabledScope(!ComponentAssetBackup.CanRestore || busy)) {
                if (!GUILayout.Button(new GUIContent("还原上次预制体修改", "恢复最近一批已保存的预制体；不会覆盖之后又修改过的文件。"))) {
                    return;
                }

                try {
                    var count = ComponentAssetBackup.Restore();
                    BeginSetup();
                    ShowMessage($"已还原 {count} 个预制体文件。可重新预览检查。", MessageType.Info);
                } catch (Exception exception) {
                    ShowError(exception);
                }
            }
        }

        private void Apply() {
            if (m_Plan == null || SelectedEntries().Length == 0) {
                ShowMessage("请先预览并勾选要替换的组件。", MessageType.Info);
                return;
            }

            try {
                var result = ComponentReplacementBatch.Apply(m_Plan, Progress);
                m_Result = result;
                m_Plan = null;
                m_Page = Page.Result;
                m_BodyScroll = Vector2.zero;
                m_Message = null;

                foreach (var issue in result.issues) {
                    Debug.LogWarning("[Component Replacer] " + issue);
                }
            } catch (Exception exception) {
                Invalidate();
                ShowError(exception);
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Progress(string label, float progress) {
            if (EditorUtility.DisplayCancelableProgressBar("组件替换", label, progress)) {
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