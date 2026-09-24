using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    internal enum TextureSliceMode {
        MaxSize,
        Grid
    }

    [Serializable]
    internal sealed class TextureSliceOptions {
        public TextureSliceMode Mode;
        public Vector2Int TileSize = new(2048, 2048);
        public Vector2Int Grid = new(2, 2);
        public Vector2Int Overlap;
        public bool Trim;
        public bool SkipEmpty = true;
        public float AlphaThreshold = 0.01f;
    }

    [Serializable]
    internal sealed class TextureSliceTile {
        public int Row;

        public int Column;

        // 坐标均以原图左下角为原点；行号则按视觉顺序从上到下。
        public RectInt Cell;
        public RectInt Content;
        public bool Empty;
        public bool Included = true;
        public string File;
    }

    /// <summary>切割几何和透明边扫描，与窗口、文件写入分离，预览与导出共用同一结果。</summary>
    internal static class TextureSliceLayout {
        private const int k_MaxTiles = 4096;

        internal static List<TextureSliceTile> Build(int width, int height, Color32[] pixels,
            TextureSliceOptions options, Action<float> progress = null) {
            if (width <= 0 || height <= 0 || pixels == null || pixels.LongLength != (long)width * height) {
                throw new ArgumentException("图片像素数据无效。");
            }

            int columns, rows;

            if (options.Mode == TextureSliceMode.MaxSize) {
                if (options.TileSize.x <= 0 || options.TileSize.y <= 0 ||
                    options.Overlap.x < 0 || options.Overlap.y < 0 ||
                    options.Overlap.x >= options.TileSize.x || options.Overlap.y >= options.TileSize.y) {
                    throw new ArgumentException("切片尺寸必须大于零，重叠像素必须非负且小于对应切片尺寸。");
                }

                columns = Count(width, options.TileSize.x, options.Overlap.x);
                rows = Count(height, options.TileSize.y, options.Overlap.y);
            } else {
                columns = options.Grid.x;
                rows = options.Grid.y;

                if (columns <= 0 || rows <= 0 || columns > width || rows > height) {
                    throw new ArgumentException("行列数必须大于零，且不能超过图片对应方向的像素数。");
                }
            }

            if ((long)columns * rows > k_MaxTiles) {
                throw new ArgumentException($"切片超过 {k_MaxTiles} 张，请增大切片尺寸或减少行列数。");
            }

            var result = new List<TextureSliceTile>(columns * rows);
            var threshold = Mathf.Clamp01(options.AlphaThreshold) * 255f;

            for (var row = 0; row < rows; row++) {
                for (var column = 0; column < columns; column++) {
                    progress?.Invoke((float)result.Count / (columns * rows));
                    int left, top, right, bottom;

                    if (options.Mode == TextureSliceMode.MaxSize) {
                        left = column * (options.TileSize.x - options.Overlap.x);
                        top = row * (options.TileSize.y - options.Overlap.y);
                        right = (int)Math.Min(width, (long)left + options.TileSize.x);
                        bottom = (int)Math.Min(height, (long)top + options.TileSize.y);
                    } else {
                        // 整数边界分配余数，保证非整除尺寸也完整覆盖、不丢像素。
                        left = (int)((long)column * width / columns);
                        right = (int)((long)(column + 1) * width / columns);
                        top = (int)((long)row * height / rows);
                        bottom = (int)((long)(row + 1) * height / rows);
                    }

                    var cell = new RectInt(left, height - bottom, right - left, bottom - top);
                    var tile = new TextureSliceTile { Row = row, Column = column, Cell = cell, Content = cell };

                    if (options.Trim || options.SkipEmpty) {
                        var bounds = FindContent(pixels, width, cell, threshold, options.Trim,
                            fraction => progress?.Invoke((result.Count + fraction) / (columns * rows)));
                        tile.Empty = bounds.width == 0;
                        tile.Included = !tile.Empty || !options.SkipEmpty;

                        // 保留全透明切片时使用原尺寸，避免导出 0×0 图片。
                        if (options.Trim && !tile.Empty) {
                            tile.Content = bounds;
                        }
                    }

                    result.Add(tile);
                }
            }

            return result;
        }

        private static int Count(int length, int size, int overlap) =>
            length <= size ? 1 : 1 + (int)(((long)length - size + size - overlap - 1) / (size - overlap));

        private static RectInt FindContent(Color32[] pixels, int width, RectInt cell, float threshold, bool trim,
            Action<float> progress) {
            var minX = cell.xMax;
            var minY = cell.yMax;
            var maxX = cell.xMin - 1;
            var maxY = cell.yMin - 1;

            for (var y = cell.yMin; y < cell.yMax; y++) {
                // 大切片内部也提供取消检查点，避免只能等整块扫描完成。
                if ((y - cell.yMin) % 32 == 0) {
                    progress?.Invoke((float)(y - cell.yMin) / cell.height);
                }

                for (var x = cell.xMin; x < cell.xMax; x++) {
                    if (pixels[y * width + x].a <= threshold) {
                        continue;
                    }

                    if (!trim) {
                        return cell;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            return maxX < minX ? new RectInt() : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}