using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>负责映射校验、字体度量及资源写入，与窗口绘制分离。</summary>
    internal static class SpriteFontBuilder {
        // TMP 的 atlasTextures setter 不清理 atlasTexture 缓存，也没有公开刷新方法。
        // 编辑器内更换图集时需同步这个缓存，否则直到重启前都可能读到旧图集。
        private static readonly FieldInfo s_AtlasTextureCache = typeof(TMP_FontAsset).GetField(
            "m_AtlasTexture", BindingFlags.Instance | BindingFlags.NonPublic);

        internal readonly struct Entry {
            public readonly uint Unicode;
            public readonly string Name;
            public readonly RectInt Pixels;
            public readonly GlyphMetrics Metrics;

            public Entry(uint unicode, string name, RectInt pixels, GlyphMetrics metrics) {
                Unicode = unicode;
                Name = name;
                Pixels = pixels;
                Metrics = metrics;
            }
        }

        // 换行用于编辑排版；空格由独立参数生成，不占用图集中的切片。
        internal static List<uint> ParseCharacters(string text) {
            var result = new List<uint>();
            var seen = new HashSet<uint>();

            for (var i = 0; i < (text?.Length ?? 0); i++) {
                var c = text[i];

                if (c == '\r' || c == '\n' || c == '\t' || c == '\uFEFF' || c == ' ') {
                    continue;
                }

                if (char.IsControl(c) || char.IsLowSurrogate(c) ||
                    (char.IsHighSurrogate(c) && (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])))) {
                    throw new InvalidOperationException($"第 {i + 1} 个位置包含无效或不支持的控制字符。");
                }

                var unicode = (uint)char.ConvertToUtf32(text, i);

                if (char.IsHighSurrogate(c)) {
                    i++;
                }

                if (!seen.Add(unicode)) {
                    throw new InvalidOperationException($"字符「{char.ConvertFromUtf32((int)unicode)}」重复（U+{unicode:X4}）。每个字符只能映射一个切片。");
                }

                result.Add(unicode);
            }

            return result;
        }

        internal static List<Entry> BuildEntries(SpriteFontSettings settings) {
            using var source = SpriteFontSource.Load(settings);
            return BuildEntries(settings, source);
        }

        internal static List<Entry> BuildEntries(SpriteFontSettings settings, SpriteFontSource source) {
            if (settings.FontSize < 1 || settings.FontSize > 512 || settings.SpaceWidth < 0 ||
                settings.Baseline < 0 || settings.Baseline > settings.FontSize || settings.LineSpacing < 0) {
                throw new InvalidOperationException("字号需要在 1–512 之间；基线在 0–字号之间；空格宽度和额外行距不能为负。");
            }

            var allSprites = source.Sprites;
            var byId = allSprites.ToDictionary(sprite => sprite.spriteID.ToString());
            var mapped = new Dictionary<string, uint>();
            var usedCharacters = new HashSet<uint>();

            foreach (var mapping in settings.Mappings) {
                if (string.IsNullOrWhiteSpace(mapping.Character)) {
                    continue;
                }

                if (!byId.TryGetValue(mapping.SpriteId, out var sprite)) {
                    throw new InvalidOperationException("有已填写字符的切片被删除或重新切片，请清除失效映射后重新填写。");
                }

                var parsed = ParseCharacters(mapping.Character);

                if (parsed.Count != 1 || char.ConvertFromUtf32((int)parsed[0]) != mapping.Character) {
                    throw new InvalidOperationException($"切片「{sprite.name}」只能填写一个 Unicode 字符，留空则跳过。空格由工具自动生成。");
                }

                if (!usedCharacters.Add(parsed[0])) {
                    throw new InvalidOperationException($"字符「{mapping.Character}」重复。一个字符只能对应一个 Sprite。");
                }

                if (!mapped.TryAdd(mapping.SpriteId, parsed[0])) {
                    throw new InvalidOperationException($"切片「{sprite.name}」存在重复映射。");
                }
            }

            var sprites = allSprites.Where(sprite => mapped.ContainsKey(sprite.spriteID.ToString())).ToArray();

            if (sprites.Length == 0) {
                throw new InvalidOperationException("请在需要使用的 Sprite 旁填写字符；留空的切片不参与生成。");
            }

            var characters = sprites.Select(sprite => mapped[sprite.spriteID.ToString()]).ToArray();
          
            // 源图模式保留原图坐标；合成图集的坐标已是像素坐标。
            var scaleX = (float)source.Texture.width / source.Width;
            var scaleY = (float)source.Texture.height / source.Height;
            var maxHeight = sprites.Max(s => s.rect.height);
            var entries = new List<Entry>(sprites.Length + 1);

            for (var i = 0; i < sprites.Length; i++) {
                if (settings.Output != SpriteFontOutput.TextMeshPro && characters[i] > char.MaxValue) {
                    throw new InvalidOperationException($"UGUI Text 不支持字符 U+{characters[i]:X}。请仅生成 TextMeshPro 字体。");
                }

                var rect = sprites[i].rect;
                var x = Mathf.RoundToInt(rect.xMin * scaleX);
                var y = Mathf.RoundToInt(rect.yMin * scaleY);
                var pixels = new RectInt(x, y, Mathf.RoundToInt(rect.xMax * scaleX) - x,
                    Mathf.RoundToInt(rect.yMax * scaleY) - y);

                if (pixels.width <= 0 || pixels.height <= 0 || x < 0 || y < 0 ||
                    pixels.xMax > source.Texture.width || pixels.yMax > source.Texture.height) {
                    throw new InvalidOperationException($"切片「{sprites[i].name}」超出图集或缩放后尺寸为零，请检查切片与导入尺寸。");
                }

                // 归一化只改变排版尺寸，UV 始终使用真实切片，避免采样到相邻字符。
                var scale = settings.FontSize / (settings.NormalizeHeight ? rect.height : maxHeight);
                var width = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
                var height = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));
                var advance = width + settings.LetterSpacing;

                if (advance <= 0) {
                    throw new InvalidOperationException("字距过小，导致字符的前进宽度不大于零。");
                }

                entries.Add(new Entry(characters[i], sprites[i].name, pixels,
                    new GlyphMetrics(width, height, 0, height - settings.Baseline, advance)));
            }

            entries.Add(new Entry(32, "空格（自动）", new RectInt(),
                new GlyphMetrics(0, 0, 0, 0, settings.SpaceWidth)));
            return entries;
        }

        internal static void Generate(SpriteFontSettings settings) {
            using var source = SpriteFontSource.Load(settings);
            var entries = BuildEntries(settings, source);
            var settingsPath = AssetDatabase.GetAssetPath(settings);

            if (!settingsPath.StartsWith("Assets/", StringComparison.Ordinal)) {
                throw new InvalidOperationException("请先将制作配置保存到 Assets 内。");
            }

            var shader = Shader.Find("UniKit/Sprite Font");

            if (shader == null) {
                throw new InvalidOperationException("找不到 UniKit/Sprite Font Shader，请检查 SFGen 包是否完整导入。");
            }

            if (s_AtlasTextureCache == null && settings.Output != SpriteFontOutput.LegacyText) {
                throw new InvalidOperationException("当前 TMP 版本的图集缓存结构不受支持。");
            }

            var owner = "SFGen:" + AssetDatabase.AssetPathToGUID(settingsPath);
            var legacy = OwnedOutput(settings.LegacyFont, owner);
            var tmp = OwnedOutput(settings.TMPFont, owner);
            var stem = settingsPath.Substring(0, settingsPath.LastIndexOf('.'));
            var legacyPath = stem + " Text.fontsettings";
            var tmpPath = stem + " TMP.asset";
            var generatedAtlas = OwnedOutput(settings.GeneratedAtlas, owner);
            var atlasPath = generatedAtlas != null ? AssetDatabase.GetAssetPath(generatedAtlas) : stem + " Atlas.png";

            if (source.IsGenerated && generatedAtlas == null &&
                (File.Exists(atlasPath) || AssetDatabase.LoadMainAssetAtPath(atlasPath) != null)) {
                throw new InvalidOperationException($"字体图集输出已被其他资源占用：{atlasPath}");
            }
            var makeLegacy = settings.Output != SpriteFontOutput.TextMeshPro;
            var makeTMP = settings.Output != SpriteFontOutput.LegacyText;

            // 先检查所有输出，避免覆盖手工创建的同名资源或借用其他字体的材质。
            if (makeLegacy) {
                ValidateOutput(legacyPath, legacy, legacy != null ? legacy.material : null);
            }

            if (makeTMP) {
                ValidateOutput(tmpPath, tmp, tmp != null ? tmp.material : null);
            }

            var texture = source.Texture;

            if (source.IsGenerated) {
                File.WriteAllBytes(atlasPath, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
               
                var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 8192;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.userData = owner;
                
                // 只修改本工具生成的图片；更新时保持 PNG 的 GUID 不变。
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

                if (texture.width != source.Texture.width || texture.height != source.Texture.height) {
                    throw new InvalidOperationException("字体图集被平台导入设置缩放，请移除生成图集的平台尺寸覆盖后重试。");
                }

                settings.GeneratedAtlas = texture;
            }

            // 复制配置后生成新字体，不通过复制过来的引用覆盖原配置的输出。
            settings.LegacyFont = legacy;
            settings.TMPFont = tmp;
            EditorUtility.SetDirty(settings);

            if (makeLegacy) {
                if (settings.LegacyFont == null) {
                    settings.LegacyFont = new Font(settings.name + " Text");
                    AssetDatabase.CreateAsset(settings.LegacyFont, legacyPath);
                    SetOwner(legacyPath, owner);
                }

                WriteLegacy(settings, entries, GetMaterial(settings.LegacyFont, shader, texture));
            }

            if (makeTMP) {
                if (settings.TMPFont == null) {
                    settings.TMPFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
                    settings.TMPFont.name = settings.name + " TMP";
                    AssetDatabase.CreateAsset(settings.TMPFont, tmpPath);
                    SetOwner(tmpPath, owner);
                }

                WriteTMP(settings, entries, GetMaterial(settings.TMPFont, shader, texture));
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static T OwnedOutput<T>(T asset, string owner) where T : Object {
            return asset != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset))?.userData == owner
                ? asset : null;
        }

        private static void SetOwner(string path, string owner) {
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = owner;
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        private static void ValidateOutput(string newPath, Object existing, Material material) {
            if (existing == null) {
                if (AssetDatabase.LoadMainAssetAtPath(newPath) != null || File.Exists(newPath)) {
                    throw new InvalidOperationException($"输出路径已被其他资源占用：{newPath}。请换一个配置名称。");
                }

                return;
            }

            var path = AssetDatabase.GetAssetPath(existing);

            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                (material != null && AssetDatabase.GetAssetPath(material) != path)) {
                throw new InvalidOperationException($"字体「{existing.name}」不在 Assets 内，或正在使用外部材质，请恢复生成时的内嵌材质后更新。");
            }
        }

        private static Material GetMaterial(Object font, Shader shader, Texture2D atlas) {
            var path = AssetDatabase.GetAssetPath(font);
            var material = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault();

            if (material == null) {
                material = new Material(shader) { name = font.name + " Material" };
                AssetDatabase.AddObjectToAsset(material, font);
            }

            material.shader = shader;
            material.mainTexture = atlas;
            material.SetFloat("_TextureWidth", atlas.width);
            material.SetFloat("_TextureHeight", atlas.height);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void WriteLegacy(SpriteFontSettings settings, List<Entry> entries, Material material) {
            var font = settings.LegacyFont;
            var atlas = material.mainTexture;
            var infos = new CharacterInfo[entries.Count];

            for (var i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                var uv = new Rect((float)entry.Pixels.x / atlas.width,
                    (float)entry.Pixels.y / atlas.height,
                    (float)entry.Pixels.width / atlas.width,
                    (float)entry.Pixels.height / atlas.height);
                infos[i] = new CharacterInfo {
                    index = (int)entry.Unicode,
                    size = settings.FontSize,
                    minX = 0,
                    maxX = (int)entry.Metrics.width,
                    minY = (int)(entry.Metrics.horizontalBearingY - entry.Metrics.height),
                    maxY = (int)entry.Metrics.horizontalBearingY,
                    advance = (int)entry.Metrics.horizontalAdvance,
                    uvBottomLeft = new Vector2(uv.xMin, uv.yMin),
                    uvBottomRight = new Vector2(uv.xMax, uv.yMin),
                    uvTopLeft = new Vector2(uv.xMin, uv.yMax),
                    uvTopRight = new Vector2(uv.xMax, uv.yMax)
                };
            }

            font.material = material;
            font.characterInfo = infos;
            
            // Legacy Font 的基础度量没有公开 setter，必须写入 Unity 的序列化字段。
            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_FontSize").floatValue = settings.FontSize;
            serialized.FindProperty("m_Ascent").floatValue = settings.FontSize - settings.Baseline;
            serialized.FindProperty("m_LineSpacing").floatValue = settings.FontSize + settings.LineSpacing;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(font);

            // 静态 Font 的 characterInfo 更新不会通知 Text；同时清理显示和布局缓存。
            foreach (var text in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>()) {
                if (text.font == font && !EditorUtility.IsPersistent(text)) {
                    text.cachedTextGeneratorForLayout.Invalidate();
                    text.FontTextureChanged();
                }
            }
        }

        private static void WriteTMP(SpriteFontSettings settings, List<Entry> entries, Material material) {
            var font = settings.TMPFont;
            var atlas = (Texture2D)material.mainTexture;
            
            // 从切片直接建立静态位图字体，不需要系统字体、FontEngine 或可读纹理副本。
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.isMultiAtlasTexturesEnabled = false;
            font.atlasTextures = new[] { atlas };
            s_AtlasTextureCache.SetValue(font, atlas);
            font.material = material;
            font.faceInfo = new FaceInfo {
                familyName = settings.name,
                styleName = "Regular",
                pointSize = settings.FontSize,
                scale = 1,
                lineHeight = settings.FontSize + settings.LineSpacing,
                ascentLine = settings.FontSize - settings.Baseline,
                capLine = settings.FontSize - settings.Baseline,
                meanLine = (settings.FontSize - settings.Baseline) * 0.5f,
                baseline = 0,
                descentLine = -settings.Baseline,
                tabWidth = settings.SpaceWidth
            };
            font.glyphTable.Clear();
            font.characterTable.Clear();

            for (var i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                var rect = entry.Pixels;
                var glyph = new Glyph((uint)i + 1, entry.Metrics,
                    new GlyphRect(rect.x, rect.y, rect.width, rect.height), 1, 0);
                font.glyphTable.Add(glyph);
                font.characterTable.Add(new TMP_Character(entry.Unicode, font, glyph));
            }

            // Apply 会触发 TMP.OnValidate，因此先备齐材质、图集和字形表。
            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.FindProperty("m_AtlasWidth").intValue = atlas.width;
            serialized.FindProperty("m_AtlasHeight").intValue = atlas.height;
            serialized.FindProperty("m_AtlasPadding").intValue = 0;
            serialized.FindProperty("m_AtlasRenderMode").intValue = (int)GlyphRenderMode.COLOR;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 更新查找表，确保已引用这个 Font Asset 的文本可以使用新映射。
            font.ReadFontAssetDefinition();
            EditorUtility.SetDirty(font);
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, font);
        }
    }
}
