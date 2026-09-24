using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>读取副本和导出资源，不改写源图片或其导入设置。</summary>
    internal static class TextureSliceIO {
        internal static Texture2D Read(Texture2D source, out string note) {
            var path = AssetDatabase.GetAssetPath(source);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (source == null || importer == null) {
                throw new InvalidOperationException("请选择项目中的图片资源。");
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var original = extension is ".png" or ".jpg" or ".jpeg";
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            width = original ? width : source.width;
            height = original ? height : source.height;

            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize || (long)width * height > 64_000_000L) {
                throw new InvalidOperationException("图片超过当前预览能力：单边不能超过显卡纹理上限，总像素不能超过 6400 万。请先缩小或分割源文件。");
            }

            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false, !importer.sRGBTexture) {
                hideFlags = HideFlags.HideAndDontSave, 
                name = "Texture Slicer Preview"
            };

            try {
                if (original) {
                    var file = Path.GetFullPath(path);

                    if (!File.Exists(file)) {
                        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);

                        if (package != null) {
                            file = Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
                        }
                    }

                    if (!copy.LoadImage(File.ReadAllBytes(file), false)) {
                        throw new InvalidOperationException("无法解码源图片。");
                    }

                    note = $"原始文件像素：{copy.width} × {copy.height}（不受导入 Max Size 和压缩影响）";
                } else {
                    var active = RenderTexture.active;
                    var srgb = GL.sRGBWrite;
                    var target = RenderTexture.GetTemporary(source.width, source.height, 0,
                        RenderTextureFormat.ARGB32, importer.sRGBTexture ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);

                    try {
                        // sRGB 来源回写编码值；线性数据纹理不经过 Gamma 编码。
                        GL.sRGBWrite = importer.sRGBTexture && QualitySettings.activeColorSpace == ColorSpace.Linear;
                        Graphics.Blit(source, target);
                        RenderTexture.active = target;
                        copy.Reinitialize(source.width, source.height, TextureFormat.RGBA32, false);
                        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                        copy.Apply();
                    } finally {
                        RenderTexture.active = active;
                        GL.sRGBWrite = srgb;
                        RenderTexture.ReleaseTemporary(target);
                    }

                    note = $"使用导入后像素：{copy.width} × {copy.height}。需要原始分辨率时，请转为 PNG／JPG。";
                }

                copy.filterMode = source.filterMode;
                return copy;
            } catch {
                Object.DestroyImmediate(copy);
                throw;
            }
        }

        [Serializable]
        private sealed class Manifest {
            public int Version = 1;
            public string Source;
            public string SourceGuid;
            public int Width;
            public int Height;
            public string Coordinates = "左下角像素坐标；Row/Column 从左上角开始，索引从 0 开始";
            public TextureSliceOptions Options;
            public TextureSliceTile[] Tiles;
        }

        internal static string Export(Texture2D source, Texture2D readable, Color32[] pixels,
            List<TextureSliceTile> tiles, TextureSliceOptions options, string destination, string prefix,
            bool sprite, bool manifest, Action<string, float> progress) {
            if (!AssetDatabase.IsValidFolder(destination) ||
                (destination != "Assets" && !destination.StartsWith("Assets/", StringComparison.Ordinal))) {
                throw new InvalidOperationException("请选择项目 Assets 内已导入的文件夹。");
            }

            var selected = tiles.Where(tile => tile.Included).ToArray();

            if (selected.Length == 0) {
                throw new InvalidOperationException("没有选中任何可导出的切片。");
            }

            prefix = CleanName(string.IsNullOrWhiteSpace(prefix) ? source.name : prefix);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{destination}/{prefix}_slices");
            var folder = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(destination, Path.GetFileName(path)));

            if (string.IsNullOrEmpty(folder)) {
                throw new IOException("无法创建输出文件夹。");
            }

            foreach (var tile in tiles) {
                tile.File = "";
            }

            try {
                var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source));

                for (var i = 0; i < selected.Length; i++) {
                    var tile = selected[i];
                    progress?.Invoke($"写入 {i + 1}/{selected.Length}", (float)i / selected.Length);
                    
                    var rect = tile.Content;
                    var crop = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false, !sourceImporter.sRGBTexture);

                    try {
                        var output = new Color32[rect.width * rect.height];

                        for (var row = 0; row < rect.height; row++) {
                            Array.Copy(pixels, (rect.y + row) * readable.width + rect.x,
                                output, row * rect.width, rect.width);
                        }

                        crop.SetPixels32(output);
                        crop.Apply();
                        tile.File = $"{prefix}_r{tile.Row:000}_c{tile.Column:000}.png";
                        
                        var file = folder + "/" + tile.File;
                        File.WriteAllBytes(file, crop.EncodeToPNG());
                        AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
                        
                        var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
                        importer.filterMode = source.filterMode;
                        importer.npotScale = TextureImporterNPOTScale.None;
                        importer.maxTextureSize = 16384;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.mipmapEnabled = false;
                        importer.isReadable = false;
                        importer.sRGBTexture = sourceImporter.sRGBTexture;
                        importer.alphaSource = TextureImporterAlphaSource.FromInput;
                        importer.wrapMode = TextureWrapMode.Clamp;
                        importer.SaveAndReimport();
                        
                        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(file);

                        if (imported.width != rect.width || imported.height != rect.height) {
                            throw new InvalidOperationException("输出被平台导入预设缩放，请检查 TextureImporter 预设。");
                        }
                    } finally {
                        Object.DestroyImmediate(crop);
                    }
                }

                if (manifest) {
                    var sourcePath = AssetDatabase.GetAssetPath(source);
                    var data = new Manifest {
                        Source = sourcePath, SourceGuid = AssetDatabase.AssetPathToGUID(sourcePath),
                        Width = readable.width, Height = readable.height, Options = options, Tiles = tiles.ToArray()
                    };
                    var file = folder + "/slices.json";
                    File.WriteAllText(file, JsonUtility.ToJson(data, true));
                    AssetDatabase.ImportAsset(file);
                }

                return folder;
            } catch {
                AssetDatabase.DeleteAsset(folder);

                foreach (var tile in tiles) {
                    tile.File = "";
                }

                throw;
            }
        }

        private static string CleanName(string name) {
            foreach (var character in Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*")) {
                name = name.Replace(character, '_');
            }

            name = name.Trim().Trim('.');
            return string.IsNullOrEmpty(name) ? "Texture" : name;
        }
    }
}
