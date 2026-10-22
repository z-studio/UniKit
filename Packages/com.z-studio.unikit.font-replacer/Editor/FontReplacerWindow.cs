using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 编辑器交互入口，只负责参数、预览展示和操作反馈，不直接遍历或修改字体组件。
    /// 主流程：DrawOptions 收集参数 → Scan 生成快照 → DrawPreview 调整勾选 → Apply 执行。
    /// 参数/选择/Undo 改变会使快照失效；搜索、翻页和说明折叠仅改变展示，不重新扫描。
    /// </summary>
    internal sealed class FontReplacerWindow : EditorWindow {
        private const int k_PageSize = 50;

        // 延迟到 OnGUI 创建，沿用 Unity 当前皮肤；统一各区域的内边距与说明文字样式。
        private static class Styles {
            internal static readonly GUIStyle Page = new() { padding = new RectOffset(10, 10, 8, 8) };

            internal static readonly GUIStyle Section = new(EditorStyles.helpBox) {
                padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset()
            };

            internal static readonly GUIStyle Notes = new(EditorStyles.wordWrappedMiniLabel) {
                padding = new RectOffset(), margin = new RectOffset(0, 0, 2, 0)
            };

            internal static readonly GUIStyle AssetGroup = new(EditorStyles.helpBox) {
                padding = new RectOffset(6, 6, 4, 4), margin = new RectOffset()
            };

            internal static readonly GUIStyle AssetTitle = new(EditorStyles.label) {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(),
                margin = new RectOffset()
            };

            internal static readonly GUIStyle AssetCount = new(AssetTitle) {
                fontStyle = FontStyle.Normal, normal = { textColor = EditorStyles.miniLabel.normal.textColor }
            };

            internal static readonly GUIStyle DropArea = new(EditorStyles.centeredGreyMiniLabel) {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 11,
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        // 持久化的是输入与展示开关，让脚本重载后仍能恢复窗口配置。
        [SerializeField]
        private FontReplacementOptions m_Options = new();

        // 两个字体页签各自保存参数；切换页签不再丢失已选择的字体、材质和处理范围。
        [SerializeField]
        private FontReplacementOptions m_OtherOptions = new() { kind = FontReplacementKind.UnityTextAndTextMesh };

        [SerializeField]
        private bool m_ShowNotes;

        [SerializeField]
        private bool m_ShowExclusions;

        [SerializeField]
        private bool m_UseCustomMaterial;

        // 预览不序列化：其中含场景对象引用和磁盘快照，重载后应重新建立，不能当作永久配置。
        private FontReplacementPreview m_Preview;

        private Vector2 m_SettingsScroll;

        // 使用实际内容高度决定视口，避免 ExpandHeight(false) 的最小高度估算制造假溢出。
        private float m_SettingsContentHeight;
        private float m_ActionsHeight = 60;

        private Vector2 m_PreviewScroll;

        // 搜索和页码只作用于当前预览的显示；实际执行仍读取全部条目的 selected。
        private string m_Search = "";
        private int m_Page;
        private Object m_FontFilter;
        private bool m_UseFontFilter;
        private string m_Message;
        private MessageType m_MessageType;
        private double m_NextProgress;

        [MenuItem("Tools/UniKit/Font Replacer", priority = 830)]
        private static void Open() => GetWindow<FontReplacerWindow>("Font Replacer");

        private void OnEnable() {
            minSize = new Vector2(460, 400);

            // 由已保存的材质引用恢复开关，避免“隐藏了自定义材质但仍在使用它”。
            m_UseCustomMaterial = m_Options.targetMaterial != null;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable() {
            // 编辑器事件是静态订阅，窗口关闭时解除，避免已关闭窗口继续接收回调。
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnSelectionChanged() {
            // 只有 Selection 范围把当前选择作为输入；切换 Project 选择不影响其他范围的计划。
            if (m_Options.scope == FontReplacementScope.SelectedHierarchyObjectsAndChildren && m_Preview != null) {
                InvalidatePreview("选择已改变，请重新扫描。");
            }

            Repaint();
        }

        private void OnUndoRedo() {
            // 原字体/材质可能随 Undo 改变，即使窗口参数相同，也不能继续使用旧预览。
            InvalidatePreview("已撤销或重做，请重新扫描检查当前字体。");
            Repaint();
        }

        /// <summary>统一退出“已有计划”状态，清除旧分页与反馈；不清空用户已填好的参数。</summary>
        private void InvalidatePreview(string message = null) {
            m_Preview = null;
            m_Page = 0;
            m_Message = message;
            m_MessageType = MessageType.Info;
        }

        private void OnGUI() {
            using (new EditorGUILayout.VerticalScope(Styles.Page)) {
                var messageHeight = string.IsNullOrEmpty(m_Message) ? 0 :
                    EditorStyles.helpBox.CalcHeight(new GUIContent(m_Message), position.width - 20) + 4;
                var availableHeight = Mathf.Max(80, position.height - m_ActionsHeight - messageHeight - 28);

                if (m_Preview != null) {
                    // 有预览时至少为结果保留一块可操作区域。
                    availableHeight = Mathf.Max(80, Mathf.Min(availableHeight - 120, position.height * 0.5f));
                }

                var contentHeight = m_SettingsContentHeight > 0 ? m_SettingsContentHeight : availableHeight;
                var viewportHeight = Mathf.Min(contentHeight + 2, availableHeight);
                var needsScroll = contentHeight > viewportHeight;

                if (!needsScroll) {
                    m_SettingsScroll = Vector2.zero;
                }

                // 显式内容边界，避免 GUILayout 滚动容器再次用文字的最小高度估算溢出。
                var viewport = GUILayoutUtility.GetRect(position.width - 20, viewportHeight);
                var contentWidth = viewport.width - (needsScroll ? GUI.skin.verticalScrollbar.fixedWidth + 2 : 0);
                m_SettingsScroll = GUI.BeginScrollView(viewport, m_SettingsScroll,
                    new Rect(0, 0, contentWidth, contentHeight));
                GUILayout.BeginArea(new Rect(0, 0, contentWidth, contentHeight));

                using (var content = new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(false))) {
                    // 只监视参数；说明折叠、搜索和勾选不应误触发重新扫描。
                    EditorGUI.BeginChangeCheck();
                    DrawOptions();

                    if (EditorGUI.EndChangeCheck()) {
                        InvalidatePreview();
                    }

                    if (Event.current.type == EventType.Repaint &&
                        Mathf.Abs(m_SettingsContentHeight - content.rect.height) > 0.5f) {
                        m_SettingsContentHeight = content.rect.height;
                        Repaint();
                    }
                }

                GUILayout.EndArea();
                GUI.EndScrollView();

                EditorGUILayout.Space(6);

                if (!string.IsNullOrEmpty(m_Message)) {
                    EditorGUILayout.HelpBox(m_Message, m_MessageType);
                }

                if (m_Preview != null) {
                    DrawPreview();
                }

                GUILayout.FlexibleSpace();

                using (var footer = new EditorGUILayout.VerticalScope()) {
                    var separator = GUILayoutUtility.GetRect(position.width - 20, 1);
                    EditorGUI.DrawRect(separator, new Color(0, 0, 0, EditorGUIUtility.isProSkin ? 0.3f : 0.15f));

                    if (m_ShowNotes) {
                        EditorGUILayout.Space(6);
                        DrawNotes();
                    }

                    EditorGUILayout.Space(6);
                    DrawActions();

                    if (Event.current.type == EventType.Repaint &&
                        Mathf.Abs(m_ActionsHeight - footer.rect.height) > 0.5f) {
                        m_ActionsHeight = footer.rect.height;
                        Repaint();
                    }
                }
            }
        }

        /// <summary>按字体类型、替换方式和范围显示必要字段，并维护选项之间的依赖关系。</summary>
        private void DrawOptions() {
            var kind = (FontReplacementKind)GUILayout.Toolbar((int)m_Options.kind,
                new[] { "TextMeshPro", "Unity Text / TextMesh" }, GUILayout.Height(22));

            if (kind != m_Options.kind) {
                (m_Options, m_OtherOptions) = (m_OtherOptions, m_Options);
                m_Options.kind = kind;
                m_UseCustomMaterial = m_Options.targetMaterial != null;
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(Styles.Section)) {
                DrawFontOptions();
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(Styles.Section)) {
                DrawScopeOptions();
            }
        }

        private void DrawFontOptions() {
            EditorGUILayout.LabelField("1. 选择字体", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            // 显示顺序与枚举数值分开，调整常用选项顺序不会改变已序列化的枚举含义。
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label("替换方式", GUILayout.Width(54));
                m_Options.filter = (FontReplacementFilter)EditorGUILayout.IntPopup((int)m_Options.filter,
                    new[] { "只替换源字体 → 目标字体", "所有字体 → 目标字体", "未设置或已丢失的字体 → 目标字体" },
                    new[] {
                        (int)FontReplacementFilter.ReplaceSourceFontOnly, (int)FontReplacementFilter.ReplaceAllFonts,
                        (int)FontReplacementFilter.ReplaceUnassignedOrMissingFonts
                    });
            }

            EditorGUILayout.Space(3);
            var type = m_Options.kind == FontReplacementKind.TextMeshPro ? typeof(TMP_FontAsset) : typeof(Font);

            Object target;

            // 所有行使用相同的显式标签宽度，避免 ObjectField 内置标签与独立 Label 的边距差异。
            using (new EditorGUILayout.HorizontalScope()) {
                if (m_Options.filter == FontReplacementFilter.ReplaceSourceFontOnly) {
                    GUILayout.Label(new GUIContent("源字体", "被替换的字体；只处理使用它的组件。TMP 请选择 .asset 字体资产。"),
                        GUILayout.Width(54));
                    m_Options.sourceFont = EditorGUILayout.ObjectField(m_Options.sourceFont, type, false);
                    GUILayout.Label("→", GUILayout.Width(16));
                }

                GUILayout.Label(new GUIContent("目标字体", "替换后使用的字体。TMP 请选择 TMP Font Asset（.asset），不能直接使用 .ttf/.otf。"),
                    GUILayout.Width(54));
                target = EditorGUILayout.ObjectField(m_Options.targetFont, type, false);
            }

            if (target != m_Options.targetFont) {
                m_Options.targetFont = target;

                // 字体改变后，旧 preset 不再适合新图集，回到新字体的默认材质。
                m_Options.targetMaterial = null;
                m_UseCustomMaterial = false;
            }

            if (m_Options.kind == FontReplacementKind.TextMeshPro && m_Options.targetFont != null) {
                EditorGUILayout.Space(3);
                m_UseCustomMaterial = EditorGUILayout.ToggleLeft(
                    new GUIContent("指定目标材质", "默认使用目标字体材质；需要描边、阴影等样式时，指定该字体的 Material Preset。"),
                    m_UseCustomMaterial);

                if (m_UseCustomMaterial) {
                    EditorGUILayout.Space(2);

                    using (new EditorGUILayout.HorizontalScope()) {
                        GUILayout.Label("材质预设", GUILayout.Width(54));
                        m_Options.targetMaterial = (Material)EditorGUILayout.ObjectField(
                            m_Options.targetMaterial, typeof(Material), false);
                    }
                } else {
                    // 关闭开关必须同时清空执行参数，不能只把材质输入框藏起来。
                    m_Options.targetMaterial = null;
                }
            }
        }

        private void DrawScopeOptions() {
            EditorGUILayout.LabelField("2. 选择处理范围", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);

            // 下拉框按常用操作排列，与枚举声明顺序独立。
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label("替换范围", GUILayout.Width(54));
                m_Options.scope = (FontReplacementScope)EditorGUILayout.IntPopup((int)m_Options.scope,
                    new[] { "Project 资源（文件夹含子文件夹）", "Hierarchy 选中的物体及子物体" },
                    new[] {
                        (int)FontReplacementScope.ProjectAssetsIncludingSubfolders,
                        (int)FontReplacementScope.SelectedHierarchyObjectsAndChildren
                    });
            }

            switch (m_Options.scope) {
                case FontReplacementScope.SelectedHierarchyObjectsAndChildren: {
                    EditorGUILayout.Space(3);
                    var count = Selection.gameObjects.Count(go => !EditorUtility.IsPersistent(go));
                    EditorGUILayout.LabelField("当前选择", $"{count} 个场景物体（包含子节点，重叠项自动去重）");
                    break;
                }
                
                case FontReplacementScope.ProjectAssetsIncludingSubfolders: {
                    EditorGUILayout.Space(3);
                    
                    // 共用同一行 Rect，避免 GUILayout 对不同内容分别计算高度，造成复选框上下错位。
                    var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(new Rect(row.x, row.y, 54, row.height), "扫描选项");
                    m_Options.includePrefabs = EditorGUI.ToggleLeft(new Rect(row.x + 58, row.y, 74, row.height),
                        "预制体", m_Options.includePrefabs);
                    m_Options.includeScenes = EditorGUI.ToggleLeft(new Rect(row.x + 132, row.y, 60, row.height),
                        "场景", m_Options.includeScenes);
                    
                    // 排除作为扫描选项的低频入口；收起时不再占用独立列表栏。
                    var changed = GUI.changed;
                    var excludedCount = m_Options.excludedAssets.Count(asset => asset != null);
                    m_ShowExclusions = GUI.Toggle(new Rect(row.xMax - 104, row.y, 104, row.height),
                        m_ShowExclusions, new GUIContent($"排除资源 {excludedCount}", "编辑扫描时需要跳过的资源。"),
                        EditorStyles.miniButton);
                    GUI.changed = changed;
                    EditorGUILayout.Space(3);
                    DrawAssets(m_Options.assets, "添加", true);

                    if (m_ShowExclusions) {
                        EditorGUILayout.Space(3);

                        using (new EditorGUILayout.VerticalScope(Styles.AssetGroup)) {
                            using (new EditorGUILayout.HorizontalScope()) {
                                DrawAssetHeading("排除资源", excludedCount);
                                DrawAssetButtons(m_Options.excludedAssets, "添加", false);
                            }

                            DrawAssets(m_Options.excludedAssets, "添加");
                        }
                    }

                    break;
                }
            }
        }

        // 纯展示区域，放在参数的 Begin/EndChangeCheck 之外，展开说明不会让预览失效。
        private void DrawNotes() {
            using (new EditorGUILayout.VerticalScope(Styles.AssetGroup)) {
                // 合并为一个按正文实际行高排版的文本块，避免每条 LabelField 的最小高度叠加。
                var text = m_Options.kind == FontReplacementKind.TextMeshPro
                    ? "材质：默认使用目标字体材质；描边、阴影等样式请指定目标 Material Preset。\n"
                    : "";
                text += "保存：已打开场景支持 Undo，需手动保存；其他资产直接保存，使用版本控制回退。\n" +
                        "继承：修改源预制体可能影响范围外的实例，排除项也遵循 Unity 的继承规则。";
                GUILayout.Label(text, Styles.Notes);
            }
        }

        /// <summary>窗口侧统一判断拖放/选择的对象是否属于可扫描范围；不据此保证资产可写。</summary>
        private static bool IsScopeAsset(Object asset) {
            return FontReplacementOptions.IsScopeAsset(asset);
        }

        private static void AddAssets(List<Object> list, Object[] assets) {
            // 多选加入时先过滤不支持的对象，再按引用去重；文件夹重叠交由 Scanner 展开后去重。
            foreach (var asset in assets.Where(IsScopeAsset)) {
                if (!list.Contains(asset)) {
                    list.Add(asset);
                }
            }

            // 按钮/拖放直接修改 List，显式通知外层 ChangeCheck 使旧计划失效。
            GUI.changed = true;
        }

        private static void DrawAssets(List<Object> list, string addLabel, bool allowEntireAssetsFolder = false) {
            Rect dropRect;

            // 标题、内容、底部工具栏保持一致；整个列表都可拖入资源。
            using (var group =
                   new EditorGUILayout.VerticalScope(allowEntireAssetsFolder ? Styles.AssetGroup : GUIStyle.none)) {
                if (allowEntireAssetsFolder) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        DrawAssetHeading("扫描资源", list.Count(asset => asset != null));
                        DrawAssetButtons(list, addLabel, allowEntireAssetsFolder);
                    }
                }

                for (var i = 0; i < list.Count; i++) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        list[i] = EditorGUILayout.ObjectField(list[i], typeof(Object), false, GUILayout.Height(20));

                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(20))) {
                            // 移除后下一个元素前移，回退索引，避免本轮跳过它。
                            list.RemoveAt(i--);
                            GUI.changed = true;
                        }
                    }
                }

                if (list.Count == 0) {
                    GUILayout.Label(allowEntireAssetsFolder ? "拖入需要扫描的资源" : "拖入需要跳过的资源（可选）",
                        Styles.DropArea, GUILayout.Height(24));
                }

                dropRect = group.rect;
            }

            var current = Event.current;

            if (!dropRect.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)) {
                return;
            }

            var accepted = DragAndDrop.objectReferences.Any(IsScopeAsset);
            // DragUpdated 只反馈能否放入，真正修改列表要等 DragPerform，防止拖动中重复添加。
            DragAndDrop.visualMode = accepted ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (current.type == EventType.DragPerform && accepted) {
                DragAndDrop.AcceptDrag();
                AddAssets(list, DragAndDrop.objectReferences);
            }

            current.Use();
        }

        // 标题与数量共用字体大小、行高和垂直对齐，仅用字重与颜色区分层级。
        private static void DrawAssetHeading(string title, int count) {
            var countText = $"{count} 项";
            var titleWidth = Styles.AssetTitle.CalcSize(new GUIContent(title)).x;
            var countWidth = Styles.AssetCount.CalcSize(new GUIContent(countText)).x;
            var row = GUILayoutUtility.GetRect(titleWidth + 6 + countWidth, 20);
            GUI.Label(new Rect(row.x, row.y, titleWidth, row.height), title, Styles.AssetTitle);
            GUI.Label(new Rect(row.x + titleWidth + 6, row.y, countWidth, row.height), countText, Styles.AssetCount);
        }

        private static void DrawAssetButtons(List<Object> list, string addLabel, bool allowEntireAssetsFolder) {
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(addLabel, EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(20))) {
                list.Add(null);
                GUI.changed = true;
            }

            using (new EditorGUI.DisabledScope(!Selection.objects.Any(IsScopeAsset))) {
                if (GUILayout.Button(new GUIContent("加入选中", "加入 Project 中选中的场景、预制体和文件夹。"),
                        EditorStyles.miniButton, GUILayout.Width(82), GUILayout.Height(20))) {
                    AddAssets(list, Selection.objects);
                }
            }

            if (allowEntireAssetsFolder && GUILayout.Button(
                    new GUIContent("整个 Assets", "用 Assets 根目录替换扫描列表，递归扫描其中的场景和预制体。"),
                    EditorStyles.miniButton, GUILayout.Width(82), GUILayout.Height(20))) {
                list.Clear();
                list.Add(AssetDatabase.LoadMainAssetAtPath("Assets"));
                GUI.changed = true;
            }
        }

        /// <summary>
        /// 窗口侧预检：先检查编辑器状态，再复用公共参数校验，最后检查窗口独有开关和当前选择。
        /// 这用于提前解释按钮为何不可用；Scanner / Applier 仍会各自校验，不能只依赖 UI。
        /// </summary>
        private string GetValidationMessage() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                return "请退出播放模式后操作。";
            }

            if (EditorApplication.isCompiling) {
                return "等待脚本编译完成后操作。";
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null) {
                return "请先保存并关闭 Prefab Mode。";
            }

            var error = m_Options.Validate();

            if (error != null) {
                return error;
            }

            if (m_Options.kind == FontReplacementKind.TextMeshPro && m_UseCustomMaterial
                                                                  && m_Options.targetMaterial == null) {
                return "请选择目标 Material Preset，或关闭“指定目标材质”使用默认材质。";
            }

            if (m_Options.scope == FontReplacementScope.SelectedHierarchyObjectsAndChildren &&
                !Selection.gameObjects.Any(go => !EditorUtility.IsPersistent(go))) {
                return "请在 Hierarchy 中选择场景物体。";
            }

            return null;
        }

        /// <summary>固定底部操作区：基于全部勾选项计算数量和保存边界，不受搜索与分页限制。</summary>
        private void DrawActions() {
            var error = GetValidationMessage();
            var selected = m_Preview?.entries.Where(entry => entry.selected).ToArray();

            using (new EditorGUILayout.HorizontalScope()) {
                m_ShowNotes = GUILayout.Toggle(m_ShowNotes, "使用说明", GUI.skin.button,
                    GUILayout.Width(66), GUILayout.Height(28));

                if (selected != null) {
                    var live = selected.Count(entry => entry.isLive);
                    var disk = selected.Length - live;
                    GUILayout.Label(new GUIContent($"场景 {live} · 资产 {disk}",
                            "场景内修改支持 Undo，需手动保存；磁盘资产直接保存。"), Styles.AssetCount,
                        GUILayout.Height(28));
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(error != null)) {
                    if (GUILayout.Button(new GUIContent(m_Preview == null ? "扫描预览" : "重新扫描", error),
                            GUILayout.Width(m_Preview == null ? 120 : 88), GUILayout.Height(28))) {
                        Scan();
                        // 扫描改变预览结构，留到下一轮 Layout 再绘制。
                        GUIUtility.ExitGUI();
                    }
                }

                if (m_Preview != null) {
                    using (new EditorGUI.DisabledScope(error != null || selected.Length == 0)) {
                        if (GUILayout.Button(new GUIContent($"替换所选（{selected.Length}）", error),
                                GUILayout.Width(144), GUILayout.Height(28))) {
                            Apply();
                        }
                    }
                }
            }
        }

        private void Scan() {
            // 先清除旧快照及显示筛选；新扫描失败或取消时，不能让旧结果继续看起来可执行。
            InvalidatePreview();
            m_Search = "";
            m_UseFontFilter = false;
            m_FontFilter = null;
            m_PreviewScroll = Vector2.zero;

            try {
                m_NextProgress = 0;
                m_Preview = FontReplacementScanner.Scan(m_Options, Progress);

                if (m_Preview.issues.Count > 0) {
                    // 窗口只展示少量摘要以免挤占列表，完整问题逐条写入 Console。
                    m_Message = string.Join("\n", m_Preview.issues.Take(3));
                    m_MessageType = MessageType.Warning;

                    foreach (var issue in m_Preview.issues) {
                        Debug.LogWarning("Font Replacer: " + issue);
                    }
                }
            } catch (OperationCanceledException) {
                m_Message = "扫描已取消，未修改任何字体。";
                m_MessageType = MessageType.Info;
            } catch (Exception exception) {
                m_Message = exception.Message;
                m_MessageType = MessageType.Error;
            } finally {
                // 进度条是编辑器全局 UI，所有结束路径都必须关闭。
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>展示已有快照，不做资产扫描；按“搜索匹配 → 跨页勾选 → 当前页绘制”处理。</summary>
        private void DrawPreview() {
            var entries = m_Preview.entries;
            EditorGUILayout.LabelField($"3. 检查替换清单（{entries.Count} 项）", EditorStyles.boldLabel);

            if (entries.Count == 0) {
                var message = m_Options.filter switch {
                    FontReplacementFilter.ReplaceSourceFontOnly => "当前范围未发现使用源字体的组件，请检查源字体与范围。",
                    FontReplacementFilter.ReplaceUnassignedOrMissingFonts => "当前范围没有未设置字体或字体已丢失的组件。TMP 可能已自动使用默认字体。",
                    _ => "当前范围没有需要替换的组件，已使用目标字体的组件会自动跳过。"
                };
                EditorGUILayout.HelpBox(message, MessageType.Info);
                GUILayout.FlexibleSpace();
                return;
            }

            EditorGUI.BeginChangeCheck();

            // 按原字体资产分组而非名字，避免同名字体被误认为同一来源。
            var fonts = entries.GroupBy(entry => entry.OriginalFont)
                .OrderBy(group => group.Key != null ? group.Key.name : "").ToArray();

            if (fonts.Length > 1) {
                var choices = new[] { "全部原字体" }.Concat(fonts.Select(group => {
                    var groupName = group.Key != null ? group.Key.name : "未指定 / 丢失";
                    var path = group.Key != null ? AssetDatabase.GetAssetPath(group.Key) : "";
                    return $"{groupName}（{group.Count()} 项） {path}";
                })).ToArray();
                
                var index = m_UseFontFilter ? Array.FindIndex(fonts, group => group.Key == m_FontFilter) + 1 : 0;
                index = EditorGUILayout.Popup("原字体筛选", index, choices);
                m_UseFontFilter = index > 0;
                m_FontFilter = index > 0 ? fonts[index - 1].Key : null;
            } else {
                m_UseFontFilter = false;
            }

            using (new EditorGUILayout.HorizontalScope()) {
                m_Search = EditorGUILayout.TextField(new GUIContent("搜索", "对象路径、资产路径、组件类型或原字体名称"), m_Search);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_Search))) {
                    if (GUILayout.Button("清空", GUILayout.Width(44))) {
                        m_Search = "";
                        GUI.changed = true;
                    }
                }
            }

            if (EditorGUI.EndChangeCheck()) {
                // 搜索范围改变后从第一页顶部开始，避免保留旧页码而出现空白或越界页。
                m_Page = 0;
                m_PreviewScroll = Vector2.zero;
            }

            // matches 包含所有搜索匹配页，selected 则包含整个计划，两个集合不能混为一谈。
            var matches = entries.Where(MatchesPreviewFilter).ToArray();
            var selected = entries.Count(entry => entry.selected);
            var matchedSelected = matches.Count(entry => entry.selected);

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField($"匹配 {matches.Length} 项 / 总计 {entries.Count} 项");

                using (new EditorGUI.DisabledScope(matches.Length == 0)) {
                    if (GUILayout.Button(new GUIContent("只选当前筛选", "仅勾选所有匹配页中的组件，同时取消筛选外的勾选。"), GUILayout.Width(100))) {
                        SelectOnlyMatchingEntries();
                    }

                    if (GUILayout.Button("取消匹配项", GUILayout.Width(92))) {
                        SetMatchingEntriesSelected(false);
                    }
                }

                using (new EditorGUI.DisabledScope(selected == 0)) {
                    if (GUILayout.Button("全部取消", GUILayout.Width(76))) {
                        foreach (var entry in entries) {
                            entry.selected = false;
                        }
                    }
                }
            }

            // 搜索隐藏结果不会取消勾选；明确提示隐藏的选择，避免用户误以为只替换当前显示项。
            var hiddenSelected = selected - matchedSelected;

            if (hiddenSelected > 0) {
                EditorGUILayout.HelpBox($"筛选外还有 {hiddenSelected} 个已勾选项，执行时也会替换。可点击“只选当前筛选”仅处理匹配项。", MessageType.Info);
            }

            // 向上取整并至少保留一页；分页只限制 IMGUI 绘制成本，不截断预览和执行数据。
            var pages = Math.Max(1, (matches.Length + k_PageSize - 1) / k_PageSize);
            m_Page = Mathf.Clamp(m_Page, 0, pages - 1);

            if (pages > 1) {
                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUI.DisabledScope(m_Page == 0)) {
                        if (GUILayout.Button("上一页")) {
                            m_Page--;
                            m_PreviewScroll = Vector2.zero;
                        }
                    }

                    var page = EditorGUILayout.DelayedIntField(m_Page + 1, GUILayout.Width(50));

                    if (page != m_Page + 1) {
                        m_Page = Mathf.Clamp(page - 1, 0, pages - 1);
                        m_PreviewScroll = Vector2.zero;
                    }

                    GUILayout.Label($"/ {pages} 页（每页 {k_PageSize} 项）", EditorStyles.centeredGreyMiniLabel);

                    using (new EditorGUI.DisabledScope(m_Page == pages - 1)) {
                        if (GUILayout.Button("下一页")) {
                            m_Page++;
                            m_PreviewScroll = Vector2.zero;
                        }
                    }
                }
            }

            m_PreviewScroll = EditorGUILayout.BeginScrollView(m_PreviewScroll, GUILayout.ExpandHeight(true));

            if (matches.Length == 0) {
                EditorGUILayout.HelpBox("没有筛选匹配项，可清空搜索或切换到“全部原字体”。", MessageType.Info);
            }

            foreach (var entry in matches.Skip(m_Page * k_PageSize).Take(k_PageSize)) {
                DrawEntry(entry);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(FontReplacementEntry entry) {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                using (new EditorGUILayout.VerticalScope()) {
                    entry.selected = EditorGUILayout.ToggleLeft(new GUIContent(entry.ObjectPath, entry.ObjectPath),
                        entry.selected, EditorStyles.boldLabel);
                    var font = entry.OriginalFont != null ? entry.OriginalFont.name : "未指定 / 丢失";
                    var targetName = m_Preview.options.targetFont != null
                        ? m_Preview.options.targetFont.name : "目标字体已失效";
                    EditorGUILayout.LabelField(new GUIContent(
                            $"{entry.ComponentType.Split('.').Last()} · {font} → {targetName}",
                            $"原字体：{AssetDatabase.GetAssetPath(entry.OriginalFont)}\n目标字体：{AssetDatabase.GetAssetPath(m_Preview.options.targetFont)}"),
                        EditorStyles.miniLabel);
                    var path = string.IsNullOrEmpty(entry.AssetPath) ? "未保存场景" : entry.AssetPath;
                    EditorGUILayout.LabelField(new GUIContent(path, path), EditorStyles.miniLabel);
                }

                // 翻页和重绘只查路径是否存在；用户点击定位时才加载资产，避免为了按钮加载整页 Prefab。
                var canLocate = entry.isLive ? entry.liveComponent != null
                    : !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.AssetPath));

                using (new EditorGUI.DisabledScope(!canLocate)) {
                    if (GUILayout.Button(new GUIContent("定位", "在 Hierarchy 或 Project 中突出显示对象"), GUILayout.Width(44))) {
                        // 只高亮、不修改 Selection，避免定位操作反过来使 Selection 范围的预览失效。
                        var target = entry.isLive ? (Object)entry.liveComponent
                            : AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);

                        if (target != null) {
                            EditorGUIUtility.PingObject(target);
                        }
                    }
                }
            }
        }

        // 纯展示筛选，不修改 Scanner 已收集的候选集合，也不隐式改变 selected。
        private bool MatchesPreviewFilter(FontReplacementEntry entry) {
            if (m_UseFontFilter && entry.OriginalFont != m_FontFilter) {
                return false;
            }

            var fontName = entry.OriginalFont != null ? entry.OriginalFont.name : "";
            return string.IsNullOrWhiteSpace(m_Search) ||
                   $"{entry.AssetPath} {entry.ObjectPath} {entry.ComponentType} {fontName}"
                       .IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 明确的“仅选择筛选结果”：跨页生效，同时清除筛选外的选择。
        private void SelectOnlyMatchingEntries() {
            foreach (var entry in m_Preview.entries) {
                entry.selected = MatchesPreviewFilter(entry);
            }
        }

        private void SetMatchingEntriesSelected(bool selected) {
            // “匹配项”包含所有匹配页，而非仅当前页；其他搜索结果保留原勾选状态。
            foreach (var entry in m_Preview.entries.Where(MatchesPreviewFilter)) {
                entry.selected = selected;
            }
        }

        private void Apply() {
            // 执行前固定总勾选数，用于取消后计算尚未处理的条目；实际改写由 Applier 再校验。
            var selected = m_Preview.entries.Where(entry => entry.selected).ToArray();

            try {
                m_NextProgress = 0;
                var result = FontReplacementApplier.Apply(m_Preview, Progress);
                m_Message = $"{(result.Cancelled ? "已取消，已完成的修改保留。" : "替换完成。")} " +
                            $"直接修改 {result.Changed} 项，已继承/已更新 {result.AlreadyUpdated} 项，" +
                            $"其他跳过 {result.Skipped - result.AlreadyUpdated} 项。";

                if (result.Cancelled) {
                    m_Message += $"未处理 {selected.Length - result.Changed - result.Skipped} 项。";
                }

                if (result.Skipped > result.AlreadyUpdated) {
                    m_Message += "\n其他跳过项已变化或无法写入，可重新扫描；具体原因见下方摘要及 Console。";
                }

                if (result.SceneChanges > 0) {
                    // 根据实际成功修改的类型提示，避免仅处理磁盘资产也误报“场景尚未保存”。
                    m_Message += $"\n场景内修改 {result.SceneChanges} 项，可 Undo，请手动保存。";
                }

                if (result.AssetChanges > 0) {
                    m_Message += $"\n磁盘资产修改 {result.AssetChanges} 项，已保存。";
                }

                if (result.issues.Count > 0) {
                    m_Message += "\n" + string.Join("\n", result.issues.Take(3));
                }

                foreach (var issue in result.issues) {
                    Debug.LogWarning("Font Replacer: " + issue);
                }

                m_MessageType = result.Cancelled || result.issues.Count > 0 ? MessageType.Warning : MessageType.Info;
            } catch (Exception exception) {
                m_Message = $"替换中断：{exception.Message}。请检查已完成的修改并重新扫描。";
                m_MessageType = MessageType.Error;
            } finally {
                // 成功、取消或失败都可能已改变一部分对象，旧快照一律作废，后续操作必须重扫。
                m_Preview = null;
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>统一桥接扫描/应用的进度回调，最多约每 50ms 刷新一次，避免节点遍历频繁重绘。</summary>
        private void Progress(string path, float progress) {
            if (EditorApplication.timeSinceStartup < m_NextProgress) {
                return;
            }

            m_NextProgress = EditorApplication.timeSinceStartup + 0.05;

            if (EditorUtility.DisplayCancelableProgressBar("Font Replacer", path, progress)) {
                // 以异常把取消传回服务层；扫描放弃计划，应用保留已完成的修改，不在窗口内吞掉取消。
                throw new OperationCanceledException();
            }
        }
    }
}