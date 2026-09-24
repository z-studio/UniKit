using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    /// <summary>参数与预览共用切割结果；更改参数后重算，手动排除只影响当前方案。</summary>
    internal sealed class TextureSlicerWindow : EditorWindow {
        [SerializeField] private Texture2D m_Source;
        [SerializeField] private TextureSliceOptions m_Options = new();
        [SerializeField] private bool m_ExportSprite = true;
        [SerializeField] private bool m_Manifest = true;
        [SerializeField] private string m_Prefix = "";

        private Texture2D m_Readable;
        private Color32[] m_Pixels;
        private List<TextureSliceTile> m_Tiles;
        private Vector2 m_SettingsScroll;
        private Vector2 m_PreviewScroll;
        private float m_Zoom = 1;
        private bool m_Fit = true;
        private bool m_ReadDirty = true;
        private bool m_LayoutDirty = true;
        private bool m_ShowRestoreSelection;
        private double m_RebuildAfter;
        private Hash128 m_Dependency;
        private string m_Note;
        private string m_Error;
        private string m_Result;
        private string m_LastOutput;

        [MenuItem("Tools/UniKit/Texture Slicer", priority = 820)]
        private static void Open() {
            var window = GetWindow<TextureSlicerWindow>("Texture Slicer");

            if (Selection.activeObject is Texture2D texture) {
                window.m_Source = texture;
                window.m_ReadDirty = true;
            }
        }

        private void OnEnable() {
            minSize = new Vector2(900, 620);
            m_ReadDirty = true;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable() {
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            ReleaseTexture();
        }

        private void OnEditorUpdate() {
            if (m_LayoutDirty && m_Readable != null && EditorApplication.timeSinceStartup >= m_RebuildAfter) {
                Repaint();
            }
        }

        private void OnProjectChanged() {
            if (m_Source == null || AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(m_Source))
                != m_Dependency) {
                m_ReadDirty = true;
                Repaint();
            }
        }

        private void ReleaseTexture() {
            if (m_Readable != null) {
                DestroyImmediate(m_Readable);
            }

            m_Readable = null;
            m_Pixels = null;
            m_Tiles = null;
        }

        private void RefreshPreview() {
            try {
                if (m_ReadDirty) {
                    m_ReadDirty = false;
                    ReleaseTexture();
                    m_Note = null;
                    m_Result = null;
                    m_LayoutDirty = true;
                    m_Fit = true;
                    m_PreviewScroll = Vector2.zero;

                    if (m_Source == null) {
                        m_LayoutDirty = false;
                        m_Error = null;
                        return;
                    }

                    m_Readable = TextureSliceIO.Read(m_Source, out m_Note);
                    m_Pixels = m_Readable.GetPixels32();
                    m_Dependency = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(m_Source));
                }

                if (!m_LayoutDirty || m_Readable == null) {
                    return;
                }

                m_LayoutDirty = false;
                m_Tiles = null;
                m_Error = null;
                m_Result = null;
                
                var nextProgressUpdate = 0.0;
                m_Tiles = TextureSliceLayout.Build(m_Readable.width, m_Readable.height, m_Pixels, m_Options,
                    value => {
                        // 扫描检查点密集，但进度窗口最多每 50ms 更新一次。
                        var now = EditorApplication.timeSinceStartup;

                        if (now < nextProgressUpdate) {
                            return;
                        }

                        nextProgressUpdate = now + 0.05;

                        if (EditorUtility.DisplayCancelableProgressBar("计算切割预览", "扫描透明区域与切片边界", value)) {
                            throw new OperationCanceledException();
                        }
                    });
            } catch (OperationCanceledException) {
                m_LayoutDirty = false;
                m_Error = "已取消预览计算。点击“重试”可继续。";
            } catch (Exception exception) {
                m_LayoutDirty = false;
                m_Error = exception.Message;
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnGUI() {
            // 仅在 Layout 阶段替换结果，保证一轮 IMGUI 事件的布局一致。
            if (Event.current.type == EventType.Layout) {
                if (m_ReadDirty || m_LayoutDirty && GUIUtility.hotControl == 0 &&
                    EditorApplication.timeSinceStartup >= m_RebuildAfter) {
                    RefreshPreview();
                }

                // 仅在 Layout 切换按钮可见性，避免点击后立即增减 GUILayout 控件。
                m_ShowRestoreSelection = !m_ReadDirty && !m_LayoutDirty && m_Tiles != null &&
                    m_Tiles.Any(tile => !tile.Included && (!tile.Empty || !m_Options.SkipEmpty));
            }

            using (new EditorGUILayout.HorizontalScope()) {
                using (var scroll = new EditorGUILayout.ScrollViewScope(m_SettingsScroll, GUILayout.Width(320))) {
                    m_SettingsScroll = scroll.scrollPosition;
                    DrawSettings();
                }

                using (new EditorGUILayout.VerticalScope()) {
                    DrawPreview();
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                if (!string.IsNullOrEmpty(m_Result)) {
                    EditorGUILayout.LabelField(m_Result, EditorStyles.wordWrappedLabel);
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUI.DisabledScope(m_ReadDirty || m_LayoutDirty || m_Tiles == null ||
                                                       !m_Tiles.Any(tile => tile.Included)
                                                       || EditorApplication.isCompiling)) {
                        if (GUILayout.Button("选择目录并导出切片", GUILayout.Height(34))) {
                            Export();
                        }
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_LastOutput))) {
                        if (GUILayout.Button("定位输出", GUILayout.Width(90), GUILayout.Height(34))) {
                            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(m_LastOutput));
                        }
                    }
                }
            }
        }

        private void DrawSettings() {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("1  选择图片", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            m_Source = (Texture2D)EditorGUILayout.ObjectField(m_Source, typeof(Texture2D), false);

            if (EditorGUI.EndChangeCheck()) {
                m_ReadDirty = true;
            }

            if (!string.IsNullOrEmpty(m_Note)) {
                EditorGUILayout.HelpBox(m_Note, MessageType.Info);
            }

            GUILayout.Space(12);
            EditorGUILayout.LabelField("2  切割方案", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            m_Options.Mode = (TextureSliceMode)EditorGUILayout.Popup("切割方式", (int)m_Options.Mode,
                new[] { "按最大像素尺寸", "按行列数均分" });

            if (m_Options.Mode == TextureSliceMode.MaxSize) {
                m_Options.TileSize = EditorGUILayout.Vector2IntField("最大宽高（像素）", m_Options.TileSize);
                m_Options.Overlap = EditorGUILayout.Vector2IntField("相邻重叠（X、Y）", m_Options.Overlap);
                EditorGUILayout.LabelField("末尾切片保留实际尺寸，重叠区域不额外生成尾片。", EditorStyles.wordWrappedMiniLabel);
            } else {
                m_Options.Grid = EditorGUILayout.Vector2IntField("列数 X · 行数 Y", m_Options.Grid);
                EditorGUILayout.LabelField("完整覆盖图片，不能整除时各块最多相差 1 像素。", EditorStyles.wordWrappedMiniLabel);
            }

            m_Options.Trim = EditorGUILayout.Toggle("裁掉透明边缘", m_Options.Trim);
            m_Options.SkipEmpty = EditorGUILayout.Toggle("跳过全透明切片", m_Options.SkipEmpty);

            using (new EditorGUI.DisabledScope(!m_Options.Trim && !m_Options.SkipEmpty)) {
                m_Options.AlphaThreshold = EditorGUILayout.Slider("透明阈值", m_Options.AlphaThreshold, 0, 1);
            }

            if (EditorGUI.EndChangeCheck()) {
                m_LayoutDirty = true;
                m_RebuildAfter = EditorApplication.timeSinceStartup + 0.3;
            }

            EditorGUILayout.LabelField("更改方案会重新计算，并重置手动排除的切片。", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(12);
            EditorGUILayout.LabelField("3  输出设置", EditorStyles.boldLabel);
            m_ExportSprite = EditorGUILayout.Popup("输出图片类型", m_ExportSprite ? 1 : 0,
                new[] { "普通纹理", "Sprite（Single）" }) == 1;
            m_Prefix = EditorGUILayout.TextField("文件名前缀", m_Prefix);
            EditorGUILayout.LabelField("留空使用源图名称；文件按行、列编号。", EditorStyles.wordWrappedMiniLabel);
            m_Manifest = EditorGUILayout.Toggle("导出坐标清单 JSON", m_Manifest);
            EditorGUILayout.HelpBox("每次导出到新的独立文件夹，不覆盖旧文件。清单记录原始区域、裁剪区域与文件名，便于还原拼接位置。", MessageType.None);
        }

        private void DrawPreview() {
            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.Label("切割预览", GUILayout.Width(65));

                if (GUILayout.Button("适应", EditorStyles.toolbarButton, GUILayout.Width(42))) {
                    m_Fit = true;
                }

                if (GUILayout.Button("1:1", EditorStyles.toolbarButton, GUILayout.Width(36))) {
                    m_Fit = false;
                    m_Zoom = 1;
                }

                EditorGUI.BeginChangeCheck();
                m_Zoom = GUILayout.HorizontalSlider(m_Zoom, 0.05f, 4, GUILayout.Width(100));

                if (EditorGUI.EndChangeCheck()) {
                    m_Fit = false;
                }

                GUILayout.FlexibleSpace();

                if (m_ShowRestoreSelection) {
                    using (new EditorGUI.DisabledScope(m_ReadDirty || m_LayoutDirty)) {
                        if (GUILayout.Button("恢复选择", EditorStyles.toolbarButton)) {
                            foreach (var tile in m_Tiles) {
                                tile.Included = !tile.Empty || !m_Options.SkipEmpty;
                            }

                            Repaint();
                        }
                    }
                }
            }

            if (m_Source == null) {
                EditorGUILayout.HelpBox("将图片拖入左侧输入框，即可预览切割方案。也可先在 Project 选中图片，再打开工具。", MessageType.Info);
                GUILayout.FlexibleSpace();
                return;
            }

            if (!string.IsNullOrEmpty(m_Error)) {
                EditorGUILayout.HelpBox(m_Error, MessageType.Warning);

                if (GUILayout.Button("重试", GUILayout.Width(80))) {
                    // 扫描取消时复用已读取的像素，不重复解码大图或重置缩放位置。
                    m_ReadDirty |= m_Readable == null || m_Pixels == null;
                    m_LayoutDirty = true;
                    m_RebuildAfter = 0;
                    Repaint();
                }
            }

            if (m_Readable == null) {
                GUILayout.FlexibleSpace();
                return;
            }

            EditorGUILayout.LabelField(m_LayoutDirty
                    ? "参数已更改，停止调整后更新预览…"
                    : m_Tiles == null
                        ? "等待有效切割方案"
                        : $"共 {m_Tiles.Count} 块 · 将导出 {m_Tiles.Count(tile => tile.Included)} 块 · 点击切片可排除／恢复",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("蓝框：原始切片　绿框：裁剪后区域　暗色：不导出", EditorStyles.miniLabel);
            
            var viewport =
                GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var scale = m_Fit ? Mathf.Min((viewport.width - 20) / m_Readable.width,
                (viewport.height - 20) / m_Readable.height) : m_Zoom;
            scale = Mathf.Max(0.001f, scale);
            
            var imageRect = new Rect(0, 0, m_Readable.width * scale, m_Readable.height * scale);
            var content = new Rect(0, 0, Mathf.Max(viewport.width - 18, imageRect.width),
                Mathf.Max(viewport.height - 18, imageRect.height));
            // 进入滚动区域前记录窗口坐标下的命中，排除被裁切的不可见内容。
            var mouseInViewport = viewport.Contains(Event.current.mousePosition);
            m_PreviewScroll = GUI.BeginScrollView(viewport, m_PreviewScroll, content);
            EditorGUI.DrawTextureTransparent(imageRect, m_Readable);

            if (m_Tiles != null) {
                foreach (var tile in m_Tiles) {
                    var cell = PreviewRect(tile.Cell, scale);
                    Handles.DrawSolidRectangleWithOutline(cell, tile.Included ? Color.clear : new Color(0, 0, 0, 0.65f),
                        new Color(0.2f, 0.65f, 1));

                    if (tile.Included && tile.Content != tile.Cell) {
                        Handles.DrawSolidRectangleWithOutline(PreviewRect(tile.Content, scale), Color.clear,
                            Color.green);
                    }

                    if (cell.width > 65 && cell.height > 25) {
                        GUI.Label(new Rect(cell.x + 3, cell.y + 3, cell.width - 6, 20),
                            $"{tile.Row + 1},{tile.Column + 1}", EditorStyles.whiteMiniLabel);
                    }
                }

                if (mouseInViewport && Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                    !m_ReadDirty && !m_LayoutDirty) {
                    var tile = m_Tiles.LastOrDefault(item =>
                        PreviewRect(item.Cell, scale).Contains(Event.current.mousePosition));

                    if (tile != null && (!tile.Empty || !m_Options.SkipEmpty)) {
                        tile.Included = !tile.Included;
                        Event.current.Use();
                        Repaint();
                    }
                }
            }

            GUI.EndScrollView();
        }

        private Rect PreviewRect(RectInt rect, float scale) =>
            new(rect.x * scale, (m_Readable.height - rect.yMax) * scale, rect.width * scale, rect.height * scale);

        private void Export() {
            var absolute = EditorUtility.SaveFolderPanel("选择输出位置（Assets 内）", Application.dataPath, "");

            if (string.IsNullOrEmpty(absolute)) {
                return;
            }

            try {
                var root = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
                absolute = Path.GetFullPath(absolute).Replace('\\', '/').TrimEnd('/');

                if (absolute != root && !absolute.StartsWith(root + "/", StringComparison.Ordinal)) {
                    throw new InvalidOperationException("输出目录必须位于当前项目 Assets 内。");
                }

                AssetDatabase.Refresh();

                if (m_Source == null || AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(m_Source))
                    != m_Dependency) {
                    m_ReadDirty = true;
                    throw new InvalidOperationException("源图片已发生变化，请等待预览重新计算后再导出。");
                }

                m_LastOutput = TextureSliceIO.Export(m_Source, m_Readable, m_Pixels, m_Tiles, m_Options,
                    "Assets" + absolute.Substring(root.Length), m_Prefix, m_ExportSprite, m_Manifest,
                    (message, progress) => {
                        if (EditorUtility.DisplayCancelableProgressBar("导出切片", message, progress)) {
                            throw new OperationCanceledException();
                        }
                    });
                m_Result = $"已导出 {m_Tiles.Count(tile => tile.Included)} 张切片：{m_LastOutput}";
                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(m_LastOutput));
            } catch (OperationCanceledException) {
                m_Result = "已取消导出，本次未完成的输出目录已清理。";
            } catch (Exception exception) {
                m_Result = "导出失败：" + exception.Message;
                Debug.LogException(exception);
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
