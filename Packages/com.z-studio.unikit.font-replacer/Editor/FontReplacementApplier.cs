using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 统计单位是组件条目，不是资产文件数。Changed = SceneChanges + AssetChanges。
    /// Skipped 包含过期、不可写和已从 Prefab 源继承变化的项；取消后尚未处理的项不计入 Skipped。
    /// </summary>
    public sealed class FontReplacementResult {
        public int Changed { get; internal set; }
        public int SceneChanges { get; internal set; }
        public int AssetChanges { get; internal set; }
        public int Skipped { get; internal set; }

        /// <summary>Skipped 中已经达到目标字体和材质的条目，常见于 Prefab 继承传播。</summary>
        public int AlreadyUpdated { get; internal set; }

        public bool Cancelled { get; internal set; }

        public readonly List<string> issues = new();
    }

    /// <summary>
    /// 把只读预览变成修改：先校验磁盘快照，再按依赖顺序保存资产，最后更新存活场景对象。
    /// 资产通过独立加载/保存落盘；场景内修改只标脏并记录 Undo，不代替用户保存场景。
    /// </summary>
    public static class FontReplacementApplier {
        private enum ReplacementOutcome {
            Changed,
            AlreadyUpdated,
            Stale
        }

        /// <summary>
        /// 仅应用预览中勾选且仍匹配快照的项；在磁盘资产之间或存活场景组件之间检查取消。
        /// 取消后保留已完成的修改，不承诺整次操作的事务回滚。
        /// </summary>
        public static FontReplacementResult Apply(FontReplacementPreview preview,
            Action<string, float> progress = null) {
            FontReplacementScanner.EnsureEditMode();

            if (preview == null) {
                throw new ArgumentNullException(nameof(preview));
            }

            var error = preview.options.Validate();

            if (error != null) {
                throw new ArgumentException(error);
            }

            var result = new FontReplacementResult();
            var selected = preview.entries.Where(entry => entry.selected).ToArray();
            
            // 同一个磁盘文件只加载与保存一次，不为每个组件重复执行资产 I/O。
            var disk = selected.Where(entry => !entry.isLive).GroupBy(entry => entry.AssetPath)
                .ToDictionary(group => group.Key, group => group.ToList());

            // 先校验全部文件，再更新依赖源。否则嵌套 Prefab 的继承更新可能干扰后续校验。
            var checks = disk.Keys.ToArray();

            try {
                for (var i = 0; i < checks.Length; i++) {
                    var path = checks[i];
                    
                    // 哈希与版本控制查询也可能耗时，此阶段同样允许取消，尚不会产生字体修改。
                    progress?.Invoke("检查：" + path, 0.1f * i / checks.Length);

                    try {
                        if (FontReplacementScanner.Fingerprint(path) != disk[path][0].fingerprint) {
                            throw new InvalidOperationException("扫描后文件或依赖发生变化，请重新扫描。");
                        }

                        if (!AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.ForceUpdate) ||
                            (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) {
                            throw new InvalidOperationException("资产只读或尚未从版本控制签出。");
                        }
                    } catch (Exception exception) {
                        result.Skipped += disk[path].Count;
                        result.issues.Add($"{path}: {exception.Message}");
                        disk.Remove(path);
                    }
                }
            } catch (OperationCanceledException) {
                result.Cancelled = true;
                return result;
            }

            var paths = OrderDependencies(disk.Keys);
            
            // 只把存活场景对象的修改合并为一次撤销；磁盘保存不因此获得跨文件回滚能力。
            Undo.IncrementCurrentGroup();

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Fonts");
            var units = paths.Count + selected.Count(entry => entry.isLive);
            var completed = 0;

            try {
                for (var i = 0; i < paths.Count; i++) {
                    var path = paths[i];
                    progress?.Invoke(path, 0.1f + 0.9f * completed++ / Math.Max(1, units));

                    try {
                        ApplyAsset(path, disk[path], preview.options, result);
                    } catch (Exception exception) {
                        // 单个资产失败不终止后续资产；已成功保存的文件保留，错误单独汇总。
                        result.Skipped += disk[path].Count;
                        result.issues.Add($"{path}: {exception.Message}");
                    }
                }

                // 最后处理存活实例，让它们先继承上游字体变化，避免生成多余 override。
                foreach (var entry in selected.Where(entry => entry.isLive)) {
                    progress?.Invoke(entry.ObjectPath, 0.1f + 0.9f * completed++ / Math.Max(1, units));
                    var component = FontReplacementScanner.Resolve(entry, null);

                    try {
                        var outcome = TryReplace(entry, component, preview.options, true);

                        if (outcome == ReplacementOutcome.Changed) {
                            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                        }

                        RecordOutcome(result, entry, outcome, true);
                    } catch (Exception exception) {
                        // 一个组件异常不应丢失前面成功项的统计；已开始的写入仍由同一 Undo 分组兜底。
                        if (component != null) {
                            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                        }

                        result.Skipped++;
                        result.issues.Add($"{entry.AssetPath} / {entry.ObjectPath}: {exception.Message}；如有部分修改可 Undo。");
                    }
                }
            } catch (OperationCanceledException) {
                result.Cancelled = true;
            } finally {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return result;
        }

        /// <summary>
        /// 深度优先、依赖先于使用者：先更新源 Prefab，再访问嵌套 Prefab、Variant 和场景。
        /// 仅排序本次已选资产，不会因为存在依赖就扩大用户选择的修改范围。
        /// </summary>
        private static List<string> OrderDependencies(IEnumerable<string> paths) {
            var included = new HashSet<string>(paths, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();

            void Visit(string path) {
                // 先标记再递归，既避免共享依赖重复排序，也防止异常依赖环导致无限递归。
                if (!visited.Add(path)) {
                    return;
                }

                foreach (var dependency in AssetDatabase.GetDependencies(path, false)
                             .OrderBy(p => p, StringComparer.Ordinal)) {
                    if (included.Contains(dependency)) {
                        Visit(dependency);
                    }
                }

                // 后序入列，保证本次包含的直接或间接依赖都先处理。
                ordered.Add(path);
            }

            foreach (var path in included.OrderBy(p => p, StringComparer.Ordinal)) {
                Visit(path);
            }

            return ordered;
        }

        /// <summary>在临时加载内容上应用一个磁盘文件的全部选中项，保存成功后才计入修改统计。</summary>
        private static void ApplyAsset(string path, List<FontReplacementEntry> entries,
            FontReplacementOptions options, FontReplacementResult result) {
            var prefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            GameObject root = null;
            Scene scene = default;
            
            // 原先列在 Hierarchy 中但处于卸载状态的场景，处理后仍需保留为卸载项。
            var wasListed = !prefab && SceneManager.GetSceneByPath(path).IsValid();

            if (!prefab && SceneManager.GetSceneByPath(path).isLoaded) {
                // 扫描后才打开的场景可能已有新编辑，不能再当作可直接覆写的磁盘资产。
                throw new InvalidOperationException("扫描后该场景被打开，请重新扫描以保留场景内编辑。");
            }

            if (!prefab && Enumerable.Range(0, SceneManager.sceneCount)
                    .Any(index => string.IsNullOrEmpty(SceneManager.GetSceneAt(index).path))) {
                throw new InvalidOperationException("Unity 无法在存在 Untitled 场景时批量打开其他场景；请先保存该场景后重试。");
            }

            var activeScene = SceneManager.GetActiveScene();

            try {
                if (prefab) {
                    root = PrefabUtility.LoadPrefabContents(path);
                } else {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }

                var roots = prefab ? new[] { root } : scene.GetRootGameObjects();
                var changed = 0;
                var outcomes = new List<(FontReplacementEntry entry, ReplacementOutcome outcome)>();

                foreach (var entry in entries) {
                    var component = FontReplacementScanner.Resolve(entry, roots);
                    var outcome = TryReplace(entry, component, options, false);

                    if (outcome == ReplacementOutcome.Changed) {
                        changed++;
                    }

                    outcomes.Add((entry, outcome));
                }

                if (changed > 0) {
                    // 全部跳过时不保存，避免无实际字体修改也重写资产文件。
                    if (prefab) {
                        PrefabUtility.SaveAsPrefabAsset(root, path, out var success);

                        if (!success) {
                            throw new IOException("保存 Prefab 失败。");
                        }
                    } else if (!EditorSceneManager.SaveScene(scene)) {
                        throw new IOException("保存场景失败。");
                    }
                }

                // 只有保存成功才提交结果，避免保存失败也把临时对象上的变化计为成功。
                foreach (var item in outcomes) {
                    RecordOutcome(result, item.entry, item.outcome, false);
                }
            } finally {
                // 取消、定位失败或保存异常都必须释放临时内容，并还原活动场景。
                if (root != null) {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                if (scene.IsValid() && scene.isLoaded) {
                    EditorSceneManager.CloseScene(scene, !wasListed);
                }

                if (activeScene.IsValid() && activeScene.isLoaded) {
                    SceneManager.SetActiveScene(activeScene);
                }
            }
        }

        private static ReplacementOutcome TryReplace(FontReplacementEntry entry, Component component,
            FontReplacementOptions options, bool undo) {
            // 预览后被人工修改，或已从上游 Prefab 继承新字体的项不再覆盖。
            if (component == null || FontReplacementComponent.ReadFont(component) != entry.OriginalFont ||
                FontReplacementComponent.ReadMaterial(component) != entry.OriginalMaterial) {
                return component != null && FontReplacementComponent.IsTarget(component, options)
                    ? ReplacementOutcome.AlreadyUpdated : ReplacementOutcome.Stale;
            }

            FontReplacementComponent.Replace(component, options, undo);
            return ReplacementOutcome.Changed;
        }

        private static void RecordOutcome(FontReplacementResult result, FontReplacementEntry entry,
            ReplacementOutcome outcome, bool live) {
            if (outcome == ReplacementOutcome.Changed) {
                result.Changed++;

                if (live) {
                    result.SceneChanges++;
                } else {
                    result.AssetChanges++;
                }

                return;
            }

            result.Skipped++;

            if (outcome == ReplacementOutcome.AlreadyUpdated) {
                result.AlreadyUpdated++;
            } else {
                result.issues.Add($"{entry.AssetPath} / {entry.ObjectPath}: 对象、原字体或材质已变化，请重新扫描。");
            }
        }
    }
}