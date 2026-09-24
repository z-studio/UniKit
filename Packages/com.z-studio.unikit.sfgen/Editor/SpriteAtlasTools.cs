using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.U2D;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>编辑模式下导出 Sprite 资源；从原始图片读取，不依赖 SpriteAtlas 的运行时打包结果。</summary>
    public static class SpriteAtlasTools {
        private const string k_MenuRoot = "Tools/UniKit/Sprite Atlas Tools/";

        [MenuItem(k_MenuRoot + "SpriteAtlas 导出 TMP Sprite Asset", priority = 800)]
        private static void SpriteAtlas2TmpSpriteMenu() => ExportSelection(true, true);

        [MenuItem(k_MenuRoot + "SpriteAtlas 导出 Sprite 图片", priority = 801)]
        private static void SpriteAtlas2SpriteSheet() => ExportSelection(true, false);

        [MenuItem(k_MenuRoot + "Sprite 图片拆分为散图", priority = 802)]
        private static void SpriteSheet2Sprites() => ExportSelection(false, false);

        [MenuItem(k_MenuRoot + "SpriteAtlas 导出 TMP Sprite Asset", true)]
        [MenuItem(k_MenuRoot + "SpriteAtlas 导出 Sprite 图片", true)]
        private static bool CanExportAtlas() =>
            !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.objects.Any(asset => asset is SpriteAtlas);

        [MenuItem(k_MenuRoot + "Sprite 图片拆分为散图", true)]
        private static bool CanSplitSprites() =>
            !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.objects.Any(asset => asset is Sprite || asset is Texture2D &&
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset)) is TextureImporter importer &&
                importer.textureType == TextureImporterType.Sprite);

        private static void ExportSelection(bool atlasMode, bool makeTMP) {
            var selected = Selection.objects.Where(asset => atlasMode ? asset is SpriteAtlas :
                asset is Sprite || asset is Texture2D &&
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset)) is TextureImporter importer &&
                importer.textureType == TextureImporterType.Sprite).ToArray();

            if (EditorApplication.isPlayingOrWillChangePlaymode || selected.Length == 0) {
                return;
            }

            // 允许包内资源作为输入，输出统一由用户选择 Assets 内的位置。
            var destination = EditorUtility.SaveFolderPanel("选择导出位置（Assets 内）", Application.dataPath, "");

            if (string.IsNullOrEmpty(destination)) {
                return;
            }

            var root = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            destination = Path.GetFullPath(destination).Replace('\\', '/').TrimEnd('/');

            if (destination != root && !destination.StartsWith(root + "/", StringComparison.Ordinal)) {
                EditorUtility.DisplayDialog("无法导出", "请选择当前项目 Assets 内的文件夹。", "关闭");
                return;
            }

            destination = "Assets" + destination.Substring(root.Length);
            var count = 0;
            Object lastOutput = null;

            try {
                foreach (var asset in selected) {
                    EditorUtility.DisplayProgressBar("导出 Sprite 资源", asset.name, (float)count / selected.Length);
                    lastOutput = Export(asset, destination, atlasMode, makeTMP);
                    count++;
                }

                Selection.activeObject = lastOutput;
                EditorGUIUtility.PingObject(lastOutput);
                Debug.Log($"Sprite Atlas Tools：已导出 {count} 个资源到 {destination}。每次导出使用独立目录，不覆盖已有资源。");
            } catch (Exception exception) {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("导出失败", $"已完成 {count} 个资源。\n{exception.Message}", "关闭");
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        // 每个输入单独提交；失败时仅清理本次新建目录，保留已成功导出的其他输入。
        private static Object Export(Object asset, string destination, bool atlasMode, bool makeTMP) {
            var settings = ScriptableObject.CreateInstance<SpriteFontSettings>();
            string folder = null;

            try {
                settings.Source = atlasMode ? SpriteFontSourceType.SpriteAtlas : SpriteFontSourceType.Texture;
                settings.SourceAtlas = asset as SpriteAtlas;
                settings.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(asset));
                using var source = SpriteFontSource.Load(settings);
                var rects = source.Sprites;

                if (asset is Sprite selectedSprite) {
                    var id = selectedSprite.GetSpriteID();
                    rects = rects.Where(rect => rect.spriteID == id).ToArray();
                }

                if (rects.Length == 0) {
                    throw new InvalidOperationException("没有可导出的 Sprite，请先在 Sprite Editor 中 Apply。");
                }

                if (makeTMP && rects.Select(rect => TMP_TextUtilities.GetSimpleHashCode(rect.name)).Distinct().Count()
                    != rects.Length) {
                    throw new InvalidOperationException("Sprite 名称重复或 TMP 名称哈希冲突，请先为源 Sprite 设置唯一名称。");
                }

                var shader = makeTMP ? Shader.Find("TextMeshPro/Sprite") : null;

                if (makeTMP && shader == null) {
                    throw new InvalidOperationException("找不到 TextMeshPro/Sprite Shader，请先导入 TMP Essential Resources。");
                }

                var folderPath =
                    AssetDatabase.GenerateUniqueAssetPath(destination + "/" + CleanFileName(asset.name) + " Export");
                var guid = AssetDatabase.CreateFolder(destination, Path.GetFileName(folderPath));
                folder = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(folder)) {
                    throw new IOException("无法创建导出目录。");
                }

                if (atlasMode) {
                    var texture = SaveTexture(source.Texture, folder + "/Atlas.png", !makeTMP);

                    if (makeTMP) {
                        return CreateTMP(texture, rects, shader, folder + "/Sprites.asset");
                    }

                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                    var factories = new SpriteDataProviderFactories();
                    factories.Init();
                    var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                    provider.InitSpriteEditorDataProvider();
                    provider.SetSpriteRects(rects);
                    var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
                    names.SetNameFileIdPairs(rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)));
                    provider.Apply();
                    importer.SaveAndReimport();
                    return AssetDatabase.LoadMainAssetAtPath(folder);
                }

                var readable = CopyReadableTexture(source.Texture);

                try {
                    foreach (var sprite in rects) {
                        // Sprite Editor 使用原图坐标，GPU 回读使用导入尺寸，需转换后再裁切。
                        var x = Mathf.RoundToInt(sprite.rect.xMin * readable.width / source.Width);
                        var y = Mathf.RoundToInt(sprite.rect.yMin * readable.height / source.Height);
                        var width = Mathf.RoundToInt(sprite.rect.xMax * readable.width / source.Width) - x;
                        var height = Mathf.RoundToInt(sprite.rect.yMax * readable.height / source.Height) - y;

                        if (width <= 0 || height <= 0) {
                            throw new InvalidOperationException($"切片「{sprite.name}」在导入缩放后尺寸为零。");
                        }

                        var crop = new Texture2D(width, height, TextureFormat.RGBA32, false);

                        try {
                            crop.SetPixels(readable.GetPixels(x, y, width, height));
                            crop.Apply();
                            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{CleanFileName(sprite.name)}.png");
                            SaveTexture(crop, path, true, false);
                            
                            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                            var textureSettings = new TextureImporterSettings();
                            importer.ReadTextureSettings(textureSettings);
                            textureSettings.spriteAlignment = (int)sprite.alignment;
                            textureSettings.spritePivot = sprite.pivot;
                            textureSettings.spriteBorder = Vector4.Scale(sprite.border,
                                new Vector4(width / sprite.rect.width, height / sprite.rect.height,
                                    width / sprite.rect.width, height / sprite.rect.height));
                            importer.SetTextureSettings(textureSettings);
                            importer.SaveAndReimport();
                        } finally {
                            Object.DestroyImmediate(crop);
                        }
                    }
                } finally {
                    Object.DestroyImmediate(readable);
                }

                return AssetDatabase.LoadMainAssetAtPath(folder);
            } catch {
                if (!string.IsNullOrEmpty(folder)) {
                    AssetDatabase.DeleteAsset(folder);
                }

                throw;
            } finally {
                Object.DestroyImmediate(settings);
            }
        }

        private static Texture2D SaveTexture(Texture2D texture, string path, bool sprite, bool multiple = true) {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = multiple ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            
            var output = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (output.width != texture.width || output.height != texture.height) {
                throw new InvalidOperationException("导出图片被平台设置缩放，请检查 TextureImporter 默认预设。");
            }

            return output;
        }

        private static TMP_SpriteAsset CreateTMP(Texture2D texture, SpriteRect[] sprites, Shader shader, string path) {
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            
            // 标记为当前字形表格式，避免 TMP 将新资源误判为旧版并尝试升级 spriteInfoList。
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.name = Path.GetFileNameWithoutExtension(path);
            asset.spriteSheet = texture;
            asset.material = new Material(shader) { name = "Sprite Material", mainTexture = texture };
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            for (var i = 0; i < sprites.Length; i++) {
                var rect = sprites[i].rect;
                var glyph = new TMP_SpriteGlyph((uint)i,
                    new GlyphMetrics(rect.width, rect.height, 0, rect.height, rect.width),
                    new GlyphRect(rect), 1, 0);
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph) { name = sprites[i].name });
            }

            asset.UpdateLookupTables();
            EditorUtility.SetDirty(asset.material);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        /// <summary>GPU 回读源纹理，不修改 Read/Write；完整恢复全局渲染状态并释放临时资源。</summary>
        internal static Texture2D CopyReadableTexture(Texture2D source) {
            var previous = RenderTexture.active;
            var previousSRGB = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D copy = null;

            try {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            } catch {
                if (copy != null) {
                    Object.DestroyImmediate(copy);
                }

                throw;
            } finally {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSRGB;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static string CleanFileName(string name) {
            // 额外处理跨平台非法字符，避免在 macOS 导出后无法在 Windows 使用。
            foreach (var c in Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*")) {
                name = name.Replace(c, '_');
            }

            name = name.Trim().Trim('.');
            return string.IsNullOrEmpty(name) ? "Sprite" : name;
        }
    }
}