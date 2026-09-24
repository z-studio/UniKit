using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 把不同来源整理为一张纹理和对应矩形。SpriteAtlas 读取原始资源，不依赖运行时打包结果。
    /// 合成纹理仅用于编辑器预览，生成字体时另行保存；调用方负责 Dispose。
    /// </summary>
    internal sealed class SpriteFontSource : IDisposable {
        public Texture2D Texture { get; private set; }
        public SpriteRect[] Sprites { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsGenerated { get; private set; }

        public static SpriteFontSource Load(SpriteFontSettings settings) {
            var source = new SpriteFontSource();

            try {
                if (settings.Source == SpriteFontSourceType.Texture) {
                    source.ReadTexture(settings.Atlas);
                } else {
                    var sprites = new List<Sprite>();

                    if (settings.Source == SpriteFontSourceType.Sprites) {
                        if (settings.SourceSprites.Any(sprite => sprite == null)) {
                            throw new InvalidOperationException("散图列表中有空引用，请移除空项或重新指定 Sprite。");
                        }

                        sprites.AddRange(settings.SourceSprites);
                    } else {
                        var atlas = settings.SourceAtlas;
                        var visited = new HashSet<SpriteAtlas>();

                        while (atlas != null && atlas.isVariant) {
                            if (!visited.Add(atlas)) {
                                throw new InvalidOperationException("SpriteAtlas 的主图集引用存在循环。");
                            }

                            atlas = atlas.GetMasterAtlas();
                        }

                        if (atlas == null) {
                            throw new InvalidOperationException("请选择 SpriteAtlas；变体图集需要有效的 Master Atlas。");
                        }

                        foreach (var packable in atlas.GetPackables()) {
                            CollectSprites(packable, sprites);
                        }
                    }

                    source.Pack(sprites.Distinct().ToArray());
                }

                switch (settings.Order) {
                    case SpriteFontOrder.Name:
                        Array.Sort(source.Sprites, (a, b) => {
                            var comparison = EditorUtility.NaturalCompare(a.name, b.name);
                            return comparison != 0 ? comparison
                                : string.CompareOrdinal(a.spriteID.ToString(), b.spriteID.ToString());
                        });
                        break;
                    case SpriteFontOrder.Position:
                        source.Sprites = SortByRows(source.Sprites);
                        break;
                }

                return source;
            } catch {
                source.Dispose();
                throw;
            }
        }

        private static SpriteRect[] SortByRows(SpriteRect[] sprites) {
            var remaining = sprites.OrderByDescending(sprite => sprite.rect.yMax)
                .ThenBy(sprite => sprite.rect.xMin).ToList();
            var sorted = new List<SpriteRect>(sprites.Length);

            while (remaining.Count > 0) {
                var anchor = remaining[0].rect;
                // 以本行最高的切片为固定参照，避免不断扩张行范围，把相邻行串在一起。
                // 垂直重叠达到较矮切片高度的一半即可归入同一行，兼容 +、小数点等矮字符。
                var row = remaining.Where(sprite => {
                    var rect = sprite.rect;
                    var overlap = Mathf.Min(anchor.yMax, rect.yMax) - Mathf.Max(anchor.yMin, rect.yMin);
                    return sprite == remaining[0] ||
                        overlap > 0 && overlap >= Mathf.Min(anchor.height, rect.height) * 0.5f;
                }).ToList();

                sorted.AddRange(row.OrderBy(sprite => sprite.rect.xMin)
                    .ThenByDescending(sprite => sprite.rect.yMax));

                foreach (var sprite in row) {
                    remaining.Remove(sprite);
                }
            }

            return sorted.ToArray();
        }

        private void ReadTexture(Texture2D texture) {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;

            if (texture == null || importer == null || importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode == SpriteImportMode.None) {
                throw new InvalidOperationException("请选择 Sprite 类型的纹理，支持 Single 或 Multiple 模式。");
            }

            Texture = texture;
            Sprites = ReadRects(importer);
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            Width = width;
            Height = height;
        }

        private static SpriteRect[] ReadRects(TextureImporter importer) {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);

            if (provider == null) {
                throw new InvalidOperationException($"无法读取切片数据：{importer.assetPath}");
            }

            provider.InitSpriteEditorDataProvider();
            return provider.GetSpriteRects();
        }

        internal static void CollectSprites(Object asset, List<Sprite> result) {
            if (asset is Sprite sprite) {
                result.Add(sprite);
                return;
            }

            var path = AssetDatabase.GetAssetPath(asset);

            if (AssetDatabase.IsValidFolder(path)) {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path })
                             .OrderBy(AssetDatabase.GUIDToAssetPath, StringComparer.Ordinal)) {
                    result.AddRange(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))
                        .OfType<Sprite>());
                }
            } else if (asset is Texture2D) {
                result.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>());
            }
        }

        private void Pack(Sprite[] sprites) {
            if (sprites.Length == 0) {
                throw new InvalidOperationException("来源中没有 Sprite。请添加散图，或为 SpriteAtlas 配置 Objects for Packing。");
            }

            var readableTextures = new Dictionary<string, Texture2D>();
            var crops = new List<Texture2D>();
            var rects = new List<SpriteRect>();

            try {
                foreach (var sprite in sprites) {
                    var path = AssetDatabase.GetAssetPath(sprite);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;

                    if (importer == null || !EditorUtility.IsPersistent(sprite)) {
                        throw new InvalidOperationException($"「{sprite.name}」不是可读取源图片的项目 Sprite 资源。");
                    }

                    var spriteId = sprite.GetSpriteID();
                    var rect = ReadRects(importer).FirstOrDefault(item => item.spriteID == spriteId);

                    if (rect == null) {
                        throw new InvalidOperationException($"找不到「{sprite.name}」的源切片，请先在 Sprite Editor 中 Apply。");
                    }

                    if (!readableTextures.TryGetValue(path, out var readable)) {
                        // 读取图片主资源，而不是 sprite.texture（后者可能已被 SpriteAtlas 替换）。
                        readable = SpriteAtlasTools.CopyReadableTexture(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                        readableTextures.Add(path, readable);
                    }

                    importer.GetSourceTextureWidthAndHeight(out var sourceWidth, out var sourceHeight);
                    var x = Mathf.RoundToInt(rect.rect.xMin * readable.width / sourceWidth);
                    var y = Mathf.RoundToInt(rect.rect.yMin * readable.height / sourceHeight);
                    var width = Mathf.RoundToInt(rect.rect.xMax * readable.width / sourceWidth) - x;
                    var height = Mathf.RoundToInt(rect.rect.yMax * readable.height / sourceHeight) - y;

                    if (width <= 0 || height <= 0 || x < 0 || y < 0 || x + width > readable.width
                        || y + height > readable.height) {
                        throw new InvalidOperationException($"「{sprite.name}」的切片在导入缩放后无效，请检查导入尺寸。");
                    }

                    var crop = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    crops.Add(crop);
                    crop.SetPixels(readable.GetPixels(x, y, width, height));
                    crop.Apply();
                    
                    // 组合资源 GUID 和切片 ID；同名文件、复制的切片 ID 也不会互相覆盖。
                    using var hash = MD5.Create();
                    var identity = AssetDatabase.AssetPathToGUID(path) + ":" + spriteId;
                    var id = new GUID(BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity)))
                        .Replace("-", ""));
                    rects.Add(new SpriteRect { name = sprite.name, spriteID = id });
                }

                Texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
                    name = "Sprite Font Preview Atlas",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                
                IsGenerated = true;
                
                var packed = Texture.PackTextures(crops.ToArray(), 2, Mathf.Min(8192, SystemInfo.maxTextureSize),
                    false);

                if (packed == null || packed.Length != crops.Count) {
                    throw new InvalidOperationException("无法合成字体图集，请减少来源数量或图片尺寸。");
                }

                for (var i = 0; i < packed.Length; i++) {
                    var pixels = new Rect(Mathf.RoundToInt(packed[i].x * Texture.width),
                        Mathf.RoundToInt(packed[i].y * Texture.height),
                        Mathf.RoundToInt(packed[i].width * Texture.width),
                        Mathf.RoundToInt(packed[i].height * Texture.height));

                    // PackTextures 超出上限时会静默缩小图片，字体工具应明确报错而不是牺牲清晰度。
                    if (!Mathf.Approximately(pixels.width, crops[i].width) || !Mathf.Approximately(pixels.height, crops[i].height)) {
                        throw new InvalidOperationException("来源图片超过单张字体图集容量（最大 8192）。请拆分配置或减小源图片尺寸。");
                    }

                    rects[i].rect = pixels;
                }

                Width = Texture.width;
                Height = Texture.height;
                Sprites = rects.ToArray();
            } finally {
                foreach (var crop in crops) {
                    Object.DestroyImmediate(crop);
                }

                foreach (var readable in readableTextures.Values) {
                    Object.DestroyImmediate(readable);
                }
            }
        }

        public void Dispose() {
            if (IsGenerated && Texture != null) {
                Object.DestroyImmediate(Texture);
            }

            Texture = null;
        }
    }
}
