using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 固定尺寸的范围浏览器。勾选负责选择，进入按钮负责浏览，两者不混用。
    /// 平时只显示当前目录/层级；搜索才跨目录汇总。资产只在勾选时加载。
    /// </summary>
    internal sealed class ComponentScopePicker : PopupWindowContent {
        internal sealed class Entry {
            internal string name;
            internal string path;
            internal string assetPath;
            internal GameObject gameObject;
            internal bool folder;
            internal bool CanBrowse => folder || gameObject != null && gameObject.transform.childCount > 0;

            internal Object Resolve() =>
                Normalize(assetPath == null ? gameObject : AssetDatabase.LoadAssetAtPath<Object>(assetPath));

            internal string Key => assetPath == null ? $"scene:{gameObject.GetInstanceID()}" : $"asset:{assetPath}";
        }

        private readonly Action<Object> m_Select;
        private readonly bool m_AllowMultiple;
        private readonly Dictionary<string, Object> m_Selected = new(StringComparer.Ordinal);
        private string[] m_AssetPaths = Array.Empty<string>();
        private GameObject[] m_SceneObjects = Array.Empty<GameObject>();
        private readonly HashSet<string> m_Folders = new(StringComparer.Ordinal);
        private HashSet<string> m_ProjectRoots = new(StringComparer.Ordinal);
        private string m_Folder = "Assets";
        private GameObject m_Parent;
        private int m_Tab;
        private string m_Search = "";
        private Vector2 m_Scroll;
        private bool m_Dirty = true;
        private bool m_FocusSearch = true;
        private GUIStyle m_PathStyle;
        private string m_LastClick;
        private Dictionary<string, Object> m_BeforeClick;

        internal ComponentScopePicker(Action<Object> select, bool allowMultiple = true, Object current = null) {
            m_Select = select;
            m_AllowMultiple = allowMultiple;
            current = Normalize(current);

            if (current == null) {
                return;
            }

            m_Selected.Add(Key(current), current);
            var path = AssetDatabase.GetAssetPath(current);

            if (!string.IsNullOrEmpty(path)) {
                m_Folder = ParentPath(path);
            } else {
                m_Tab = 1;
                m_Parent = ((GameObject)current).transform.parent?.gameObject;
            }
        }

        public override Vector2 GetWindowSize() => new(520, 460);
        internal void Show(Rect anchor) => PopupWindow.Show(anchor, this);

        public override void OnOpen() {
            EditorApplication.projectChanged += Changed;
            EditorApplication.hierarchyChanged += Changed;
        }

        public override void OnClose() {
            EditorApplication.projectChanged -= Changed;
            EditorApplication.hierarchyChanged -= Changed;
        }

        private void Changed() {
            m_Dirty = true;

            if (editorWindow != null) {
                editorWindow.Repaint();
            }
        }

        public override void OnGUI(Rect rect) {
            EnsureData();
            m_PathStyle ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            using var page = new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true));

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("选择处理范围", EditorStyles.boldLabel);
                GUILayout.Label(m_AllowMultiple ? "可跨目录多选" : "选择一项", EditorStyles.miniLabel);
            }

            var tab = GUILayout.Toolbar(m_Tab, new[] { "项目资源", "场景对象" }, GUILayout.Height(25));

            if (tab != m_Tab) {
                m_Tab = tab;
                m_Search = "";
                m_Scroll = Vector2.zero;
            }

            GUI.SetNextControlName("ScopeSearch");
            EditorGUI.BeginChangeCheck();
            m_Search = EditorGUILayout.TextField(m_Search, EditorStyles.toolbarSearchField);

            if (EditorGUI.EndChangeCheck()) {
                m_Scroll = Vector2.zero;
            }

            if (m_FocusSearch) {
                EditorGUI.FocusTextInControl("ScopeSearch");
                m_FocusSearch = false;
            }

            DrawLocation();

            var entries = Entries();

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_Scroll)) {
                m_Scroll = scroll.scrollPosition;

                if (entries.Length == 0) {
                    EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(m_Search)
                        ? "这里没有可选择的内容。可以返回上一级，或切换到另一类内容。"
                        : "没有找到匹配项，试试其他名称或清空搜索。", MessageType.Info);
                }

                foreach (var entry in entries) {
                    DrawEntry(entry, !string.IsNullOrWhiteSpace(m_Search));
                }
            }

            DrawSelectionFooter();
            var current = Event.current;

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape) {
                if (editorWindow != null) {
                    editorWindow.Close();
                }

                current.Use();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawLocation() {
            using var row = new EditorGUILayout.HorizontalScope();

            if (!string.IsNullOrWhiteSpace(m_Search)) {
                EditorGUILayout.LabelField(m_Tab == 0 ? "搜索整个项目中的文件夹和预制体" : "搜索已打开场景中的对象", EditorStyles.miniLabel);

                if (GUILayout.Button("清除", GUILayout.Width(44))) {
                    m_Search = "";
                    m_Scroll = Vector2.zero;
                }

                return;
            }

            using (new EditorGUI.DisabledScope(m_Tab == 0 ? m_Folder == null : m_Parent == null)) {
                if (GUILayout.Button("‹ 上一级", GUILayout.Width(70))) {
                    GoUp();
                }
            }

            var location = m_Tab == 0
                ? m_Folder ?? "Assets 与本地包"
                : m_Parent == null
                    ? "场景根对象"
                    : ComponentReplacementPlan.HierarchyPath(m_Parent.transform);
            
            GUILayout.Label(new GUIContent(location, location), EditorStyles.label, GUILayout.MinWidth(0));
        }

        private void DrawEntry(Entry entry, bool showPath) {
            var rect = GUILayoutUtility.GetRect(0, showPath ? 42 : 27, GUILayout.ExpandWidth(true));
            var selected = m_Selected.ContainsKey(entry.Key);

            if (selected) {
                EditorGUI.DrawRect(rect, new Color(0.22f, 0.48f, 0.75f, 0.22f));
            }

            var check = new Rect(rect.x + 4, rect.y + 5, 18, 18);
            var next = GUI.Toggle(check, selected, GUIContent.none);

            if (next != selected) {
                SetChecked(entry, next);
            }

            var label = new Rect(rect.x + 26, rect.y + 3, rect.width - 60, 21);
            var icon = EditorGUIUtility.IconContent(entry.folder
                ? "Folder Icon"
                : entry.gameObject != null
                    ? "GameObject Icon"
                    : "Prefab Icon").image;

            if (GUI.Button(label, new GUIContent(entry.name, icon, entry.path), EditorStyles.label)
                && Activate(entry, Event.current.clickCount)) {
                GUIUtility.ExitGUI();
            }

            if (showPath) {
                GUI.Label(new Rect(label.x, rect.y + 23, label.width, 17), entry.path, m_PathStyle);
            }

            if (entry.CanBrowse && GUI.Button(new Rect(rect.xMax - 28, rect.y + 2, 25, 23),
                    new GUIContent("›", entry.folder ? "进入文件夹；勾选左侧可选择文件夹本身" : "浏览子对象；勾选左侧可选择此对象"),
                    EditorStyles.miniButton)) {
                Navigate(entry);
            }
        }

        private void DrawSelectionFooter() {
            PruneSelection();
            GUILayout.Label($"已选 {m_Selected.Count} 项", EditorStyles.boldLabel);
            var names = string.Join("、", m_Selected.Values.Take(2).Select(value => value.name));

            if (m_Selected.Count > 2) {
                names += $" 等 {m_Selected.Count} 项";
            }

            GUILayout.Label(new GUIContent(names.Length == 0 ? "勾选要处理的内容；点击 › 浏览下一级。" : names,
                    string.Join("\n", m_Selected.Values.Select(AssetDatabase.GetAssetPath))),
                EditorStyles.miniLabel, GUILayout.MinWidth(0));
            
            using var buttons = new EditorGUILayout.HorizontalScope();

            using (new EditorGUI.DisabledScope(m_Selected.Count == 0)) {
                if (GUILayout.Button(m_AllowMultiple ? $"添加所选 {m_Selected.Count} 项" : "使用所选项", GUILayout.Height(29))) {
                    Confirm();
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("取消", GUILayout.Height(29), GUILayout.Width(70))) {
                if (editorWindow != null) {
                    editorWindow.Close();
                }

                GUIUtility.ExitGUI();
            }
        }

        internal Entry[] Entries() {
            EnsureData();
            var searching = !string.IsNullOrWhiteSpace(m_Search);
            var words = m_Search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (m_Tab == 1) {
                return m_SceneObjects.Where(value => value != null && (searching
                        ? Matches(value.scene.name + "/" + ComponentReplacementPlan.HierarchyPath(value.transform))
                        : value.transform.parent?.gameObject == m_Parent))
                    .Select(value => new Entry {
                        name = value.name,
                        gameObject = value,
                        path = value.scene.name + "/" + ComponentReplacementPlan.HierarchyPath(value.transform)
                    })
                    .OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            return m_AssetPaths.Where(path => searching
                    ? Matches(path)
                    : m_Folder == null
                        ? m_ProjectRoots.Contains(path)
                        : ParentPath(path) == m_Folder)
                .Select(path => new Entry {
                    name = m_ProjectRoots.Contains(path) && path != "Assets"
                        ? "本地包 · " + (PackageInfo.FindForAssetPath(path)?.displayName ?? LeafName(path))
                        : LeafName(path),
                    path = path,
                    assetPath = path,
                    folder = m_Folders.Contains(path)
                })
                .OrderBy(entry => !entry.folder).ThenBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            bool Matches(string text) => words.All(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>双击浏览时撤回第一次点击的勾选，避免意外把整个目录加入范围。</summary>
        internal bool Activate(Entry entry, int clickCount) {
            if (clickCount < 2) {
                m_LastClick = entry.Key;
                m_BeforeClick = new Dictionary<string, Object>(m_Selected, StringComparer.Ordinal);
                SetChecked(entry, !m_Selected.ContainsKey(entry.Key));
                return false;
            }

            if (entry.CanBrowse) {
                if (m_LastClick == entry.Key && m_BeforeClick != null) {
                    m_Selected.Clear();

                    foreach (var pair in m_BeforeClick) {
                        m_Selected.Add(pair.Key, pair.Value);
                    }
                }

                Navigate(entry);
                return false;
            }

            SetChecked(entry, true);
            Confirm();
            return true;
        }

        internal void Navigate(Entry entry) {
            if (!entry.CanBrowse) {
                return;
            }

            m_LastClick = null;
            m_BeforeClick = null;

            if (entry.folder) {
                m_Folder = entry.assetPath;
            } else {
                m_Parent = entry.gameObject;
            }

            m_Search = "";
            m_Scroll = Vector2.zero;
        }

        internal void GoUp() {
            m_LastClick = null;
            m_BeforeClick = null;

            if (m_Tab == 0) {
                m_Folder = m_Folder == null || m_ProjectRoots.Contains(m_Folder) ? null : ParentPath(m_Folder);
            } else {
                m_Parent = m_Parent != null ? m_Parent.transform.parent?.gameObject : null;
            }

            m_Search = "";
            m_Scroll = Vector2.zero;
        }

        internal void SetChecked(Entry entry, bool selected) {
            if (!selected) {
                m_Selected.Remove(entry.Key);
                return;
            }

            var value = entry.Resolve();

            if (value == null) {
                return;
            }

            if (!m_AllowMultiple) {
                m_Selected.Clear();
            }

            m_Selected[entry.Key] = value;
        }

        internal void Confirm() {
            PruneSelection();
            var values = m_Selected.Values.ToArray();

            if (values.Length == 0) {
                return;
            }

            if (editorWindow != null) {
                editorWindow.Close();
            }

            foreach (var value in values) {
                m_Select(value);
            }
        }

        private void EnsureData() {
            if (!m_Dirty) {
                return;
            }

            m_Dirty = false;
            m_ProjectRoots = PackageInfo.GetAllRegisteredPackages().Where(package =>
                    package.source is PackageSource.Local or PackageSource.Embedded)
                .Select(package => package.assetPath).Append("Assets").ToHashSet(StringComparer.Ordinal);
            m_Folders.Clear();
            m_AssetPaths = AssetDatabase.GetAllAssetPaths().Where(path => m_ProjectRoots.Contains(ProjectRoot(path)))
                .Where(path => {
                    if (AssetDatabase.IsValidFolder(path)) {
                        m_Folders.Add(path);
                        return true;
                    }

                    return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                }).ToArray();
            m_SceneObjects = ComponentReplacement.ContextObjects().Where(value => Normalize(value) != null).ToArray();

            if (m_Folder != null && !m_Folders.Contains(m_Folder)) {
                m_Folder = "Assets";
            }

            PruneSelection();
        }

        private void PruneSelection() {
            var values = m_Selected.Values.Select(Normalize).Where(value => value != null).Distinct().ToArray();
            m_Selected.Clear();

            foreach (var value in values) {
                m_Selected[Key(value)] = value;
            }
        }

        private static string Key(Object value) =>
            EditorUtility.IsPersistent(value)
                ? $"asset:{AssetDatabase.GetAssetPath(value)}" : $"scene:{value.GetInstanceID()}";

        private static string ParentPath(string path) {
            var index = path.LastIndexOf('/');
            return index < 0 ? null : path.Substring(0, index);
        }

        private static string LeafName(string path) => path.Substring(path.LastIndexOf('/') + 1);

        private static string ProjectRoot(string path) {
            if (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) {
                return "Assets";
            }

            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) {
                return "";
            }

            var index = path.IndexOf('/', "Packages/".Length);
            return index < 0 ? path : path.Substring(0, index);
        }

        internal static Object Normalize(Object value) {
            if (value is Component component) {
                value = component.gameObject;
            }

            if (value == null) {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(value);

            if (!string.IsNullOrEmpty(path)) {
                var root = ProjectRoot(path);

                if (root == "") {
                    return null;
                }

                if (root != "Assets") {
                    var package = PackageInfo.FindForAssetPath(path);

                    if (package == null || package.source != PackageSource.Local
                        && package.source != PackageSource.Embedded) {
                        return null;
                    }
                }

                if (AssetDatabase.IsValidFolder(path)) {
                    return value;
                }
            }

            if (value is not GameObject gameObject || (gameObject.hideFlags & HideFlags.NotEditable) != 0) {
                return null;
            }

            if (EditorUtility.IsPersistent(gameObject)) {
                return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? gameObject : null;
            }

            return gameObject.scene.IsValid() && gameObject.scene.isLoaded ? gameObject : null;
        }
    }
}