using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 单个候选组件的扫描快照，同时保存展示信息、定位信息和应用前的比对值。
    /// 临时加载的磁盘对象会在扫描后卸载，不能依赖其组件引用；存活场景对象则可保留直接引用。
    /// </summary>
    public sealed class FontReplacementEntry {
        // 只决定是否直接应用这一行；取消勾选不能阻止源 Prefab 修改通过继承传播到实例。
        public bool selected = true;

        /// <summary>所属资产路径；尚未保存的场景可能为空。</summary>
        public string AssetPath { get; internal set; }

        /// <summary>供用户查看的名称路径，也用于磁盘对象重新定位后的附加校验。</summary>
        public string ObjectPath { get; internal set; }

        public string ComponentType { get; internal set; }
        public Object OriginalFont { get; internal set; }
        public Material OriginalMaterial { get; internal set; }

        // isLive 为 true 时使用直接引用；sceneHandle 防止将已移动到其他场景的对象当成原对象。
        internal Component liveComponent;
        internal bool isLive;

        internal int sceneHandle;

        // address[0] 是扫描根数组索引，其余是逐层 child 索引；同名兄弟节点也能区分。
        internal int[] address;

        // 在 GetComponents<Component>() 数组中的位置，与完整类型名共同确认组件身份。
        internal int componentIndex;

        // 磁盘文件及其依赖的指纹，避免用户编辑或嵌套源变化后继续应用旧计划。
        internal string fingerprint;
    }

    /// <summary>一次扫描的完整计划。entries 可逐项勾选，issues 记录未能完整扫描的资产。</summary>
    public sealed class FontReplacementPreview {
        public readonly List<FontReplacementEntry> entries = new();
        public readonly List<string> issues = new();

        // 参数与原窗口解耦；应用计划时不重新读取窗口里的可变选项。
        internal FontReplacementOptions options;
    }

    /// <summary>
    /// 扫描入口：范围解析 → 资产/场景访问 → 深度遍历 → 组件适配与字体匹配 → 生成预览。
    /// 只收集替换候选，不负责勾选状态之外的写入；实际变更交给 FontReplacementApplier。
    /// </summary>
    public static class FontReplacementScanner {
        /// <summary>只读扫描。进度回调可抛 OperationCanceledException，取消后不应使用部分预览。</summary>
        public static FontReplacementPreview Scan(FontReplacementOptions options,
            Action<string, float> progress = null) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            EnsureEditMode();
            var error = options.Validate();

            if (error != null) {
                throw new ArgumentException(error);
            }

            var preview = new FontReplacementPreview {
                // 保存独立参数，调用方随后修改窗口配置不会改变已生成的计划。
                options = JsonUtility.FromJson<FontReplacementOptions>(JsonUtility.ToJson(options))
            };

            if (options.scope == FontReplacementScope.SelectedHierarchyObjectsAndChildren) {
                var roots = Selection.gameObjects.Where(go => !EditorUtility.IsPersistent(go) &&
                                                              go.scene.IsValid()
                                                              && !EditorSceneManager.IsPreviewScene(go.scene)).ToArray();
                
                // 已选父节点会递归包含子节点，因此剔除同时被选中的后代，避免重复预览。
                roots = roots.Where(go => !roots.Any(other => other != go &&
                                                              go.transform.IsChildOf(other.transform))).ToArray();

                if (roots.Length == 0) {
                    throw new ArgumentException("请在 Hierarchy 中选择物体；资产请使用“指定资产”范围。");
                }

                for (var i = 0; i < roots.Length; i++) {
                    var root = roots[i];
                    ScanRoots(new[] { root }, root.scene.path, true, null, preview,
                        (name, _) => progress?.Invoke(name, (float)i / roots.Length));
                }
            } else {
                var paths = CollectAssets(options);

                for (var i = 0; i < paths.Count; i++) {
                    var path = paths[i];
                    progress?.Invoke(path, (float)i / paths.Count);
                    var start = preview.entries.Count;

                    try {
                        VisitAsset(path, (roots, live) => ScanRoots(roots, path, live,
                            live ? null : Fingerprint(path), preview,
                            (name, _) => progress?.Invoke(name, (float)i / paths.Count)));
                    } catch (OperationCanceledException) {
                        // 取消属于整次扫描的控制流，不应被当作单个资产失败后继续扫描。
                        throw;
                    } catch (Exception exception) {
                        // 资产未完整扫描就不能保留其中已经生成的行，否则用户会误以为预览完整。
                        preview.entries.RemoveRange(start, preview.entries.Count - start);
                        
                        // 普通资产错误保留诊断，让其余资产仍可进入预览。
                        preview.issues.Add($"{path}: {exception.Message}");
                    }
                }
            }

            progress?.Invoke("扫描完成", 1);
            return preview;
        }

        // 扫描与应用都调用，防止通过 Editor API 绕过窗口按钮的状态限制。
        internal static void EnsureEditMode() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) {
                throw new InvalidOperationException("请在非播放、非编译状态下操作。");
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null) {
                throw new InvalidOperationException("请先保存并关闭 Prefab Mode，避免覆盖正在编辑的 Prefab。");
            }
        }

        /// <summary>把文件夹与显式资产展开为去重、排序后的路径，统一应用资产类型开关。</summary>
        internal static List<string> CollectAssets(FontReplacementOptions options) {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var inputs = options.assets.Where(asset => asset != null).Select(AssetDatabase.GetAssetPath).ToArray();

            if (inputs.Length == 0) {
                throw new ArgumentException("请添加 Assets 下的文件夹、场景或 Prefab。");
            }

            foreach (var path in inputs) {
                if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal)) {
                    throw new ArgumentException($"范围仅支持 Assets 下的资产：{path}");
                }

                if (AssetDatabase.IsValidFolder(path)) {
                    if (options.includePrefabs) {
                        AddFound("t:Prefab", path, paths);
                    }

                    if (options.includeScenes) {
                        AddFound("t:Scene", path, paths);
                    }
                } else if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                    if (options.includePrefabs) {
                        paths.Add(path);
                    }
                } else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) {
                    if (options.includeScenes) {
                        paths.Add(path);
                    }
                } else {
                    throw new ArgumentException($"不支持的资产：{path}");
                }
            }

            // 模型也可能被 t:Prefab 返回；仅接受可保存的 .prefab。
            return paths.Where(path => !options.IsExcluded(path) &&
                                       (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static void AddFound(string filter, string folder, HashSet<string> paths) {
            foreach (var guid in AssetDatabase.FindAssets(filter, new[] { folder })) {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private static void ScanRoots(GameObject[] roots, string path, bool live, string fingerprint,
            FontReplacementPreview preview, Action<string, float> progress) {
            // 根索引是磁盘定位地址的起点，应用时必须使用同一份资产的根数组重新解析。
            for (var i = 0; i < roots.Length; i++) {
                ScanTransform(roots[i].transform, new[] { i }, roots[i].name, path, live,
                    fingerprint, preview, progress);
            }
        }

        private static void ScanTransform(Transform transform, int[] address, string objectPath, string assetPath,
            bool live, string fingerprint, FontReplacementPreview preview, Action<string, float> progress) {
            progress?.Invoke($"{assetPath} / {objectPath}", 0);

            // 字体替换不受运行时显隐状态影响：始终遍历未激活物体，也处理被禁用的文本组件。
            // 保留完整组件数组的位置；缺失脚本虽会跳过，但不能先过滤再记录索引。
            var components = transform.GetComponents<Component>();

            for (var i = 0; i < components.Length; i++) {
                var component = components[i];

                if (component == null || !FontReplacementComponent.Supports(component, preview.options.kind)) {
                    continue;
                }

                var font = FontReplacementComponent.ReadFont(component);

                if (!preview.options.Matches(font)) {
                    continue;
                }

                preview.entries.Add(new FontReplacementEntry {
                    AssetPath = assetPath,
                    ObjectPath = objectPath,
                    ComponentType = component.GetType().FullName,
                    OriginalFont = font,
                    OriginalMaterial = FontReplacementComponent.ReadMaterial(component),
                    isLive = live,
                    liveComponent = live ? component : null,
                    sceneHandle = component.gameObject.scene.handle,
                    address = address,
                    componentIndex = i,
                    fingerprint = fingerprint
                });
            }

            for (var i = 0; i < transform.childCount; i++) {
                var child = transform.GetChild(i);
                
                // 为子节点创建新地址，避免递归时修改兄弟节点或已有预览行持有的地址。
                ScanTransform(child, address.Concat(new[] { i }).ToArray(), $"{objectPath}/{child.name}",
                    assetPath, live, fingerprint, preview, progress);
            }
        }

        /// <summary>文件内容哈希检测直接编辑；依赖哈希补充检测嵌套 Prefab、字体等上游资产变化。</summary>
        internal static string Fingerprint(string path) {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToBase64String(sha.ComputeHash(stream)) + ":" + AssetDatabase.GetAssetDependencyHash(path);
        }

        /// <summary>扫描未打开场景时使用隔离预览场景；不切换或保存用户的场景。</summary>
        private static void VisitAsset(string path, Action<GameObject[], bool> visit) {
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                // Prefab 内容仅在回调期间存活，finally 负责在取消或异常时同样释放。
                var root = PrefabUtility.LoadPrefabContents(path);

                try {
                    visit(new[] { root }, false);
                } finally {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                return;
            }

            var scene = SceneManager.GetSceneByPath(path);

            if (scene.IsValid() && scene.isLoaded) {
                // 资产范围中遇到已打开场景，也按场景内对象处理，后续才能正确支持 Undo。
                visit(scene.GetRootGameObjects(), true);
                return;
            }

            scene = EditorSceneManager.OpenPreviewScene(path);

            try {
                visit(scene.GetRootGameObjects(), false);
            } finally {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>将预览行还原为当前组件；定位或身份校验失败时返回 null，交由应用阶段跳过。</summary>
        internal static Component Resolve(FontReplacementEntry entry, GameObject[] roots) {
            if (entry.isLive) {
                var live = entry.liveComponent;
                return live != null && live.gameObject.scene.handle == entry.sceneHandle ? live : null;
            }

            var address = entry.address;

            if (address[0] >= roots.Length) {
                return null;
            }

            var transform = roots[address[0]].transform;
            var path = transform.name;

            for (var i = 1; i < address.Length; i++) {
                if (address[i] >= transform.childCount) {
                    return null;
                }

                transform = transform.GetChild(address[i]);
                path += "/" + transform.name;
            }

            if (path != entry.ObjectPath) {
                // 索引可区分重名节点，名称路径再拦截明显的层级改动；文件指纹负责整体过期校验。
                return null;
            }

            var components = transform.GetComponents<Component>();

            if (entry.componentIndex >= components.Length) {
                return null;
            }

            var component = components[entry.componentIndex];
            return component != null && component.GetType().FullName == entry.ComponentType ? component : null;
        }
    }
}