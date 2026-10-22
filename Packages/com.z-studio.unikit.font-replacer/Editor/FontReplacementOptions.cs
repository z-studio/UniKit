using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>决定处理哪些对象，不以场景是否打开区分范围；项目资产仅限 Assets 目录。</summary>
    public enum FontReplacementScope {
        [InspectorName("Hierarchy 选中的物体及子物体")]
        SelectedHierarchyObjectsAndChildren,

        [InspectorName("Project 资源（文件夹含子文件夹）")]
        ProjectAssetsIncludingSubfolders
    }

    /// <summary>TMP 使用 TMP_FontAsset，旧版 Text / TextMesh 使用 UnityEngine.Font。</summary>
    public enum FontReplacementKind {
        [InspectorName("TextMeshPro 字体（TMP）")]
        TextMeshPro,

        [InspectorName("Unity Text / TextMesh 字体")]
        UnityTextAndTextMesh
    }

    /// <summary>决定哪些现有字体进入预览；已经使用目标字体的组件在所有模式下都跳过。</summary>
    public enum FontReplacementFilter {
        /// <summary>替换范围内的全部字体；已经使用目标字体的组件自动跳过。</summary>
        [InspectorName("替换所有字体")]
        ReplaceAllFonts,

        // 按资产引用匹配，不按字体名称或原始 TTF 文件匹配。
        [InspectorName("只替换指定的源字体")]
        ReplaceSourceFontOnly,

        // 只匹配读取时为空的引用；TMP 已自动补回默认字体的对象不属于此类。
        [InspectorName("只替换未设置或已丢失的字体")]
        ReplaceUnassignedOrMissingFonts
    }

    /// <summary>
    /// 窗口与 Editor API 共用的替换参数；扫描时复制一份保存在预览中，应用使用该快照。
    /// 一次只处理一种字体类型，避免 TMP_FontAsset 与 Unity Font 混用。
    /// </summary>
    [Serializable]
    public sealed class FontReplacementOptions {
        /// <summary>处理哪些对象：Hierarchy 所选物体，或下方 assets 中指定的场景/预制体/文件夹。</summary>
        public FontReplacementScope scope = FontReplacementScope.ProjectAssetsIncludingSubfolders;

        /// <summary>选择组件所用的字体类型：TMP_FontAsset 或 UnityEngine.Font。</summary>
        public FontReplacementKind kind;

        /// <summary>范围内哪些组件需要替换；默认只匹配源字体，避免误改其他字体。</summary>
        public FontReplacementFilter filter = FontReplacementFilter.ReplaceSourceFontOnly;

        // 只有“只替换源字体”模式读取此项；切换到其他模式不必清空它。
        public Object sourceFont;

        public Object targetFont;

        // TMP 专用：空值表示使用目标字体默认材质，不表示保留原组件材质。
        public Material targetMaterial;

        // 两个资产类型开关用于项目或指定资产扫描，不影响 Hierarchy 选中物体的遍历。
        public bool includePrefabs = true;
        public bool includeScenes = true;

        // 可混合场景、预制体和文件夹；文件夹递归查找场景/预制体，展开与去重由 Scanner 负责。
        public List<Object> assets = new();

        // 仅用于磁盘资产范围；按完整路径边界排除指定文件及文件夹内全部资产。
        public List<Object> excludedAssets = new();

        /// <summary>校验与当前 Unity 选择无关的参数，返回首个错误；返回 null 表示通过。</summary>
        public string Validate() {
            if (!Enum.IsDefined(typeof(FontReplacementScope), scope)) {
                return "请选择有效的替换范围。";
            }

            var type = kind == FontReplacementKind.TextMeshPro ? typeof(TMP_FontAsset) : typeof(Font);
            var fontDescription = kind == FontReplacementKind.TextMeshPro ? "TMP 字体资产（TMP_FontAsset）"
                : "Unity 字体文件（Font）";

            if (filter == FontReplacementFilter.ReplaceSourceFontOnly &&
                (sourceFont == null || !type.IsInstanceOfType(sourceFont))) {
                return $"请选择要被替换的{fontDescription}。";
            }

            if (targetFont == null || !type.IsInstanceOfType(targetFont)) {
                return $"请选择替换后的{fontDescription}。";
            }

            if (!EditorUtility.IsPersistent(targetFont)) {
                return "目标字体必须是已保存的字体资产，不能使用临时创建的对象。";
            }

            if (filter == FontReplacementFilter.ReplaceSourceFontOnly && sourceFont == targetFont) {
                return "源字体与目标字体相同，无需替换。";
            }

            if (kind == FontReplacementKind.TextMeshPro) {
                // 字体与材质必须引用同一图集，否则字体虽换成功，渲染仍可能采样旧字形。
                var font = (TMP_FontAsset)targetFont;
                var material = targetMaterial != null ? targetMaterial : font.material;

                if (material != null && !EditorUtility.IsPersistent(material)) {
                    return "目标材质必须是已保存的材质资产，不能使用临时材质实例。";
                }

                if (material == null || font.material == null ||
                    !material.HasProperty(ShaderUtilities.ID_MainTex) ||
                    font.material.GetTexture(ShaderUtilities.ID_MainTex) == null ||
                    material.GetTexture(ShaderUtilities.ID_MainTex) !=
                    font.material.GetTexture(ShaderUtilities.ID_MainTex)) {
                    return "目标材质必须使用目标 TMP 字体的图集；可留空以使用字体默认材质。";
                }
            }

            if (scope == FontReplacementScope.ProjectAssetsIncludingSubfolders &&
                !includePrefabs && !includeScenes) {
                return "请至少启用预制体（Prefab）或场景。";
            }

            if (scope == FontReplacementScope.ProjectAssetsIncludingSubfolders) {
                var hasInput = false;
                var hasEnabledInput = false;

                foreach (var asset in assets) {
                    if (asset == null) {
                        continue;
                    }

                    if (!IsScopeAsset(asset)) {
                        return "处理范围仅支持 Assets 下的文件夹、场景和预制体，请移除其他对象。";
                    }

                    hasInput = true;
                    var path = AssetDatabase.GetAssetPath(asset);

                    // 这里只检查直接添加的类型，不遍历文件夹，避免窗口每次重绘都重复搜索资产。
                    hasEnabledInput |= AssetDatabase.IsValidFolder(path)
                                       || (includePrefabs && path.EndsWith(".prefab",
                                           StringComparison.OrdinalIgnoreCase))
                                       || (includeScenes
                                           && path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
                }

                if (!hasInput) {
                    return "请从 Project 拖入需要处理的场景、预制体或文件夹。";
                }

                if (!hasEnabledInput) {
                    return "添加的资源类型已关闭，请勾选对应的场景/预制体类型，或调整资源列表。";
                }

                foreach (var asset in excludedAssets) {
                    if (asset == null) {
                        continue;
                    }

                    if (!IsScopeAsset(asset)) {
                        return $"不支持的排除项：{asset.name}。请选择 Assets 下的文件夹、场景或 Prefab。";
                    }
                }
            }

            return null;
        }

        // 窗口的拖放过滤与公共 API 校验共用同一规则，避免两处支持的资产类型不一致。
        internal static bool IsScopeAsset(Object asset) {
            var path = AssetDatabase.GetAssetPath(asset);
            return path == "Assets" || (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                                        (AssetDatabase.IsValidFolder(path)
                                         || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                                         || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                                        )
                );
        }

        internal bool IsExcluded(string path) {
            foreach (var asset in excludedAssets) {
                if (asset == null) {
                    continue;
                }

                var excludedPath = AssetDatabase.GetAssetPath(asset);

                if (path == excludedPath || (AssetDatabase.IsValidFolder(excludedPath) &&
                                             path.StartsWith(excludedPath + "/", StringComparison.Ordinal))) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>扫描时的候选条件；应用前还需核对原字体、原材质和对象是否仍有效。</summary>
        internal bool Matches(Object font) {
            if (font == targetFont) {
                return false;
            }

            return filter switch {
                FontReplacementFilter.ReplaceAllFonts => true,
                FontReplacementFilter.ReplaceSourceFontOnly => font == sourceFont,
                FontReplacementFilter.ReplaceUnassignedOrMissingFonts => font == null,
                _ => false
            };
        }
    }
}