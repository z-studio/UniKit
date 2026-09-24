using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    /// <summary>制作入口：输入、映射、排版预览与输出，不承担字体资源的构建细节。</summary>
    internal sealed class SpriteFontWindow : EditorWindow {
        [SerializeField]
        private SpriteFontSettings m_Settings;
        
        [SerializeField] 
        private string m_PreviewText = "";
        
        private SerializedObject m_SerializedSettings;
        private List<SpriteFontBuilder.Entry> m_Entries;
        private Vector2 m_Scroll;
        private Vector2 m_PreviewScroll;
        private float m_PreviewWidth;
        private string m_Error;
        private string m_Result;
        private SpriteRect[] m_Sprites;
        private SpriteFontSource m_Source;
        private bool m_ReloadSource = true;
        private string m_AtlasError;
        private bool m_HasMissingSprites;
        private bool m_ShowLayoutOptions;
        private int m_Page;
        private bool m_NeedsRefresh = true;

        [MenuItem("Tools/UniKit/Sprite Font Generator", priority = 810)]
        private static void Open() {
            GetWindow<SpriteFontWindow>("Sprite Font");
        }

        [OnOpenAsset]
        private static bool OpenSettings(int entityId, int line) {
            if (EditorUtility.EntityIdToObject(entityId) is not SpriteFontSettings settings) {
                return false;
            }

            Open();
            GetWindow<SpriteFontWindow>().SetSettings(settings);
            return true;
        }

        private void OnEnable() {
            minSize = new Vector2(900, 640);
            
            // 没有持久化配置时停留在起始页，不创建隐藏的临时配置。
            SetSettings(EditorUtility.IsPersistent(m_Settings) ? m_Settings : null);

            EditorApplication.projectChanged += RequestRefresh;
            Undo.undoRedoPerformed += RequestRefresh;
            m_NeedsRefresh = true;
            m_ReloadSource = true;
        }

        private void OnDisable() {
            EditorApplication.projectChanged -= RequestRefresh;
            Undo.undoRedoPerformed -= RequestRefresh;
            m_Source?.Dispose();
            m_Source = null;
            m_Entries = null;
        }

        private void OnDestroy() {
            if (m_Settings != null && !EditorUtility.IsPersistent(m_Settings)) {
                DestroyImmediate(m_Settings);
            }
        }

        private void SetSettings(SpriteFontSettings settings) {
            if (m_Settings != null && m_Settings != settings && !EditorUtility.IsPersistent(m_Settings)) {
                DestroyImmediate(m_Settings);
            }

            m_Settings = settings;

            m_Source?.Dispose();
            m_Source = null;
            m_Entries = null;
            m_Sprites = null;
            m_SerializedSettings = settings != null ? new SerializedObject(settings) : null;
            m_Page = 0;
            m_PreviewText = "";
            m_Result = null;
            RequestRefresh();
        }

        private void RequestRefresh() {
            m_ReloadSource = true;
            m_NeedsRefresh = true;
            Repaint();
        }

        private void OnGUI() {
            if (m_Settings == null) {
                DrawStartPage();
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.Label("制作配置", GUILayout.Width(70));
               
                var selected = EditorGUILayout.ObjectField(EditorUtility.IsPersistent(m_Settings) ? m_Settings : null,
                    typeof(SpriteFontSettings), false) as SpriteFontSettings;

                if (selected != m_Settings) {
                    SetSettings(selected);
                    GUI.FocusControl(null);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("新建配置", EditorStyles.toolbarButton, GUILayout.Width(72))) {
                    CreateNewSettings();
                    GUIUtility.ExitGUI();
                }
            }

            m_SerializedSettings.Update();

            // 等到下一轮 Layout 再切换有效/无效预览，保持当前输入事件的控件结构稳定。
            if (m_NeedsRefresh && Event.current.type == EventType.Layout) {
                RefreshEntries();
            }

            var settingsWidth = Mathf.Clamp(position.width * 0.45f, 380, 460);
            // 预览使用右栏的可用宽度进行换行，扣除栏间距、滚动条和内边距。
            m_PreviewWidth = Mathf.Max(100, position.width - settingsWidth - 40);

            using (new EditorGUILayout.HorizontalScope()) {
                using (var settingsScroll = new EditorGUILayout.ScrollViewScope(m_Scroll,
                    GUILayout.Width(settingsWidth))) {
                    m_Scroll = settingsScroll.scrollPosition;
                    EditorGUILayout.Space(8);
                    DrawInput();
                    DrawLayout();
                }

                GUILayout.Space(8);

                using (var previewScroll = new EditorGUILayout.ScrollViewScope(m_PreviewScroll)) {
                    m_PreviewScroll = previewScroll.scrollPosition;
                    EditorGUILayout.Space(8);
                    DrawPreview();
                }
            }

            DrawOutput();

            if (m_SerializedSettings.ApplyModifiedProperties()) {
                m_Result = null;
                m_NeedsRefresh = true;
                Repaint();
            }
        }

        private void DrawStartPage() {
            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(460))) {
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("开始制作 Sprite 字体", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("尚未分配制作配置。请先新建配置，或选择已有配置继续编辑。",
                        EditorStyles.wordWrappedLabel);
                    GUILayout.Space(16);

                    if (GUILayout.Button("新建制作配置", GUILayout.Height(36))) {
                        CreateNewSettings();
                        GUIUtility.ExitGUI();
                    }

                    GUILayout.Space(12);
                    EditorGUILayout.LabelField("选择已有配置", EditorStyles.boldLabel);
                    var selected = EditorGUILayout.ObjectField(null, typeof(SpriteFontSettings), false) as SpriteFontSettings;

                    if (selected != null) {
                        SetSettings(selected);
                        GUI.FocusControl(null);
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.LabelField("可拖入配置资源、点击右侧圆圈选择，或在 Project 中双击配置。",
                        EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(16);
                }

                GUILayout.FlexibleSpace();
            }

            GUILayout.FlexibleSpace();
        }

        private void CreateNewSettings() {
            var path = EditorUtility.SaveFilePanelInProject("新建 Sprite Font 制作配置",
                "Sprite Font", "asset", "选择新配置的保存位置，生成的字体将保存在同一目录。");

            // 取消或路径冲突时，不丢弃当前正在编辑的配置。
            if (string.IsNullOrEmpty(path)) {
                return;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) {
                EditorUtility.DisplayDialog("无法新建", "此位置已有资源，请选择新名称。要编辑已有配置，请在窗口顶部选择它。", "关闭");
                return;
            }

            var settings = CreateInstance<SpriteFontSettings>();
            AssetDatabase.CreateAsset(settings, path);
            SetSettings(settings);
            m_PreviewText = "";
            m_Scroll = Vector2.zero;
            m_PreviewScroll = Vector2.zero;

            m_ShowLayoutOptions = false;
            GUI.FocusControl(null);
            EditorGUIUtility.PingObject(settings);
        }

        private void DrawInput() {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField("1  Sprite 与字符映射", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var source = m_SerializedSettings.FindProperty("Source");
                source.enumValueIndex = EditorGUILayout.Popup("来源类型", source.enumValueIndex,
                    new[] { "Sprite 纹理（Single、Multiple）", "散图 Sprite", "SpriteAtlas" });

                if ((SpriteFontSourceType)source.enumValueIndex == SpriteFontSourceType.Texture) {
                    Field("Atlas", "Sprite 纹理", "支持 Single 和 Multiple 模式的 Sprite 纹理。");
                } else if ((SpriteFontSourceType)source.enumValueIndex == SpriteFontSourceType.SpriteAtlas) {
                    Field("SourceAtlas", "SpriteAtlas", "读取原始 Sprite 资源，并合成字体专用图集；无需运行或 Pack Preview。");
                } else {
                    DrawSpriteSources();
                }

                if (EditorGUI.EndChangeCheck()) {
                    // 重新读取来源，但保留仍存在的 Sprite 映射；移除项由失效映射提示处理。
                    m_ReloadSource = true;
                    m_Page = 0;
                }

                EditorGUI.BeginChangeCheck();
                var order = m_SerializedSettings.FindProperty("Order");
                order.enumValueIndex = EditorGUILayout.Popup("字符列表排序", order.enumValueIndex,
                    new[] { "来源顺序", "名称自然排序（2 在 10 前）", "按行从上到下，行内从左到右" });

                if (EditorGUI.EndChangeCheck()) {
                    m_ReloadSource = true;
                }

                EditorGUILayout.LabelField("在每个 Sprite 旁填写字符，留空跳过。排序不改变对应关系。",
                    EditorStyles.wordWrappedMiniLabel);

                if (m_Sprites != null) {
                    DrawMapping();
                } else {
                    EditorGUILayout.HelpBox(m_AtlasError ?? "请选择字体图集。", MessageType.Info);
                }
            }
        }

        private void DrawSpriteSources() {
            var sprites = m_SerializedSettings.FindProperty("SourceSprites");
            EditorGUILayout.PropertyField(sprites, new GUIContent("Sprite 列表（展开可移除）"), true);

            if (GUILayout.Button("添加 Project 中选中的 Sprite / 图片 / 文件夹")) {
                AddSpriteSources(Selection.objects);
            }

            var dropArea = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "拖入 Sprite、Sprite 图片或文件夹（可多选）", EditorStyles.helpBox);
            var current = Event.current;

            if (dropArea.Contains(current.mousePosition) &&
                (current.type == EventType.DragUpdated || current.type == EventType.DragPerform)) {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (current.type == EventType.DragPerform) {
                    DragAndDrop.AcceptDrag();
                    AddSpriteSources(DragAndDrop.objectReferences);
                }

                current.Use();
            }
        }

        private void AddSpriteSources(UnityEngine.Object[] objects) {
            var added = new List<Sprite>();

            try {
                foreach (var item in objects) {
                    SpriteFontSource.CollectSprites(item, added);
                }
            } catch (InvalidOperationException exception) {
                EditorUtility.DisplayDialog("无法添加 Sprite", exception.Message, "关闭");
                return;
            }

            var sprites = m_SerializedSettings.FindProperty("SourceSprites");
            var existing = new HashSet<UnityEngine.Object>();

            for (var i = 0; i < sprites.arraySize; i++) {
                existing.Add(sprites.GetArrayElementAtIndex(i).objectReferenceValue);
            }

            foreach (var sprite in added) {
                if (existing.Add(sprite)) {
                    var index = sprites.arraySize;
                    sprites.InsertArrayElementAtIndex(index);
                    sprites.GetArrayElementAtIndex(index).objectReferenceValue = sprite;
                }
            }

            m_ReloadSource = true;
        }

        private void DrawLayout() {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField("2  字体与排版", EditorStyles.boldLabel);
                
                var output = m_SerializedSettings.FindProperty("Output");
                output.enumValueIndex = EditorGUILayout.Popup("生成类型", output.enumValueIndex,
                    new[] { "UGUI Text", "TextMeshPro", "UGUI Text + TextMeshPro" });
                
                Field("FontSize", "设计字号", "字符最高尺寸及字体度量的基准。推荐与原始图集字符高度接近。");
                Field("NormalizeHeight", "统一字符高度", "开启：每个切片等比缩放到设计字号。关闭：所有切片使用相同比例，保留大小差异。");
                m_ShowLayoutOptions = EditorGUILayout.Foldout(m_ShowLayoutOptions, "间距与基线", true);

                if (m_ShowLayoutOptions) {
                    Field("Baseline", "基线距底部", "0 表示字符底边位于基线上。增大后字符整体下移，适用于包含下伸部的字体。");
                    Field("LetterSpacing", "额外字距", "每个可见字符之后额外增加的宽度，单位为设计字号下的像素，可为负。");
                    Field("LineSpacing", "额外行距", "行高 = 设计字号 + 额外行距。");
                    Field("SpaceWidth", "空格宽度", "空格不显示图片，只向右推进指定宽度。");
                }
            }
        }

        private void Field(string name, string label, string tooltip) {
            EditorGUILayout.PropertyField(m_SerializedSettings.FindProperty(name), new GUIContent(label, tooltip));
        }

        private void RefreshEntries() {
            m_NeedsRefresh = false;
            m_Entries = null;
            m_Error = null;

            m_HasMissingSprites = false;

            if (m_ReloadSource) {
                m_ReloadSource = false;
                m_Source?.Dispose();
                m_Source = null;
                m_Sprites = null;
                m_AtlasError = null;

                try {
                    m_Source = SpriteFontSource.Load(m_Settings);
                    m_Sprites = m_Source.Sprites;
                } catch (Exception exception) {
                    m_AtlasError = exception.Message;
                }
            }

            if (m_Source == null) {
                m_Error = m_AtlasError;
                return;
            }

            var ids = new HashSet<string>(m_Sprites.Select(sprite => sprite.spriteID.ToString()));
            m_HasMissingSprites = m_Settings.Mappings.Any(mapping => !ids.Contains(mapping.SpriteId));

            try {
                m_Entries = SpriteFontBuilder.BuildEntries(m_Settings, m_Source);
            } catch (Exception exception) {
                m_Error = exception.Message;
            }
        }

        private void DrawPreview() {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField("3  效果预览", EditorStyles.boldLabel);

                if (m_Entries == null) {
                    EditorGUILayout.HelpBox(m_Error ?? "请先填写字符映射。", MessageType.Info);

                    if (m_Source?.Texture != null) {
                        var preview = GUILayoutUtility.GetRect(100, 180);
                        EditorGUI.DrawTextureTransparent(preview, m_Source.Texture, ScaleMode.ScaleToFit);
                    }

                    return;
                }

                EditorGUILayout.LabelField(new GUIContent("预览文字", "留空时预览全部已映射字符；支持 Enter 换行。"));
                m_PreviewText = EditorGUILayout.TextArea(m_PreviewText, GUILayout.Height(54));
                EditorGUILayout.Space(6);
                DrawTextPreview();
                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField("图集", EditorStyles.miniBoldLabel);
                var atlas = m_Source.Texture;
                var availableWidth = m_PreviewWidth;
                var scale = Mathf.Min(availableWidth / atlas.width, 220f / atlas.height);
                var frame = GUILayoutUtility.GetRect(availableWidth, atlas.height * scale);
                var area = new Rect(frame.x + (frame.width - atlas.width * scale) * 0.5f,
                    frame.y, atlas.width * scale, atlas.height * scale);
                EditorGUI.DrawTextureTransparent(area, atlas);

                for (var i = 0; i < m_Entries.Count - 1; i++) {
                    var pixels = m_Entries[i].Pixels;
                    var rect = new Rect(area.x + pixels.x * scale,
                        area.y + (atlas.height - pixels.yMax) * scale, pixels.width * scale, pixels.height * scale);
                    GUI.Box(rect, GUIContent.none);
                    GUI.Label(new Rect(rect.x, rect.y, 36, 18), (i + 1).ToString(), EditorStyles.whiteMiniLabel);
                }

                EditorGUILayout.LabelField($"已映射 {m_Entries.Count - 1} 个字符，自动添加空格。", EditorStyles.miniLabel);

                EditorGUILayout.LabelField("预览按设计字号逐字换行，行距与基线使用左侧参数。", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawTextPreview() {
            const float k_Padding = 8;
            var sample = string.IsNullOrEmpty(m_PreviewText)
                ? string.Concat(m_Entries.Where(entry => entry.Unicode != 32)
                    .Select(entry => char.ConvertFromUtf32((int)entry.Unicode)))
                : m_PreviewText;
            var width = m_PreviewWidth;
            var glyphs = LayoutPreview(sample, width - k_Padding * 2, out var lineCount, out var missing);
            var lineHeight = m_Settings.FontSize + m_Settings.LineSpacing;
            var height = m_Settings.FontSize + (lineCount - 1) * lineHeight + k_Padding * 2;
            // 使用同一宽度进行排版和绘制，避免自动换行与预览边界不一致。
            var area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));
            GUI.BeginClip(area);

            for (var line = 0; line < lineCount; line++) {
                var baseline = k_Padding + m_Settings.FontSize - m_Settings.Baseline + line * lineHeight;
                EditorGUI.DrawRect(new Rect(0, baseline, area.width, 1), new Color(0.4f, 0.6f, 0.8f, 0.5f));
            }

            foreach (var glyph in glyphs) {
                var rect = glyph.Rect;
                rect.position += Vector2.one * k_Padding;
                var pixels = glyph.Entry.Pixels;
                var atlas = m_Source.Texture;
                GUI.DrawTextureWithTexCoords(rect, atlas,
                    new Rect((float)pixels.x / atlas.width, (float)pixels.y / atlas.height,
                        (float)pixels.width / atlas.width, (float)pixels.height / atlas.height));
            }

            GUI.EndClip();

            if (missing) {
                EditorGUILayout.LabelField("预览文字中有未映射字符，当前以空白占位。", EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>先完成手动/自动换行，再分配绘制高度，后面的图集不会覆盖新增的行。</summary>
        private List<(SpriteFontBuilder.Entry Entry, Rect Rect)> LayoutPreview(string sample, float width,
            out int lineCount, out bool missing) {
            var result = new List<(SpriteFontBuilder.Entry, Rect)>();
            var x = 0f;
            lineCount = 1;
            missing = false;
            sample = sample.Replace("\r\n", "\n").Replace('\r', '\n');

            for (var i = 0; i < sample.Length; i++) {
                var c = sample[i];

                if (c == '\uFEFF') {
                    continue;
                }

                if (c == '\n') {
                    x = 0;
                    lineCount++;
                    continue;
                }

                if (char.IsLowSurrogate(c) || (char.IsHighSurrogate(c) &&
                    (i + 1 == sample.Length || !char.IsLowSurrogate(sample[i + 1])))) {
                    missing = true;
                    continue;
                }

                var unicode = (uint)char.ConvertToUtf32(sample, i);

                if (char.IsHighSurrogate(c)) {
                    i++;
                }

                var index = m_Entries.FindIndex(entry => entry.Unicode == unicode);
                var entry = index >= 0 ? m_Entries[index] : default;
                var advance = index >= 0 ? entry.Metrics.horizontalAdvance : m_Settings.SpaceWidth;

                if (c == '\t') {
                    advance = m_Settings.SpaceWidth * 4;
                } else if (index < 0) {
                    missing = true;
                }

                var extent = Mathf.Max(advance, entry.Metrics.horizontalBearingX + entry.Metrics.width);

                // 宽于整行的单个字形仍绘制一次并裁切，不额外插入空行。
                if (x > 0 && x + extent > width) {
                    x = 0;
                    lineCount++;
                }

                if (index >= 0 && entry.Pixels.width > 0) {
                    var baseline = m_Settings.FontSize - m_Settings.Baseline
                        + (lineCount - 1) * (m_Settings.FontSize + m_Settings.LineSpacing);
                    result.Add((entry, new Rect(x + entry.Metrics.horizontalBearingX,
                        baseline - entry.Metrics.horizontalBearingY, entry.Metrics.width, entry.Metrics.height)));
                }

                x += advance;
            }

            return result;
        }

        private void DrawMapping() {
            const int k_PageSize = 8;
            var mappings = m_SerializedSettings.FindProperty("Mappings");
            var pageCount = Mathf.Max(1, Mathf.CeilToInt((float)m_Sprites.Length / k_PageSize));
            m_Page = Mathf.Clamp(m_Page, 0, pageCount - 1);
            var sourceWidth = m_Source.Width;
            var sourceHeight = m_Source.Height;

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Sprite", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("字符", EditorStyles.miniBoldLabel, GUILayout.Width(64));
            }

            for (var i = m_Page * k_PageSize; i < Mathf.Min((m_Page + 1) * k_PageSize, m_Sprites.Length); i++) {
                var sprite = m_Sprites[i];
                var id = sprite.spriteID.ToString();
                var mappingIndex = -1;

                for (var j = 0; j < mappings.arraySize; j++) {
                    if (mappings.GetArrayElementAtIndex(j).FindPropertyRelative("SpriteId").stringValue == id) {
                        mappingIndex = j;
                        break;
                    }
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    var preview = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36));
                    var rect = sprite.rect;

                    if (rect.width > 0 && rect.height > 0) {
                        var size = rect.size * Mathf.Min(36 / rect.width, 36 / rect.height);
                        GUI.DrawTextureWithTexCoords(new Rect(preview.center - size * 0.5f, size), m_Source.Texture,
                            new Rect(rect.x / sourceWidth, rect.y / sourceHeight,
                                rect.width / sourceWidth, rect.height / sourceHeight));
                    }

                    EditorGUILayout.LabelField(new GUIContent(sprite.name, sprite.name), GUILayout.Height(36));
                    var character = mappingIndex < 0 ? "" : mappings.GetArrayElementAtIndex(mappingIndex)
                        .FindPropertyRelative("Character").stringValue;
                    GUI.SetNextControlName("SpriteFontCharacter_" + id);
                    var edited = EditorGUILayout.TextField(character, GUILayout.Width(64), GUILayout.Height(28));

                    if (edited != character) {
                        if (string.IsNullOrWhiteSpace(edited)) {
                            if (mappingIndex >= 0) {
                                mappings.DeleteArrayElementAtIndex(mappingIndex);
                            }
                        } else {
                            if (mappingIndex < 0) {
                                mappingIndex = mappings.arraySize;
                                mappings.InsertArrayElementAtIndex(mappingIndex);
                            }

                            var mapping = mappings.GetArrayElementAtIndex(mappingIndex);
                            mapping.FindPropertyRelative("SpriteId").stringValue = id;
                            mapping.FindPropertyRelative("Character").stringValue = edited;
                        }
                    }
                }
            }

            if (pageCount > 1) {
                using (new EditorGUILayout.HorizontalScope()) {
                    var nextPage = m_Page;

                    using (new EditorGUI.DisabledScope(m_Page == 0)) {
                        if (GUILayout.Button("上一页")) {
                            nextPage--;
                        }
                    }

                    GUILayout.Label($"{m_Page + 1} / {pageCount}", GUILayout.Width(60));

                    using (new EditorGUI.DisabledScope(m_Page == pageCount - 1)) {
                        if (GUILayout.Button("下一页")) {
                            nextPage++;
                        }
                    }

                    if (nextPage != m_Page) {
                        m_Page = nextPage;
                        GUI.FocusControl(null);
                        m_SerializedSettings.ApplyModifiedProperties();
                        RequestRefresh();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (m_HasMissingSprites) {
                EditorGUILayout.HelpBox("有切片已被删除或重新切片，原映射不会自动套用到其他图片。", MessageType.Warning);

                if (GUILayout.Button("清除失效映射")) {
                    var ids = new HashSet<string>(m_Sprites.Select(sprite => sprite.spriteID.ToString()));

                    for (var i = mappings.arraySize - 1; i >= 0; i--) {
                        if (!ids.Contains(mappings.GetArrayElementAtIndex(i).FindPropertyRelative("SpriteId").stringValue)) {
                            mappings.DeleteArrayElementAtIndex(i);
                        }
                    }
                }
            }
        }

        private void DrawOutput() {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                if (!string.IsNullOrEmpty(m_Result)) {
                    EditorGUILayout.HelpBox(m_Result, MessageType.Info);
                }

                var saved = EditorUtility.IsPersistent(m_Settings);
                EditorGUILayout.LabelField(saved ? AssetDatabase.GetAssetPath(m_Settings) :
                    "首次生成时选择配置保存位置，字体保存在同一目录。", EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUI.DisabledScope(m_Entries == null || EditorApplication.isCompiling)) {
                    if (GUILayout.Button(saved ? "生成 / 更新字体" : "保存配置并生成字体", GUILayout.Height(32))) {
                        m_SerializedSettings.ApplyModifiedProperties();
                        Generate();
                    }
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    if (m_Settings.LegacyFont != null && GUILayout.Button("定位 Text 字体")) {
                        EditorGUIUtility.PingObject(m_Settings.LegacyFont);
                    }

                    if (m_Settings.TMPFont != null && GUILayout.Button("定位 TMP 字体")) {
                        EditorGUIUtility.PingObject(m_Settings.TMPFont);
                    }
                }
            }
        }

        private void Generate() {
            try {
                // 再次读取切片，不能依赖绘制时的缓存来生成文件。
                SpriteFontBuilder.BuildEntries(m_Settings, m_Source);

                if (!EditorUtility.IsPersistent(m_Settings)) {
                    var path = EditorUtility.SaveFilePanelInProject("保存 Sprite Font 制作配置",
                        (m_Settings.Source == SpriteFontSourceType.Texture ? m_Settings.Atlas.name : "Sprite") + " Font", "asset", "双击此配置可以继续编辑和更新字体。");

                    if (string.IsNullOrEmpty(path)) {
                        return;
                    }

                    if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) {
                        throw new InvalidOperationException("此位置已有资源。请在顶部打开原配置，或选择新名称。");
                    }

                    m_Settings.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(m_Settings, path);
                }

                SpriteFontBuilder.Generate(m_Settings);
                m_Result = "生成完成。将字体拖入对应文本组件的 Font / Font Asset 字段即可使用。";
            } catch (Exception exception) {
                m_Result = "生成失败：" + exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
