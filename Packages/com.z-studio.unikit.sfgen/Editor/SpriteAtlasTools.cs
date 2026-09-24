using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 注意：Unity2022.3.x某个版本后出现了bug，SpriteAtlas的GetSprites接口只有在项目运行中才能获取到正确的textureRect值，非工具bug。
    /// </summary>
    public static class SpriteAtlasTools {
        [MenuItem("Tools/UniKit/Sprite Atlas Tools/SpriteAtlas -> TMP_SpriteAsset (运行时使用)", priority = 800)]
        private static void SpriteAtlas2TmpSpriteMenu() {
            var objs = Selection.objects;

            foreach (var o in objs) {
                if (o is SpriteAtlas atlas) {
                    SpriteAtlas2TMPSpriteAsset(atlas);
                }
            }
        }

        private static void SpriteAtlas2TMPSpriteAsset(SpriteAtlas atlas) {
            var atlasPath = AssetDatabase.GetAssetPath(atlas);
            var directoryName = Path.GetDirectoryName(atlasPath);
            var fileName = Path.GetFileNameWithoutExtension(atlasPath);
            var tmpSpriteAssetPath = GetCombinePath(directoryName, $"{fileName}.asset");
            var textureOutputPath = GetCombinePath(directoryName, $"{fileName}.png");

            if (!SpriteAtlas2Texture(atlas, textureOutputPath)) {
                return;
            }

            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);
            TMP_SpriteAsset spriteAsset;

            if (File.Exists(tmpSpriteAssetPath)) {
                spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(tmpSpriteAssetPath);
            } else {
                spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                AssetDatabase.CreateAsset(spriteAsset, tmpSpriteAssetPath);
            }

            spriteAsset.spriteSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(textureOutputPath);
            spriteAsset.spriteCharacterTable.Clear();
            spriteAsset.spriteGlyphTable.Clear();

            if (spriteAsset.material == null) {
                var material = new Material(Shader.Find("TextMeshPro/Sprite")) {
                    mainTexture = spriteAsset.spriteSheet
                };

                AssetDatabase.AddObjectToAsset(material, spriteAsset);
                AssetDatabase.SaveAssetIfDirty(spriteAsset);
                spriteAsset.material = material;
            }

            var spNameTrim = "(Clone)".ToCharArray();

            for (var i = 0; i < sprites.Length; i++) {
                var sp = sprites[i];
                var spRect = sp.textureRect;

                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new UnityEngine.TextCore.GlyphMetrics(
                        spRect.width,
                        spRect.height,
                        0,
                        spRect.height,
                        spRect.width
                    ),
                    new UnityEngine.TextCore.GlyphRect(spRect),
                    1,
                    0
                );

                spriteAsset.spriteGlyphTable.Add(glyph);

                var spChar = new TMP_SpriteCharacter(0xFFFE, glyph) {
                    name = sp.name.TrimEnd(spNameTrim)
                };

                spriteAsset.spriteCharacterTable.Add(spChar);
            }

            AssetDatabase.SaveAssetIfDirty(spriteAsset);
        }

        [MenuItem("Tools/UniKit/Sprite Atlas Tools/SpriteAtlas -> SpriteSheet (运行时使用)", priority = 801)]
        private static void SpriteAtlas2SpriteSheet() {
            var objs = Selection.objects;

            foreach (var o in objs) {
                if (o is SpriteAtlas atlas) {
                    SpriteAtlas2SpriteSheet(atlas);
                }
            }
        }

        private static void SpriteAtlas2SpriteSheet(SpriteAtlas atlas) {
            var atlasPath = AssetDatabase.GetAssetPath(atlas);
            var directoryName = Path.GetDirectoryName(atlasPath);
            var fileName = Path.GetFileNameWithoutExtension(atlasPath);
            var textureOutputPath = GetCombinePath(directoryName, $"{fileName}_sheet.png");

            if (!SpriteAtlas2Texture(atlas, textureOutputPath, TextureImporterType.Sprite)) {
                return;
            }

            var texImporter = AssetImporter.GetAtPath(textureOutputPath) as TextureImporter;
            var factory = new SpriteDataProviderFactories();
            factory.Init();

            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(texImporter);
            dataProvider.InitSpriteEditorDataProvider();
            dataProvider.SetSpriteRects(GetSpriteRects(atlas));
            dataProvider.Apply();

            texImporter.SaveAndReimport();
        }

        /// <summary>
        /// 导出Multiple类型的Sprite为碎图
        /// </summary>
        [MenuItem("Tools/UniKit/Sprite Atlas Tools/SpriteSheet -> sprites", priority = 802)]
        private static void SpriteSheet2Sprites() {
            var selectAssetsCount = Selection.objects.Length;
            EditorUtility.DisplayProgressBar($"拆分图集(0/{selectAssetsCount})", "Export sprite sheet to sprites...", 0);

            var slicedSpritesAssets = new List<string>();

            for (var i = 0; i < selectAssetsCount; i++) {
                var selectObj = Selection.objects[i];

                if (selectObj == null) {
                    continue;
                }

                var objType = selectObj.GetType();

                if (objType != typeof(Sprite) && objType != typeof(Texture2D)) {
                    Debug.LogError($"导出碎图sprites失败! 你选择的资源不是Sprite或Texture2D类型");
                    continue;
                }

                var spPath = AssetDatabase.GetAssetPath(selectObj);
                var spTex = AssetDatabase.LoadAssetAtPath<Texture2D>(spPath);

                if (spTex == null) {
                    continue;
                }

                var texImporter = AssetImporter.GetAtPath(spPath) as TextureImporter;

                if (texImporter.textureType != TextureImporterType.Sprite ||
                    texImporter.spriteImportMode != SpriteImportMode.Multiple) {
                    Debug.LogError($"导出碎图sprites失败! 你选择的资源不是Sprite类型或SpriteMode不是Multiple类型: {spPath}");
                    continue;
                }

                var texReadable = texImporter.isReadable;

                if (!texReadable) {
                    texImporter.isReadable = true;
                    texImporter.SaveAndReimport();
                }

                var outputDir = GetCombinePath(
                    Path.GetDirectoryName(spPath),
                    $"{Path.GetFileNameWithoutExtension(spPath)}_sliced"
                );

                if (!Directory.Exists(outputDir)) {
                    Directory.CreateDirectory(outputDir);
                }

                var providerFactories = new SpriteDataProviderFactories();
                providerFactories.Init();

                var texProvider = providerFactories.GetSpriteEditorDataProviderFromObject(spTex);
                texProvider.InitSpriteEditorDataProvider();

                var spRects = texProvider.GetSpriteRects();
                var childrenSpCount = spRects.Length;

                for (var spIndex = 0; spIndex < childrenSpCount; spIndex++) {
                    var spRect = spRects[spIndex];
                    var tex = new Texture2D((int)spRect.rect.width, (int)spRect.rect.height);
                    tex.SetPixels(spTex.GetPixels((int)spRect.rect.x, (int)spRect.rect.y, tex.width, tex.height));
                    tex.Apply();

                    var fileName = GetCombinePath(outputDir, $"{CleanFileName(spRect.name)}.png");

                    if (File.Exists(fileName)) {
                        File.Delete(fileName);
                    }

                    EditorUtility.DisplayProgressBar(
                        $"拆分图集({i + 1}/{selectAssetsCount})",
                        $"导出进度({spIndex}/{childrenSpCount}){System.Environment.NewLine}正在导出碎图{spRect}",
                        (i + 1) / (float)selectAssetsCount
                    );

                    File.WriteAllBytes(fileName, tex.EncodeToPNG());
                    slicedSpritesAssets.Add(fileName);
                }

                texImporter.isReadable = texReadable;
                texImporter.SaveAndReimport();
            }

            AssetDatabase.Refresh();

            foreach (var item in slicedSpritesAssets) {
                var texImporter = AssetImporter.GetAtPath(item) as TextureImporter;

                if (texImporter == null) {
                    continue;
                }

                texImporter.textureType = TextureImporterType.Sprite;
                texImporter.spriteImportMode = SpriteImportMode.Single;
                texImporter.alphaIsTransparency = true;
                texImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                texImporter.mipmapEnabled = false;
                texImporter.SaveAndReimport();
            }

            EditorUtility.ClearProgressBar();
        }

        private static bool SpriteAtlas2Texture(
            SpriteAtlas atlas,
            string outputPath,
            TextureImporterType textureType = TextureImporterType.Default
        ) {
            if (atlas == null || atlas.spriteCount == 0) {
                return false;
            }

            var getPreviewFunc = typeof(UnityEditor.U2D.SpriteAtlasExtensions).GetMethod(
                "GetPreviewTextures",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );

            if (null == getPreviewFunc) {
                return false;
            }

            var previews = getPreviewFunc.Invoke(null, new object[] { atlas }) as Texture2D[];

            if (previews is not { Length: 1 }) {
                Debug.LogError($"SpriteAtlas转换为Texture失败: 图集存在{previews?.Length}个子图集,请修改MaxTextureSize以确保为单图集");
                return false;
            }

            // 通过SpriteAtlasExtensions.GetPreviewTextures拿到的都是压缩过的贴图，并且贴图数据是在一片不可读内存上，
            // 不能直接使用EncodeToPNG解码保存为png文件。所以我们需要通过Graphics接口把贴图复制出来。
            var atlasTexture = previews[0];
            var readableAtlasTex = CopyReadableTexture(atlasTexture);

            try {
                File.WriteAllBytes(outputPath, readableAtlasTex.EncodeToPNG());
            } catch (System.Exception e) {
                Debug.LogException(e);
                return false;
            } finally {
                Object.DestroyImmediate(readableAtlasTex);
            }

            AssetDatabase.Refresh();
            var texImporter = AssetImporter.GetAtPath(outputPath) as TextureImporter;

            if (texImporter == null) {
                Debug.LogError("TextureImporter转换失败");
                return false;
            }

            texImporter.textureType = textureType;

            if (textureType == TextureImporterType.Sprite) {
                texImporter.spriteImportMode = SpriteImportMode.Multiple;
                texImporter.isReadable = true;
            }

            texImporter.textureShape = TextureImporterShape.Texture2D;
            texImporter.alphaIsTransparency = true;
            texImporter.SaveAndReimport();
            return true;
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

        private static SpriteRect[] GetSpriteRects(SpriteAtlas atlas) {
            if (atlas == null || atlas.spriteCount == 0) {
                return null;
            }

            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);

            var spriteRects = new SpriteRect[sprites.Length];
            var spNameTrim = "(Clone)".ToCharArray();

            for (var i = 0; i < sprites.Length; i++) {
                var sp = sprites[i];

                spriteRects[i] = new SpriteRect {
                    name = sp.name.Trim(spNameTrim),
                    rect = sp.textureRect
                };
            }

            return spriteRects;
        }

        private static string GetCombinePath(params string[] args) {
            return Path.Combine(args).Replace('\\', '/');
        }

        private static string CleanFileName(string fileName) {
            if (string.IsNullOrEmpty(fileName)) {
                return "unnamed";
            }

            // 只替换操作系统禁止的字符
            char[] invalidChars = Path.GetInvalidFileNameChars();
            fileName = invalidChars.Aggregate(fileName, (current, c) => current.Replace(c, '_'));

            // 去掉开头的点，防止隐藏文件
            fileName = fileName.TrimStart('.');
            
            if (string.IsNullOrEmpty(fileName)) {
                fileName = "unnamed";
            }

            return fileName;
        }
    }
}